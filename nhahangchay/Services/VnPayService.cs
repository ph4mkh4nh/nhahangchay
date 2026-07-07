using Microsoft.Extensions.Options;
using nhahangchay.Models;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace nhahangchay.Services
{
    public class VnPayService
    {
        private readonly VnPaySettings _settings;

        public VnPayService(IOptions<VnPaySettings> options)
        {
            _settings = options.Value;
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_settings.TmnCode) &&
            !string.IsNullOrWhiteSpace(_settings.HashSecret) &&
            !string.IsNullOrWhiteSpace(_settings.PaymentUrl);

        public string CreatePaymentUrl(Booking booking, string ipAddress, string returnUrl)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("Thiếu cấu hình VNPay.");
            }

            if (booking.DepositAmount <= 0)
            {
                throw new InvalidOperationException("Đơn này không yêu cầu thanh toán cọc.");
            }
            var now = DateTime.UtcNow.AddHours(7);
            var amount = (long)(booking.DepositAmount * 100);
            var paymentRef = CreatePaymentReference(booking.Id, now);

            var parameters = new SortedDictionary<string, string>
            {
                ["vnp_Version"] = "2.1.0",
                ["vnp_Command"] = "pay",
                ["vnp_TmnCode"] = _settings.TmnCode,
                ["vnp_Amount"] = amount.ToString(),
                ["vnp_CreateDate"] = now.ToString("yyyyMMddHHmmss"),
                ["vnp_CurrCode"] = "VND",
                ["vnp_IpAddr"] = string.IsNullOrWhiteSpace(ipAddress) ? "127.0.0.1" : ipAddress,
                ["vnp_Locale"] = "vn",
                ["vnp_OrderInfo"] = "Thanh toan coc dat ban SenVie",
                ["vnp_OrderType"] = "billpayment",
                ["vnp_ReturnUrl"] = string.IsNullOrWhiteSpace(_settings.ReturnUrl) ? returnUrl : _settings.ReturnUrl,
                ["vnp_TxnRef"] = paymentRef
            };
            var queryString = BuildQueryString(parameters);
            var secureHash = HmacSha512(_settings.HashSecret, queryString);

            return $"{_settings.PaymentUrl}?{queryString}&vnp_SecureHash={secureHash}";
        }

        public bool ValidateReturnData(IDictionary<string, string> queryData)
        {
            if (!queryData.TryGetValue("vnp_SecureHash", out var receivedHash) ||
                string.IsNullOrWhiteSpace(receivedHash))
            {
                return false;
            }

            var data = queryData
                .Where(x =>
                    x.Key.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Key, "vnp_SecureHash", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Key, "vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(x => x.Key, x => x.Value);

            var sortedData = new SortedDictionary<string, string>(data);
            var hashData = BuildQueryString(sortedData);
            var calculatedHash = HmacSha512(_settings.HashSecret, hashData);

            return string.Equals(receivedHash, calculatedHash, StringComparison.OrdinalIgnoreCase);
        }

        public string GetValue(IDictionary<string, string> queryData, string key)
        {
            return queryData.TryGetValue(key, out var value) ? value : "";
        }
        public int? GetBookingIdFromPaymentReference(string paymentReference)
        {
            if (string.IsNullOrWhiteSpace(paymentReference))
            {
                return null;
            }

            if (!paymentReference.StartsWith("SV", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (paymentReference.Length < 10)
            {
                return null;
            }

            var bookingIdText = paymentReference.Substring(2, 8);

            if (!int.TryParse(bookingIdText, out var bookingId))
            {
                return null;
            }

            return bookingId;
        }

        private static string CreatePaymentReference(int bookingId, DateTime createdAt)
        {
            var safeBookingId = bookingId.ToString().PadLeft(8, '0');
            return $"SV{safeBookingId}{createdAt:yyyyMMddHHmmss}";
        }
        private static string BuildQueryString(SortedDictionary<string, string> parameters)
        {
            return string.Join("&", parameters.Select(x =>
                $"{UrlEncode(x.Key)}={UrlEncode(x.Value)}"
            ));
        }

        private static string UrlEncode(string value)
        {
            return WebUtility.UrlEncode(value ?? "") ?? "";
        }

        private static string HmacSha512(string key, string input)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var inputBytes = Encoding.UTF8.GetBytes(input);

            using var hmac = new HMACSHA512(keyBytes);
            var hashBytes = hmac.ComputeHash(inputBytes);

            return Convert.ToHexString(hashBytes).ToLower();
        }
    }
}