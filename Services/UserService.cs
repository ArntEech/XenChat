using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using XenChat.Data;
using XenChat.Models;

namespace XenChat.Services
{
    public class UserService
    {
        private readonly XenChatDbContext _db;
        private readonly IConfiguration? _configuration;

        public UserService(XenChatDbContext db, IConfiguration? configuration = null)
        {
            _db = db;
            _configuration = configuration;
        }

        public List<User> GetAllUsers()
        {
            return _db.Users.ToList();
        }

        public User GetUserById(int id)
        {
            return _db.Users.FirstOrDefault(u => u.Id == id);
        }

        public User Authenticate(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return null;

            var normalizedEmail = email.Trim().ToLower();
            return _db.Users.FirstOrDefault(u =>
                u.Email != null &&
                u.Email.ToLower() == normalizedEmail &&
                u.Password == password);
        }

        public void CreateUser(User user)
        {
            System.Diagnostics.Debug.WriteLine($"[CreateUser] Starting for {user.Username} / {user.Email}");

            var existing = _db.Users.FirstOrDefault(u =>
                u.Username.ToLower() == user.Username.ToLower() ||
                u.Email.ToLower() == user.Email.ToLower());

            if (existing != null)
            {
                System.Diagnostics.Debug.WriteLine($"[CreateUser] DUPLICATE. Existing Id={existing.Id}, Email={existing.Email}");
                return;
            }

            _db.Users.Add(user);
            _db.SaveChanges();

            System.Diagnostics.Debug.WriteLine($"[CreateUser] SAVED. New Id={user.Id}");
            System.Diagnostics.Debug.WriteLine($"[CreateUser] Total after save: {_db.Users.Count()}");
        }

        public string GenerateToken(User user, TimeSpan? lifetime = null)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var secret = _configuration?["Jwt:Key"] ?? "XenChat_Secret_Key_For_Jwt_Auth_2026_Min_32_Chars!";
            var key = Encoding.UTF8.GetBytes(secret);
            var expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(7));
            var notBefore = expires < DateTime.UtcNow ? expires.AddMinutes(-1) : DateTime.UtcNow;

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim("id", user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim("username", user.Username)
                }),
                NotBefore = notBefore,
                Expires = expires,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var tokenHandler = new JwtSecurityTokenHandler();
            var secret = _configuration?["Jwt:Key"] ?? "XenChat_Secret_Key_For_Jwt_Auth_2026_Min_32_Chars!";
            var key = Encoding.UTF8.GetBytes(secret);

            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out _);

                return principal;
            }
            catch
            {
                return null;
            }
        }
    }
}