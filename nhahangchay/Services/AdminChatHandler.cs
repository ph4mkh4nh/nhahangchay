namespace nhahangchay.Services
{
    public class AdminChatHandler : IChatHandler
    {
        public async Task<string> ProcessMessageAsync(string userId, string message)
        {
            // TẠI ĐÂY: Bạn sẽ viết code lưu tin nhắn 'message' của 'userId' vào Database.
            // (Hiện tại chúng ta giả lập trả về một thông báo trước)

            await Task.Delay(500); // Giả lập thời gian lưu mạng
            return "Tin nhắn đã gửi đến Admin. Chúng tôi sẽ phản hồi bạn qua Email hoặc số điện thoại sớm nhất!";
        }
    }
}