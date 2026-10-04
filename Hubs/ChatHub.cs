using Microsoft.AspNetCore.SignalR;

namespace XenChat.Hubs
{
    public class ChatHub : Hub
    {
        public async Task SendMessage(int senderId, int receiverId, string message, string senderName, int messageId = 0)
        {
            await Clients.All.SendAsync("ReceiveMessage", senderId, receiverId, message, senderName, DateTime.Now.ToString("HH:mm"), messageId);
        }

        public async Task DeleteMessage(int messageId, int senderId, int receiverId)
        {
            await Clients.All.SendAsync("MessageDeleted", messageId, senderId, receiverId);
        }
    }
}