using Microsoft.AspNetCore.SignalR;
using nhahangchay.Data;
using nhahangchay.Models; // Sửa lại theo namespace chuẩn của bạn
using System;
using System.Threading.Tasks;

namespace nhahangchay.Hubs
{
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;

        // Tiêm Database vào Tổng đài
        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        // Hàm 1: Đăng ký phòng khi vừa vào web
        public async Task JoinRoom(string roomId, bool isAdmin)
        {
            if (isAdmin)
            {
                // Admin sẽ được đưa vào một căn phòng VIP tên là "AdminGroup"
                await Groups.AddToGroupAsync(Context.ConnectionId, "AdminGroup");
            }
            else
            {
                // Khách hàng sẽ được đưa vào căn phòng mang tên của chính họ
                await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            }
        }

        // Hàm 2: Dành cho Khách hàng gửi tin lên Lễ tân
        public async Task SendMessageToAdmin(string roomId, string senderName, string message)
        {
            // Bước A: Lưu vào Database
            _context.ChatMessages.Add(new ChatMessage { RoomId = roomId, SenderName = senderName, Message = message });
            await _context.SaveChangesAsync();

            // Bước B: Phát loa tin nhắn này vào phòng "AdminGroup" (Cho Lễ tân nghe) 
            // VÀ phát ngược lại vào phòng của Khách (để màn hình khách tự hiện lên)
            await Clients.Group("AdminGroup").SendAsync("ReceiveMessage", roomId, senderName, message);
            await Clients.Group(roomId).SendAsync("ReceiveMessage", roomId, senderName, message);
        }

        // Hàm 3: Dành cho Admin (Lễ tân) trả lời đích danh một Khách hàng
        public async Task ReplyToUser(string targetRoomId, string message)
        {
            // Bước A: Lưu vào Database
            _context.ChatMessages.Add(new ChatMessage { RoomId = targetRoomId, SenderName = "Admin", Message = message });
            await _context.SaveChangesAsync();

            // Bước B: Bắn tin nhắn trực tiếp vào phòng của Khách đó, và báo lại cho các Admin khác cùng biết
            await Clients.Group(targetRoomId).SendAsync("ReceiveMessage", targetRoomId, "Admin", message);
            await Clients.Group("AdminGroup").SendAsync("ReceiveMessage", targetRoomId, "Admin", message);
        }
    }
}