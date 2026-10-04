using Microsoft.AspNetCore.Mvc;
using XenChat.Data;
using XenChat.Models;
using XenChat.Services;

namespace XenChat.Controllers
{
    public class HomeController : Controller
    {
        private readonly UserService _userService;
        private readonly MessageService _messageService;
        private readonly XenChatDbContext _db;
        private readonly CloudinaryService _cloudinary;

        public HomeController(UserService userService, MessageService messageService, XenChatDbContext db, CloudinaryService cloudinary)
        {
            _userService = userService;
            _messageService = messageService;
            _db = db;
            _cloudinary = cloudinary;
        }

        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var currentUser = _userService.GetUserById(userId.Value);
            if (currentUser == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            var users = _userService.GetAllUsers().Where(u => u.Id != userId.Value).ToList();

            ViewBag.CurrentUser = currentUser;
            ViewBag.LastMessages = new Dictionary<int, string>();
            ViewBag.LastMessageTimes = new Dictionary<int, string>();
            ViewBag.UnreadCounts = new Dictionary<int, int>();

            foreach (var user in users)
            {
                var messages = _messageService.GetConversation(userId.Value, user.Id);
                var lastMessage = messages.LastOrDefault();

                ViewBag.LastMessages[user.Id] = lastMessage?.Content ?? "No messages yet";
                ViewBag.LastMessageTimes[user.Id] = lastMessage?.Timestamp.ToString("HH:mm") ?? "";

                var unreadCount = messages.Count(m => m.SenderId == user.Id && m.ReceiverId == userId.Value && !m.IsRead);
                ViewBag.UnreadCounts[user.Id] = unreadCount;
            }

            var cutoff = DateTime.Now.AddHours(-24);
            var activeStatuses = _db.Statuses
                .Where(s => s.CreatedAt >= cutoff)
                .OrderByDescending(s => s.CreatedAt)
                .ToList();
            ViewBag.Statuses = activeStatuses;

            var favoriteUserIds = _db.Favorites
                .Where(f => f.UserId == userId.Value)
                .Select(f => f.FavoriteUserId)
                .ToHashSet();
            ViewBag.FavoriteUserIds = favoriteUserIds;

            return View(users);
        }

        [HttpPost]
        public IActionResult ToggleFavorite(int targetUserId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            var authHeader = Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var principal = _userService.ValidateToken(token);
                if (principal != null)
                {
                    var idClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                  ?? principal.FindFirst("id")?.Value;
                    if (int.TryParse(idClaim, out int tokenUserId))
                    {
                        var tokenUser = _userService.GetUserById(tokenUserId);
                        if (tokenUser != null)
                        {
                            userId = tokenUserId;
                            HttpContext.Session.SetInt32("UserId", tokenUser.Id);
                            HttpContext.Session.SetString("Username", tokenUser.Username);
                        }
                    }
                }
            }

            if (userId == null)
                return Unauthorized(new { success = false, error = "Not logged in" });

            if (userId.Value == targetUserId)
                return Json(new { success = false, error = "Cannot favorite yourself" });

            var targetUser = _userService.GetUserById(targetUserId);
            if (targetUser == null)
                return Json(new { success = false, error = "User not found" });

            var existing = _db.Favorites.FirstOrDefault(f => f.UserId == userId.Value && f.FavoriteUserId == targetUserId);
            bool isFavorite;
            if (existing != null)
            {
                _db.Favorites.Remove(existing);
                isFavorite = false;
            }
            else
            {
                _db.Favorites.Add(new Favorite
                {
                    UserId = userId.Value,
                    FavoriteUserId = targetUserId
                });
                isFavorite = true;
            }

            _db.SaveChanges();

            var count = _db.Favorites.Count(f => f.UserId == userId.Value);
            return Json(new { success = true, isFavorite, count });
        }

        public IActionResult Camera()
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");
            return View();
        }

        public IActionResult Updates()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var currentUser = _userService.GetUserById(userId.Value);
            if (currentUser == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            var cutoff = DateTime.Now.AddHours(-24);
            var activeStatuses = _db.Statuses
                .Where(s => s.CreatedAt >= cutoff)
                .OrderByDescending(s => s.CreatedAt)
                .ToList();

            ViewBag.CurrentUser = currentUser;
            ViewBag.Statuses = activeStatuses;

            return View();
        }

        [HttpGet]
        public IActionResult AddStatus()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var currentUser = _userService.GetUserById(userId.Value);
            if (currentUser == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.CurrentUser = currentUser;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> AddStatus(IFormFile statusImage, string? caption, string? returnUrl)
        {
            var target = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Home/Updates";
            return await CreateStatus(statusImage, caption, target);
        }

        [HttpGet("/Privacy")]
        [HttpGet("/Home/Privacy")]
        public IActionResult Privacy()
        {
            return View();
        }

        [HttpGet("/Settings")]
        [HttpGet("/Home/Settings")]
        public IActionResult Settings(string? tab)
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");
            if (!string.IsNullOrEmpty(tab))
                return RedirectToAction("Profile", new { tab });
            return RedirectToAction("Profile");
        }

        [HttpGet("/Profile")]
        [HttpGet("/Home/Profile")]
        public IActionResult Profile(string? tab)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var user = _userService.GetUserById(userId.Value);
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.ActiveTab = string.IsNullOrWhiteSpace(tab) ? "profile" : tab.ToLower();
            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> Profile(string username, string? profileInfo, IFormFile? avatarFile, bool removeAvatar = false)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var user = _userService.GetUserById(userId.Value);
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                TempData["Error"] = "Username cannot be empty";
                return RedirectToAction("Profile");
            }

            var trimmedUsername = username.Trim();
            var existingUser = _userService.GetAllUsers()
                .FirstOrDefault(u => u.Id != user.Id && u.Username.Equals(trimmedUsername, StringComparison.OrdinalIgnoreCase));
            if (existingUser != null)
            {
                TempData["Error"] = "Username is already taken";
                return RedirectToAction("Profile");
            }

            if (removeAvatar)
            {
                user.Avatar = "";
            }
            else if (avatarFile != null && avatarFile.Length > 0)
            {
                var ext = Path.GetExtension(avatarFile.FileName).ToLowerInvariant();
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                if (!allowedExtensions.Contains(ext) || (avatarFile.ContentType != null && !avatarFile.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
                {
                    TempData["Error"] = "Only image files (.jpg, .jpeg, .png, .gif, .webp) are allowed.";
                    return RedirectToAction("Profile");
                }

                try
                {
                    user.Avatar = await _cloudinary.UploadImageAsync(avatarFile, "xenchat/avatars");
                }
                catch (Exception ex)
                {
                    TempData["Error"] = $"Image upload failed: {ex.Message}";
                    return RedirectToAction("Profile");
                }
            }

            user.Username = trimmedUsername;
            user.ProfileInfo = profileInfo?.Trim() ?? "";

            _db.SaveChanges();

            HttpContext.Session.SetString("Username", user.Username);
            TempData["Success"] = "Profile updated successfully!";

            return RedirectToAction("Profile");
        }

        [HttpPost]
        public async Task<IActionResult> CreateStatus(IFormFile statusImage, string? caption, string? returnUrl)
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            var authHeader = Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var principal = _userService.ValidateToken(token);
                if (principal != null)
                {
                    var idClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                  ?? principal.FindFirst("id")?.Value;
                    if (int.TryParse(idClaim, out int tokenUserId))
                    {
                        var tokenUser = _userService.GetUserById(tokenUserId);
                        if (tokenUser != null)
                        {
                            userId = tokenUserId;
                            HttpContext.Session.SetInt32("UserId", tokenUser.Id);
                            HttpContext.Session.SetString("Username", tokenUser.Username);
                        }
                    }
                }
            }

            var isJson = Request.Headers["Accept"].ToString().Contains("application/json") ||
                         Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            if (userId == null)
            {
                if (isJson) return Unauthorized(new { success = false, error = "Not logged in" });
                return RedirectToAction("Login", "Account");
            }

            var user = _userService.GetUserById(userId.Value);
            if (user == null)
            {
                if (isJson) return Unauthorized(new { success = false, error = "User not found" });
                return RedirectToAction("Login", "Account");
            }

            if (statusImage != null && statusImage.Length > 0)
            {
                var ext = Path.GetExtension(statusImage.FileName).ToLowerInvariant();
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                if (allowed.Contains(ext))
                {
                    string mediaUrl;
                    try
                    {
                        mediaUrl = await _cloudinary.UploadImageAsync(statusImage, "xenchat/status");
                    }
                    catch (Exception ex)
                    {
                        if (isJson) return Json(new { success = false, error = $"Image upload failed: {ex.Message}" });
                        TempData["Error"] = $"Image upload failed: {ex.Message}";
                        return RedirectToAction("AddStatus");
                    }

                    var status = new Status
                    {
                        UserId = user.Id,
                        Username = user.Username,
                        UserAvatar = user.Avatar,
                        MediaUrl = mediaUrl,
                        Caption = caption?.Trim(),
                        CreatedAt = DateTime.Now
                    };

                    _db.Statuses.Add(status);
                    _db.SaveChanges();

                    if (isJson)
                    {
                        return Json(new { success = true, mediaUrl = status.MediaUrl, caption = status.Caption, username = status.Username, time = status.CreatedAt.ToString("HH:mm") });
                    }
                }
                else
                {
                    if (isJson) return Json(new { success = false, error = "Invalid image format. Allowed: JPG, PNG, GIF, WebP." });
                    TempData["Error"] = "Invalid image format. Allowed: JPG, PNG, GIF, WebP.";
                    return RedirectToAction("AddStatus");
                }
            }
            else
            {
                if (isJson) return Json(new { success = false, error = "No image file provided." });
                TempData["Error"] = "Please select an image file to share in your status.";
                return RedirectToAction("AddStatus");
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Updates");
        }
    }
}