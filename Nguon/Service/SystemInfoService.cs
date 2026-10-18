using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Cung cấp thông tin về hệ thống và tiến trình hiện tại.
    /// Dùng cho nút "Thông tin hệ thống" trong MainWindow.
    /// </summary>
    public static class SystemInfoService
    {
        /// <summary>
        /// Trả về báo cáo đầy đủ dạng text về OS, máy, user, .NET, process, quyền.
        /// </summary>
        public static string GetSystemReport()
        {
            var sb = new StringBuilder();

            // ================== THÔNG TIN HỆ THỐNG ==================
            sb.AppendLine("=== THÔNG TIN HỆ THỐNG ===");
            sb.AppendLine($"OS          : {Environment.OSVersion}");
            sb.AppendLine($"Máy tính    : {Environment.MachineName}");
            sb.AppendLine($"User        : {Environment.UserName}");
            sb.AppendLine($"64-bit OS   : {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"64-bit Proc : {Environment.Is64BitProcess}");
            sb.AppendLine($"Số CPU      : {Environment.ProcessorCount}");
            sb.AppendLine($".NET        : {Environment.Version}");
            sb.AppendLine($"Thư mục app : {Environment.CurrentDirectory}");
            sb.AppendLine($"System Dir  : {Environment.SystemDirectory}");
            sb.AppendLine();

            // ================== THÔNG TIN TIẾN TRÌNH ==================
            sb.AppendLine("=== TIẾN TRÌNH HIỆN TẠI ===");
            using (var me = Process.GetCurrentProcess())
            {
                sb.AppendLine($"Tên         : {me.ProcessName}");
                sb.AppendLine($"PID         : {me.Id}");
                sb.AppendLine($"RAM dùng    : {me.WorkingSet64 / 1024 / 1024} MB");
                sb.AppendLine($"RAM ảo      : {me.VirtualMemorySize64 / 1024 / 1024} MB");
                sb.AppendLine($"Thread      : {me.Threads.Count}");
                sb.AppendLine($"Bắt đầu     : {me.StartTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Thời gian   : {(DateTime.Now - me.StartTime).TotalSeconds:F1} giây");
            }
            sb.AppendLine();

            // ================== QUYỀN ==================
            sb.AppendLine("=== QUYỀN ===");
            sb.AppendLine($"Admin       : {(IsAdmin() ? "CÓ (Administrator)" : "KHÔNG (User thường)")}");
            sb.AppendLine($"Interactive : {Environment.UserInteractive}");

            return sb.ToString();
        }

        /// <summary>
        /// Kiểm tra app đang chạy với quyền Administrator hay không.
        /// </summary>
        public static bool IsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Báo cáo ngắn gọn (1 dòng) để hiển thị trên status bar.
        /// </summary>
        public static string GetShortInfo()
        {
            return $".NET {Environment.Version} | " +
                   $"{Environment.MachineName} | " +
                   $"{Environment.UserName} | " +
                   $"{(IsAdmin() ? "Admin" : "User")}";
        }
    }
}