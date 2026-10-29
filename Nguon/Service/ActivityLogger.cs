using System;
using System.IO;
using System.Text;

namespace PersonalInfoManager.Services
{





    public static class ActivityLogger
    {
        private static readonly string _logRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "Logs");

        private static readonly object _lock = new();




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

            }
        }




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




        public static string[] ListLogFiles()
        {
            if (!Directory.Exists(_logRoot)) return Array.Empty<string>();
            return Directory.GetFiles(_logRoot, "activity_*.log");
        }




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