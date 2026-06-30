using System;

namespace nhahangchay.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public string RoomId { get; set; } // Mã phòng (SĐT khách hoặc ID ngẫu nhiên)
        public string SenderName { get; set; } // Tên người gửi (Khách hoặc Admin)
        public string Message { get; set; } // Nội dung tin nhắn
        public DateTime CreatedAt { get; set; } = DateTime.Now; // Thời gian gửi
        public bool IsRead { get; set; } = false;
    }
}