namespace XenChat.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string ProfileInfo { get; set; }
        public string Avatar { get; set; }

        public string AvatarPath => string.IsNullOrWhiteSpace(Avatar)
            ? "/images/avatars/user.png"
            : (Avatar.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || Avatar.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || Avatar.StartsWith("//")
                ? Avatar
                : (Avatar.StartsWith("/")
                    ? Avatar
                    : (Avatar.StartsWith("images/") ? "/" + Avatar : $"/images/avatars/{Avatar}")));
        public string Initial => string.IsNullOrEmpty(Username) ? "?" : Username.Substring(0, 1).ToUpper();
    }
}