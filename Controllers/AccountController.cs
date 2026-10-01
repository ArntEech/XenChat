using Microsoft.AspNetCore.Mvc;
using XenChat.Models;
using XenChat.Services;

namespace XenChat.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserService _userService;
        private readonly PendingSignupStore _pendingStore;
        private readonly EmailService _emailService;

        public AccountController(
            UserService userService,
            PendingSignupStore pendingStore,
            EmailService emailService)
        {
            _userService = userService;
            _pendingStore = pendingStore;
            _emailService = emailService;
        }

        [HttpGet]
        public IActionResult Login()
        {
            HttpContext.Session.Clear();
            return View();
        }

        [HttpPost]
        public IActionResult Login(string email, string password)
        {
            System.Diagnostics.Debug.WriteLine("====================================================");
            System.Diagnostics.Debug.WriteLine($"[Login] email='{email}' password='{password}'");

            var allUsers = _userService.GetAllUsers();
            System.Diagnostics.Debug.WriteLine($"[Login] Loaded {allUsers.Count} users:");
            foreach (var u in allUsers)
            {
                System.Diagnostics.Debug.WriteLine($"[Login]    Id={u.Id} | {u.Username} | {u.Email}");
            }

            var user = _userService.Authenticate(email, password);
            if (user != null)
            {
                System.Diagnostics.Debug.WriteLine($"[Login] SUCCESS → {user.Username}");
                HttpContext.Session.SetInt32("UserId", user.Id);
                HttpContext.Session.SetString("Username", user.Username);
                return RedirectToAction("Index", "Home");
            }

            System.Diagnostics.Debug.WriteLine("[Login] FAILED — no match");
            System.Diagnostics.Debug.WriteLine("====================================================");
            ViewBag.Error = "Invalid email or password";
            return View();
        }

        [HttpGet]
        public IActionResult Signup() => View();

        [HttpPost]
        public async Task<IActionResult> Signup(string username, string email, string password, string confirmPassword)
        {
            System.Diagnostics.Debug.WriteLine("====================================================");
            System.Diagnostics.Debug.WriteLine($"[Signup POST] username='{username}', email='{email}'");

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "All fields are required";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match";
                return View();
            }

            var existing = _userService.GetAllUsers()
                .FirstOrDefault(u =>
                    u.Username.Equals(username, StringComparison.OrdinalIgnoreCase) ||
                    (u.Email != null && u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                System.Diagnostics.Debug.WriteLine($"[Signup POST] User already exists: {existing.Email}");
                ViewBag.Error = "Username or email already taken";
                return View();
            }

            var otp = new Random().Next(100000, 999999).ToString();
            System.Diagnostics.Debug.WriteLine($"[Signup POST] Generated OTP = {otp}");

            _pendingStore.Save(email, new PendingSignup
            {
                Username = username,
                Email = email,
                Password = password,
                Otp = otp,
                ExpiresAt = DateTime.Now.AddMinutes(5)
            });

            System.Diagnostics.Debug.WriteLine($"[Signup POST] PendingSignup stored for {email}");

            try
            {
                await _emailService.SendOtpAsync(email, otp);
                System.Diagnostics.Debug.WriteLine($"[Signup POST] OTP email sent successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Signup POST] Email failed: {ex.Message}");
                ViewBag.Error = "Could not send verification email";
                return View();
            }

            TempData["VerifyEmail"] = email;
            return RedirectToAction("Verify");
        }

        [HttpGet]
        public IActionResult Verify()
        {
            var email = TempData["VerifyEmail"]?.ToString();
            System.Diagnostics.Debug.WriteLine($"[Verify GET] email={email}");

            if (string.IsNullOrEmpty(email))
                return RedirectToAction("Signup");

            TempData.Keep("VerifyEmail");
            ViewBag.Email = email;
            return View();
        }

        [HttpPost]
        public IActionResult Verify(string code)
        {
            var email = TempData["VerifyEmail"]?.ToString();
            System.Diagnostics.Debug.WriteLine("====================================================");
            System.Diagnostics.Debug.WriteLine($"[Verify POST] email='{email}' code='{code}'");

            if (string.IsNullOrEmpty(email))
            {
                System.Diagnostics.Debug.WriteLine("[Verify POST] No email in TempData → redirecting to Signup");
                return RedirectToAction("Signup");
            }

            var pending = _pendingStore.Get(email);
            if (pending == null)
            {
                System.Diagnostics.Debug.WriteLine("[Verify POST] No pending signup found");
                TempData["VerifyEmail"] = email;
                TempData["VerifyError"] = "No pending signup found";
                return RedirectToAction("Verify");
            }

            System.Diagnostics.Debug.WriteLine($"[Verify POST] Pending found. Expected OTP='{pending.Otp}', expires={pending.ExpiresAt}");

            if (DateTime.Now > pending.ExpiresAt)
            {
                System.Diagnostics.Debug.WriteLine("[Verify POST] OTP expired");
                _pendingStore.Remove(email);
                TempData["VerifyEmail"] = email;
                TempData["VerifyError"] = "Code expired";
                return RedirectToAction("Verify");
            }

            if (pending.Otp != code?.Trim())
            {
                System.Diagnostics.Debug.WriteLine($"[Verify POST] Wrong OTP. Expected='{pending.Otp}', got='{code}'");
                TempData["VerifyEmail"] = email;
                TempData["VerifyError"] = "Invalid code";
                return RedirectToAction("Verify");
            }

            System.Diagnostics.Debug.WriteLine($"[Verify POST] OTP correct → calling CreateUser for {pending.Username} / {pending.Email}");

            _userService.CreateUser(new User
            {
                Username = pending.Username,
                Email = pending.Email,
                Password = pending.Password,
                ProfileInfo = pending.Username,
                Avatar = "user.png"
            });

            System.Diagnostics.Debug.WriteLine("[Verify POST] CreateUser returned");

            var check = _userService.GetAllUsers();
            System.Diagnostics.Debug.WriteLine($"[Verify POST] After create, total users = {check.Count}");

            _pendingStore.Remove(email);

            return RedirectToAction("AccountCreated");
        }

        [HttpGet]
        public IActionResult AccountCreated() => View();

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}