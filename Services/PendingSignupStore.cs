using XenChat.Models;

namespace XenChat.Services
{
    public class PendingSignupStore
    {
        private readonly Dictionary<string, PendingSignup> _pending = new();
        private readonly object _lock = new();

        public void Save(string email, PendingSignup signup)
        {
            lock (_lock)
            {
                _pending[email.ToLower()] = signup;
            }
        }

        public PendingSignup Get(string email)
        {
            lock (_lock)
            {
                return _pending.TryGetValue(email.ToLower(), out var signup) ? signup : null;
            }
        }

        public void Remove(string email)
        {
            lock (_lock)
            {
                _pending.Remove(email.ToLower());
            }
        }
    }
}