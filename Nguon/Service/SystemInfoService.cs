using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace PersonalInfoManager.Services
{

    public static class SystemInfoService
    {

        public static string GetSystemReport()
        {
            var sb = new StringBuilder();
            bool english = LanguageService.IsEnglish;

            sb.AppendLine(english ? "=== SYSTEM INFORMATION ===" : "=== THÔNG TIN HỆ THỐNG ===");
            AppendField(sb, english ? "OS" : "Hệ điều hành", Environment.OSVersion.ToString());
            AppendField(sb, english ? "Computer" : "Máy tính", Environment.MachineName);
            AppendField(sb, english ? "User" : "Người dùng", Environment.UserName);
            AppendField(sb, english ? "64-bit OS" : "Hệ điều hành 64-bit", FormatBoolean(Environment.Is64BitOperatingSystem, english));
            AppendField(sb, english ? "64-bit process" : "Tiến trình 64-bit", FormatBoolean(Environment.Is64BitProcess, english));
            AppendField(sb, english ? "CPU count" : "Số CPU", Environment.ProcessorCount.ToString());
            AppendField(sb, ".NET", Environment.Version.ToString());
            AppendField(sb, english ? "App folder" : "Thư mục ứng dụng", Environment.CurrentDirectory);
            AppendField(sb, english ? "System directory" : "Thư mục hệ thống", Environment.SystemDirectory);
            sb.AppendLine();

            sb.AppendLine(english ? "=== CURRENT PROCESS ===" : "=== TIẾN TRÌNH HIỆN TẠI ===");
            using (var me = Process.GetCurrentProcess())
            {
                AppendField(sb, english ? "Name" : "Tên", me.ProcessName);
                AppendField(sb, "PID", me.Id.ToString());
                AppendField(sb, english ? "RAM used" : "RAM đang dùng", $"{me.WorkingSet64 / 1024 / 1024} MB");
                AppendField(sb, english ? "Virtual RAM" : "RAM ảo", $"{me.VirtualMemorySize64 / 1024 / 1024} MB");
                AppendField(sb, english ? "Threads" : "Số luồng", me.Threads.Count.ToString());
                AppendField(sb, english ? "Started" : "Bắt đầu", me.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                double uptimeSeconds = (DateTime.Now - me.StartTime).TotalSeconds;
                AppendField(sb, english ? "Uptime" : "Thời gian hoạt động", $"{uptimeSeconds:F1} {(english ? "seconds" : "giây")}");
            }
            sb.AppendLine();

            sb.AppendLine(english ? "=== PERMISSIONS ===" : "=== QUYỀN ===");
            AppendField(sb, english ? "Administrator" : "Quản trị viên", IsAdmin()
                ? (english ? "Yes (Administrator)" : "Có (Quản trị viên)")
                : (english ? "No (Standard user)" : "Không (Người dùng thường)"));
            AppendField(sb, english ? "Interactive" : "Tương tác người dùng", FormatBoolean(Environment.UserInteractive, english));

            return sb.ToString();
        }

        private static void AppendField(StringBuilder builder, string label, string value)
        {
            builder.Append(label.PadRight(22)).Append(" : ").AppendLine(value);
        }

        private static string FormatBoolean(bool value, bool english) => value
            ? (english ? "Yes" : "Có")
            : (english ? "No" : "Không");

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

        public static string GetShortInfo()
        {
            return $".NET {Environment.Version} | " +
                   $"{Environment.MachineName} | " +
                   $"{Environment.UserName} | " +
                   $"{(IsAdmin() ? "Admin" : "User")}";
        }
    }
}
