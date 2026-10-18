using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Lưu user đang đăng nhập trong suốt vòng đời ứng dụng.
    /// Dùng static để mọi Window đều truy cập được.
    /// </summary>
    public static class SessionService
    {
        public static User? CurrentUser { get; private set; }

        public static bool IsLoggedIn => CurrentUser != null;
        public static bool IsAdmin => CurrentUser?.Role == UserRole.Admin;

        public static void Login(User user) => CurrentUser = user;

        public static void Logout() => CurrentUser = null;
    }
}