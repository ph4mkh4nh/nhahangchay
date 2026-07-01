using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using nhahangchay.Models;

namespace nhahangchay.Services
{
    public class EmailService
    {
        private readonly EmailSettings _settings;

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
            Console.WriteLine("========== EMAIL DEBUG ==========");
            Console.WriteLine($"SmtpHost: {_settings.SmtpHost}");
            Console.WriteLine($"SmtpPort: {_settings.SmtpPort}");
            Console.WriteLine($"SenderEmail: {_settings.SenderEmail}");
            Console.WriteLine($"SenderName: {_settings.SenderName}");
            Console.WriteLine($"Username: {_settings.Username}");
            Console.WriteLine($"Password exists: {!string.IsNullOrWhiteSpace(_settings.Password)}");
            Console.WriteLine("=================================");
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
    }
}