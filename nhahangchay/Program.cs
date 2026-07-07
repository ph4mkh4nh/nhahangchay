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

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

builder.Services.AddHostedService<AutoCancelBookingService>();

builder.Services.Configure<CloudinarySettings>(
    builder.Configuration.GetSection("CloudinarySettings")
);

builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings")
);

builder.Services.AddScoped<CloudinaryService>();
builder.Services.AddScoped<EmailService>();

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
    options.SignIn.RequireConfirmedEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

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
//  tài khoản admin mặc định.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    var adminEmail = "admin@uef.com";
    var adminPassword = "123456";

    var roles = new[] { "Admin", "Staff" };

    foreach (var roleName in roles)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }

    var admin = await userManager.FindByEmailAsync(adminEmail);

    if (admin == null)
    {
        admin = new AppUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FullName = "Quản Lý",
            EmailConfirmed = true
        };

        await userManager.CreateAsync(admin, adminPassword);
    }
    else
    {
        admin.UserName = adminEmail;
        admin.Email = adminEmail;
        admin.FullName = string.IsNullOrWhiteSpace(admin.FullName) ? "Quản Lý" : admin.FullName;
        admin.EmailConfirmed = true;

        await userManager.UpdateAsync(admin);

        var hasPassword = await userManager.HasPasswordAsync(admin);

        if (hasPassword)
        {
            await userManager.RemovePasswordAsync(admin);
        }

        await userManager.AddPasswordAsync(admin, adminPassword);
    }

    if (!await userManager.IsInRoleAsync(admin, "Admin"))
    {
        await userManager.AddToRoleAsync(admin, "Admin");
    }
}

app.Run();