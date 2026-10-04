using XenChat.Data;
using XenChat.Models;

namespace XenChat.Services
{
    public class MessageService
    {
        private readonly XenChatDbContext _db;

        public MessageService(XenChatDbContext db)
        {
            _db = db;
        }

        public List<Message> GetAllMessages()
        {
            return _db.Messages.ToList();
        }

        public List<Message> GetConversation(int user1Id, int user2Id)
        {
            return _db.Messages
                .Where(m => (m.SenderId == user1Id && m.ReceiverId == user2Id) ||
                            (m.SenderId == user2Id && m.ReceiverId == user1Id))
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        public void SendMessage(Message message)
        {
            message.Timestamp = DateTime.Now;
            _db.Messages.Add(message);
            _db.SaveChanges();
        }

        public string GetLastMessage(int user1Id, int user2Id)
        {
            var msg = _db.Messages
                .Where(m => (m.SenderId == user1Id && m.ReceiverId == user2Id) ||
                            (m.SenderId == user2Id && m.ReceiverId == user1Id))
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefault();

            return msg?.Content ?? "No messages yet";
        }

        public void MarkConversationAsRead(int senderId, int receiverId)
        {
            var unread = _db.Messages
                .Where(m => m.SenderId == senderId && m.ReceiverId == receiverId && !m.IsRead)
                .ToList();

            if (unread.Any())
            {
                foreach (var msg in unread)
                {
                    msg.IsRead = true;
                }
                _db.SaveChanges();
            }
        }
    }
}