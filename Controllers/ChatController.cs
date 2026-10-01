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

            var otherUser = _userService.GetUserById(userId);
            if (otherUser == null)
                return RedirectToAction("Index", "Home");

            var messages = _messageService.GetConversation(currentUserId.Value, userId);
            var currentUser = _userService.GetUserById(currentUserId.Value);
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

                var unreadCount = userMessages.Count(m => m.SenderId == user.Id && m.ReceiverId == currentUserId.Value);
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
            if (senderId == null)
                return Json(new { success = false, error = "Not logged in" });

            if (!string.IsNullOrWhiteSpace(message))
            {
                var newMessage = new Message
                {
                    SenderId = senderId.Value,
                    ReceiverId = receiverId,
                    Content = message
                };

                _messageService.SendMessage(newMessage);

                return Json(new { success = true });
            }

            return Json(new { success = false, error = "Empty message" });
        }
    }
}