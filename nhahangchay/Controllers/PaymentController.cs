using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using nhahangchay.Data;
using nhahangchay.Services;

namespace nhahangchay.Controllers
{
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly VnPayService _vnPayService;
        private readonly EmailService _emailService;

        private const int BookingCancelled = 2;
        private const int PaymentUnpaid = 0;
        private const int PaymentPaid = 1;

        public PaymentController(
            ApplicationDbContext context,
            VnPayService vnPayService,
            EmailService emailService)
        {
            _context = context;
            _vnPayService = vnPayService;
            _emailService = emailService;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateVnPayPayment(string code, bool acceptCancellationPolicy)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "Mã đơn không hợp lệ.";
                return RedirectToAction("Index", "Home");
            }

            if (!acceptCancellationPolicy)
            {
                TempData["ErrorMessage"] = "Vui lòng đọc và đồng ý với chính sách hủy/hoàn cọc trước khi thanh toán.";
                return RedirectToAction("BookingSuccess", "Home", new { code });
            }

            if (!_vnPayService.IsConfigured)
            {
                TempData["ErrorMessage"] = "Hệ thống chưa cấu hình VNPay Sandbox. Vui lòng dùng MoMo Demo hoặc liên hệ quản trị viên.";
                return RedirectToAction("BookingSuccess", "Home", new { code });
            }

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.BookingCode == code);

            if (booking == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn đặt bàn.";
                return RedirectToAction("Index", "Home");
            }

            if (booking.Status == BookingCancelled)
            {
                TempData["ErrorMessage"] = "Đơn đặt bàn này đã bị hủy, không thể thanh toán.";
                return RedirectToAction("BookingSuccess", "Home", new { code });
            }

            if (booking.PaymentStatus == PaymentPaid)
            {
                TempData["SuccessMessage"] = "Đơn này đã được thanh toán cọc trước đó.";
                return RedirectToAction("BookingSuccess", "Home", new { code });
            }

            if (booking.DepositAmount <= 0)
            {
                TempData["ErrorMessage"] = "Đơn này không yêu cầu thanh toán cọc.";
                return RedirectToAction("BookingSuccess", "Home", new { code });
            }

            var returnUrl = Url.Action(
                nameof(VnPayReturn),
                "Payment",
                null,
                Request.Scheme
            ) ?? "";

            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            if (clientIp == "::1")
            {
                clientIp = "127.0.0.1";
            }

            try
            {
                var paymentUrl = _vnPayService.CreatePaymentUrl(booking, clientIp, returnUrl);
                return Redirect(paymentUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine("========== CREATE VNPAY PAYMENT ERROR ==========");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("================================================");

                TempData["ErrorMessage"] = "Không tạo được liên kết thanh toán VNPay. Vui lòng thử lại hoặc dùng MoMo Demo.";
                return RedirectToAction("BookingSuccess", "Home", new { code });
            }
        }

        [HttpGet]
        public async Task<IActionResult> VnPayReturn()
        {
            var queryData = Request.Query.ToDictionary(
                x => x.Key,
                x => x.Value.ToString()
            );
            var paymentReference = _vnPayService.GetValue(queryData, "vnp_TxnRef");
            var bookingId = _vnPayService.GetBookingIdFromPaymentReference(paymentReference);

            if (bookingId == null)
            {
                TempData["ErrorMessage"] = "Không nhận được mã thanh toán hợp lệ từ VNPay.";
                return RedirectToAction("Index", "Home");
            }

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == bookingId.Value);

            if (booking == null || string.IsNullOrWhiteSpace(booking.BookingCode))
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn đặt bàn sau khi thanh toán.";
                return RedirectToAction("Index", "Home");
            }

            var bookingCode = booking.BookingCode;
            if (!_vnPayService.ValidateReturnData(queryData))
            {
                TempData["ErrorMessage"] = "Dữ liệu thanh toán VNPay không hợp lệ hoặc sai chữ ký bảo mật.";
                return RedirectToAction("BookingSuccess", "Home", new { code = bookingCode });
            }

            var responseCode = _vnPayService.GetValue(queryData, "vnp_ResponseCode");
            var transactionStatus = _vnPayService.GetValue(queryData, "vnp_TransactionStatus");
            var amountText = _vnPayService.GetValue(queryData, "vnp_Amount");

            if (responseCode != "00" || transactionStatus != "00")
            {
                TempData["ErrorMessage"] = "Thanh toán VNPay chưa thành công hoặc đã bị hủy.";
                return RedirectToAction("BookingSuccess", "Home", new { code = bookingCode });
            }

            if (!long.TryParse(amountText, out var paidAmount))
            {
                TempData["ErrorMessage"] = "Không đọc được số tiền thanh toán từ VNPay.";
                return RedirectToAction("BookingSuccess", "Home", new { code = bookingCode });
            }

            var expectedAmount = (long)(booking.DepositAmount * 100);

            if (paidAmount != expectedAmount)
            {
                TempData["ErrorMessage"] = "Số tiền thanh toán không khớp với tiền cọc của đơn.";
                return RedirectToAction("BookingSuccess", "Home", new { code = bookingCode });
            }

            if (booking.Status == BookingCancelled)
            {
                TempData["ErrorMessage"] = "Đơn đã bị hủy, không thể ghi nhận thanh toán.";
                return RedirectToAction("BookingSuccess", "Home", new { code = bookingCode });
            }

            if (booking.PaymentStatus == PaymentPaid)
            {
                TempData["SuccessMessage"] = "Đơn này đã được ghi nhận thanh toán trước đó.";
                return RedirectToAction("BookingSuccess", "Home", new { code = bookingCode });
            }

            booking.PaymentStatus = PaymentPaid;
            await _context.SaveChangesAsync();

            try
            {
                var bookingUrl = Url.Action(
                    "BookingSuccess",
                    "Home",
                    new { code = booking.BookingCode },
                    Request.Scheme
                );

                var emailSent = await _emailService.SendBookingConfirmationEmailAsync(
                    booking,
                    bookingUrl
                );

                TempData["SuccessMessage"] = emailSent
                    ? "Thanh toán VNPay thành công. SenVie đã gửi mã QR check-in về email của bạn."
                    : "Thanh toán VNPay thành công, nhưng hệ thống chưa gửi được email QR. Bạn vẫn có thể xem mã QR tại trang này.";
            }
            catch (Exception ex)
            {
                Console.WriteLine("========== SEND QR EMAIL AFTER VNPAY ERROR ==========");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("=====================================================");

                TempData["SuccessMessage"] = "Thanh toán VNPay thành công, nhưng hệ thống chưa gửi được email QR. Bạn vẫn có thể xem mã QR tại trang này.";
            }

            return RedirectToAction("BookingSuccess", "Home", new { code = bookingCode });
        }
    }
}