using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Filters;
using MimeKit;
using nhahangchay.Data;
using nhahangchay.Models;
using nhahangchay.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace nhahangchay.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private const int BookingPending = 0;
        private const int BookingCheckedIn = 1;
        private const int BookingCancelled = 2;

        private const int PaymentUnpaid = 0;
        private const int PaymentPaid = 1;

        private const int MaxDishImageSize = 5 * 1024 * 1024;

        private static readonly string[] AllowedDishImageTypes =
        {
            "image/jpeg",
            "image/png",
            "image/webp",
            "image/jpg"
        };

        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly CloudinaryService _cloudinaryService;

        public AdminController(
            ApplicationDbContext context,
            IConfiguration config,
            CloudinaryService cloudinaryService)
        {
            _context = context;
            _config = config;
            _cloudinaryService = cloudinaryService;
        }
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var currentEmail = User.Identity?.Name;

            if (!string.Equals(currentEmail, "admin@uef.com", StringComparison.OrdinalIgnoreCase))
            {
                context.Result = RedirectToAction("Index", "Home");
                return;
            }

            base.OnActionExecuting(context);
        }
        // =======================================================
        // BOOKING MANAGEMENT
        // =======================================================

        [HttpGet]
        public async Task<IActionResult> Index(
            string? searchString,
            int? statusFilter,
            string? dateFilter,
            DateTime? fromDate,
            DateTime? toDate,
            string? quickFilter,
            int page = 1,
            int pageSize = 10)
        {
            if (page < 1) page = 1;

            var allowedPageSizes = new[] { 10, 25, 50 };
            if (!allowedPageSizes.Contains(pageSize))
            {
                pageSize = 10;
            }

            if (string.IsNullOrWhiteSpace(quickFilter))
            {
                // Mặc định ưu tiên đơn đang cần nhân viên xử lý.
                quickFilter = "pending";
            }

            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            ViewBag.TodayCount = await _context.Bookings.CountAsync(b =>
                b.BookingDate >= today && b.BookingDate < tomorrow);

            ViewBag.PendingCount = await _context.Bookings.CountAsync(b => b.Status == BookingPending);
            ViewBag.CheckedInCount = await _context.Bookings.CountAsync(b => b.Status == BookingCheckedIn);
            ViewBag.CancelledCount = await _context.Bookings.CountAsync(b => b.Status == BookingCancelled);
            ViewBag.DepositDebtCount = await _context.Bookings.CountAsync(b =>
                b.DepositAmount > 0 && b.PaymentStatus == PaymentUnpaid && b.Status == BookingPending);
            ViewBag.TodayDepositCollected = await _context.Bookings
                .Where(b =>
                    b.DepositAmount > 0 &&
                    b.PaymentStatus == PaymentPaid &&
                    b.BookingDate >= today &&
                    b.BookingDate < tomorrow)
                .SumAsync(b => (decimal?)b.DepositAmount) ?? 0;

            ViewBag.PendingDepositTotal = await _context.Bookings
                .Where(b =>
                    b.DepositAmount > 0 &&
                    b.PaymentStatus == PaymentUnpaid &&
                    b.Status == BookingPending)
                .SumAsync(b => (decimal?)b.DepositAmount) ?? 0;
            var bookings = _context.Bookings.AsNoTracking().AsQueryable();

            bookings = ApplyQuickFilter(bookings, quickFilter, today);

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var keyword = searchString.Trim();

                bookings = bookings.Where(b =>
                    b.CustomerName.Contains(keyword) ||
                    b.PhoneNumber.Contains(keyword) ||
                    (b.BookingCode != null && b.BookingCode.Contains(keyword)));
            }

            if (statusFilter.HasValue)
            {
                bookings = bookings.Where(b => b.Status == statusFilter.Value);
            }

            if (dateFilter == "today")
            {
                bookings = bookings.Where(b => b.BookingDate >= today && b.BookingDate < today.AddDays(1));
            }
            else if (dateFilter == "tomorrow")
            {
                var start = today.AddDays(1);
                var end = today.AddDays(2);

                bookings = bookings.Where(b => b.BookingDate >= start && b.BookingDate < end);
            }
            else if (dateFilter == "week")
            {
                var end = today.AddDays(7);

                bookings = bookings.Where(b => b.BookingDate >= today && b.BookingDate < end);
            }
            else if (dateFilter == "range")
            {
                if (fromDate.HasValue)
                {
                    var start = fromDate.Value.Date;
                    bookings = bookings.Where(b => b.BookingDate >= start);
                }

                if (toDate.HasValue)
                {
                    var end = toDate.Value.Date.AddDays(1);
                    bookings = bookings.Where(b => b.BookingDate < end);
                }
            }

            var totalItems = await bookings.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            if (totalPages > 0 && page > totalPages)
            {
                page = totalPages;
            }

            var result = await bookings
                .OrderByDescending(b => b.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentFilter = statusFilter;
            ViewBag.CurrentDateFilter = dateFilter;
            ViewBag.CurrentFromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentQuickFilter = quickFilter;

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = totalItems;
            ViewBag.TotalPages = totalPages;

            return View(result);
        }

        [HttpGet]
        public IActionResult CheckIn()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ProcessCheckIn(string bookingCode)
        {
            var normalizedCode = NormalizeBookingCode(bookingCode);

            if (string.IsNullOrEmpty(normalizedCode))
            {
                TempData["Error"] = "Vui lòng quét QR hoặc nhập mã code!";
                return RedirectToAction(nameof(CheckIn));
            }

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.BookingCode != null && b.BookingCode.ToUpper() == normalizedCode);

            if (booking == null)
            {
                TempData["Error"] = $"Không tìm thấy đơn đặt bàn nào với mã '{normalizedCode}'!";
                return RedirectToAction(nameof(CheckIn));
            }


            if (booking.Status == BookingCancelled)
            {
                TempData["Warning"] = $"Đơn {booking.BookingCode} đã bị hủy, không thể check-in.";
                return RedirectToAction(nameof(CheckIn));
            }

            if (booking.Status == BookingCheckedIn)
            {
                TempData["Warning"] = $"Đơn của khách {booking.CustomerName} đã được check-in trước đó rồi!";
                return RedirectToAction(nameof(CheckIn));
            }

            if (booking.DepositAmount > 0 && booking.PaymentStatus == PaymentUnpaid)
            {
                TempData["Warning"] = $"Đơn {booking.BookingCode} còn nợ cọc {booking.DepositAmount:N0}đ. Vui lòng xác nhận thu cọc trước khi check-in.";
                return RedirectToAction(nameof(CheckIn));
            }

            booking.Status = BookingCheckedIn;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Check-in thành công! Khách hàng: {booking.CustomerName} - Số khách: {booking.NumberOfGuests}";
            return RedirectToAction(nameof(CheckIn));
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var booking = await _context.Bookings
                .AsNoTracking()
                .Include(b => b.BookingDetails)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
            {
                return NotFound();
            }

            return View(booking);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DirectCheckIn(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);

            if (booking == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn đặt bàn.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.Status == BookingCancelled)
            {
                TempData["ErrorMessage"] = $"Đơn {booking.BookingCode} đã bị hủy, không thể check-in.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.Status == BookingCheckedIn)
            {
                TempData["ErrorMessage"] = $"Đơn {booking.BookingCode} đã được check-in trước đó.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.DepositAmount > 0 && booking.PaymentStatus == PaymentUnpaid)
            {
                booking.PaymentStatus = PaymentPaid;
                TempData["SuccessMessage"] = $"Đã check-in và xác nhận thu cọc tại quầy cho đơn {booking.BookingCode}.";
            }
            else
            {
                TempData["SuccessMessage"] = $"Đã check-in thành công cho đơn {booking.BookingCode}!";
            }

            booking.Status = BookingCheckedIn;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveDeposit(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);

            if (booking == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng!";
                return RedirectToAction(nameof(Index));
            }

            if (booking.DepositAmount <= 0)
            {
                TempData["ErrorMessage"] = "Đơn hàng này không yêu cầu đặt cọc.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.PaymentStatus == PaymentPaid)
            {
                TempData["ErrorMessage"] = "Đơn hàng này đã được xác nhận thanh toán cọc rồi.";
                return RedirectToAction(nameof(Index));
            }

            booking.PaymentStatus = PaymentPaid;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã xác nhận thu cọc {booking.DepositAmount:N0}đ cho đơn {booking.BookingCode}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> DeleteBooking(int id)
        {
            // Giữ action cũ để không vỡ route cũ. Logic thực tế là hủy đơn, không xóa cứng.
            return CancelBooking(id);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelBooking(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);

            if (booking == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn đặt bàn cần hủy.";
                return RedirectToAction(nameof(Index));
            }

            if (booking.Status != BookingPending)
            {
                TempData["ErrorMessage"] = $"Chỉ có thể hủy đơn đang chờ duyệt. Đơn {booking.BookingCode} không thể hủy ở trạng thái hiện tại.";
                return RedirectToAction(nameof(Index));
            }

            booking.Status = BookingCancelled;

            if (!string.IsNullOrWhiteSpace(booking.Email))
            {
                await SendCancellationEmail(booking.Email, booking.CustomerName, booking.BookingCode ?? "");
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã hủy đơn {booking.BookingCode} và gửi email báo cho khách!";
            return RedirectToAction(nameof(Index));
        }

        // =======================================================
        // CHAT
        // =======================================================

        [HttpGet]
        public IActionResult ChatManager()
        {
            return View();
        }

        // =======================================================
        // DISH MANAGEMENT
        // =======================================================

        [HttpGet]
        public IActionResult AddDish()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDish(Dish model, IFormFile? imageFile)
        {
            ValidateDishImage(imageFile, required: true);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!await UploadDishImageToModel(model, imageFile!))
            {
                return View(model);
            }

            model.CreatedAt = DateTime.Now;
            model.IsAvailable = false;

            _context.Dishes.Add(model);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã thêm món '{model.Name}' vào thực đơn thành công!";
            return RedirectToAction(nameof(Dishes));
        }

        [HttpGet]
        public async Task<IActionResult> Dishes()
        {
            var dishes = await _context.Dishes
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            return View(dishes);
        }

        [HttpGet]
        public async Task<IActionResult> MenuPreview()
        {
            var dishes = await _context.Dishes
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            return View(dishes);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDishAvailability(int id)
        {
            var dish = await _context.Dishes.FindAsync(id);

            if (dish == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy món ăn.";
                return RedirectToAction(nameof(Dishes));
            }

            dish.IsAvailable = !dish.IsAvailable;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = dish.IsAvailable
                ? $"Món '{dish.Name}' đã được bật hiển thị trên website."
                : $"Món '{dish.Name}' đã được ẩn khỏi website.";

            return RedirectToAction(nameof(Dishes));
        }

        [HttpGet]
        public async Task<IActionResult> EditDish(int id)
        {
            var dish = await _context.Dishes.FindAsync(id);

            if (dish == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy món ăn cần sửa.";
                return RedirectToAction(nameof(Dishes));
            }

            return View(dish);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDish(int id, Dish model, IFormFile? imageFile)
        {
            if (id != model.Id)
            {
                TempData["ErrorMessage"] = "Dữ liệu món ăn không hợp lệ.";
                return RedirectToAction(nameof(Dishes));
            }

            var dish = await _context.Dishes.FindAsync(id);

            if (dish == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy món ăn cần sửa.";
                return RedirectToAction(nameof(Dishes));
            }

            ValidateDishImage(imageFile, required: false);

            if (!ModelState.IsValid)
            {
                model.ImageUrl = dish.ImageUrl;
                model.CloudinaryPublicId = dish.CloudinaryPublicId;
                return View(model);
            }

            dish.Name = model.Name;
            dish.Category = model.Category;
            dish.Price = model.Price;
            dish.Description = model.Description;
            dish.IsAvailable = model.IsAvailable;

            if (imageFile != null && imageFile.Length > 0)
            {
                var oldPublicId = dish.CloudinaryPublicId;

                if (!await UploadDishImageToModel(dish, imageFile))
                {
                    model.ImageUrl = dish.ImageUrl;
                    model.CloudinaryPublicId = dish.CloudinaryPublicId;
                    return View(model);
                }

                await TryDeleteCloudinaryImageAsync(oldPublicId);
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã cập nhật món '{dish.Name}' thành công.";
            return RedirectToAction(nameof(Dishes));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDish(int id)
        {
            var dish = await _context.Dishes.FindAsync(id);

            if (dish == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy món ăn cần xóa.";
                return RedirectToAction(nameof(Dishes));
            }

            var hasBookingDetails = await _context.BookingDetails
                .AnyAsync(x => x.DishId == id);

            if (hasBookingDetails)
            {
                dish.IsAvailable = false;

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] =
                    $"Món \"{dish.Name}\" đã từng được khách chọn trong đơn đặt bàn nên không thể xóa khỏi lịch sử. Hệ thống đã ẩn món này khỏi menu khách hàng.";

                return RedirectToAction(nameof(Dishes));
            }

            if (!string.IsNullOrWhiteSpace(dish.CloudinaryPublicId))
            {
                await TryDeleteCloudinaryImageAsync(dish.CloudinaryPublicId);
            }

            _context.Dishes.Remove(dish);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã xóa món \"{dish.Name}\" khỏi hệ thống.";

            return RedirectToAction(nameof(Dishes));
        }

        // =======================================================
        // HELPERS
        // =======================================================

        private IQueryable<Booking> ApplyQuickFilter(IQueryable<Booking> bookings, string quickFilter, DateTime today)
        {
            return quickFilter switch
            {
                "pending" => bookings.Where(b => b.Status == BookingPending),
                "today" => bookings.Where(b => b.BookingDate >= today && b.BookingDate < today.AddDays(1)),
                "debt" => bookings.Where(b => b.DepositAmount > 0 && b.PaymentStatus == PaymentUnpaid && b.Status == BookingPending),
                "checkedin" => bookings.Where(b => b.Status == BookingCheckedIn),
                "cancelled" => bookings.Where(b => b.Status == BookingCancelled),
                "all" => bookings,
                _ => bookings.Where(b => b.Status == BookingPending)
            };
        }

        private void ValidateDishImage(IFormFile? imageFile, bool required)
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                if (required)
                {
                    ModelState.AddModelError("ImageUrl", "Vui lòng chọn ảnh món ăn.");
                }

                return;
            }

            if (!AllowedDishImageTypes.Contains(imageFile.ContentType.ToLower()))
            {
                ModelState.AddModelError("ImageUrl", "Chỉ cho phép upload ảnh JPG, PNG hoặc WEBP.");
            }

            if (imageFile.Length > MaxDishImageSize)
            {
                ModelState.AddModelError("ImageUrl", "Ảnh không được vượt quá 5MB.");
            }
        }

        private async Task<bool> UploadDishImageToModel(Dish dish, IFormFile imageFile)
        {
            var uploadResult = await _cloudinaryService.UploadDishImageAsync(imageFile);

            if (uploadResult.Error != null)
            {
                ModelState.AddModelError("ImageUrl", "Upload ảnh thất bại: " + uploadResult.Error.Message);
                return false;
            }

            dish.ImageUrl = uploadResult.SecureUrl.ToString();
            dish.CloudinaryPublicId = uploadResult.PublicId;

            return true;
        }

        private async Task TryDeleteCloudinaryImageAsync(string? publicId)
        {
            if (string.IsNullOrWhiteSpace(publicId))
            {
                return;
            }

            try
            {
                await _cloudinaryService.DeleteImageAsync(publicId);
            }
            catch
            {
                // Không chặn thao tác admin nếu Cloudinary lỗi khi xóa ảnh cũ.
            }
        }

        private string NormalizeBookingCode(string rawCode)
        {
            if (string.IsNullOrWhiteSpace(rawCode))
            {
                return "";
            }

            var value = rawCode.Trim();

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                var query = uri.Query.TrimStart('?');

                if (!string.IsNullOrEmpty(query))
                {
                    var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var pair in pairs)
                    {
                        var parts = pair.Split('=', 2);

                        if (parts.Length != 2)
                        {
                            continue;
                        }

                        var key = Uri.UnescapeDataString(parts[0]);
                        var val = Uri.UnescapeDataString(parts[1]);

                        if (key.Equals("bookingCode", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("code", StringComparison.OrdinalIgnoreCase))
                        {
                            value = val;
                            break;
                        }
                    }
                }

                if (value == rawCode.Trim())
                {
                    value = uri.Segments.LastOrDefault()?.Trim('/') ?? value;
                }
            }

            var index = value.IndexOf("GS-", StringComparison.OrdinalIgnoreCase);

            if (index >= 0)
            {
                value = value.Substring(index);
            }

            value = value
                .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? value;

            return value.Trim().ToUpperInvariant();
        }

        private async Task SendCancellationEmail(string toEmail, string customerName, string bookingCode)
        {
            try
            {
                var senderEmail = _config["EmailSettings:SenderEmail"];
                var senderPassword = _config["EmailSettings:SenderPassword"];

                if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(senderPassword))
                {
                    Console.WriteLine("Thiếu cấu hình EmailSettings.");
                    return;
                }

                var emailMessage = new MimeMessage();
                emailMessage.From.Add(new MailboxAddress("SenVie Restaurant", senderEmail));
                emailMessage.To.Add(new MailboxAddress("Quý Khách", toEmail));
                emailMessage.ReplyTo.Add(new MailboxAddress("No Reply", "noreply@senvierstaurant.com"));
                emailMessage.Subject = "THÔNG BÁO HỦY ĐƠN ĐẶT BÀN - SenVie";

                emailMessage.Body = new BodyBuilder
                {
                    HtmlBody = $@"
                        <div style='font-family: Arial, sans-serif; padding: 20px; border: 1px solid #dc3545; border-radius: 10px;'>
                            <h2 style='color: #dc3545;'>THÔNG BÁO HỦY ĐƠN ĐẶT BÀN</h2>
                            <p>Kính chào <strong>{customerName}</strong>,</p>
                            <p>Nhà hàng chay SenVie rất tiếc phải thông báo: Đơn đặt bàn có mã <strong style='color: red;'>{bookingCode}</strong> của bạn đã bị hủy bởi Quản trị viên hệ thống.</p>
                            <p>Nếu bạn có bất kỳ thắc mắc nào hoặc muốn đặt lại bàn, vui lòng liên hệ Hotline: <strong>0123.456.789</strong> hoặc chat trực tiếp với Quản lý tại website.</p>
                            <p style='color: #888; font-size: 12px; margin-top: 20px;'>* Vui lòng không trả lời email này vì đây là hộp thư tự động.</p>
                            <br/>
                            <p><i>Trân trọng,<br/>Ban Quản lý SenVie Restaurant</i></p>
                        </div>"
                }.ToMessageBody();

                using var smtp = new MailKit.Net.Smtp.SmtpClient();

                await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(senderEmail, senderPassword);
                await smtp.SendAsync(emailMessage);
                await smtp.DisconnectAsync(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi gửi mail hủy: " + ex.Message);
            }
        }
    }
}