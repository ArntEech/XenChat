namespace XenChat.Models
{
    public class PendingSignup
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string Otp { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}