namespace nhahangchay.Services
{
    // Đây là "Ổ cắm". Bất kỳ ai muốn làm hệ thống Chat (Admin hay AI) đều phải tuân thủ thiết kế này.
    public interface IChatHandler
    {
        // Hàm xử lý tin nhắn chung
        Task<string> ProcessMessageAsync(string userId, string message);
    }
}