using System;
using System.IO;
using System.Text;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Ghi log hoạt động của user vào file local.
    /// Log chia theo ngày để dễ tra cứu.
    /// Vị trí: %AppData%\PersonalInfoManager\Logs\activity_yyyy-MM-dd.log
    /// </summary>
    public static class ActivityLogger
    {
        private static readonly string _logRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "Logs");

        private static readonly object _lock = new();

        /// <summary>
        /// Ghi một dòng log hoạt động.
        /// </summary>
        public static void Log(string username, string action, string detail = "")
        {
            try
            {
                Directory.CreateDirectory(_logRoot);

                string logFile = Path.Combine(_logRoot,
                    $"activity_{DateTime.Now:yyyy-MM-dd}.log");

                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " +
                              $"[{username}] " +
                              $"[{action}] " +
                              $"{detail}";

                lock (_lock)
                {
                    File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Không để log lỗi làm sập app
            }
        }

        /// <summary>
        /// Đọc toàn bộ log trong ngày (dùng cho AdminWindow).
        /// </summary>
        public static string ReadTodayLog()
        {
            try
            {
                string logFile = Path.Combine(_logRoot,
                    $"activity_{DateTime.Now:yyyy-MM-dd}.log");

                if (!File.Exists(logFile))
                    return "(Chưa có log hôm nay)";

                return File.ReadAllText(logFile, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return $"Lỗi đọc log: {ex.Message}";
            }
        }

        /// <summary>
        /// Liệt kê các file log đã có.
        /// </summary>
        public static string[] ListLogFiles()
        {
            if (!Directory.Exists(_logRoot)) return Array.Empty<string>();
            return Directory.GetFiles(_logRoot, "activity_*.log");
        }

        /// <summary>
        /// Xóa log cũ hơn N ngày.
        /// </summary>
        public static void CleanupOldLogs(int keepDays = 30)
        {
            if (!Directory.Exists(_logRoot)) return;

            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.GetFiles(_logRoot, "activity_*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch { }
            }
        }
    }
}