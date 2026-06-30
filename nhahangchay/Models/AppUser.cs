using Microsoft.AspNetCore.Identity;
using nhahangchay.Models;
using System.Collections.Generic;

namespace nhahangchay.Models
{
    // Kế thừa IdentityUser để có sẵn các cột Email, PasswordHash, PhoneNumber...
    public class AppUser : IdentityUser
    {
        public string FullName { get; set; }

        // Cột lưu trữ điểm tích lũy của khách hàng
        public int LoyaltyPoints { get; set; } = 0;

        // Một khách hàng có thể có nhiều đơn đặt bàn
        public ICollection<Booking> Bookings { get; set; }
    }
}