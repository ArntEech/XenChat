using Microsoft.EntityFrameworkCore;
using Npgsql;
using XenChat.Data;
using XenChat.Hubs;
using XenChat.Services;

// Enable legacy timestamp behavior for Npgsql to seamlessly handle DateTime across PostgreSQL models
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// 1. Load .env file into environment variables
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFile))
{
    foreach (var line in File.ReadAllLines(envFile))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            continue;

        var eqIdx = trimmed.IndexOf('=');
        if (eqIdx > 0)
        {
            var key = trimmed.Substring(0, eqIdx).Trim();
            var val = trimmed.Substring(eqIdx + 1).Trim();
            if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
            {
                val = val.Substring(1, val.Length - 2);
            }
            Environment.SetEnvironmentVariable(key, val);
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

// Helper to convert postgres:// or postgresql:// URLs to Npgsql format
static string ConvertPostgreSqlUrl(string raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return raw;
    if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');

        var npgsqlBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Username = username,
            Password = password,
            Database = database,
            SslMode = SslMode.Require,
            TrustServerCertificate = true,
            Pooling = true
        };
        return npgsqlBuilder.ConnectionString;
    }
    return raw;
}

// 2. Resolve Database Connection String (Strictly from .env DATABASE_URL)
var envDbUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(envDbUrl))
{
    throw new InvalidOperationException("DATABASE_URL environment variable is not configured. XenChat requires PostgreSQL from the .env file.");
}

var pgConnectionString = ConvertPostgreSqlUrl(envDbUrl);
Console.WriteLine("[Database] Strictly using PostgreSQL from .env DATABASE_URL.");
builder.Services.AddDbContext<XenChatDbContext>(options =>
    options.UseNpgsql(pgConnectionString));

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
    try
    {
        db.Database.EnsureCreated(); // Creates tables and seeds initial users in PostgreSQL if missing

        Console.WriteLine($"[Database] Successfully connected: {db.Database.ProviderName}");
        Console.WriteLine($"[Database] Users in DB: {db.Users.Count()}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Database] Initialization error: {ex.Message}");
    }
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