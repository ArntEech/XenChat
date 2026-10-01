using XenChat.Data;
using XenChat.Models;

namespace XenChat.Services
{
    public class UserService
    {
        private readonly XenChatDbContext _db;

        public UserService(XenChatDbContext db)
        {
            _db = db;
        }

        public List<User> GetAllUsers()
        {
            return _db.Users.ToList();
        }

        public User GetUserById(int id)
        {
            return _db.Users.FirstOrDefault(u => u.Id == id);
        }

        public User Authenticate(string email, string password)
        {
            return _db.Users.FirstOrDefault(u =>
                u.Email != null &&
                u.Email.ToLower() == email.ToLower() &&
                u.Password == password);
        }

        public void CreateUser(User user)
        {
            System.Diagnostics.Debug.WriteLine($"[CreateUser] Starting for {user.Username} / {user.Email}");

            var existing = _db.Users.FirstOrDefault(u =>
                u.Username.ToLower() == user.Username.ToLower() ||
                u.Email.ToLower() == user.Email.ToLower());

            if (existing != null)
            {
                System.Diagnostics.Debug.WriteLine($"[CreateUser] DUPLICATE. Existing Id={existing.Id}, Email={existing.Email}");
                return;
            }

            _db.Users.Add(user);
            _db.SaveChanges();

            System.Diagnostics.Debug.WriteLine($"[CreateUser] SAVED. New Id={user.Id}");
            System.Diagnostics.Debug.WriteLine($"[CreateUser] Total after save: {_db.Users.Count()}");
        }
    }
}