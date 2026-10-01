using Microsoft.AspNetCore.SignalR;

namespace XenChat.Hubs
{
    public class ChatHub : Hub
    {
        public async Task SendMessage(int senderId, int receiverId, string message, string senderName)
        {
            await Clients.All.SendAsync("ReceiveMessage", senderId, receiverId, message, senderName, DateTime.Now.ToString("HH:mm"));
        }
    }
}