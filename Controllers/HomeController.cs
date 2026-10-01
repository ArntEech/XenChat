using Microsoft.AspNetCore.Mvc;
using XenChat.Services;

namespace XenChat.Controllers
{
    public class HomeController : Controller
    {
        private readonly UserService _userService;
        private readonly MessageService _messageService;

        public HomeController(UserService userService, MessageService messageService)
        {
            _userService = userService;
            _messageService = messageService;
        }

        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var users = _userService.GetAllUsers().Where(u => u.Id != userId).ToList();
            var currentUser = _userService.GetUserById(userId.Value);

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

                var unreadCount = messages.Count(m => m.SenderId == user.Id && m.ReceiverId == userId.Value);
                ViewBag.UnreadCounts[user.Id] = unreadCount;
            }

            return View(users);
        }

        public IActionResult Camera()
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");
            return View();
        }

        public IActionResult Updates()
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");
            return View();
        }

        public IActionResult Profile()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var user = _userService.GetUserById(userId.Value);
            return View(user);
        }
    }
}