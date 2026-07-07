namespace nhahangchay.Models
{
    public class VnPaySettings
    {
        public string TmnCode { get; set; } = "";
        public string HashSecret { get; set; } = "";
        public string PaymentUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
        public string ReturnUrl { get; set; } = "";
    }
}