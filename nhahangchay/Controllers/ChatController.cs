using Microsoft.AspNetCore.Mvc;
using nhahangchay.Services;

namespace nhahangchay.Controllers
{
    public class ChatController : Controller
    {
        [HttpPost]
        public async Task<IActionResult> SendMessage(string message, string chatType)
        {
            // Lấy ID của khách hàng đang đăng nhập (nếu có)
            string userId = User.Identity.IsAuthenticated ? User.Identity.Name : "Khách vãng lai";

            IChatHandler chatHandler;

            // Thuật toán chọn "Phích cắm" dựa vào yêu cầu từ giao diện
            if (chatType == "AI")
            {
                // Tương lai: chatHandler = new AIChatHandler();
                return Json(new { reply = "Tính năng AI đang được nâng cấp!" });
            }
            else
            {
                // Mặc định là Chat với Admin
                chatHandler = new AdminChatHandler();
            }

            // Gọi hàm xử lý và lấy câu trả lời
            string replyMessage = await chatHandler.ProcessMessageAsync(userId, message);

            // Trả kết quả về cho Javascript (Frontend)
            return Json(new { reply = replyMessage });
        }
    }
}