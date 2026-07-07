using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using nhahangchay.Data;
using nhahangchay.Models;
using nhahangchay.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace nhahangchay.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly UserManager<AppUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;

        private const string OtpLoginProvider = "SenVie";
        private const string EmailOtpTokenName = "RegisterEmailOtp";
        private const int EmailOtpExpireMinutes = 10;

        public AccountController(
            SignInManager<AppUser> signInManager,
            UserManager<AppUser> userManager,
            ApplicationDbContext context,
            EmailService emailService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
            _emailService = emailService;
        }

        // LOGIN
        [HttpGet]
        public async Task<IActionResult> Login()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var currentUser = await _userManager.GetUserAsync(User);

                if (currentUser != null)
                {
                    return await RedirectAfterLoginAsync(currentUser);
                }

                return RedirectToAction("Index", "Home");
            }

            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            email = email?.Trim().ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Vui lòng nhập đầy đủ Email và Mật khẩu!";
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email);

            if (user != null)
            {
                var passwordCorrect = await _userManager.CheckPasswordAsync(user, password);

                if (passwordCorrect && !await _userManager.IsEmailConfirmedAsync(user))
                {
                    var sent = await GenerateAndSendRegisterOtpAsync(user);

                    TempData["InfoMessage"] = sent
                        ? "Tài khoản của bạn chưa xác nhận email. SenVie đã gửi lại mã OTP."
                        : "Tài khoản chưa xác nhận email, nhưng hệ thống chưa gửi được OTP. Vui lòng thử gửi lại mã.";

                    return RedirectToAction(nameof(VerifyEmailOtp), new { email = user.Email });
                }
            }

            var result = await _signInManager.PasswordSignInAsync(
                email,
                password,
                isPersistent: false,
                lockoutOnFailure: false
            );
            if (result.Succeeded)
            {
                var signedInUser = await _userManager.FindByEmailAsync(email);

                if (signedInUser == null)
                {
                    return RedirectToAction("Index", "Home");
                }

                return await RedirectAfterLoginAsync(signedInUser);
            }
            if (result.IsNotAllowed)
            {
                TempData["InfoMessage"] = "Vui lòng xác nhận email bằng mã OTP trước khi đăng nhập.";
                return RedirectToAction(nameof(VerifyEmailOtp), new { email });
            }

            ViewBag.Error = "Sai Email hoặc Mật khẩu. Vui lòng thử lại!";
            return View();
        }
        private async Task<IActionResult> RedirectAfterLoginAsync(AppUser user)
        {
            if (string.Equals(user.Email, "admin@uef.com", StringComparison.OrdinalIgnoreCase) ||
                await _userManager.IsInRoleAsync(user, "Admin"))
            {
                return RedirectToAction("Index", "Admin");
            }

            if (await _userManager.IsInRoleAsync(user, "Staff"))
            {
                return RedirectToAction("CheckIn", "Admin");
            }

            return RedirectToAction("Index", "Home");
        }
        // REGISTER + OTP
        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

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

            var weakPasswords = new[]
            {
                "123456",
                "12345678",
                "111111",
                "password",
                "qwerty",
                "abc123"
            };

            if (weakPasswords.Contains(password.ToLower()))
            {
                ViewBag.Error = "Mật khẩu quá dễ đoán. Vui lòng chọn mật khẩu khác an toàn hơn.";
                return View();
            }

            var existedUser = await _userManager.FindByEmailAsync(email);

            if (existedUser != null)
            {
                if (!await _userManager.IsEmailConfirmedAsync(existedUser))
                {
                    var resent = await GenerateAndSendRegisterOtpAsync(existedUser);

                    TempData["InfoMessage"] = resent
                        ? "Email này đã được đăng ký nhưng chưa xác nhận. SenVie đã gửi lại mã OTP."
                        : "Email này đã được đăng ký nhưng chưa xác nhận. Hệ thống chưa gửi được OTP, vui lòng thử lại.";

                    return RedirectToAction(nameof(VerifyEmailOtp), new { email });
                }

                ViewBag.Error = "Email này đã được đăng ký. Vui lòng đăng nhập hoặc dùng email khác.";
                return View();
            }

            var user = new AppUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = false
            };

            var result = await _userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                ViewBag.Error = string.Join(" ", result.Errors.Select(e => e.Description));
                return View();
            }

            var sent = await GenerateAndSendRegisterOtpAsync(user);

            if (!sent)
            {
                TempData["ErrorMessage"] = "Tài khoản đã được tạo nhưng hệ thống chưa gửi được OTP. Vui lòng thử gửi lại mã hoặc liên hệ nhà hàng.";
            }
            else
            {
                TempData["SuccessMessage"] = "Tài khoản đã được tạo. Vui lòng kiểm tra email để lấy mã OTP xác nhận.";
            }

            return RedirectToAction(nameof(VerifyEmailOtp), new { email = user.Email });
        }

        [HttpGet]
        public IActionResult VerifyEmailOtp(string email)
        {
            email = email?.Trim().ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction(nameof(Register));
            }

            ViewBag.Email = email;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyEmailOtp(string email, string otp)
        {
            email = email?.Trim().ToLower() ?? "";
            otp = otp?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(otp))
            {
                ViewBag.Email = email;
                ViewBag.Error = "Vui lòng nhập mã OTP.";
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                ViewBag.Email = email;
                ViewBag.Error = "Không tìm thấy tài khoản cần xác nhận.";
                return View();
            }

            if (await _userManager.IsEmailConfirmedAsync(user))
            {
                await _signInManager.SignInAsync(user, isPersistent: false);
                return await RedirectAfterLoginAsync(user);
            }

            var verifyResult = await VerifyRegisterOtpAsync(user, otp);

            if (!verifyResult.IsSuccess)
            {
                ViewBag.Email = email;
                ViewBag.Error = verifyResult.Message;
                return View();
            }
            await _signInManager.SignInAsync(user, isPersistent: false);

            TempData["SuccessMessage"] = "Xác nhận email thành công. Chào mừng bạn đến với SenVie!";
            return await RedirectAfterLoginAsync(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendEmailOtp(string email)
        {
            email = email?.Trim().ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction(nameof(Register));
            }

            var user = await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy tài khoản cần gửi lại OTP.";
                return RedirectToAction(nameof(Register));
            }

            if (await _userManager.IsEmailConfirmedAsync(user))
            {
                TempData["SuccessMessage"] = "Email này đã được xác nhận. Bạn có thể đăng nhập.";
                return RedirectToAction(nameof(Login));
            }

            var sent = await GenerateAndSendRegisterOtpAsync(user);

            TempData["InfoMessage"] = sent
                ? "SenVie đã gửi lại mã OTP. Vui lòng kiểm tra email."
                : "Không gửi được OTP. Vui lòng kiểm tra cấu hình email hoặc thử lại sau.";

            return RedirectToAction(nameof(VerifyEmailOtp), new { email });
        }

        // OTP HELPERS
        private static string GenerateOtpCode()
        {
            return RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        }

        private async Task<bool> GenerateAndSendRegisterOtpAsync(AppUser user)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return false;
            }

            var otp = GenerateOtpCode();
            var expiresAt = DateTime.UtcNow.AddMinutes(EmailOtpExpireMinutes);
            var tokenValue = $"{otp}|{expiresAt:O}";

            await _userManager.SetAuthenticationTokenAsync(
                user,
                OtpLoginProvider,
                EmailOtpTokenName,
                tokenValue
            );

            var safeName = WebUtility.HtmlEncode(user.FullName ?? "quý khách");
            var safeOtp = WebUtility.HtmlEncode(otp);

            var htmlBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 560px; margin: 0 auto; padding: 24px; border: 1px solid #dcebd3; border-radius: 14px;'>
    <h2 style='color:#7DBA4E; margin-top:0;'>Xác nhận tài khoản SenVie</h2>

    <p>Kính chào <strong>{safeName}</strong>,</p>

    <p>Cảm ơn bạn đã đăng ký tài khoản tại <strong>SenVie Vegan Dining</strong>.</p>

    <p>Mã OTP xác nhận email của bạn là:</p>

    <div style='font-size: 32px; letter-spacing: 8px; font-weight: bold; color: #1f3b20; background: #f4fbef; border: 1px dashed #7DBA4E; border-radius: 12px; padding: 16px; text-align:center;'>
        {safeOtp}
    </div>

    <p style='margin-top:18px;'>
        Mã này có hiệu lực trong <strong>{EmailOtpExpireMinutes} phút</strong>.
    </p>

    <p>Nếu bạn không thực hiện đăng ký tài khoản tại SenVie, vui lòng bỏ qua email này.</p>

    <hr style='border:none; border-top:1px solid #eee; margin:22px 0;' />

    <p style='color:#888; font-size:12px; margin-bottom:0;'>
        Đây là email tự động, vui lòng không trả lời email này.
    </p>
</div>";

            return await _emailService.SendEmailAsync(
                user.Email,
                user.FullName ?? "Quý khách",
                "Mã OTP xác nhận tài khoản SenVie",
                htmlBody
            );
        }

        private async Task<(bool IsSuccess, string Message)> VerifyRegisterOtpAsync(AppUser user, string otp)
        {
            var tokenValue = await _userManager.GetAuthenticationTokenAsync(
                user,
                OtpLoginProvider,
                EmailOtpTokenName
            );

            if (string.IsNullOrWhiteSpace(tokenValue))
            {
                return (false, "Mã OTP không tồn tại hoặc đã hết hạn. Vui lòng bấm gửi lại mã.");
            }

            var parts = tokenValue.Split('|');

            if (parts.Length != 2)
            {
                return (false, "Mã OTP không hợp lệ. Vui lòng bấm gửi lại mã.");
            }

            var savedOtp = parts[0];
            var expiresText = parts[1];

            if (!DateTime.TryParse(
                    expiresText,
                    null,
                    DateTimeStyles.RoundtripKind,
                    out var expiresAt))
            {
                return (false, "Không đọc được thời hạn OTP. Vui lòng bấm gửi lại mã.");
            }

            if (DateTime.UtcNow > expiresAt)
            {
                await _userManager.RemoveAuthenticationTokenAsync(
                    user,
                    OtpLoginProvider,
                    EmailOtpTokenName
                );

                return (false, "Mã OTP đã hết hạn. Vui lòng bấm gửi lại mã.");
            }

            if (!string.Equals(savedOtp, otp, StringComparison.Ordinal))
            {
                return (false, "Mã OTP không đúng. Vui lòng kiểm tra lại email.");
            }

            user.EmailConfirmed = true;

            var updateResult = await _userManager.UpdateAsync(user);

            if (!updateResult.Succeeded)
            {
                return (false, "Không thể xác nhận tài khoản. Vui lòng thử lại.");
            }

            await _userManager.RemoveAuthenticationTokenAsync(
                user,
                OtpLoginProvider,
                EmailOtpTokenName
            );

            return (true, "Xác nhận email thành công.");
        }
        // FORGOT PASSWORD
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            email = email?.Trim().ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "Vui lòng nhập email đã đăng ký.";
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email);

            if (user == null || !await _userManager.IsEmailConfirmedAsync(user))
            {
                ViewBag.Success = "Nếu email tồn tại trong hệ thống, SenVie sẽ gửi hướng dẫn đặt lại mật khẩu.";
                return View();
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            var resetUrl = Url.Action(
                nameof(ResetPassword),
                "Account",
                new
                {
                    email = user.Email,
                    token
                },
                Request.Scheme
            );

            if (string.IsNullOrWhiteSpace(resetUrl))
            {
                ViewBag.Error = "Không tạo được liên kết đặt lại mật khẩu. Vui lòng thử lại.";
                return View();
            }

            var sent = await _emailService.SendPasswordResetEmailAsync(user, resetUrl);

            ViewBag.Success = sent
                ? "SenVie đã gửi email hướng dẫn đặt lại mật khẩu. Vui lòng kiểm tra hộp thư."
                : "Hệ thống chưa gửi được email đặt lại mật khẩu. Vui lòng thử lại sau.";

            return View();
        }

        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            email = email?.Trim().ToLower() ?? "";
            token = token ?? "";

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                TempData["ErrorMessage"] = "Liên kết đặt lại mật khẩu không hợp lệ.";
                return RedirectToAction(nameof(Login));
            }

            ViewBag.Email = email;
            ViewBag.Token = token;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string email, string token, string password, string confirmPassword)
        {
            email = email?.Trim().ToLower() ?? "";
            token = token ?? "";

            ViewBag.Email = email;
            ViewBag.Token = token;

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                ViewBag.Error = "Vui lòng điền đầy đủ thông tin.";
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

            var weakPasswords = new[]
            {
        "123456",
        "12345678",
        "111111",
        "password",
        "qwerty",
        "abc123"
    };

            if (weakPasswords.Contains(password.ToLower()))
            {
                ViewBag.Error = "Mật khẩu quá dễ đoán. Vui lòng chọn mật khẩu khác an toàn hơn.";
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                ViewBag.Error = "Không tìm thấy tài khoản cần đặt lại mật khẩu.";
                return View();
            }

            var result = await _userManager.ResetPasswordAsync(user, token, password);

            if (!result.Succeeded)
            {
                ViewBag.Error = "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn. Vui lòng yêu cầu gửi lại email.";
                return View();
            }

            TempData["SuccessMessage"] = "Đặt lại mật khẩu thành công. Bạn có thể đăng nhập bằng mật khẩu mới.";
            return RedirectToAction(nameof(Login));
        }
        // LOGOUT
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        // PROFILE
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var myBookings = await _context.Bookings
                .Include(b => b.BookingDetails)
                .Where(b => b.AppUserId == user.Id)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            DateTime vietnamTimeNow = DateTime.UtcNow.AddHours(7);
            bool isChanged = false;

            foreach (var booking in myBookings)
            {
                if (booking.Status == 0 &&
                    booking.DepositAmount > 0 &&
                    booking.PaymentStatus == 0)
                {
                    if ((vietnamTimeNow - booking.CreatedAt).TotalMinutes > 15)
                    {
                        booking.Status = 2;
                        isChanged = true;
                    }
                }
            }

            if (isChanged)
            {
                await _context.SaveChangesAsync();
            }

            return View(myBookings);
        }
    }
}