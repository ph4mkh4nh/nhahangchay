using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using nhahangchay.Models;
using System.Net;

namespace nhahangchay.Services
{
    public class EmailService
    {
        private readonly EmailSettings _settings;

        private const int PaymentUnpaid = 0;

        public EmailService(IOptions<EmailSettings> options)
        {
            _settings = options.Value;
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_settings.SmtpHost) &&
            !string.IsNullOrWhiteSpace(_settings.SenderEmail) &&
            !string.IsNullOrWhiteSpace(_settings.Password);

        public async Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody)
        {
            try
            {
                if (!IsConfigured || string.IsNullOrWhiteSpace(toEmail))
                {
                    Console.WriteLine("Thiếu cấu hình EmailSettings hoặc email người nhận.");
                    return false;
                }

                var message = new MimeMessage();

                message.From.Add(new MailboxAddress(
                    string.IsNullOrWhiteSpace(_settings.SenderName) ? "SenVie Restaurant" : _settings.SenderName,
                    _settings.SenderEmail
                ));

                message.To.Add(new MailboxAddress(
                    string.IsNullOrWhiteSpace(toName) ? "Quý khách" : toName,
                    toEmail
                ));

                message.Subject = subject;

                message.Body = new BodyBuilder
                {
                    HtmlBody = htmlBody
                }.ToMessageBody();

                var username = string.IsNullOrWhiteSpace(_settings.Username)
                    ? _settings.SenderEmail
                    : _settings.Username;

                var secureSocketOption = _settings.SmtpPort == 465
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls;

                using var smtp = new SmtpClient();

                await smtp.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secureSocketOption);
                await smtp.AuthenticateAsync(username, _settings.Password);
                await smtp.SendAsync(message);
                await smtp.DisconnectAsync(true);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("========== EMAIL SEND ERROR ==========");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("======================================");
                return false;
            }
        }

        public async Task<bool> SendBookingConfirmationEmailAsync(Booking booking, string? bookingUrl)
        {
            if (string.IsNullOrWhiteSpace(booking.Email))
            {
                return false;
            }

            var customerName = WebUtility.HtmlEncode(booking.CustomerName ?? "Quý khách");
            var bookingCode = WebUtility.HtmlEncode(booking.BookingCode ?? "");
            var bookingType = WebUtility.HtmlEncode(booking.BookingType ?? "");
            var omakaseTier = WebUtility.HtmlEncode(booking.OmakaseTier ?? "");
            var bookingUrlSafe = WebUtility.HtmlEncode(bookingUrl ?? "");
            var qrData = WebUtility.UrlEncode(booking.BookingCode ?? "");
            var qrImageUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=250x250&data={qrData}";

            var isWaitingDeposit = booking.DepositAmount > 0 && booking.PaymentStatus == PaymentUnpaid;

            string subject;
            string actionBlock;

            if (isWaitingDeposit)
            {
                subject = "Yêu cầu thanh toán cọc đặt bàn - SenVie";

                actionBlock = $@"
                    <div style='background:#fff8e1; padding:16px; border-radius:10px; border-left:5px solid #ffc107; margin-top:18px;'>
                        <h3 style='margin-top:0; color:#8a5a00;'>Đơn của bạn cần thanh toán cọc</h3>
                        <p>Số tiền cọc cần thanh toán: <strong style='color:#d9534f'>{booking.DepositAmount:N0}đ</strong></p>
                        <p>Vui lòng hoàn tất thanh toán cọc để đơn được ghi nhận đầy đủ và nhận mã QR check-in.</p>

                        <p style='margin-top:16px;'>
                            <a href='{bookingUrlSafe}' style='display:inline-block; background:#7DBA4E; color:white; padding:12px 22px; border-radius:25px; text-decoration:none; font-weight:bold;'>
                                Đi đến trang thanh toán
                            </a>
                        </p>
                    </div>

                    <div style='background:#f8f9fa; padding:16px; border-radius:10px; margin-top:16px;'>
                        <h3 style='margin-top:0;'>Chính sách hủy / hoàn cọc</h3>
                        <ul style='padding-left:20px; line-height:1.7;'>
                            <li><strong>Bàn VIP:</strong> Hủy trước 24 tiếng được hỗ trợ hoàn/chuyển 100% cọc. Hủy dưới 24 tiếng không hoàn cọc.</li>
                            <li><strong>Omakase Chay:</strong> Hủy trước 48 tiếng hoàn/chuyển 100%; từ 24 đến dưới 48 tiếng hoàn/chuyển 50%; dưới 24 tiếng không hoàn cọc.</li>
                            <li>Việc hoàn/chuyển cọc được nhà hàng liên hệ và xử lý thủ công.</li>
                        </ul>
                    </div>";
            }
            else
            {
                subject = "Xác nhận đặt bàn & mã QR check-in - SenVie";

                actionBlock = $@"
                    <div style='background:#f4fbef; padding:16px; border-radius:10px; border-left:5px solid #7DBA4E; margin-top:18px;'>
                        <p>Đơn đặt bàn của bạn đã được ghi nhận. Vui lòng đưa <strong>mã QR</strong> dưới đây cho lễ tân khi đến nhà hàng:</p>
                        <div style='text-align:center; margin-top:16px;'>
                            <img src='{qrImageUrl}' alt='Mã QR Check-in' style='border:2px solid #1f3b20; padding:6px; border-radius:12px;' />
                        </div>
                    </div>";
            }

            var omakaseLine = string.IsNullOrWhiteSpace(booking.OmakaseTier)
                ? ""
                : $"<li><strong>Gói Omakase:</strong> {omakaseTier}</li>";
            var bookingInfoList = isWaitingDeposit
    ? $@"
        <li><strong>Loại bàn:</strong> {bookingType}</li>
        {omakaseLine}
        <li><strong>Thời gian đến:</strong> {booking.BookingDate:dd/MM/yyyy HH:mm}</li>
        <li><strong>Số khách:</strong> {booking.NumberOfGuests}</li>
        <li><strong>Tiền cọc cần thanh toán:</strong> {booking.DepositAmount:N0}đ</li>"
    : $@"
        <li><strong>Mã đơn:</strong> <span style='color:#dc3545; font-size:18px; font-weight:bold;'>{bookingCode}</span></li>
        <li><strong>Loại bàn:</strong> {bookingType}</li>
        {omakaseLine}
        <li><strong>Thời gian đến:</strong> {booking.BookingDate:dd/MM/yyyy HH:mm}</li>
        <li><strong>Số khách:</strong> {booking.NumberOfGuests}</li>
        <li><strong>Tiền cọc:</strong> {booking.DepositAmount:N0}đ</li>";
            var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width:620px; margin:0 auto; padding:24px; border:1px solid #dcebd3; border-radius:14px;'>
                    <h2 style='color:#7DBA4E; margin-top:0;'>CẢM ƠN BẠN ĐÃ ĐẶT BÀN!</h2>

                    <p>Kính chào <strong>{customerName}</strong>,</p>
                    <p>Nhà hàng chay <strong>SenVie Vegan Dining</strong> đã nhận được yêu cầu giữ chỗ của bạn.</p>

                    <ul style='line-height:1.8; padding-left:20px;'>
    {bookingInfoList}
</ul>

                    {actionBlock}

                    <p style='margin-top:22px;'>Hẹn gặp bạn tại SenVie!</p>

                    <hr style='border:none; border-top:1px solid #eee; margin:22px 0;' />

                    <p style='color:#888; font-size:12px; margin-bottom:0;'>
                        Đây là email tự động, vui lòng không trả lời email này.
                    </p>
                </div>";

            return await SendEmailAsync(
                booking.Email,
                booking.CustomerName ?? "Quý khách",
                subject,
                htmlBody
            );
        }

        public async Task<bool> SendCustomerCancelledBookingEmailAsync(
            Booking booking,
            string policyMessage,
            decimal refundAmount,
            decimal feeAmount)
        {
            if (string.IsNullOrWhiteSpace(booking.Email))
            {
                return false;
            }

            var customerName = WebUtility.HtmlEncode(booking.CustomerName ?? "Quý khách");
            var bookingCode = WebUtility.HtmlEncode(booking.BookingCode ?? "");
            var bookingType = WebUtility.HtmlEncode(booking.BookingType ?? "");
            var safePolicyMessage = WebUtility.HtmlEncode(policyMessage);

            var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width:620px; margin:0 auto; padding:24px; border:1px solid #f1c4c4; border-radius:14px;'>
                    <h2 style='color:#dc3545; margin-top:0;'>XÁC NHẬN HỦY ĐƠN ĐẶT BÀN</h2>

                    <p>Kính chào <strong>{customerName}</strong>,</p>

                    <p>SenVie xác nhận đơn đặt bàn mã 
                        <strong style='color:#dc3545'>{bookingCode}</strong> 
                        đã được hủy theo yêu cầu của bạn.
                    </p>

                    <ul style='line-height:1.8; padding-left:20px;'>
                        <li><strong>Thời gian đặt bàn:</strong> {booking.BookingDate:dd/MM/yyyy HH:mm}</li>
                        <li><strong>Loại bàn:</strong> {bookingType}</li>
                        <li><strong>Số khách:</strong> {booking.NumberOfGuests}</li>
                        <li><strong>Tiền cọc đã ghi nhận:</strong> {booking.DepositAmount:N0}đ</li>
                        <li><strong>Số tiền dự kiến hoàn/chuyển:</strong> {refundAmount:N0}đ</li>
                        <li><strong>Phí giữ chỗ/hủy:</strong> {feeAmount:N0}đ</li>
                    </ul>

                    <div style='background:#fff8e1; padding:16px; border-radius:10px; border-left:5px solid #ffc107;'>
                        <strong>Chính sách xử lý cọc:</strong>
                        <p style='margin-bottom:0;'>{safePolicyMessage}</p>
                    </div>

                    <p style='margin-top:20px;'>
                        Do đây là hệ thống thanh toán mô phỏng, việc hoàn/chuyển cọc sẽ được nhà hàng liên hệ và xử lý thủ công.
                    </p>

                    <p>Nếu cần hỗ trợ thêm, vui lòng liên hệ nhà hàng hoặc chat trực tiếp trên website.</p>

                    <hr style='border:none; border-top:1px solid #eee; margin:22px 0;' />

                    <p style='color:#888; font-size:12px; margin-bottom:0;'>
                        Đây là email tự động, vui lòng không trả lời email này.
                    </p>
                </div>";

            return await SendEmailAsync(
                booking.Email,
                booking.CustomerName ?? "Quý khách",
                "Xác nhận hủy đơn đặt bàn - SenVie",
                htmlBody
            );
        }

        public async Task<bool> SendAdminCancelledBookingEmailAsync(Booking booking)
        {
            if (string.IsNullOrWhiteSpace(booking.Email))
            {
                return false;
            }

            var customerName = WebUtility.HtmlEncode(booking.CustomerName ?? "Quý khách");
            var bookingCode = WebUtility.HtmlEncode(booking.BookingCode ?? "");
            var bookingType = WebUtility.HtmlEncode(booking.BookingType ?? "");

            var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width:620px; margin:0 auto; padding:24px; border:1px solid #f1c4c4; border-radius:14px;'>
                    <h2 style='color:#dc3545; margin-top:0;'>THÔNG BÁO HỦY ĐƠN ĐẶT BÀN</h2>

                    <p>Kính chào <strong>{customerName}</strong>,</p>

                    <p>
                        SenVie rất tiếc phải thông báo: đơn đặt bàn mã 
                        <strong style='color:#dc3545'>{bookingCode}</strong> 
                        đã được hủy bởi quản trị viên nhà hàng.
                    </p>

                    <ul style='line-height:1.8; padding-left:20px;'>
                        <li><strong>Thời gian đặt bàn:</strong> {booking.BookingDate:dd/MM/yyyy HH:mm}</li>
                        <li><strong>Loại bàn:</strong> {bookingType}</li>
                        <li><strong>Số khách:</strong> {booking.NumberOfGuests}</li>
                    </ul>

                    <div style='background:#fff8e1; padding:16px; border-radius:10px; border-left:5px solid #ffc107;'>
                        <strong>Lý do thường gặp:</strong>
                        <p style='margin-bottom:0;'>
                            Nhà hàng cần điều chỉnh lịch phục vụ, phòng riêng, nguyên liệu hoặc phát hiện đơn chưa đủ điều kiện xử lý.
                            Nếu cần hỗ trợ thêm, vui lòng liên hệ trực tiếp SenVie.
                        </p>
                    </div>

                    <p style='margin-top:20px;'>
                        Bạn có thể đặt lại bàn trên website hoặc chat trực tiếp với quản lý để được hỗ trợ.
                    </p>

                    <hr style='border:none; border-top:1px solid #eee; margin:22px 0;' />

                    <p style='color:#888; font-size:12px; margin-bottom:0;'>
                        Đây là email tự động, vui lòng không trả lời email này.
                    </p>
                </div>";

            return await SendEmailAsync(
                booking.Email,
                booking.CustomerName ?? "Quý khách",
                "Thông báo hủy đơn đặt bàn - SenVie",
                htmlBody
            );
        }
        public async Task<bool> SendPasswordResetEmailAsync(AppUser user, string resetUrl)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return false;
            }

            var safeName = WebUtility.HtmlEncode(user.FullName ?? "quý khách");
            var safeResetUrl = WebUtility.HtmlEncode(resetUrl);

            var htmlBody = $@"
        <div style='font-family: Arial, sans-serif; max-width:620px; margin:0 auto; padding:24px; border:1px solid #dcebd3; border-radius:14px;'>
            <h2 style='color:#7DBA4E; margin-top:0;'>ĐẶT LẠI MẬT KHẨU SEN VIE</h2>

            <p>Kính chào <strong>{safeName}</strong>,</p>

            <p>SenVie nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.</p>

            <p style='margin-top:18px;'>
                <a href='{safeResetUrl}' style='display:inline-block; background:#7DBA4E; color:white; padding:12px 22px; border-radius:25px; text-decoration:none; font-weight:bold;'>
                    Đặt lại mật khẩu
                </a>
            </p>

            <p style='margin-top:18px; color:#555;'>
                Nếu nút trên không mở được, hãy sao chép đường dẫn dưới đây và dán vào trình duyệt:
            </p>

            <p style='word-break:break-all; background:#f6fbf2; padding:12px; border-radius:10px; color:#1f3b20;'>
                {safeResetUrl}
            </p>

            <p>Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này.</p>

            <hr style='border:none; border-top:1px solid #eee; margin:22px 0;' />

            <p style='color:#888; font-size:12px; margin-bottom:0;'>
                Đây là email tự động, vui lòng không trả lời email này.
            </p>
        </div>";

            return await SendEmailAsync(
                user.Email,
                user.FullName ?? "Quý khách",
                "Đặt lại mật khẩu tài khoản SenVie",
                htmlBody
            );
        }
    }
}