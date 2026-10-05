using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using XenChat.Data;
using XenChat.Models;
using XenChat.Services;

namespace XenChat.Controllers
{
    [ApiController]
    [Route("api")]
    public class ApiController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly MessageService _messageService;
        private readonly PendingSignupStore _pendingStore;
        private readonly EmailService _emailService;
        private readonly CloudinaryService _cloudinary;
        private readonly XenChatDbContext _db;

        public ApiController(
            UserService userService,
            MessageService messageService,
            PendingSignupStore pendingStore,
            EmailService emailService,
            CloudinaryService cloudinary,
            XenChatDbContext db)
        {
            _userService = userService;
            _messageService = messageService;
            _pendingStore = pendingStore;
            _emailService = emailService;
            _cloudinary = cloudinary;
            _db = db;
        }

        private User? GetAuthenticatedUser()
        {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var principal = _userService.ValidateToken(token);
                if (principal != null)
                {
                    var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("id")?.Value;
                    if (int.TryParse(idClaim, out int tokenUserId))
                    {
                        return _userService.GetUserById(tokenUserId);
                    }
                }
            }

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId.HasValue)
            {
                return _userService.GetUserById(sessionUserId.Value);
            }

            return null;
        }

        // ==========================================
        // AUTHENTICATION ENDPOINTS
        // ==========================================

        public class LoginRequest
        {
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
        }

        [HttpPost("auth/login")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            {
                return BadRequest(new { success = false, error = "Please provide both email and password." });
            }

            var user = _userService.Authenticate(req.Email, req.Password);
            if (user == null)
            {
                return Unauthorized(new { success = false, error = "Invalid email or password." });
            }

            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("Username", user.Username);

            var token = _userService.GenerateToken(user);
            return Ok(new
            {
                success = true,
                token,
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    avatar = user.Avatar,
                    avatarPath = user.AvatarPath,
                    initial = user.Initial,
                    profileInfo = user.ProfileInfo
                }
            });
        }

        public class SignupRequest
        {
            public string Username { get; set; } = "";
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
            public string ConfirmPassword { get; set; } = "";
        }

        [HttpPost("auth/signup")]
        public async Task<IActionResult> Signup([FromBody] SignupRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) ||
                string.IsNullOrWhiteSpace(req.Email) ||
                string.IsNullOrWhiteSpace(req.Password))
            {
                return BadRequest(new { success = false, error = "All fields are required." });
            }

            if (req.Password != req.ConfirmPassword)
            {
                return BadRequest(new { success = false, error = "Passwords do not match." });
            }

            var existing = _userService.GetAllUsers()
                .FirstOrDefault(u =>
                    u.Username.Equals(req.Username.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    (u.Email != null && u.Email.Equals(req.Email.Trim(), StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                return BadRequest(new { success = false, error = "Username or email is already taken." });
            }

            var otp = new Random().Next(100000, 999999).ToString();
            _pendingStore.Save(req.Email.Trim(), new PendingSignup
            {
                Username = req.Username.Trim(),
                Email = req.Email.Trim(),
                Password = req.Password,
                Otp = otp,
                ExpiresAt = DateTime.Now.AddMinutes(10)
            });

            try
            {
                await _emailService.SendOtpAsync(req.Email.Trim(), otp);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = $"Could not send verification email: {ex.Message}" });
            }

            return Ok(new { success = true, email = req.Email.Trim(), message = "Verification code sent to your email." });
        }

        public class VerifyOtpRequest
        {
            public string Email { get; set; } = "";
            public string Code { get; set; } = "";
        }

        [HttpPost("auth/verify-otp")]
        public IActionResult VerifyOtp([FromBody] VerifyOtpRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Code))
            {
                return BadRequest(new { success = false, error = "Email and verification code are required." });
            }

            var pending = _pendingStore.Get(req.Email.Trim());
            if (pending == null)
            {
                return BadRequest(new { success = false, error = "No pending registration found or session expired. Please sign up again." });
            }

            if (DateTime.Now > pending.ExpiresAt)
            {
                _pendingStore.Remove(req.Email.Trim());
                return BadRequest(new { success = false, error = "Verification code has expired. Please sign up again." });
            }

            if (pending.Otp != req.Code.Trim())
            {
                return BadRequest(new { success = false, error = "Invalid verification code." });
            }

            var newUser = new User
            {
                Username = pending.Username,
                Email = pending.Email,
                Password = pending.Password,
                ProfileInfo = pending.Username,
                Avatar = "user.png"
            };

            _userService.CreateUser(newUser);
            _pendingStore.Remove(req.Email.Trim());

            // Authenticate newly created user
            var user = _userService.Authenticate(newUser.Email, pending.Password) ?? newUser;
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("Username", user.Username);

            var token = _userService.GenerateToken(user);
            return Ok(new
            {
                success = true,
                token,
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    avatar = user.Avatar,
                    avatarPath = user.AvatarPath,
                    initial = user.Initial,
                    profileInfo = user.ProfileInfo
                }
            });
        }

        public class ResendOtpRequest
        {
            public string Email { get; set; } = "";
        }

        [HttpPost("auth/resend-otp")]
        public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email))
            {
                return BadRequest(new { success = false, error = "Email is required." });
            }

            var pending = _pendingStore.Get(req.Email.Trim());
            if (pending == null)
            {
                return BadRequest(new { success = false, error = "No pending registration found for this email." });
            }

            var otp = new Random().Next(100000, 999999).ToString();
            pending.Otp = otp;
            pending.ExpiresAt = DateTime.Now.AddMinutes(10);
            _pendingStore.Save(req.Email.Trim(), pending);

            try
            {
                await _emailService.SendOtpAsync(req.Email.Trim(), otp);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = $"Failed to send email: {ex.Message}" });
            }

            return Ok(new { success = true, message = "Verification code resent." });
        }

        [HttpGet("auth/me")]
        public IActionResult GetMe()
        {
            var user = GetAuthenticatedUser();
            if (user == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            return Ok(new
            {
                success = true,
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    avatar = user.Avatar,
                    avatarPath = user.AvatarPath,
                    initial = user.Initial,
                    profileInfo = user.ProfileInfo
                }
            });
        }

        [HttpPost("auth/logout")]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return Ok(new { success = true });
        }

        // ==========================================
        // CHATS & OVERVIEW ENDPOINTS
        // ==========================================

        [HttpGet("chats/overview")]
        public IActionResult GetChatsOverview()
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            var userId = currentUser.Id;
            var allUsers = _userService.GetAllUsers();

            var pinnedUserIds = _db.PinnedChats
                .Where(p => p.UserId == userId)
                .Select(p => p.PinnedUserId)
                .ToHashSet();
            pinnedUserIds.Add(userId); // Automatically pin self-chat at top

            var favoriteUserIds = _db.Favorites
                .Where(f => f.UserId == userId)
                .Select(f => f.FavoriteUserId)
                .ToHashSet();

            var otherUsers = allUsers.Where(u => u.Id != userId).ToList();
            var displayUsers = new List<User> { currentUser };
            displayUsers.AddRange(otherUsers.OrderByDescending(u => pinnedUserIds.Contains(u.Id)));

            var userSummaries = new List<object>();
            foreach (var u in displayUsers)
            {
                var messages = _messageService.GetConversation(userId, u.Id);
                var lastMsg = messages.LastOrDefault();

                string lastMessageContent;
                int unreadCount;

                if (u.Id == userId)
                {
                    lastMessageContent = lastMsg?.Content ?? "Message yourself";
                    unreadCount = 0;
                }
                else
                {
                    lastMessageContent = lastMsg?.Content ?? "No messages yet";
                    unreadCount = messages.Count(m => m.SenderId == u.Id && m.ReceiverId == userId && !m.IsRead);
                }

                userSummaries.Add(new
                {
                    id = u.Id,
                    username = u.Username,
                    email = u.Email,
                    avatar = u.Avatar,
                    avatarPath = u.AvatarPath,
                    initial = u.Initial,
                    profileInfo = u.ProfileInfo,
                    isSelf = (u.Id == userId),
                    isPinned = pinnedUserIds.Contains(u.Id),
                    isFavorite = favoriteUserIds.Contains(u.Id),
                    lastMessage = lastMessageContent,
                    lastMessageTime = lastMsg?.Timestamp.ToString("HH:mm") ?? "",
                    unreadCount = unreadCount
                });
            }

            var cutoff = DateTime.Now.AddHours(-24);
            var activeStatuses = _db.Statuses
                .Where(s => s.CreatedAt >= cutoff)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new
                {
                    id = s.Id,
                    userId = s.UserId,
                    username = s.Username,
                    userAvatar = s.UserAvatar,
                    mediaUrl = s.MediaUrl,
                    caption = s.Caption,
                    createdAt = s.CreatedAt
                })
                .ToList();

            return Ok(new
            {
                success = true,
                currentUser = new
                {
                    id = currentUser.Id,
                    username = currentUser.Username,
                    email = currentUser.Email,
                    avatar = currentUser.Avatar,
                    avatarPath = currentUser.AvatarPath,
                    initial = currentUser.Initial,
                    profileInfo = currentUser.ProfileInfo
                },
                users = userSummaries,
                statuses = activeStatuses,
                pinnedUserIds = pinnedUserIds.ToList(),
                favoriteUserIds = favoriteUserIds.ToList()
            });
        }

        [HttpGet("chats/conversation/{targetUserId}")]
        public IActionResult GetConversation(int targetUserId)
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            var targetUser = _userService.GetUserById(targetUserId);
            if (targetUser == null)
            {
                return NotFound(new { success = false, error = "User not found." });
            }

            var messages = _messageService.GetConversation(currentUser.Id, targetUserId);
            _messageService.MarkConversationAsRead(currentUser.Id, targetUserId);

            var isFavorite = _db.Favorites.Any(f => f.UserId == currentUser.Id && f.FavoriteUserId == targetUserId);
            var isPinned = (currentUser.Id == targetUserId) || _db.PinnedChats.Any(p => p.UserId == currentUser.Id && p.PinnedUserId == targetUserId);

            return Ok(new
            {
                success = true,
                otherUser = new
                {
                    id = targetUser.Id,
                    username = targetUser.Username,
                    email = targetUser.Email,
                    avatar = targetUser.Avatar,
                    avatarPath = targetUser.AvatarPath,
                    initial = targetUser.Initial,
                    profileInfo = targetUser.ProfileInfo,
                    isSelf = (targetUser.Id == currentUser.Id)
                },
                isFavorite,
                isPinned,
                messages = messages.Select(m => new
                {
                    messageId = m.MessageId,
                    senderId = m.SenderId,
                    receiverId = m.ReceiverId,
                    content = m.Content,
                    timestamp = m.Timestamp.ToString("HH:mm"),
                    isRead = m.IsRead
                })
            });
        }

        public class SendMessageRequest
        {
            public int ReceiverId { get; set; }
            public string Message { get; set; } = "";
        }

        [HttpPost("chats/send-message")]
        public IActionResult SendMessage([FromBody] SendMessageRequest req)
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            if (string.IsNullOrWhiteSpace(req.Message))
            {
                return BadRequest(new { success = false, error = "Message cannot be empty." });
            }

            var saved = new Message
            {
                SenderId = currentUser.Id,
                ReceiverId = req.ReceiverId,
                Content = req.Message.Trim(),
                Timestamp = DateTime.Now,
                IsRead = false
            };
            _messageService.SendMessage(saved);

            return Ok(new
            {
                success = true,
                messageId = saved.MessageId,
                timestamp = saved.Timestamp.ToString("HH:mm"),
                content = saved.Content
            });
        }

        [HttpPost("chats/send-image")]
        [RequestSizeLimit(20_000_000)] // 20 MB max
        public async Task<IActionResult> SendImage([FromForm] int receiverId, [FromForm] IFormFile? imageFile, [FromForm] string? caption)
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            if (imageFile == null || imageFile.Length == 0)
            {
                return BadRequest(new { success = false, error = "No image file provided." });
            }

            try
            {
                var url = await _cloudinary.UploadImageAsync(imageFile, "xenchat/chat_images");
                var cleanCaption = caption?.Trim() ?? "";
                var content = string.IsNullOrEmpty(cleanCaption)
                    ? $"[img]{url}[/img]"
                    : $"[img]{url}[/img]{cleanCaption}";

                var saved = new Message
                {
                    SenderId = currentUser.Id,
                    ReceiverId = receiverId,
                    Content = content,
                    Timestamp = DateTime.Now,
                    IsRead = false
                };
                _messageService.SendMessage(saved);

                return Ok(new
                {
                    success = true,
                    messageId = saved.MessageId,
                    content = saved.Content,
                    imageUrl = url,
                    timestamp = saved.Timestamp.ToString("HH:mm")
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = $"Image upload failed: {ex.Message}" });
            }
        }

        public class DeleteMessageRequest
        {
            public int MessageId { get; set; }
        }

        [HttpPost("chats/delete-message")]
        public IActionResult DeleteMessage([FromBody] DeleteMessageRequest req)
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            var deleted = _messageService.DeleteMessage(req.MessageId, currentUser.Id);
            if (!deleted)
            {
                return BadRequest(new { success = false, error = "Cannot delete message or message not found." });
            }

            return Ok(new { success = true });
        }

        public class TargetUserRequest
        {
            public int TargetUserId { get; set; }
        }

        [HttpPost("chats/toggle-pin")]
        public IActionResult TogglePin([FromBody] TargetUserRequest req)
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            if (req.TargetUserId == currentUser.Id)
            {
                return Ok(new { success = true, isPinned = true, message = "Your personal chat is permanently pinned." });
            }

            var existing = _db.PinnedChats.FirstOrDefault(p => p.UserId == currentUser.Id && p.PinnedUserId == req.TargetUserId);
            bool isPinned;
            if (existing != null)
            {
                _db.PinnedChats.Remove(existing);
                isPinned = false;
            }
            else
            {
                _db.PinnedChats.Add(new PinnedChat
                {
                    UserId = currentUser.Id,
                    PinnedUserId = req.TargetUserId
                });
                isPinned = true;
            }

            _db.SaveChanges();
            return Ok(new { success = true, isPinned });
        }

        [HttpPost("chats/toggle-favorite")]
        public IActionResult ToggleFavorite([FromBody] TargetUserRequest req)
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            var existing = _db.Favorites.FirstOrDefault(f => f.UserId == currentUser.Id && f.FavoriteUserId == req.TargetUserId);
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
                    UserId = currentUser.Id,
                    FavoriteUserId = req.TargetUserId
                });
                isFavorite = true;
            }

            _db.SaveChanges();
            var count = _db.Favorites.Count(f => f.UserId == currentUser.Id);
            return Ok(new { success = true, isFavorite, count });
        }

        // ==========================================
        // STATUS (STORIES) ENDPOINTS
        // ==========================================

        [HttpPost("status/upload")]
        [RequestSizeLimit(25_000_000)]
        public async Task<IActionResult> UploadStatus([FromForm] IFormFile? statusImage, [FromForm] string? caption)
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            if (statusImage == null || statusImage.Length == 0)
            {
                return BadRequest(new { success = false, error = "No status image provided." });
            }

            try
            {
                var url = await _cloudinary.UploadImageAsync(statusImage, "xenchat/statuses");
                var status = new Status
                {
                    UserId = currentUser.Id,
                    Username = currentUser.Username,
                    UserAvatar = currentUser.Avatar,
                    MediaUrl = url,
                    Caption = caption?.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                _db.Statuses.Add(status);
                _db.SaveChanges();

                return Ok(new
                {
                    success = true,
                    status = new
                    {
                        id = status.Id,
                        userId = status.UserId,
                        username = status.Username,
                        userAvatar = status.UserAvatar,
                        mediaUrl = status.MediaUrl,
                        caption = status.Caption,
                        createdAt = status.CreatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = $"Status upload failed: {ex.Message}" });
            }
        }

        public class DeleteStatusRequest
        {
            public int StatusId { get; set; }
        }

        [HttpPost("status/delete")]
        public IActionResult DeleteStatus([FromBody] DeleteStatusRequest req)
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            var status = _db.Statuses.FirstOrDefault(s => s.Id == req.StatusId && s.UserId == currentUser.Id);
            if (status == null)
            {
                return NotFound(new { success = false, error = "Status not found or unauthorized." });
            }

            _db.Statuses.Remove(status);
            _db.SaveChanges();
            return Ok(new { success = true });
        }

        // ==========================================
        // USER PROFILE ENDPOINTS
        // ==========================================

        [HttpGet("user/profile")]
        public IActionResult GetProfile()
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            return Ok(new
            {
                success = true,
                user = new
                {
                    id = currentUser.Id,
                    username = currentUser.Username,
                    email = currentUser.Email,
                    avatar = currentUser.Avatar,
                    avatarPath = currentUser.AvatarPath,
                    initial = currentUser.Initial,
                    profileInfo = currentUser.ProfileInfo
                }
            });
        }

        [HttpPost("user/profile")]
        public async Task<IActionResult> UpdateProfile(
            [FromForm] string? username,
            [FromForm] string? profileInfo,
            [FromForm] IFormFile? avatarFile,
            [FromForm] bool removeAvatar = false)
        {
            var currentUser = GetAuthenticatedUser();
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, error = "Not authenticated." });
            }

            var user = _db.Users.FirstOrDefault(u => u.Id == currentUser.Id);
            if (user == null)
            {
                return NotFound(new { success = false, error = "User not found." });
            }

            if (!string.IsNullOrWhiteSpace(username))
            {
                var trimmed = username.Trim();
                var existing = _db.Users.FirstOrDefault(u => u.Id != user.Id && u.Username.ToLower() == trimmed.ToLower());
                if (existing != null)
                {
                    return BadRequest(new { success = false, error = "Username is already taken." });
                }
                user.Username = trimmed;
            }

            if (profileInfo != null)
            {
                user.ProfileInfo = profileInfo.Trim();
            }

            if (removeAvatar)
            {
                user.Avatar = "user.png";
            }
            else if (avatarFile != null && avatarFile.Length > 0)
            {
                try
                {
                    user.Avatar = await _cloudinary.UploadImageAsync(avatarFile, "xenchat/avatars");
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { success = false, error = $"Avatar upload failed: {ex.Message}" });
                }
            }

            _db.SaveChanges();
            HttpContext.Session.SetString("Username", user.Username);

            return Ok(new
            {
                success = true,
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    avatar = user.Avatar,
                    avatarPath = user.AvatarPath,
                    initial = user.Initial,
                    profileInfo = user.ProfileInfo
                }
            });
        }
    }
}
