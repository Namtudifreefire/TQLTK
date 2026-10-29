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

                string line = FormatLine(DateTime.Now, username, action, detail);

                lock (_lock)
                {
                    File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {

            }
        }

        internal static string FormatLine(DateTime timestamp, string username, string action, string detail)
            => $"[{timestamp:yyyy-MM-dd HH:mm:ss}] [{EscapeField(username)}] " +
               $"[{EscapeField(action)}] {EscapeField(detail)}";

        internal static string EscapeField(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";

            var escaped = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '%': escaped.Append("%25"); break;
                    case '[': escaped.Append("%5B"); break;
                    case ']': escaped.Append("%5D"); break;
                    case '\r': escaped.Append("%0D"); break;
                    case '\n': escaped.Append("%0A"); break;
                    default:
                        if (char.IsControl(character))
                            escaped.Append("%u").Append(((int)character).ToString("X4"));
                        else
                            escaped.Append(character);
                        break;
                }
            }
            return escaped.ToString();
        }

        internal static string UnescapeField(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";

            var decoded = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] != '%' || index + 1 >= value.Length)
                {
                    decoded.Append(value[index]);
                    continue;
                }

                if (index + 5 < value.Length && value[index + 1] == 'u' &&
                    ushort.TryParse(value.AsSpan(index + 2, 4), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out ushort control))
                {
                    decoded.Append((char)control);
                    index += 5;
                    continue;
                }

                if (index + 2 < value.Length)
                {
                    string escape = value.Substring(index, 3);
                    char? replacement = escape switch
                    {
                        "%25" => '%',
                        "%5B" => '[',
                        "%5D" => ']',
                        "%0D" => '\r',
                        "%0A" => '\n',
                        _ => null
                    };
                    if (replacement is { } character)
                    {
                        decoded.Append(character);
                        index += 2;
                        continue;
                    }
                }

                decoded.Append(value[index]);
            }
            return decoded.ToString();
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
