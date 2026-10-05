using Microsoft.AspNetCore.Mvc;
using XenChat.Data;
using XenChat.Models;
using XenChat.Services;

namespace XenChat.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserService _userService;
        private readonly PendingSignupStore _pendingStore;
        private readonly EmailService _emailService;
        private readonly GoogleAuthService _googleAuthService;
        private readonly XenChatDbContext _db;

        public AccountController(
            UserService userService,
            PendingSignupStore pendingStore,
            EmailService emailService,
            GoogleAuthService googleAuthService,
            XenChatDbContext db)
        {
            _userService = userService;
            _pendingStore = pendingStore;
            _emailService = emailService;
            _googleAuthService = googleAuthService;
            _db = db;
        }

        [HttpGet]
        public IActionResult Login(string? logout)
        {
            ViewBag.GoogleClientId = _googleAuthService.ClientId;

            if (logout == "true")
            {
                HttpContext.Session.Clear();
                return View();
            }

            if (HttpContext.Session.GetInt32("UserId") != null)
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        [HttpPost]
        public IActionResult Login(string email, string password)
        {
            var isJsonRequest = Request.Headers["Accept"].ToString().Contains("application/json") ||
                                Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                if (isJsonRequest)
                {
                    return Json(new { success = false, error = "Please enter both email and password" });
                }
                ViewBag.Error = "Please enter both email and password";
                return View();
            }

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
                var token = _userService.GenerateToken(user);

                if (isJsonRequest)
                {
                    return Json(new { success = true, token, username = user.Username, userId = user.Id });
                }

                ViewBag.Token = token;
                return View();
            }

            System.Diagnostics.Debug.WriteLine("[Login] FAILED — no match");
            System.Diagnostics.Debug.WriteLine("====================================================");
            if (isJsonRequest)
            {
                return Json(new { success = false, error = "Invalid email or password" });
            }
            ViewBag.Error = "Invalid email or password";
            return View();
        }

        [HttpGet]
        public IActionResult Signup()
        {
            ViewBag.GoogleClientId = _googleAuthService.ClientId;
            return View();
        }

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
        public IActionResult Verify(string code, string? email)
        {
            if (string.IsNullOrEmpty(email))
            {
                email = TempData["VerifyEmail"]?.ToString();
            }

            System.Diagnostics.Debug.WriteLine("====================================================");
            System.Diagnostics.Debug.WriteLine($"[Verify POST] email='{email}' code='{code}'");

            if (string.IsNullOrEmpty(email))
            {
                System.Diagnostics.Debug.WriteLine("[Verify POST] No email provided → redirecting to Signup");
                return RedirectToAction("Signup");
            }

            var pending = _pendingStore.Get(email);
            if (pending == null)
            {
                System.Diagnostics.Debug.WriteLine("[Verify POST] No pending signup found");
                TempData["VerifyEmail"] = email;
                TempData["VerifyError"] = "No pending signup found or session expired. Please sign up again.";
                return RedirectToAction("Verify");
            }

            System.Diagnostics.Debug.WriteLine($"[Verify POST] Pending found. Expected OTP='{pending.Otp}', expires={pending.ExpiresAt}");

            if (DateTime.Now > pending.ExpiresAt)
            {
                System.Diagnostics.Debug.WriteLine("[Verify POST] OTP expired");
                _pendingStore.Remove(email);
                TempData["VerifyEmail"] = email;
                TempData["VerifyError"] = "Verification code has expired. Please sign up again.";
                return RedirectToAction("Verify");
            }

            if (pending.Otp != code?.Trim())
            {
                System.Diagnostics.Debug.WriteLine($"[Verify POST] Wrong OTP. Expected='{pending.Otp}', got='{code}'");
                TempData["VerifyEmail"] = email;
                TempData["VerifyError"] = "Invalid verification code. Please check and try again.";
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

            _pendingStore.Remove(email);
            TempData["SignupSuccess"] = "Account created successfully! Please log in.";

            return RedirectToAction("AccountCreated");
        }

        [HttpPost]
        public async Task<IActionResult> ResendCode(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                email = TempData["VerifyEmail"]?.ToString();
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return Json(new { success = false, error = "Email address is required." });
            }

            var pending = _pendingStore.Get(email);
            if (pending == null)
            {
                return Json(new { success = false, error = "No pending registration found for this email." });
            }

            var otp = new Random().Next(100000, 999999).ToString();
            pending.Otp = otp;
            pending.ExpiresAt = DateTime.Now.AddMinutes(5);
            _pendingStore.Save(email, pending);

            TempData["VerifyEmail"] = email;

            try
            {
                await _emailService.SendOtpAsync(email, otp);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResendCode] Failed to send email: {ex.Message}");
            }

            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult AccountCreated() => View();

        [HttpPost]
        public IActionResult RestoreSession()
        {
            string? token = null;
            var authHeader = Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader.Substring("Bearer ".Length).Trim();
            }
            else if (Request.HasFormContentType && Request.Form.ContainsKey("token"))
            {
                token = Request.Form["token"];
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return Unauthorized(new { success = false, error = "No token provided" });
            }

            var principal = _userService.ValidateToken(token);
            if (principal == null)
            {
                return Unauthorized(new { success = false, error = "Invalid or expired token" });
            }

            var idClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? principal.FindFirst("id")?.Value;

            if (int.TryParse(idClaim, out int userId))
            {
                var user = _userService.GetUserById(userId);
                if (user != null)
                {
                    HttpContext.Session.SetInt32("UserId", user.Id);
                    HttpContext.Session.SetString("Username", user.Username);
                    return Ok(new { success = true, userId = user.Id, username = user.Username });
                }
            }

            return Unauthorized(new { success = false, error = "User not found" });
        }

        [HttpGet("/auth/google")]
        [HttpGet("/Account/GoogleLogin")]
        public IActionResult GoogleLogin(string? returnUrl)
        {
            if (!_googleAuthService.IsConfigured)
            {
                TempData["Error"] = "Google OAuth is not configured yet. Please set GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET in your .env or environment variables.";
                return RedirectToAction("Login");
            }

            var state = Guid.NewGuid().ToString("N");
            HttpContext.Session.SetString("GoogleAuthState", state);
            if (!string.IsNullOrEmpty(returnUrl))
            {
                HttpContext.Session.SetString("GoogleAuthReturnUrl", returnUrl);
            }

            var redirectUri = _googleAuthService.GetRedirectUri(Request);
            var authUrl = _googleAuthService.GetAuthorizationUrl(redirectUri, state);
            return Redirect(authUrl);
        }

        [HttpGet("/auth/google/callback")]
        [HttpPost("/auth/google/callback")]
        public async Task<IActionResult> GoogleCallback(
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error,
            [FromForm] string? credential,
            [FromQuery] string? returnUrl)
        {
            if (!string.IsNullOrEmpty(error))
            {
                TempData["Error"] = $"Google sign-in was cancelled or denied ({error}).";
                return RedirectToAction("Login");
            }

            GoogleUserInfo? userInfo = null;

            // 1. Google Identity Services (One Tap) credential response
            if (!string.IsNullOrEmpty(credential))
            {
                userInfo = await _googleAuthService.ValidateCredentialAsync(credential);
            }
            // 2. Standard OAuth 2.0 authorization code flow
            else if (!string.IsNullOrEmpty(code))
            {
                var savedState = HttpContext.Session.GetString("GoogleAuthState");
                HttpContext.Session.Remove("GoogleAuthState");

                if (!string.IsNullOrEmpty(savedState) && !string.Equals(savedState, state, StringComparison.Ordinal))
                {
                    TempData["Error"] = "Invalid Google OAuth state. Please try logging in again.";
                    return RedirectToAction("Login");
                }

                var redirectUri = _googleAuthService.GetRedirectUri(Request);
                userInfo = await _googleAuthService.ExchangeCodeForUserInfoAsync(code, redirectUri);
            }

            if (userInfo == null || string.IsNullOrWhiteSpace(userInfo.Email))
            {
                TempData["Error"] = "Could not authenticate with Google. Please check your credentials and try again.";
                return RedirectToAction("Login");
            }

            var normalizedEmail = userInfo.Email.Trim().ToLowerInvariant();
            var existingUser = _db.Users.FirstOrDefault(u => u.Email.ToLower() == normalizedEmail);

            User user;
            if (existingUser != null)
            {
                user = existingUser;

                // If user doesn't have an avatar yet or has default, set Google picture
                if ((string.IsNullOrWhiteSpace(user.Avatar) || user.Avatar == "user.png") && !string.IsNullOrWhiteSpace(userInfo.Picture))
                {
                    user.Avatar = userInfo.Picture;
                    _db.SaveChanges();
                }
            }
            else
            {
                // New user signing up with Google OAuth
                string baseUsername = !string.IsNullOrWhiteSpace(userInfo.GivenName)
                    ? userInfo.GivenName.Trim().ToLowerInvariant()
                    : (!string.IsNullOrWhiteSpace(userInfo.Name)
                        ? userInfo.Name.Replace(" ", "").Trim().ToLowerInvariant()
                        : normalizedEmail.Split('@')[0].ToLowerInvariant());

                baseUsername = System.Text.RegularExpressions.Regex.Replace(baseUsername, @"[^a-z0-9]", "");
                if (string.IsNullOrWhiteSpace(baseUsername))
                {
                    baseUsername = "user";
                }

                string uniqueUsername = baseUsername;
                int counter = 1;
                while (_db.Users.Any(u => u.Username.ToLower() == uniqueUsername.ToLower()))
                {
                    uniqueUsername = $"{baseUsername}{counter++}";
                }

                var randomPassword = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

                user = new User
                {
                    Username = uniqueUsername,
                    Email = normalizedEmail,
                    Password = randomPassword,
                    ProfileInfo = "Hey! I am using XenChat.",
                    Avatar = !string.IsNullOrWhiteSpace(userInfo.Picture) ? userInfo.Picture : "user.png"
                };

                _userService.CreateUser(user);
            }

            // Set session
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("Username", user.Username);

            // Generate JWT token
            var token = _userService.GenerateToken(user);

            var targetUrl = returnUrl;
            if (string.IsNullOrEmpty(targetUrl))
            {
                targetUrl = HttpContext.Session.GetString("GoogleAuthReturnUrl");
                HttpContext.Session.Remove("GoogleAuthReturnUrl");
            }

            if (string.IsNullOrEmpty(targetUrl) || !Url.IsLocalUrl(targetUrl))
            {
                targetUrl = Url.Action("Index", "Home") ?? "/";
            }

            ViewBag.Token = token;
            ViewBag.TargetUrl = targetUrl;
            ViewBag.Username = user.Username;

            return View("GoogleSuccess");
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", new { logout = "true" });
        }
    }
}