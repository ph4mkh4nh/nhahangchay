using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using nhahangchay.Data;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace nhahangchay.Services
{
    // Kế thừa BackgroundService để chạy ngầm liên tục
    public class AutoCancelBookingService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;

        public AutoCancelBookingService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)// vòng lặp
            {
                try
                {
                    
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        DateTime vietnamTimeNow = DateTime.UtcNow.AddHours(7);

                        // Tìm các đơn: Đang chờ (0) VÀ thời gian hiện tại đã lố qua thời gian đặt bàn 30 phút
                        var unpaidDepositBookings = context.Bookings
                            .Where(b =>
                                b.Status == 0 &&
                                b.DepositAmount > 0 &&
                                b.PaymentStatus == 0 &&
                                vietnamTimeNow > b.CreatedAt.AddMinutes(15))
                            .ToList();

                        var noShowBookings = context.Bookings
                            .Where(b =>
                                b.Status == 0 &&
                                vietnamTimeNow > b.BookingDate.AddMinutes(30))
                            .ToList();

                        var bookingsToCancel = unpaidDepositBookings
                            .Concat(noShowBookings)
                            .Distinct()
                            .ToList();

                        if (bookingsToCancel.Any())
                        {
                            foreach (var booking in bookingsToCancel)
                            {
                                booking.Status = 2;
                            }

                            await context.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Ghi log lỗi nếu cần thiết
                }
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);// nghỉ mỗi 5p
            }
        }
    }
}