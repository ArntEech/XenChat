using Microsoft.AspNetCore.Mvc;
using XenChat.Models;
using XenChat.Services;

namespace XenChat.Controllers
{
    public class ChatController : Controller
    {
        private readonly UserService _userService;
        private readonly MessageService _messageService;

        public ChatController(UserService userService, MessageService messageService)
        {
            _userService = userService;
            _messageService = messageService;
        }

        public IActionResult Index(int userId)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
                return RedirectToAction("Login", "Account");

            if (userId == currentUserId.Value)
                return RedirectToAction("Index", "Home");

            var currentUser = _userService.GetUserById(currentUserId.Value);
            if (currentUser == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            var otherUser = _userService.GetUserById(userId);
            if (otherUser == null)
                return RedirectToAction("Index", "Home");

            // Mark incoming messages from otherUser as read
            _messageService.MarkConversationAsRead(userId, currentUserId.Value);

            var messages = _messageService.GetConversation(currentUserId.Value, userId);
            var allUsers = _userService.GetAllUsers().Where(u => u.Id != currentUserId.Value).ToList();

            ViewBag.CurrentUserId = currentUserId.Value;
            ViewBag.CurrentUser = currentUser;
            ViewBag.OtherUser = otherUser;

            var lastMessages = new Dictionary<int, string>();
            var lastMessageTimes = new Dictionary<int, string>();
            var unreadCounts = new Dictionary<int, int>();

            foreach (var user in allUsers)
            {
                var userMessages = _messageService.GetConversation(currentUserId.Value, user.Id);
                var lastMessage = userMessages.LastOrDefault();

                lastMessages[user.Id] = lastMessage?.Content ?? "No messages yet";
                lastMessageTimes[user.Id] = lastMessage?.Timestamp.ToString("HH:mm") ?? "";

                // Only count unread messages sent by this user to current user
                var unreadCount = userMessages.Count(m => m.SenderId == user.Id && m.ReceiverId == currentUserId.Value && !m.IsRead);
                unreadCounts[user.Id] = unreadCount;
            }

            ViewBag.LastMessages = lastMessages;
            ViewBag.LastMessageTimes = lastMessageTimes;
            ViewBag.UnreadCounts = unreadCounts;

            ViewData["AllUsers"] = allUsers;

            return View(messages);
        }

        [HttpPost]
        public IActionResult SendMessage(int receiverId, string message)
        {
            var senderId = HttpContext.Session.GetInt32("UserId");

            var authHeader = Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var principal = _userService.ValidateToken(token);
                if (principal == null)
                {
                    return Unauthorized(new { success = false, error = "Invalid or expired token" });
                }

                var idClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                              ?? principal.FindFirst("id")?.Value;

                if (int.TryParse(idClaim, out int tokenUserId))
                {
                    var tokenUser = _userService.GetUserById(tokenUserId);
                    if (tokenUser == null)
                    {
                        return Unauthorized(new { success = false, error = "User not found" });
                    }
                    senderId = tokenUserId;
                    HttpContext.Session.SetInt32("UserId", tokenUser.Id);
                    HttpContext.Session.SetString("Username", tokenUser.Username);
                }
                else
                {
                    return Unauthorized(new { success = false, error = "Invalid token payload" });
                }
            }

            if (senderId == null)
                return Unauthorized(new { success = false, error = "Not logged in" });

            if (receiverId == senderId.Value)
                return Json(new { success = false, error = "Cannot message yourself" });

            if (string.IsNullOrWhiteSpace(message))
                return Json(new { success = false, error = "Empty message" });

            var recipient = _userService.GetUserById(receiverId);
            if (recipient == null)
                return Json(new { success = false, error = "Recipient not found" });

            var newMessage = new Message
            {
                SenderId = senderId.Value,
                ReceiverId = receiverId,
                Content = message.Trim()
            };

            _messageService.SendMessage(newMessage);

            return Json(new { success = true, timestamp = newMessage.Timestamp.ToString("HH:mm") });
        }
    }
}