using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace XenChat.Hubs
{
    public class ChatHub : Hub
    {
        // Thread-safe online user tracking: userId -> set of active connectionIds
        private static readonly ConcurrentDictionary<int, HashSet<string>> _onlineUsers = new();
        private static readonly ConcurrentDictionary<string, int> _connectionUserMap = new();

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_connectionUserMap.TryRemove(Context.ConnectionId, out int userId))
            {
                if (_onlineUsers.TryGetValue(userId, out var connections))
                {
                    lock (connections)
                    {
                        connections.Remove(Context.ConnectionId);
                        if (connections.Count == 0)
                        {
                            _onlineUsers.TryRemove(userId, out _);
                        }
                    }

                    if (!_onlineUsers.ContainsKey(userId))
                    {
                        await Clients.All.SendAsync("UserStatusChanged", userId, false);
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task RegisterUser(int userId)
        {
            _connectionUserMap[Context.ConnectionId] = userId;
            var connections = _onlineUsers.GetOrAdd(userId, _ => new HashSet<string>());
            lock (connections)
            {
                connections.Add(Context.ConnectionId);
            }

            await Clients.All.SendAsync("UserStatusChanged", userId, true);
            await Clients.Caller.SendAsync("OnlineUsersList", _onlineUsers.Keys.ToList());
        }

        public async Task GetOnlineUsers()
        {
            await Clients.Caller.SendAsync("OnlineUsersList", _onlineUsers.Keys.ToList());
        }

        public async Task SetUserStatus(int userId, bool isOnline)
        {
            if (!isOnline)
            {
                _onlineUsers.TryRemove(userId, out _);
            }
            else
            {
                var connections = _onlineUsers.GetOrAdd(userId, _ => new HashSet<string>());
                lock (connections)
                {
                    connections.Add(Context.ConnectionId);
                }
            }
            await Clients.All.SendAsync("UserStatusChanged", userId, isOnline);
        }

        public async Task SendMessage(int senderId, int receiverId, string message, string senderName, int messageId = 0)
        {
            var targetConnections = new HashSet<string>();
            if (_onlineUsers.TryGetValue(senderId, out var sConns))
            {
                lock (sConns) { targetConnections.UnionWith(sConns); }
            }
            if (_onlineUsers.TryGetValue(receiverId, out var rConns))
            {
                lock (rConns) { targetConnections.UnionWith(rConns); }
            }

            if (targetConnections.Count > 0)
            {
                await Clients.Clients(targetConnections.ToList()).SendAsync("ReceiveMessage", senderId, receiverId, message, senderName, DateTime.Now.ToString("HH:mm"), messageId);
            }
            else
            {
                await Clients.All.SendAsync("ReceiveMessage", senderId, receiverId, message, senderName, DateTime.Now.ToString("HH:mm"), messageId);
            }
        }

        public async Task DeleteMessage(int messageId, int senderId, int receiverId)
        {
            await Clients.All.SendAsync("MessageDeleted", messageId, senderId, receiverId);
        }

        // WebRTC Signaling (Audio & Video)
        public async Task CallUser(int callerId, int targetUserId, string callerName, bool isVideo = false)
        {
            await Clients.All.SendAsync("IncomingCall", callerId, targetUserId, callerName, isVideo);
        }

        public async Task AnswerCall(int callerId, int targetUserId, bool accepted, bool isVideo = false)
        {
            await Clients.All.SendAsync("CallAnswered", callerId, targetUserId, accepted, isVideo);
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