using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace nhahangchay.Models
{
    public class Dish
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên món ăn")]
        [StringLength(200)]
        public string Name { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập giá tiền")]
        [Range(typeof(decimal), "0", "10000000", ErrorMessage = "Giá tiền phải nằm trong khoảng từ 0 đến 10.000.000 VNĐ")]
        public decimal Price { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        // Link ảnh sau khi upload lên Cloudinary
        public string? ImageUrl { get; set; }

        // PublicId dùng để xóa/quản lý ảnh trên Cloudinary sau này
        public string? CloudinaryPublicId { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn loại món")]
        public string Category { get; set; }

        public bool IsAvailable { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<BookingDetail> BookingDetails { get; set; } = new List<BookingDetail>();
    }
}