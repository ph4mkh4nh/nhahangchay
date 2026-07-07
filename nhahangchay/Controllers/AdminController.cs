using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using nhahangchay.Data;
using nhahangchay.Models;
using nhahangchay.Services;
using System;
using System.Linq;
using System.Threading;
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

        private static readonly SemaphoreSlim DishCreateLock = new(1, 1);

        private readonly ApplicationDbContext _context;
        private readonly CloudinaryService _cloudinaryService;
        private readonly EmailService _emailService;
        private readonly UserManager<AppUser> _userManager;
        public AdminController(
                ApplicationDbContext context,
                CloudinaryService cloudinaryService,
                EmailService emailService,
                UserManager<AppUser> userManager)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _emailService = emailService;
            _userManager = userManager;
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
            if (page < 1)
            {
                page = 1;
            }

            var allowedPageSizes = new[] { 10, 25, 50 };

            if (!allowedPageSizes.Contains(pageSize))
            {
                pageSize = 10;
            }

            if (string.IsNullOrWhiteSpace(quickFilter))
            {
                quickFilter = "pending";
            }

            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            ViewBag.TodayCount = await _context.Bookings.CountAsync(b =>
                b.BookingDate >= today && b.BookingDate < tomorrow);

            ViewBag.PendingCount = await _context.Bookings.CountAsync(b =>
                b.Status == BookingPending);

            ViewBag.CheckedInCount = await _context.Bookings.CountAsync(b =>
                b.Status == BookingCheckedIn);

            ViewBag.CancelledCount = await _context.Bookings.CountAsync(b =>
                b.Status == BookingCancelled);

            ViewBag.DepositDebtCount = await _context.Bookings.CountAsync(b =>
                b.DepositAmount > 0 &&
                b.PaymentStatus == PaymentUnpaid &&
                b.Status == BookingPending);

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

            bookings = ApplyDateFilter(bookings, dateFilter, fromDate, toDate, today);

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
        public async Task<IActionResult> ProcessCheckIn(string? bookingCode)
        {
            var normalizedCode = NormalizeBookingCode(bookingCode);

            if (string.IsNullOrEmpty(normalizedCode))
            {
                TempData["Error"] = "Vui lòng quét QR hoặc nhập mã code!";
                return RedirectToAction(nameof(CheckIn));
            }

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b =>
                    b.BookingCode != null &&
                    b.BookingCode.ToUpper() == normalizedCode);

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
            await _context.SaveChangesAsync();

            var emailSent = await _emailService.SendAdminCancelledBookingEmailAsync(booking);

            TempData["SuccessMessage"] = emailSent
                ? $"Đã hủy đơn {booking.BookingCode} và gửi email báo cho khách."
                : $"Đã hủy đơn {booking.BookingCode}, nhưng chưa gửi được email báo cho khách.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult ChatManager()
        {
            return View();
        }
        [HttpGet]
        public async Task<IActionResult> Statistics(string? range, DateTime? fromDate, DateTime? toDate)
        {
            range = string.IsNullOrWhiteSpace(range) ? "month" : range.Trim().ToLower();

            var today = DateTime.UtcNow.AddHours(7).Date;

            DateTime startDate;
            DateTime endDate;

            switch (range)
            {
                case "today":
                    startDate = today;
                    endDate = today;
                    break;

                case "7days":
                    startDate = today.AddDays(-6);
                    endDate = today;
                    break;

                case "custom":
                    startDate = fromDate?.Date ?? today.AddDays(-6);
                    endDate = toDate?.Date ?? today;

                    if (endDate < startDate)
                    {
                        var temp = startDate;
                        startDate = endDate;
                        endDate = temp;
                    }
                    break;

                case "month":
                default:
                    range = "month";
                    startDate = new DateTime(today.Year, today.Month, 1);
                    endDate = startDate.AddMonths(1).AddDays(-1);
                    break;
            }

            var queryEndDate = endDate.AddDays(1);

            var bookings = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.CreatedAt >= startDate && b.CreatedAt < queryEndDate)
                .ToListAsync();

            var bookingIds = bookings.Select(b => b.Id).ToList();

            var estimatedPreOrderTotal = 0m;

            if (bookingIds.Count > 0)
            {
                estimatedPreOrderTotal = await _context.BookingDetails
                    .AsNoTracking()
                    .Where(d => bookingIds.Contains(d.BookingId))
                    .SumAsync(d => (decimal?)d.LineTotal) ?? 0;
            }

            var viewModel = new AdminStatisticsViewModel
            {
                Range = range,
                FromDate = startDate,
                ToDate = endDate,

                TotalBookings = bookings.Count,
                PendingBookings = bookings.Count(b => b.Status == BookingPending),
                CheckedInBookings = bookings.Count(b => b.Status == BookingCheckedIn),
                CancelledBookings = bookings.Count(b => b.Status == BookingCancelled),

                DepositCollected = bookings
                    .Where(b => b.DepositAmount > 0 && b.PaymentStatus == PaymentPaid)
                    .Sum(b => b.DepositAmount),

                PendingDeposit = bookings
                    .Where(b => b.DepositAmount > 0 &&
                                b.PaymentStatus == PaymentUnpaid &&
                                b.Status == BookingPending)
                    .Sum(b => b.DepositAmount),

                EstimatedPreOrderTotal = estimatedPreOrderTotal,

                StandardBookings = bookings.Count(b =>
                    b.BookingType != null &&
                    b.BookingType.Contains("Tiêu Chuẩn", StringComparison.OrdinalIgnoreCase)),

                VipBookings = bookings.Count(b =>
                    b.BookingType != null &&
                    b.BookingType.Contains("VIP", StringComparison.OrdinalIgnoreCase)),

                OmakaseBookings = bookings.Count(b =>
                    b.BookingType != null &&
                    b.BookingType.Contains("Omakase", StringComparison.OrdinalIgnoreCase))
            };

            viewModel.DailyStats = bookings
                .GroupBy(b => b.CreatedAt.Date)
                .Select(g => new AdminDailyStatisticItem
                {
                    Day = g.Key,
                    Total = g.Count(),
                    Pending = g.Count(b => b.Status == BookingPending),
                    CheckedIn = g.Count(b => b.Status == BookingCheckedIn),
                    Cancelled = g.Count(b => b.Status == BookingCancelled),
                    DepositCollected = g
                        .Where(b => b.DepositAmount > 0 && b.PaymentStatus == PaymentPaid)
                        .Sum(b => b.DepositAmount)
                })
                .OrderByDescending(x => x.Day)
                .ToList();

            viewModel.MonthlyStats = bookings
                .GroupBy(b => new DateTime(b.CreatedAt.Year, b.CreatedAt.Month, 1))
                .Select(g => new AdminMonthlyStatisticItem
                {
                    Month = g.Key,
                    Total = g.Count(),
                    Pending = g.Count(b => b.Status == BookingPending),
                    CheckedIn = g.Count(b => b.Status == BookingCheckedIn),
                    Cancelled = g.Count(b => b.Status == BookingCancelled),
                    DepositCollected = g
                        .Where(b => b.DepositAmount > 0 && b.PaymentStatus == PaymentPaid)
                        .Sum(b => b.DepositAmount)
                })
                .OrderByDescending(x => x.Month)
                .ToList();

            viewModel.BookingTypeStats = bookings
                .GroupBy(b => string.IsNullOrWhiteSpace(b.BookingType) ? "Không xác định" : b.BookingType)
                .Select(g => new AdminBookingTypeStatisticItem
                {
                    BookingType = g.Key,
                    Total = g.Count(),
                    CheckedIn = g.Count(b => b.Status == BookingCheckedIn),
                    Cancelled = g.Count(b => b.Status == BookingCancelled),
                    DepositCollected = g
                        .Where(b => b.DepositAmount > 0 && b.PaymentStatus == PaymentPaid)
                        .Sum(b => b.DepositAmount)
                })
                .OrderByDescending(x => x.Total)
                .ToList();

            return View(viewModel);
        }
        [HttpGet]
        public async Task<IActionResult> Users(string? searchString)
        {
            var usersQuery = _context.Users.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var keyword = searchString.Trim().ToLower();

                usersQuery = usersQuery.Where(u =>
                    (u.Email != null && u.Email.ToLower().Contains(keyword)) ||
                    (u.FullName != null && u.FullName.ToLower().Contains(keyword)));
            }

            var users = await usersQuery
                .OrderBy(u => u.Email == "admin@uef.com" ? 0 : 1)
                .ThenBy(u => u.Email)
                .ToListAsync();

            var bookingCounts = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.AppUserId != null)
                .GroupBy(b => b.AppUserId!)
                .Select(g => new
                {
                    UserId = g.Key,
                    Count = g.Count()
                })
                .ToDictionaryAsync(x => x.UserId, x => x.Count);

            ViewBag.CurrentSearch = searchString;
            ViewBag.BookingCounts = bookingCounts;
            ViewBag.TotalUsers = await _context.Users.CountAsync();
            ViewBag.ConfirmedUsers = await _context.Users.CountAsync(u => u.EmailConfirmed);
            ViewBag.LockedUsers = await _context.Users.CountAsync(u =>
                u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow);

            return View(users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserLock(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                TempData["ErrorMessage"] = "Không tìm thấy tài khoản cần xử lý.";
                return RedirectToAction(nameof(Users));
            }

            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy tài khoản cần xử lý.";
                return RedirectToAction(nameof(Users));
            }

            if (string.Equals(user.Email, "admin@uef.com", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Không thể khóa tài khoản quản trị chính.";
                return RedirectToAction(nameof(Users));
            }

            var isLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

            if (isLocked)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                TempData["SuccessMessage"] = $"Đã mở khóa tài khoản {user.Email}.";
            }
            else
            {
                await _userManager.SetLockoutEnabledAsync(user, true);
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                TempData["SuccessMessage"] = $"Đã khóa tài khoản {user.Email}.";
            }

            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                TempData["ErrorMessage"] = "Không tìm thấy tài khoản cần xóa.";
                return RedirectToAction(nameof(Users));
            }

            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy tài khoản cần xóa.";
                return RedirectToAction(nameof(Users));
            }

            if (string.Equals(user.Email, "admin@uef.com", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Không thể xóa tài khoản quản trị chính.";
                return RedirectToAction(nameof(Users));
            }

            var bookingCount = await _context.Bookings.CountAsync(b => b.AppUserId == user.Id);

            if (bookingCount > 0)
            {
                await _userManager.SetLockoutEnabledAsync(user, true);
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));

                TempData["ErrorMessage"] =
                    $"Tài khoản {user.Email} đã có {bookingCount} đơn đặt bàn nên không xóa để tránh mất lịch sử. Hệ thống đã khóa tài khoản này.";

                return RedirectToAction(nameof(Users));
            }

            var result = await _userManager.DeleteAsync(user);

            TempData["SuccessMessage"] = result.Succeeded
                ? $"Đã xóa tài khoản {user.Email}."
                : "Không thể xóa tài khoản. Vui lòng thử lại.";

            return RedirectToAction(nameof(Users));
        }
        [HttpGet]
        public IActionResult AddDish()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDish(Dish model, IFormFile? imageFile)
        {
            if (model == null)
            {
                TempData["ErrorMessage"] = "Dữ liệu món ăn không hợp lệ.";
                return RedirectToAction(nameof(AddDish));
            }

            NormalizeDishInput(model);
            ValidateDishImage(imageFile, required: true);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            await DishCreateLock.WaitAsync();

            try
            {
                var duplicateSince = DateTime.Now.AddMinutes(-3);
                var isRecentDuplicate = await _context.Dishes.AnyAsync(x =>
                    x.Name == model.Name &&
                    x.Category == model.Category &&
                    x.Price == model.Price &&
                    x.CreatedAt >= duplicateSince);

                if (isRecentDuplicate)
                {
                    TempData["ErrorMessage"] = $"Món \"{model.Name}\" vừa được thêm trước đó. Hệ thống đã chặn thao tác gửi trùng.";
                    return RedirectToAction(nameof(Dishes));
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
            finally
            {
                DishCreateLock.Release();
            }
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
            NormalizeDishInput(model);
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
            var hasBookingDetails = await _context.BookingDetails.AnyAsync(x => x.DishId == id);
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
        private IQueryable<Booking> ApplyDateFilter(
            IQueryable<Booking> bookings,
            string? dateFilter,
            DateTime? fromDate,
            DateTime? toDate,
            DateTime today)
        {
            if (dateFilter == "today")
            {
                return bookings.Where(b => b.BookingDate >= today && b.BookingDate < today.AddDays(1));
            }

            if (dateFilter == "tomorrow")
            {
                var start = today.AddDays(1);
                var end = today.AddDays(2);
                return bookings.Where(b => b.BookingDate >= start && b.BookingDate < end);
            }

            if (dateFilter == "week")
            {
                var end = today.AddDays(7);
                return bookings.Where(b => b.BookingDate >= today && b.BookingDate < end);
            }

            if (dateFilter == "range")
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

            return bookings;
        }

        private void NormalizeDishInput(Dish dish)
        {
            dish.Name = dish.Name?.Trim() ?? "";
            dish.Category = dish.Category?.Trim() ?? "";
            dish.Description = dish.Description?.Trim();
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
            var contentType = imageFile.ContentType?.ToLowerInvariant() ?? "";
            if (!AllowedDishImageTypes.Contains(contentType))
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
            }
        }

        private string NormalizeBookingCode(string? rawCode)
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
    }
}