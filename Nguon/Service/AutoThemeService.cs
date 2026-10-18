using System;
using System.Windows;
using System.Windows.Threading;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Tự động đổi theme Sáng/Tối theo giờ.
    /// Đồng bộ trên TẤT CẢ window: Main, Admin, Login, Register, ChangePassword, EditAccount.
    /// </summary>
    public static class AutoThemeService
    {
        public static bool IsEnabled { get; private set; } = false;

        private static DispatcherTimer? _timer;

        /// <summary>Giờ bắt đầu dùng Light theme (mặc định 6h).</summary>
        public static int LightStartHour { get; set; } = 6;

        /// <summary>Giờ bắt đầu dùng Dark theme (mặc định 18h).</summary>
        public static int DarkStartHour { get; set; } = 18;

        // ================== KHỞI ĐỘNG / DỪNG ==================

        public static void Start()
        {
            IsEnabled = true;

            ApplyCurrentThemeByTime();

            _timer?.Stop();
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _timer.Tick += (_, _) => ApplyCurrentThemeByTime();
            _timer.Start();
        }

        public static void Stop()
        {
            IsEnabled = false;
            _timer?.Stop();
            _timer = null;
        }

        // ================== ÁP DỤNG THEO GIỜ ==================

        private static void ApplyCurrentThemeByTime()
        {
            if (!IsEnabled) return;

            int hour = DateTime.Now.Hour;
            bool shouldBeLight = hour >= LightStartHour && hour < DarkStartHour;

            // Chỉ áp dụng khi theme thay đổi → tránh refresh liên tục
            if (shouldBeLight && ThemeService.IsDarkMode)
            {
                ThemeService.ApplyLight(save: false);
                LogAutoSwitch("Light");
            }
            else if (!shouldBeLight && !ThemeService.IsDarkMode)
            {
                ThemeService.ApplyDark(save: false);
                LogAutoSwitch("Dark");
            }
        }

        /// <summary>
        /// Ghi log khi auto theme đổi.
        /// </summary>
        private static void LogAutoSwitch(string themeName)
        {
            try
            {
                ActivityLogger.Log("SYSTEM", "AUTO_THEME_SWITCH",
                    $"Tự động đổi theme sang {themeName} lúc {DateTime.Now:HH:mm}");
            }
            catch { }
        }

        public static string GetCurrentThemeByTime()
        {
            int hour = DateTime.Now.Hour;
            bool isLight = hour >= LightStartHour && hour < DarkStartHour;
            return isLight ? "Light" : "Dark";
        }
    }
}