using nhahangchay.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using nhahangchay.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace nhahangchay.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly UserManager<AppUser> _userManager;

        // 1. THÊM ỐNG NƯỚC KẾT NỐI DATABASE
        private readonly ApplicationDbContext _context;

        // Sửa hàm khởi tạo để nhận thêm _context
        public AccountController(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager, ApplicationDbContext context)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context; // Gán ống nước vào
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Vui lòng nhập đầy đủ Email và Mật khẩu!";
                return View();
            }

            var result = await _signInManager.PasswordSignInAsync(email, password, isPersistent: false, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                // Nếu là Lễ tân thì vào Admin, nếu là Khách thì về Trang chủ
                if (email == "admin@uef.com")
                {
                    return RedirectToAction("Index", "Admin");
                }
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = "Sai Email hoặc Mật khẩu. Vui lòng thử lại!";
            return View();
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string fullName, string email, string password, string confirmPassword)
        {
            fullName = fullName?.Trim() ?? "";
            email = email?.Trim().ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                ViewBag.Error = "Vui lòng điền đầy đủ thông tin!";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu nhập lại không khớp.";
                return View();
            }

            if (password.Length < 6)
            {
                ViewBag.Error = "Mật khẩu phải có ít nhất 6 ký tự.";
                return View();
            }

            var weakPasswords = new[] { "123456", "12345678", "111111", "password", "qwerty", "abc123" };

            if (weakPasswords.Contains(password.ToLower()))
            {
                ViewBag.Error = "Mật khẩu quá dễ đoán. Vui lòng chọn mật khẩu khác an toàn hơn.";
                return View();
            }

            var existedUser = await _userManager.FindByEmailAsync(email);
            if (existedUser != null)
            {
                ViewBag.Error = "Email này đã được đăng ký. Vui lòng đăng nhập hoặc dùng email khác.";
                return View();
            }

            var user = new AppUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, password);

            if (result.Succeeded)
            {
                await _signInManager.SignInAsync(user, isPersistent: false);
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = string.Join(" ", result.Errors.Select(e => e.Description));
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }


        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            // Lấy thông tin người dùng đang thao tác
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login");
            }
            // Lấy lịch sử đơn hàng
            var myBookings = await _context.Bookings
                .Include(b => b.BookingDetails)
                .Where(b => b.AppUserId == user.Id)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            //  QUÉT & HỦY ĐƠN CHƯA THANH TOÁN QUÁ 15 PHÚT
            DateTime vietnamTimeNow = DateTime.UtcNow.AddHours(7);
            bool isChanged = false;

            foreach (var booking in myBookings)
            {
                // Nếu đơn đang Chờ duyệt (0) + Yêu cầu có cọc (>0) + Chưa thanh toán (0)
                if (booking.Status == 0 && booking.DepositAmount > 0 && booking.PaymentStatus == 0)
                {
                    // Nếu thời gian hiện tại vượt quá thời gian tạo đơn 15 phút
                    if ((vietnamTimeNow - booking.CreatedAt).TotalMinutes > 15)
                    {
                        booking.Status = 2; // Ép thành trạng thái Đã Hủy
                        isChanged = true;
                    }
                }
            }

            // Nếu có đơn bị hệ thống chém, phải lưu lại thay đổi vào Database
            if (isChanged)
            {
                await _context.SaveChangesAsync();
            }

            // Đưa gói dữ liệu đã được làm sạch ra ngoài giao diện
            return View(myBookings);
        }
    }
}