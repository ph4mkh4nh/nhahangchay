using nhahangchay.Models;
using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace nhahangchay.Models
{
    public class Booking
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên của bạn")]
        [StringLength(100)]
        public string CustomerName { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập số điện thoại")]
        [StringLength(15)]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        public string PhoneNumber { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập địa chỉ email để nhận xác nhận")]
        [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng (VD: ten@gmail.com)")]
        [StringLength(150)]
        public string Email { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn ngày giờ đến")]
        public DateTime BookingDate { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập số lượng khách")]
        [Range(1, 50, ErrorMessage = "Số lượng khách phải từ 1 đến 50 người")]
        public int NumberOfGuests { get; set; }
        [StringLength(500)]
        public string? SpecialRequest { get; set; }
        // Trạng thái đơn:
        // 0 = Chờ duyệt / Chưa đến
        // 1 = Đã check-in
        // 2 = Đã hủy
        // 3 = Hoàn thành
        // Lưu loại dịch vụ: "Bàn Tiêu Chuẩn", "Bàn VIP", hoặc "Dịch Vụ Tiệc Chay"
        [Required]
        public string BookingType { get; set; } = "Bàn Tiêu Chuẩn";
        public int Status { get; set; } = 0;

        // Tự động lưu thời điểm bấm đặt bàn
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string? BookingCode { get; set; }

        public string? AppUserId { get; set; }
        public AppUser? AppUser { get; set; }

        // Đánh dấu đơn này đã được cộng điểm cho tài khoản chưa? (Chống cộng lặp)
        public bool IsPointsClaimed { get; set; } = false;
        public string? OmakaseTier { get; set; }

        // Số tiền cọc hệ thống yêu cầu khách phải trả
        public decimal DepositAmount { get; set; } = 0;

        // Trạng thái thanh toán cọc: 0 = Chưa thanh toán, 1 = Đã thanh toán
        public int PaymentStatus { get; set; } = 0;

        // Danh sách món khách chọn trước để nhà hàng chuẩn bị. Không dùng để thanh toán online.
        public ICollection<BookingDetail> BookingDetails { get; set; } = new List<BookingDetail>();
    }
}