using nhahangchay.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace nhahangchay.Data
{
    // ĐỔI TỪ DbContext THÀNH IdentityDbContext<AppUser>
    public class ApplicationDbContext : IdentityDbContext<AppUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Bảng Booking của bạn vẫn giữ nguyên ở đây
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }

        // THÊM DÒNG NÀY VÀO NGAY BÊN DƯỚI:
        public DbSet<Dish> Dishes { get; set; }
        public DbSet<BookingDetail> BookingDetails { get; set; }
    }
}