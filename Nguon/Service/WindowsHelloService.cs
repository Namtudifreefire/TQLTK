using System;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using WindowsHello;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Dịch vụ xác thực Windows Hello cho WPF desktop.
    /// Dùng thư viện HaemmerElectronics.SeppPenner.WindowsHello.
    ///
    /// Cơ chế: CHỈ dùng PromptToDecrypt() — đảm bảo 1 hộp thoại duy nhất mỗi lần gọi.
    /// </summary>
    public static class WindowsHelloService
    {
        // ================================================================
        // ================== LOG HELPER ==================
        // ================================================================

        private static void Log(string msg)
        {
            string line = "[Hello] " + msg;
            System.Diagnostics.Debug.WriteLine(line);
            System.Diagnostics.Trace.WriteLine(line);

            try
            {
                string logPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "windows_hello_debug.log");

                File.AppendAllText(
                    logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
            catch { }
        }

        // ================================================================
        // ================== KIỂM TRA KHẢ DỤNG ==================
        // ================================================================

        /// <summary>
        /// Hỏi Windows xem máy này đã có và đã thiết lập Windows Hello (PIN / vân tay / khuôn mặt) chưa.
        /// Nếu chính lời gọi kiểm tra bị lỗi (không xác định được) thì giữ hành vi cũ là coi như khả dụng,
        /// bước xác thực thật phía sau sẽ báo lỗi nếu thực sự không dùng được.
        /// </summary>
        public static async Task<bool> IsAvailableAsync()
        {
            try
            {
                var availability = await global::Windows.Security.Credentials.UI
                    .UserConsentVerifier.CheckAvailabilityAsync();

                Log($"IsAvailableAsync: {availability}");

                return availability == global::Windows.Security.Credentials.UI
                    .UserConsentVerifierAvailability.Available;
            }
            catch (Exception ex)
            {
                Log($"IsAvailableAsync không kiểm tra được ({ex.GetType().Name}: {ex.Message}) → coi như khả dụng");
                return true;
            }
        }

        // ================================================================
        // ================== BẬT TÍNH NĂNG (1 hộp thoại) ==================
        // ================================================================

        /// <summary>
        /// Bật tính năng Windows Hello — CHỈ HIỆN 1 HỘP THOẠI DUY NHẤT.
        /// Chỉ dùng Encrypt + PromptToDecrypt, KHÔNG gọi SetPersistentKeyName.
        /// </summary>
        public static Task<bool> EnableAsync(Window owner, string message)
        {
            try
            {
                Log("EnableAsync bắt đầu");

                var hwnd = new WindowInteropHelper(owner).Handle;
                Log($"HWND = 0x{hwnd.ToInt64():X}");

                if (hwnd == IntPtr.Zero)
                {
                    Log("HWND = IntPtr.Zero");
                    return Task.FromResult(false);
                }

                var provider = WinHelloProvider.CreateInstance(message, hwnd);
                Log("Đã tạo WinHelloProvider");

                // ✅ Chỉ Encrypt + PromptToDecrypt — PromptToDecrypt tự dùng key mặc định
                var data = new byte[] { 0x50, 0x49, 0x4D };
                var encryptedData = provider.Encrypt(data);
                Log("Encrypt hoàn tất");

                var decryptedData = provider.PromptToDecrypt(encryptedData);
                Log("PromptToDecrypt hoàn tất");

                bool verified = decryptedData != null && decryptedData.Length == data.Length;
                Log($"EnableAsync Result = {verified}");

                return Task.FromResult(verified);
            }
            catch (Exception ex)
            {
                Log($"EnableAsync EXCEPTION: {ex.GetType().Name} - {ex.Message}");
                Log($"HRESULT: 0x{ex.HResult:X8}");
                return Task.FromResult(false);
            }
        }

        // ================================================================
        // ================== XÁC THỰC (đăng nhập) ==================
        // ================================================================

        /// <summary>
        /// Yêu cầu xác thực Windows Hello — CHỈ HIỆN 1 HỘP THOẠI DUY NHẤT.
        /// </summary>
        public static Task<bool> VerifyAsync(Window owner, string message)
        {
            try
            {
                Log("VerifyAsync bắt đầu");

                var hwnd = new WindowInteropHelper(owner).Handle;
                Log($"HWND = 0x{hwnd.ToInt64():X}");

                if (hwnd == IntPtr.Zero)
                {
                    Log("HWND = IntPtr.Zero, không thể xác thực");
                    return Task.FromResult(false);
                }

                var provider = WinHelloProvider.CreateInstance(message, hwnd);
                Log("Đã tạo WinHelloProvider");

                var data = new byte[] { 0x50, 0x49, 0x4D };
                var encryptedData = provider.Encrypt(data);
                Log("Encrypt hoàn tất");

                var decryptedData = provider.PromptToDecrypt(encryptedData);
                Log("PromptToDecrypt hoàn tất");

                bool verified = decryptedData != null && decryptedData.Length == data.Length;
                Log($"VerifyAsync Result = {verified}");

                return Task.FromResult(verified);
            }
            catch (Exception ex)
            {
                Log($"VerifyAsync EXCEPTION: {ex.GetType().Name} - {ex.Message}");
                Log($"HRESULT: 0x{ex.HResult:X8}");
                return Task.FromResult(false);
            }
        }

        // ================================================================
        // ================== SID USER WINDOWS ==================
        // ================================================================

        public static string GetCurrentWindowsSid()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return identity.User?.Value ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}