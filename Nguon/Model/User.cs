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

        // Thông tin cá nhân
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";

        // Windows Hello
        public bool WindowsHelloEnabled { get; set; } = false;
        public string WindowsHelloSid { get; set; } = "";
    }
}