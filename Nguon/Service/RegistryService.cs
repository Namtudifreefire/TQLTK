using System;
using System.Windows;
using Microsoft.Win32;

namespace PersonalInfoManager.Services
{
    public static class RegistryService
    {
        private const string AppKey = @"Software\TQLTK";

        public static string GetLanguage()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                return string.Equals(key?.GetValue("Language")?.ToString(), "en", StringComparison.OrdinalIgnoreCase)
                    ? "en" : "vi";
            }
            catch { return "vi"; }
        }

        public static void SetLanguage(string language)
        {
            string normalized = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("Language", normalized, RegistryValueKind.String);
            }
            catch { }
        }

        public static int GetAutoLockMinutes()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                object? value = key?.GetValue("AutoLockMinutes");
                if (int.TryParse(value?.ToString(), out int minutes) && minutes is 1 or 5 or 10 or 15)
                    return minutes;
            }
            catch { }
            return 5;
        }

        public static void SetAutoLockMinutes(int minutes)
        {
            if (minutes is not (1 or 5 or 10 or 15)) throw new ArgumentOutOfRangeException(nameof(minutes));
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("AutoLockMinutes", minutes, RegistryValueKind.DWord);
            }
            catch { }
        }

        public static bool GetLockWhenMinimized()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                return bool.TryParse(key?.GetValue("LockWhenMinimized")?.ToString(), out bool value) && value;
            }
            catch { return false; }
        }

        public static void SetLockWhenMinimized(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("LockWhenMinimized", enabled.ToString());
            }
            catch { }
        }
        public static string GetTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                return key?.GetValue("Theme")?.ToString() ?? "Light";
            }
            catch
            {
                return "Light";
            }
        }

        public static void SetTheme(string theme)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("Theme", theme ?? "Light");
            }
            catch { }
        }
        public static string GetLastUser()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                return key?.GetValue("LastUser")?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        public static void SetLastUser(string username)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("LastUser", username ?? "");
            }
            catch { }
        }

        public static void ClearLastUser()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey, writable: true);
                key?.DeleteValue("LastUser", throwOnMissingValue: false);
            }
            catch { }
        }
        public static (double width, double height) GetWindowSize(
            double defaultW = 1100, double defaultH = 700)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                var w = key?.GetValue("WindowWidth");
                var h = key?.GetValue("WindowHeight");

                double width = w != null
                    ? double.Parse(w.ToString()!)
                    : defaultW;
                double height = h != null
                    ? double.Parse(h.ToString()!)
                    : defaultH;

                if (width < 800) width = defaultW;
                if (height < 500) height = defaultH;

                return (width, height);
            }
            catch
            {
                return (defaultW, defaultH);
            }
        }

        public static void SetWindowSize(double width, double height)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("WindowWidth", width.ToString("0"));
                key?.SetValue("WindowHeight", height.ToString("0"));
            }
            catch { }
        }
        public static (double left, double top) GetWindowPosition()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                var l = key?.GetValue("WindowLeft");
                var t = key?.GetValue("WindowTop");

                if (l == null || t == null)
                    return (double.NaN, double.NaN);

                double left = double.Parse(l.ToString()!);
                double top = double.Parse(t.ToString()!);

                double screenW = SystemParameters.PrimaryScreenWidth;
                double screenH = SystemParameters.PrimaryScreenHeight;

                if (left < -screenW || left > screenW * 2 ||
                    top < -screenH || top > screenH * 2)
                    return (double.NaN, double.NaN);

                return (left, top);
            }
            catch
            {
                return (double.NaN, double.NaN);
            }
        }

        public static void SetWindowPosition(double left, double top)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("WindowLeft", left.ToString("0"));
                key?.SetValue("WindowTop", top.ToString("0"));
            }
            catch { }
        }

        public static void ClearWindowPosition()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey, writable: true);
                key?.DeleteValue("WindowLeft", throwOnMissingValue: false);
                key?.DeleteValue("WindowTop", throwOnMissingValue: false);
            }
            catch { }
        }
        public static bool GetAutoThemeEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                var val = key?.GetValue("AutoThemeEnabled");
                return val != null && bool.TryParse(val.ToString(), out bool b) && b;
            }
            catch
            {
                return false;
            }
        }

        public static void SetAutoThemeEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("AutoThemeEnabled", enabled.ToString());
            }
            catch { }
        }
        public static void Clear()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    AppKey, throwOnMissingSubKey: false);
            }
            catch { }
        }
        public static bool Exists()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static string DumpAll()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                if (key == null) return "(Registry trống)";

                var sb = new System.Text.StringBuilder();
                foreach (var name in key.GetValueNames())
                {
                    sb.AppendLine($"{name} = {key.GetValue(name)}");
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Lỗi: {ex.Message}";
            }
        }
    }
}
