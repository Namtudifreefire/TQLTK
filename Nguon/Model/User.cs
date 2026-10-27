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

        // Bảo mật đăng nhập
        /// <summary>
        /// Cờ 2FA được lưu cùng hồ sơ người dùng để việc mất file cấu hình không vô hiệu hóa 2FA.
        /// </summary>
        public bool TwoFactorEnabled { get; set; } = false;

        /// <summary>
        /// true: phải đổi mật khẩu trước khi vào app
        /// (tài khoản cũ còn dùng mật khẩu mặc định hoặc vừa được admin reset).
        /// </summary>
        public bool MustChangePassword { get; set; } = false;

        /// <summary>Số lần nhập sai mật khẩu liên tiếp (về 0 khi đăng nhập đúng hoặc khi bị khóa tạm).</summary>
        public int FailedLoginCount { get; set; } = 0;

        /// <summary>Khóa tạm thời đến thời điểm này (do nhập sai mật khẩu nhiều lần). Khác với IsActive do admin khóa.</summary>
        public DateTime? LockedUntil { get; set; }
    }
}
