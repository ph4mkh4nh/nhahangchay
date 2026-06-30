using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using nhahangchay.Data;
using nhahangchay.Hubs;
using nhahangchay.Models;
using nhahangchay.Services;

var builder = WebApplication.CreateBuilder(args);

// Render cung cấp biến PORT cho Web Service.
// Nếu chạy local/Docker mà không có PORT thì dùng 10000.
var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// PostgreSQL/Npgsql xử lý DateTime khác SQL Server.
// Dòng này giúp giảm lỗi DateTime khi chuyển project từ SQL Server sang PostgreSQL.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Ưu tiên đọc ConnectionStrings__DefaultConnection từ Environment Variables.
// Nếu không có thì fallback về appsettings.json để chạy local.
var connectionString =
    Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Chưa cấu hình ConnectionStrings__DefaultConnection.");
}

// Đổi từ SQL Server sang PostgreSQL.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

builder.Services.AddHostedService<AutoCancelBookingService>();

builder.Services.Configure<CloudinarySettings>(
    builder.Configuration.GetSection("CloudinarySettings")
);

builder.Services.AddScoped<CloudinaryService>();

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    // Dùng cho đồ án/test. Khi deploy thật nên bật lại quy tắc mật khẩu mạnh hơn.
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Trên Render, SSL nằm ở proxy phía ngoài.
// Nếu deploy bị vòng lặp redirect/502 thì tạm comment dòng dưới để test.
app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

// Identity cần Authentication trước Authorization.
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<ChatHub>("/chatHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Áp migration và tạo tài khoản admin mặc định nếu database chưa có.
// Phù hợp cho đồ án/demo. Nếu production thật, nên chạy migration thủ công.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

    var adminEmail = "admin@uef.com";
    var existingAdmin = await userManager.FindByEmailAsync(adminEmail);

    if (existingAdmin == null)
    {
        var newAdmin = new AppUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FullName = "Quản Lý",
            EmailConfirmed = true
        };

        // Mật khẩu mặc định phục vụ test đồ án. Nên đổi sau khi chạy lần đầu.
        await userManager.CreateAsync(newAdmin, "123456");
    }
}

app.Run();