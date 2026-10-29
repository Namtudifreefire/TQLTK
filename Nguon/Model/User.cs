using System;

namespace PersonalInfoManager.Models
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public UserRole Role { get; set; } = UserRole.User;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? LastLoginAt { get; set; }
        public bool IsActive { get; set; } = true;


        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";


        public bool WindowsHelloEnabled { get; set; } = false;
        public string WindowsHelloSid { get; set; } = "";





        public bool TwoFactorEnabled { get; set; } = false;





        public bool MustChangePassword { get; set; } = false;


        public int FailedLoginCount { get; set; } = 0;


        public DateTime? LockedUntil { get; set; }
    }
}
