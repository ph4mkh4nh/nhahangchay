using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace nhahangchay.Models
{
    public class BookingDetail
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }
        public Booking? Booking { get; set; }

        // DishId để tham chiếu món hiện tại. Có thể null nếu món bị xóa sau này.
        public int? DishId { get; set; }
        public Dish? Dish { get; set; }

        // Snapshot tại thời điểm khách đặt, giúp đơn cũ không bị đổi khi admin sửa tên/giá món.
        [Required]
        [StringLength(200)]
        public string DishName { get; set; } = "";

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }

        [Range(1, 99)]
        public int Quantity { get; set; } = 1;

        [Column(TypeName = "decimal(18,2)")]
        public decimal LineTotal { get; set; }

        [StringLength(300)]
        public string? Note { get; set; }
    }
}
