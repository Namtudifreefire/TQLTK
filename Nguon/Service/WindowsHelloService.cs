using System;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using WindowsHello;

namespace PersonalInfoManager.Services
{

    public static class WindowsHelloService
    {

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