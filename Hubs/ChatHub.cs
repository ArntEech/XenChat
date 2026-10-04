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

        // WebRTC Signaling
        public async Task CallUser(int callerId, int targetUserId, string callerName)
        {
            await Clients.All.SendAsync("IncomingCall", callerId, targetUserId, callerName);
        }

        public async Task AnswerCall(int callerId, int targetUserId, bool accepted)
        {
            await Clients.All.SendAsync("CallAnswered", callerId, targetUserId, accepted);
        }

        public async Task SendCallOffer(int senderId, int receiverId, string sdp)
        {
            await Clients.All.SendAsync("ReceiveCallOffer", senderId, receiverId, sdp);
        }

        public async Task SendCallAnswer(int senderId, int receiverId, string sdp)
        {
            await Clients.All.SendAsync("ReceiveCallAnswer", senderId, receiverId, sdp);
        }

        public async Task SendIceCandidate(int senderId, int receiverId, string candidateJson)
        {
            await Clients.All.SendAsync("ReceiveIceCandidate", senderId, receiverId, candidateJson);
        }

        public async Task EndCall(int senderId, int receiverId)
        {
            await Clients.All.SendAsync("CallEnded", senderId, receiverId);
        }
    }
}