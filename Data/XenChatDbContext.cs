using Microsoft.EntityFrameworkCore;
using XenChat.Models;

namespace XenChat.Data
{
    public class XenChatDbContext : DbContext
    {
        public XenChatDbContext(DbContextOptions<XenChatDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Message> Messages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Message>()
                .HasKey(m => m.MessageId);

            modelBuilder.Entity<User>().HasData(
                new User { Id = 1,  Username = "caleb",      Email = "caleb@gmail.com",      Password = "password123", ProfileInfo = "caleb",      Avatar = "caleb.png" },
                new User { Id = 2,  Username = "arnold",     Email = "arnold@gmail.com",     Password = "password123", ProfileInfo = "arnold",     Avatar = "arnold.png" },
                new User { Id = 3,  Username = "francis",    Email = "francis@gmail.com",    Password = "password123", ProfileInfo = "francis",    Avatar = "francis.png" },
                new User { Id = 4,  Username = "joana",      Email = "joana@gmail.com",      Password = "password123", ProfileInfo = "joana",      Avatar = "joana.png" },
                new User { Id = 5,  Username = "armanullah", Email = "armanullah@gmail.com", Password = "password123", ProfileInfo = "armanullah", Avatar = "armanullah.png" },
                new User { Id = 6,  Username = "afia",       Email = "afia@gmail.com",       Password = "password123", ProfileInfo = "afia",       Avatar = "afia.png" },
                new User { Id = 7,  Username = "amoako",     Email = "amoako@gmail.com",     Password = "password123", ProfileInfo = "amoako",     Avatar = "amoako.png" },
                new User { Id = 8,  Username = "benedict",   Email = "benedict@gmail.com",   Password = "password123", ProfileInfo = "benedict",   Avatar = "benedict.png" },
                new User { Id = 9,  Username = "philemon",   Email = "philemon@gmail.com",   Password = "password123", ProfileInfo = "philemon",   Avatar = "philemon.png" },
                new User { Id = 10, Username = "akan",       Email = "akan@gmail.com",       Password = "password123", ProfileInfo = "akan",       Avatar = "akan.png" },
                new User { Id = 11, Username = "panford",    Email = "panford@gmail.com",    Password = "password123", ProfileInfo = "panford",    Avatar = "panford.png" },
                new User { Id = 12, Username = "drey",       Email = "drey@gmail.com",       Password = "password123", ProfileInfo = "drey",       Avatar = "drey.png" },
                new User { Id = 13, Username = "guest",      Email = "guest@gmail.com",      Password = "password123", ProfileInfo = "guest",      Avatar = "user.png" }
            );
        }
    }
}