using System;
using System.Windows;
using System.Windows.Threading;

namespace PersonalInfoManager.Services
{

    public static class AutoThemeService
    {
        public static bool IsEnabled { get; private set; } = false;

        private static DispatcherTimer? _timer;

        public static int LightStartHour { get; set; } = 6;

        public static int DarkStartHour { get; set; } = 18;

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

        private static void ApplyCurrentThemeByTime()
        {
            if (!IsEnabled) return;

            int hour = DateTime.Now.Hour;
            bool shouldBeLight = hour >= LightStartHour && hour < DarkStartHour;

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