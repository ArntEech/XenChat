namespace XenChat.Models
{
    public class Status
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? UserAvatar { get; set; }
        public string MediaUrl { get; set; } = string.Empty;
        public string? Caption { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
