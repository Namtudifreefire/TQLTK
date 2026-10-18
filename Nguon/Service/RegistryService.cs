using System;
using System.Windows;
using Microsoft.Win32;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Lưu/tải cấu hình app vào Registry Windows.
    /// Vị trí: HKEY_CURRENT_USER\Software\TQLTK
    /// Không cần quyền admin.
    /// </summary>
    public static class RegistryService
    {
        private const string AppKey = @"Software\TQLTK";

        // ================================================================
        // ================== THEME ==================
        // ================================================================

        /// <summary>
        /// Lấy theme đã lưu. Trả về "Light" hoặc "Dark".
        /// Mặc định "Light" nếu chưa có.
        /// </summary>
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

        /// <summary>
        /// Lưu theme vào Registry.
        /// </summary>
        public static void SetTheme(string theme)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("Theme", theme ?? "Light");
            }
            catch { }
        }

        // ================================================================
        // ================== USER ĐĂNG NHẬP CUỐI ==================
        // ================================================================

        /// <summary>
        /// Lấy username đăng nhập lần cuối.
        /// </summary>
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

        /// <summary>
        /// Lưu username đăng nhập lần cuối.
        /// </summary>
        public static void SetLastUser(string username)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("LastUser", username ?? "");
            }
            catch { }
        }

        /// <summary>
        /// Xóa username đã lưu.
        /// </summary>
        public static void ClearLastUser()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey, writable: true);
                key?.DeleteValue("LastUser", throwOnMissingValue: false);
            }
            catch { }
        }

        // ================================================================
        // ================== KÍCH THƯỚC CỬA SỔ ==================
        // ================================================================

        /// <summary>
        /// Lấy kích thước cửa sổ đã lưu.
        /// </summary>
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

        /// <summary>
        /// Lưu kích thước cửa sổ.
        /// </summary>
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

        // ================================================================
        // ================== VỊ TRÍ CỬA SỔ ==================
        // ================================================================

        /// <summary>
        /// Lấy vị trí cửa sổ đã lưu (left, top).
        /// Nếu chưa lưu hoặc giá trị vô lý → trả về NaN.
        /// </summary>
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

        /// <summary>
        /// Lưu vị trí cửa sổ.
        /// </summary>
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

        /// <summary>
        /// Xóa vị trí đã lưu (reset về giữa màn hình).
        /// </summary>
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

        // ================================================================
        // ================== AUTO THEME ==================
        // ================================================================

        /// <summary>
        /// Lấy trạng thái bật/tắt auto theme (tự động đổi theme theo giờ).
        /// </summary>
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

        /// <summary>
        /// Lưu trạng thái bật/tắt auto theme.
        /// </summary>
        public static void SetAutoThemeEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue("AutoThemeEnabled", enabled.ToString());
            }
            catch { }
        }

        // ================================================================
        // ================== XÓA TOÀN BỘ CẤU HÌNH ==================
        // ================================================================

        /// <summary>
        /// Xóa toàn bộ cấu hình app (dùng khi user muốn reset).
        /// </summary>
        public static void Clear()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    AppKey, throwOnMissingSubKey: false);
            }
            catch { }
        }

        // ================================================================
        // ================== DEBUG / KIỂM TRA ==================
        // ================================================================

        /// <summary>
        /// Kiểm tra Registry có tồn tại key của app không.
        /// </summary>
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

        /// <summary>
        /// Trả về danh sách tất cả các value trong key (dùng debug).
        /// </summary>
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