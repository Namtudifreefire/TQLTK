using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{

    public static class SessionService
    {
        public static User? CurrentUser { get; private set; }

        public static bool IsLoggedIn => CurrentUser != null;
        public static bool IsAdmin => CurrentUser?.Role == UserRole.Admin;

        public static void Login(User user) => CurrentUser = user;

        public static void Logout() => CurrentUser = null;
    }
}