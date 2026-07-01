namespace nhahangchay.Models
{
    public class EmailSettings
    {
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;

        public string SenderEmail { get; set; } = "";
        public string SenderName { get; set; } = "SenVie Restaurant";

        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}