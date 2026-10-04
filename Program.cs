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

    // Ensure Messages table has SQLite autoincrement primary key
    try
    {
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Messages_Fix"" (
                ""MessageId"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""SenderId"" INTEGER NOT NULL,
                ""ReceiverId"" INTEGER NOT NULL,
                ""Content"" TEXT NOT NULL,
                ""Timestamp"" TEXT NOT NULL,
                ""IsRead"" INTEGER NOT NULL DEFAULT 0
            );
            INSERT OR IGNORE INTO ""Messages_Fix"" (""MessageId"", ""SenderId"", ""ReceiverId"", ""Content"", ""Timestamp"", ""IsRead"")
            SELECT ""MessageId"", ""SenderId"", ""ReceiverId"", ""Content"", ""Timestamp"", ""IsRead"" FROM ""Messages"";
            DROP TABLE ""Messages"";
            ALTER TABLE ""Messages_Fix"" RENAME TO ""Messages"";
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Statuses"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""UserId"" INTEGER NOT NULL,
                ""Username"" TEXT NOT NULL,
                ""UserAvatar"" TEXT NULL,
                ""MediaUrl"" TEXT NOT NULL,
                ""Caption"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL
            );
        ");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[Startup] Database tables check: {ex.Message}");
    }

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