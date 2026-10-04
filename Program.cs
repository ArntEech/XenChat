using Microsoft.EntityFrameworkCore;
using XenChat.Data;
using XenChat.Hubs;
using XenChat.Services;

var builder = WebApplication.CreateBuilder(args);

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=Data/xenchat.db";
if (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
{
    var rawPath = connectionString.Substring("Data Source=".Length).Trim();
    if (!Path.IsPathRooted(rawPath))
    {
        var absolutePath = Path.Combine(builder.Environment.ContentRootPath, rawPath.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        connectionString = $"Data Source={absolutePath}";
    }
}

builder.Services.AddDbContext<XenChatDbContext>(options =>
    options.UseSqlite(connectionString));

// Services
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddSingleton<PendingSignupStore>();
builder.Services.AddScoped<EmailService>();

// MVC + SignalR + Session
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Auto-create / migrate the database on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<XenChatDbContext>();
    db.Database.EnsureCreated();   // Creates xenchat.db + seeds 13 users if missing
    System.Diagnostics.Debug.WriteLine($"[Startup] Database ready.");
    System.Diagnostics.Debug.WriteLine($"[Startup] Users in DB: {db.Users.Count()}");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapHub<ChatHub>("/chatHub");

app.Run();