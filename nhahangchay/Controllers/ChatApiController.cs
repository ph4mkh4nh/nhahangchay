using Microsoft.AspNetCore.Mvc;
using nhahangchay.Data;
using nhahangchay.Models;
using System;
using System.Linq;
using System.Collections.Generic;

namespace nhahangchay.Controllers
{
    [Route("api/chat")]
    [ApiController]
    public class ChatApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ChatApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("users")]
        public IActionResult GetUsers()
        {
            // 1. Nhóm và lấy tin nhắn mới nhất từ DB
            var rawUsers = _context.ChatMessages
                .GroupBy(m => m.RoomId)
                .Select(g => new
                {
                    RoomId = g.Key,
                    LastMsg = g.OrderByDescending(m => m.CreatedAt).FirstOrDefault()
                })
                .ToList();

            // 2. Ánh xạ sang cấu trúc hiển thị 
            var result = rawUsers.Select(x => {
                var user = _context.Users.FirstOrDefault(u => u.Email == x.RoomId);
                var displayName = user != null ? user.FullName : x.RoomId;

                // Đếm chính xác số tin chưa đọc từ khách hàng
                int unread = _context.ChatMessages.Count(m => m.RoomId == x.RoomId && m.SenderName != "Admin" && !m.IsRead);
                var timeStr = x.LastMsg != null ? x.LastMsg.CreatedAt.ToString("HH:mm dd/MM") : "";

                return new
                {
                    roomId = x.RoomId,
                    fullName = displayName,
                    lastMessage = x.LastMsg?.Message ?? "",
                    rawTime = x.LastMsg?.CreatedAt ?? DateTime.Now,
                    lastTime = timeStr + "|" + unread // Nối chuỗi tường minh để Frontend tách dữ liệu
                };
            })
            .OrderByDescending(x => x.rawTime)
            .ToList();

            return Ok(result);
        }

        [HttpPost("read")]
        public IActionResult MarkAsRead([FromQuery] string roomId)
        {
            var unreadMessages = _context.ChatMessages
                .Where(m => m.RoomId == roomId && m.SenderName != "Admin" && !m.IsRead)
                .ToList();

            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
            }

            _context.SaveChanges();
            return Ok();
        }

        // =========================================================================
        // BỔ SUNG: HÀM LẤY LỊCH SỬ CHAT (Sửa lỗi màn hình trắng khi click vào user)
        // =========================================================================
        [HttpGet("history")]
        public IActionResult GetHistory([FromQuery] string roomId)
        {
            var messages = _context.ChatMessages
                .Where(m => m.RoomId == roomId)
                .OrderBy(m => m.CreatedAt) // Sắp xếp từ quá khứ đến hiện tại (cũ trên, mới dưới)
                .ToList();

            return Ok(messages);
        }
    }
}