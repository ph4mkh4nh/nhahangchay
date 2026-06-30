using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MimeKit;
using nhahangchay.Data;
using nhahangchay.Models;
using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace nhahangchay.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly IConfiguration _config;
        private static readonly SemaphoreSlim _bookingLock = new SemaphoreSlim(1, 1);
        private const int BookingPending = 0;
        private const int BookingCheckedIn = 1;
        private const int BookingCancelled = 2;

        private const int PaymentUnpaid = 0;
        private const int PaymentPaid = 1;

        private const int RestaurantCapacity = 100;
        private const int BookingCooldownMinutes = 2;
        private const int DepositExpireMinutes = 15;
        public HomeController(ApplicationDbContext context, UserManager<AppUser> userManager, IConfiguration config)
        {
            _context = context;
            _userManager = userManager;
            _config = config;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                ViewBag.CustomerName = user?.FullName;
                ViewBag.CustomerEmail = user?.Email;
            }

            var excludedFeaturedCategories = new[]
                {
                    "Nước Uống",
                    "Nước uống",
                    "Đồ uống",
                    "Thức uống",
                    "Tráng Miệng",
                    "Tráng miệng",
                    "Dessert",
                    "Drink"
                };

            var featuredDishes = await _context.Dishes
                .AsNoTracking()
                .Where(x => x.IsAvailable)
                .Where(x => string.IsNullOrEmpty(x.Category) || !excludedFeaturedCategories.Contains(x.Category))
                .OrderBy(x =>
                    x.Category == "Món Chính" || x.Category == "Món chính" ? 1 :
                    x.Category == "Đặc Sản" || x.Category == "Đặc sản" ? 2 :
                    x.Category == "Khai Vị" || x.Category == "Khai vị" ? 3 :
                    x.Category == "Omakase" || x.Category == "Omakase Chay" ? 4 :
                    9
                )
                .ThenByDescending(x => x.CreatedAt)
                .Take(8)
                .ToListAsync();

            ViewBag.FeaturedDishes = featuredDishes;

            return View();
        }
        [HttpGet]
        public async Task<IActionResult> Menu(string? category)
        {
            var baseQuery = _context.Dishes
                .AsNoTracking()
                .Where(x => x.IsAvailable);

            var categories = await baseQuery
                .Where(x => x.Category != null && x.Category != "")
                .Select(x => x.Category)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var dishesQuery = baseQuery;

            if (!string.IsNullOrWhiteSpace(category))
            {
                dishesQuery = dishesQuery.Where(x => x.Category == category);
            }

            var dishes = await dishesQuery
                .OrderBy(x => x.Category)
                .ThenBy(x => x.Name)
                .ToListAsync();

            ViewBag.Categories = categories;
            ViewBag.CurrentCategory = category ?? "";

            return View(dishes);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DatBan()
        {
            // Action cũ, không còn dùng. Giữ lại để tránh lỗi route cũ.
            TempData["ErrorMessage"] = "Vui lòng sử dụng form đặt bàn chính thức của hệ thống.";
            return RedirectToAction(nameof(Index));
        }
        // code random 6 ký tự
        private string GenerateBookingCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var random = new Random();

            var code = new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());

            return "GS-" + code;
        }
        private IActionResult RedirectToBookingForm()
        {
            var url = Url.Action(nameof(Index)) ?? "/";
            return Redirect(url + "#contact");
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitBooking(Booking model, string? preOrderJson)
        {
            DateTime vietnamTimeNow = DateTime.UtcNow.AddHours(7);
            model.SpecialRequest = string.IsNullOrWhiteSpace(model.SpecialRequest) ? "" : model.SpecialRequest.Trim();
            ModelState.Remove(nameof(Booking.SpecialRequest));
            if (model.BookingDate < vietnamTimeNow.AddHours(1))
            {
                TempData["ErrorMessage"] = "Thời gian đặt bàn phải đặt trước ít nhất 1 tiếng!";
                return RedirectToBookingForm();
            }
            var currentUser = User.Identity?.IsAuthenticated == true
                ? await _userManager.GetUserAsync(User)
                : null;

            Booking? unpaidBooking = null;

            if (currentUser != null)
            {
                unpaidBooking = await _context.Bookings
                    .Where(b =>
                        b.AppUserId == currentUser.Id &&
                        b.Status == BookingPending &&
                        b.DepositAmount > 0 &&
                        b.PaymentStatus == PaymentUnpaid)
                    .OrderByDescending(b => b.CreatedAt)
                    .FirstOrDefaultAsync();
            }
            else
            {
                unpaidBooking = await _context.Bookings
                    .Where(b =>
                        b.Status == BookingPending &&
                        b.DepositAmount > 0 &&
                        b.PaymentStatus == PaymentUnpaid &&
                        (b.Email == model.Email || b.PhoneNumber == model.PhoneNumber))
                    .OrderByDescending(b => b.CreatedAt)
                    .FirstOrDefaultAsync();
            }

            if (unpaidBooking != null)
            {
                TempData["ErrorMessage"] = $"Bạn đang có đơn {unpaidBooking.BookingCode} chưa thanh toán cọc. Vui lòng thanh toán hoặc hủy đơn này trước khi đặt đơn mới.";
                return RedirectToAction(nameof(BookingSuccess), new { code = unpaidBooking.BookingCode });
            }
            // SPAM: Cooldown 2 phút
            var recentSpamCheck = _context.Bookings
                .Where(b => b.PhoneNumber == model.PhoneNumber || b.Email == model.Email)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefault();

            if (recentSpamCheck != null && (vietnamTimeNow - recentSpamCheck.CreatedAt).TotalMinutes < 2)
            {
                TempData["ErrorMessage"] = "Bạn thao tác quá nhanh! Vui lòng đợi 2 phút trước khi đặt bàn tiếp theo.";
                return RedirectToBookingForm();
            }

            await _bookingLock.WaitAsync();
            try
            {
                if (ModelState.IsValid)
                {
                    DateTime startWindow = model.BookingDate.AddMinutes(-150);
                    DateTime endWindow = model.BookingDate.AddMinutes(150);

                    int totalGuestsReserved = _context.Bookings
                        .Where(b => (b.Status == 0 || b.Status == 1)
                                    && b.BookingDate > startWindow
                                    && b.BookingDate < endWindow)
                        .Sum(b => (int?)b.NumberOfGuests) ?? 0;

                    if (totalGuestsReserved + model.NumberOfGuests > RestaurantCapacity)
                    {
                        int availableSeats = RestaurantCapacity - totalGuestsReserved;
                        if (availableSeats < 0) availableSeats = 0;

                        TempData["ErrorMessage"] = $"Khung giờ này nhà hàng đã gần kín chỗ. Hiện có {totalGuestsReserved}/100 người, còn {availableSeats} chỗ. Vui lòng chọn mốc giờ khác.";
                        return RedirectToBookingForm();
                    }

                    model.BookingCode = await GenerateUniqueBookingCodeAsync();
                    model.CreatedAt = vietnamTimeNow;
                    model.Status = BookingPending;
                    model.PaymentStatus = PaymentUnpaid;
                    model.IsPointsClaimed = false;
                    model.SpecialRequest = model.SpecialRequest ?? "";

                    // OmakaseTier chỉ có ý nghĩa khi khách thật sự chọn Omakase.
                    // Select Omakase đang nằm trong form nên dù bị ẩn, trình duyệt vẫn gửi giá trị lên server.
                    // Nếu không xóa ở server, Bàn Tiêu Chuẩn/VIP có thể bị hiện nhầm "Gói Omakase".
                    if (!string.Equals(model.BookingType, "Omakase Chay", StringComparison.OrdinalIgnoreCase))
                    {
                        model.OmakaseTier = null;
                    }
                    else if (string.IsNullOrWhiteSpace(model.OmakaseTier))
                    {
                        model.OmakaseTier = "Tĩnh Tại";
                    }

                    // Không tin DepositAmount gửi từ client.
                    // Server tự tính lại tiền cọc theo loại bàn, gói Omakase và số khách.
                    model.DepositAmount = CalculateDepositAmount(
                        model.BookingType,
                        model.OmakaseTier,
                        model.NumberOfGuests
                    );

                    if (model.DepositAmount == 0)
                    {
                        model.PaymentStatus = PaymentPaid;
                    }

                    if (currentUser != null)
                    {
                        model.AppUserId = currentUser.Id;
                    }

                    try
                    {
                        _context.Bookings.Add(model);
                        await _context.SaveChangesAsync();

                        var bookingDetails = await BuildBookingDetailsAsync(preOrderJson, model.Id, model.BookingType);

                        if (bookingDetails.Any())
                        {
                            _context.BookingDetails.AddRange(bookingDetails);
                            await _context.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Lỗi lưu đơn đặt bàn: " + ex.Message);
                        TempData["ErrorMessage"] = "Có lỗi khi lưu đơn đặt bàn. Vui lòng thử lại hoặc liên hệ nhà hàng.";
                        return RedirectToBookingForm();
                    }

                    await SendConfirmationEmail(model);

                    return RedirectToAction("BookingSuccess", new { code = model.BookingCode });
                }
            }
            finally
            {
                _bookingLock.Release();
            }

            var errors = string.Join(" ", ModelState.Values
             .SelectMany(v => v.Errors)
             .Select(e => e.ErrorMessage));

            TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(errors)
                ? "Thông tin đặt bàn chưa hợp lệ. Vui lòng kiểm tra lại các trường bắt buộc."
                : errors;

            return RedirectToBookingForm();
        }

        // =======================================================
        // TRẠM KIỂM SOÁT THANH TOÁN & CHECK-IN
        // =======================================================
        [HttpGet]
        public IActionResult BookingSuccess(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return RedirectToAction("Index");
            }

            // Lấy toàn bộ thông tin đơn hàng từ Database, kèm món khách chọn trước nếu có.
            var booking = _context.Bookings
                .AsNoTracking()
                .Include(b => b.BookingDetails)
                .FirstOrDefault(b => b.BookingCode == code);

            if (booking == null)
            {
                return RedirectToAction("Index");
            }

            // Gửi cả gói dữ liệu booking ra View để giao diện tự quyết định hiển thị
            return View(booking);
        }

        // HÀM GIẢ LẬP THANH TOÁN MOMO DÀNH CHO ĐỒ ÁN
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DemoMoMoPayment(string code, bool acceptCancellationPolicy)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "Mã đơn không hợp lệ.";
                return RedirectToAction(nameof(Index));
            }
            if (!acceptCancellationPolicy)
            {
                TempData["ErrorMessage"] = "Vui lòng đọc và đồng ý với chính sách hủy/hoàn cọc trước khi thanh toán.";
                return RedirectToAction(nameof(BookingSuccess), new { code });
            }
            var booking = _context.Bookings.FirstOrDefault(b => b.BookingCode == code);

            if (booking == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn đặt bàn.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.Status == BookingCancelled)
            {
                TempData["ErrorMessage"] = "Đơn đặt bàn này đã bị hủy, không thể thanh toán.";
                return RedirectToAction(nameof(BookingSuccess), new { code });
            }

            if (booking.PaymentStatus == PaymentPaid)
            {
                return RedirectToAction(nameof(BookingSuccess), new { code });
            }

            if (booking.DepositAmount <= 0)
            {
                TempData["ErrorMessage"] = "Đơn này không yêu cầu thanh toán cọc.";
                return RedirectToAction(nameof(BookingSuccess), new { code });
            }

            booking.PaymentStatus = PaymentPaid;
            _context.SaveChanges();

            return RedirectToAction(nameof(BookingSuccess), new { code });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelMyBooking(string code)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                TempData["ErrorMessage"] = "Bạn cần đăng nhập để hủy đơn đặt bàn.";
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "Mã đơn không hợp lệ.";
                return RedirectToAction(nameof(Index));
            }

            var currentUser = await _userManager.GetUserAsync(User);

            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy tài khoản đăng nhập.";
                return RedirectToAction(nameof(Index));
            }

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.BookingCode == code);

            if (booking == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn đặt bàn.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.AppUserId != currentUser.Id)
            {
                TempData["ErrorMessage"] = "Bạn không có quyền hủy đơn đặt bàn này.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.Status != BookingPending)
            {
                TempData["ErrorMessage"] = "Chỉ có thể hủy đơn đang chờ duyệt hoặc chưa check-in.";
                return RedirectToAction(nameof(BookingSuccess), new { code });
            }

            var vietnamTimeNow = DateTime.UtcNow.AddHours(7);
            var policy = GetCancellationPolicy(booking, vietnamTimeNow);

            if (!policy.CanCancelOnline)
            {
                TempData["ErrorMessage"] = policy.CustomerMessage;
                return RedirectToAction(nameof(BookingSuccess), new { code });
            }

            booking.Status = BookingCancelled;
            await _context.SaveChangesAsync();

            await SendCustomerCancelEmail(
                booking,
                policy.CustomerMessage,
                policy.RefundAmount,
                policy.FeeAmount
            );

            TempData["SuccessMessage"] = $"Đã hủy đơn {booking.BookingCode}. {policy.CustomerMessage}";

            return RedirectToAction(nameof(BookingSuccess), new { code });
        }
        private class PreOrderCartItem
        {
            public int DishId { get; set; }
            public int Quantity { get; set; }
        }

        private async Task<List<BookingDetail>> BuildBookingDetailsAsync(string? preOrderJson, int bookingId, string? bookingType)
        {
            var result = new List<BookingDetail>();

            // Omakase là trải nghiệm do bếp trưởng thiết kế, không nhận chọn món lẻ.
            if (string.Equals(bookingType, "Omakase Chay", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            if (string.IsNullOrWhiteSpace(preOrderJson))
            {
                return result;
            }

            List<PreOrderCartItem>? cartItems;

            try
            {
                cartItems = JsonSerializer.Deserialize<List<PreOrderCartItem>>(
                    preOrderJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch
            {
                return result;
            }

            if (cartItems == null || !cartItems.Any())
            {
                return result;
            }

            var normalizedItems = cartItems
                .Where(x => x.DishId > 0 && x.Quantity > 0)
                .GroupBy(x => x.DishId)
                .Select(g => new PreOrderCartItem
                {
                    DishId = g.Key,
                    Quantity = Math.Min(g.Sum(x => x.Quantity), 20)
                })
                .Take(20)
                .ToList();

            if (!normalizedItems.Any())
            {
                return result;
            }

            var dishIds = normalizedItems.Select(x => x.DishId).ToList();

            var dishes = await _context.Dishes
                .AsNoTracking()
                .Where(x => dishIds.Contains(x.Id) && x.IsAvailable)
                .ToListAsync();

            foreach (var item in normalizedItems)
            {
                var dish = dishes.FirstOrDefault(x => x.Id == item.DishId);

                if (dish == null)
                {
                    continue;
                }

                var quantity = item.Quantity < 1 ? 1 : item.Quantity;
                var lineTotal = dish.Price * quantity;

                result.Add(new BookingDetail
                {
                    BookingId = bookingId,
                    DishId = dish.Id,
                    DishName = dish.Name,
                    UnitPrice = dish.Price,
                    Quantity = quantity,
                    LineTotal = lineTotal
                });
            }

            return result;
        }

        // tính cọc
        private decimal CalculateDepositAmount(string? bookingType, string? omakaseTier, int numberOfGuests)
        {
            if (numberOfGuests < 1)
            {
                numberOfGuests = 1;
            }

            if (bookingType == "Bàn VIP")
            {
                return 1000000;
            }

            if (bookingType == "Omakase Chay")
            {
                var tierPrice = omakaseTier switch
                {
                    "Tĩnh Tại" => 1500000m,
                    "Tinh Hoa" => 2500000m,
                    "Nghệ Nhân" => 4000000m,
                    _ => 1500000m
                };

                return tierPrice * numberOfGuests * 0.5m;
            }

            return 0;
        }

        private async Task<string> GenerateUniqueBookingCodeAsync()
        {
            string code;

            do
            {
                code = GenerateBookingCode();
            }
            while (await _context.Bookings.AnyAsync(b => b.BookingCode == code));

            return code;
        }
        // GỬI MAIL
        private async Task SendConfirmationEmail(Booking booking)
        {
            try
            {
                var senderEmail = _config["EmailSettings:SenderEmail"];
                var senderPassword = _config["EmailSettings:SenderPassword"];

                if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(senderPassword))
                {
                    return;
                }

                var emailMessage = new MimeMessage();
                emailMessage.From.Add(new MailboxAddress("SenVie Restaurant", senderEmail));
                emailMessage.To.Add(new MailboxAddress("Quý Khách", booking.Email));
                emailMessage.ReplyTo.Add(new MailboxAddress("No Reply", "noreply@senvierestaurant.com"));

                var paymentUrl = Url.Action(
                    nameof(BookingSuccess),
                    "Home",
                    new { code = booking.BookingCode },
                    Request.Scheme
                );

                var qrImageUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=250x250&data={booking.BookingCode}";

                string paymentOrQrBlock;

                if (booking.DepositAmount > 0 && booking.PaymentStatus == PaymentUnpaid)
                {
                    emailMessage.Subject = "Yêu cầu thanh toán cọc đặt bàn - SenVie";

                    paymentOrQrBlock = $@"
                <div style='background:#fff8e1; padding:15px; border-radius:8px; border-left:4px solid #ffc107; margin-top:15px;'>
                    <h3 style='margin-top:0; color:#8a5a00;'>Đơn của bạn cần thanh toán cọc</h3>
                    <p>Số tiền cọc cần thanh toán: <strong style='color:#d9534f'>{booking.DepositAmount:N0}đ</strong></p>
                    <p>Vui lòng hoàn tất thanh toán cọc để nhận mã QR check-in.</p>
                    <p>
                        <a href='{paymentUrl}' style='display:inline-block; background:#a50064; color:white; padding:12px 20px; border-radius:25px; text-decoration:none; font-weight:bold;'>
                            Đi đến trang thanh toán
                        </a>
                    </p>
                </div>

                <div style='background:#f8f9fa; padding:15px; border-radius:8px; margin-top:15px;'>
                    <h3 style='margin-top:0;'>Chính sách hủy / hoàn cọc</h3>
                    <ul>
                        <li><strong>Bàn VIP:</strong> Hủy trước 24 tiếng được hỗ trợ hoàn/chuyển 100% cọc. Hủy dưới 24 tiếng không hoàn cọc.</li>
                        <li><strong>Omakase Chay:</strong> Hủy trước 48 tiếng hoàn/chuyển 100%; từ 24 đến dưới 48 tiếng hoàn/chuyển 50%; dưới 24 tiếng không hoàn cọc.</li>
                        <li>Việc hoàn/chuyển cọc được nhà hàng liên hệ và xử lý thủ công.</li>
                    </ul>
                </div>";
                }
                else
                {
                    emailMessage.Subject = "Xác nhận đặt bàn & mã QR check-in - SenVie";

                    paymentOrQrBlock = $@"
                <p>Vui lòng đưa <strong>Mã QR</strong> dưới đây cho lễ tân quét khi bạn đến nhà hàng:</p>
                <img src='{qrImageUrl}' alt='Mã QR Check-in của bạn' style='border: 2px solid #333; padding: 5px; border-radius: 10px;' />";
                }

                emailMessage.Body = new BodyBuilder
                {
                    HtmlBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; border: 1px solid #7DBA4E; border-radius: 10px;'>
                    <h2 style='color: #7DBA4E;'>CẢM ƠN BẠN ĐÃ ĐẶT BÀN!</h2>

                    <p>Kính chào <strong>{booking.CustomerName}</strong>,</p>
                    <p>Nhà hàng chay SenVie đã nhận được yêu cầu giữ chỗ của bạn.</p>

                    <ul>
                        <li><strong>Mã đơn hàng:</strong> <span style='color: red; font-size: 18px;'>{booking.BookingCode}</span></li>
                        <li><strong>Loại bàn:</strong> {booking.BookingType}</li>
                        <li><strong>Thời gian đến:</strong> {booking.BookingDate:dd/MM/yyyy HH:mm}</li>
                        <li><strong>Số khách:</strong> {booking.NumberOfGuests}</li>
                        <li><strong>Tiền cọc:</strong> {booking.DepositAmount:N0}đ</li>
                    </ul>

                    {paymentOrQrBlock}

                    <br/>
                    <p><i>Hẹn gặp lại bạn tại SenVie!</i></p>
                    <p style='color: #888; font-size: 12px; margin-top: 20px;'>* Vui lòng không trả lời email này vì đây là hộp thư tự động.</p>
                </div>"
                }.ToMessageBody();

                using var smtp = new MailKit.Net.Smtp.SmtpClient();

                smtp.ServerCertificateValidationCallback = (s, c, h, e) => true;
                await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(senderEmail, senderPassword);
                await smtp.SendAsync(emailMessage);
                await smtp.DisconnectAsync(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi gửi mail hệ thống: " + ex.Message);
            }
        }
        private async Task SendCustomerCancelEmail(
            Booking booking,
            string policyMessage,
            decimal refundAmount,
            decimal feeAmount)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(booking.Email))
                {
                    return;
                }

                var senderEmail = _config["EmailSettings:SenderEmail"];
                var senderPassword = _config["EmailSettings:SenderPassword"];

                if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(senderPassword))
                {
                    return;
                }

                var emailMessage = new MimeMessage();
                emailMessage.From.Add(new MailboxAddress("SenVie Restaurant", senderEmail));
                emailMessage.To.Add(new MailboxAddress(booking.CustomerName, booking.Email));
                emailMessage.Subject = "Xác nhận hủy đơn đặt bàn - SenVie";

                emailMessage.Body = new BodyBuilder
                {
                    HtmlBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; border: 1px solid #dc3545; border-radius: 10px;'>
                    <h2 style='color: #dc3545;'>XÁC NHẬN HỦY ĐƠN ĐẶT BÀN</h2>

                    <p>Kính chào <strong>{booking.CustomerName}</strong>,</p>

                    <p>SenVie xác nhận đơn đặt bàn mã 
                        <strong style='color:red'>{booking.BookingCode}</strong> 
                        đã được hủy theo yêu cầu của bạn.
                    </p>

                    <ul>
                        <li><strong>Thời gian đặt bàn:</strong> {booking.BookingDate:dd/MM/yyyy HH:mm}</li>
                        <li><strong>Loại bàn:</strong> {booking.BookingType}</li>
                        <li><strong>Số khách:</strong> {booking.NumberOfGuests}</li>
                        <li><strong>Tiền cọc đã ghi nhận:</strong> {booking.DepositAmount:N0}đ</li>
                        <li><strong>Số tiền dự kiến hoàn/chuyển:</strong> {refundAmount:N0}đ</li>
                        <li><strong>Phí giữ chỗ/hủy:</strong> {feeAmount:N0}đ</li>
                    </ul>

                    <div style='background:#fff8e1; padding:15px; border-radius:8px; border-left:4px solid #ffc107;'>
                        <strong>Chính sách xử lý cọc:</strong>
                        <p style='margin-bottom:0;'>{policyMessage}</p>
                    </div>

                    <p style='margin-top:20px;'>
                        Lưu ý: Do đây là hệ thống thanh toán mô phỏng, việc hoàn/chuyển cọc sẽ được nhà hàng liên hệ và xử lý thủ công.
                    </p>

                    <p>Nếu cần hỗ trợ thêm, vui lòng liên hệ nhà hàng hoặc chat trực tiếp trên website.</p>

                    <br/>
                    <p><i>Trân trọng,<br/>SenVie Restaurant</i></p>
                </div>"
                }.ToMessageBody();

                using var smtp = new MailKit.Net.Smtp.SmtpClient();

                smtp.ServerCertificateValidationCallback = (s, c, h, e) => true;
                await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(senderEmail, senderPassword);
                await smtp.SendAsync(emailMessage);
                await smtp.DisconnectAsync(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi gửi email hủy đơn cho khách: " + ex.Message);
            }
        }
        private (bool CanCancelOnline, decimal RefundAmount, decimal FeeAmount, string CustomerMessage)
    GetCancellationPolicy(Booking booking, DateTime vietnamTimeNow)
        {
            var hoursBeforeBooking = (booking.BookingDate - vietnamTimeNow).TotalHours;
            var depositAmount = booking.DepositAmount;

            if (hoursBeforeBooking <= 1)
            {
                return (
                    false,
                    0,
                    depositAmount,
                    "Đơn sắp đến giờ phục vụ, vui lòng liên hệ trực tiếp nhà hàng để được hỗ trợ hủy."
                );
            }

            if (depositAmount <= 0 || booking.PaymentStatus == PaymentUnpaid)
            {
                return (
                    true,
                    0,
                    0,
                    "Đơn chưa phát sinh thanh toán cọc nên không có chi phí hủy."
                );
            }

            if (booking.BookingType == "Omakase Chay")
            {
                if (hoursBeforeBooking >= 48)
                {
                    return (
                        true,
                        depositAmount,
                        0,
                        $"Bạn hủy trước 48 tiếng nên được hỗ trợ hoàn hoặc chuyển 100% tiền cọc ({depositAmount:N0}đ). Nhà hàng sẽ liên hệ để xử lý."
                    );
                }

                if (hoursBeforeBooking >= 24)
                {
                    var refundAmount = depositAmount * 0.5m;
                    var feeAmount = depositAmount - refundAmount;

                    return (
                        true,
                        refundAmount,
                        feeAmount,
                        $"Bạn hủy trong khoảng 24-48 tiếng trước giờ phục vụ Omakase. Nhà hàng hỗ trợ hoàn/chuyển 50% tiền cọc ({refundAmount:N0}đ), phần phí giữ chỗ là {feeAmount:N0}đ."
                    );
                }

                return (
                    true,
                    0,
                    depositAmount,
                    $"Bạn hủy dưới 24 tiếng trước giờ phục vụ Omakase. Tiền cọc {depositAmount:N0}đ không được hoàn do nhà hàng đã chuẩn bị nguyên liệu và suất phục vụ riêng."
                );
            }

            if (booking.BookingType == "Bàn VIP")
            {
                if (hoursBeforeBooking >= 24)
                {
                    return (
                        true,
                        depositAmount,
                        0,
                        $"Bạn hủy trước 24 tiếng nên được hỗ trợ hoàn hoặc chuyển 100% tiền cọc ({depositAmount:N0}đ). Nhà hàng sẽ liên hệ để xử lý."
                    );
                }

                return (
                    true,
                    0,
                    depositAmount,
                    $"Bạn hủy dưới 24 tiếng trước giờ phục vụ. Tiền cọc {depositAmount:N0}đ không được hoàn do nhà hàng đã giữ phòng riêng và chuẩn bị dịch vụ."
                );
            }

            return (
                true,
                depositAmount,
                0,
                "Đơn đã được hủy. Nếu có phát sinh tiền cọc, nhà hàng sẽ liên hệ để xử lý."
            );
        }
    }
}