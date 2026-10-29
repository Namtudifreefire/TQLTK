using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace PersonalInfoManager.Services
{

    public static class AutoBackupService
    {
        private const string AppKey = @"Software\TQLTK";
        private const string FilePrefix = "vault_auto_";
        private const string FileExt = ".vault";

        public const int KeepCount = 7;

        public const int RemindAfterDays = 7;

        public static bool IsEnabled(string username) =>
            ReadValue("AutoBackupEnabled_" + Tag(username)) != "0";

        public static void SetEnabled(string username, bool enabled) =>
            WriteValue("AutoBackupEnabled_" + Tag(username), enabled ? "1" : "0");

        public static string GetFolder(string username)
        {
            string? saved = ReadValue("AutoBackupFolder_" + Tag(username));
            if (!string.IsNullOrWhiteSpace(saved)) return saved;

            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(docs))
                docs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PersonalInfoManager");

            return Path.Combine(docs, "TQLTK Backup");
        }

        public static string? TrySetFolder(string username, string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder)) return "Chưa chọn thư mục.";

                Directory.CreateDirectory(folder);

                string probe = Path.Combine(folder, ".tqltk_write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                return "Không ghi được vào thư mục này:\n" + ex.Message;
            }

            WriteValue("AutoBackupFolder_" + Tag(username), folder);
            return null;
        }

        public static (bool ok, string message, string? path) BackupNow(string username, string reason = "Thủ công")
        {
            try
            {
                string folder = GetFolder(username);
                Directory.CreateDirectory(folder);

                string prefix = $"{FilePrefix}{Tag(username)}_";

                foreach (var stray in Directory.GetFiles(folder, prefix + "*" + FileExt + ".tmp"))
                {
                    try { File.Delete(stray); } catch { }
                }

                string finalPath = Path.Combine(folder, $"{prefix}{DateTime.Now:yyyyMMdd_HHmmss}{FileExt}");
                string tmpPath = finalPath + ".tmp";

                var export = VaultBackupService.ExportToFile(username, tmpPath);
                if (!export.ok)
                {
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                    return (false, export.message, null);
                }

                File.Move(tmpPath, finalPath, overwrite: true);

                var old = Directory.GetFiles(folder, prefix + "*" + FileExt)
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .Skip(KeepCount);

                foreach (var f in old)
                {
                    try { File.Delete(f); } catch { }
                }

                RecordBackup(username);

                try { ActivityLogger.Log(username, "AUTO_BACKUP", $"{reason}: {Path.GetFileName(finalPath)}"); } catch { }

                return (true, export.message, finalPath);
            }
            catch (Exception ex)
            {
                return (false, "Không sao lưu được: " + ex.Message, null);
            }
        }

        public static void RunOnExit(string username)
        {
            try
            {
                if (!IsEnabled(username)) return;
                if (CountAccounts(username) <= 0) return;
                if (!NeedsBackup(username)) return;

                var r = BackupNow(username, "Khi thoát");
                if (!r.ok)
                {
                    try { ActivityLogger.Log(username, "AUTO_BACKUP_FAILED", r.message); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { ActivityLogger.Log(username, "AUTO_BACKUP_FAILED", ex.Message); } catch { }
            }
        }

        public static void RecordBackup(string username) =>
            WriteValue("LastBackupUtc_" + Tag(username), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        public static DateTime? GetLastBackupUtc(string username)
        {
            DateTime? recorded = ParseUtc(ReadValue("LastBackupUtc_" + Tag(username)));
            return Max(recorded, NewestBackupFileUtc(username));
        }

        public static bool NeedsBackup(string username)
        {
            DateTime? vault = new AccountVaultService().GetLastWriteUtc(username);
            if (vault == null) return false;

            DateTime? last = GetLastBackupUtc(username);
            return last == null || vault.Value > last.Value;
        }

        public static (string text, bool warn) GetStatusLine(string username)
        {
            try
            {
                int count = CountAccounts(username);
                if (count < 0) return ("", false);
                if (count == 0) return ("💾 Sao lưu: chưa có dữ liệu", false);

                DateTime? last = GetLastBackupUtc(username);
                string text;

                if (last == null)
                {
                    text = "💾 Chưa từng sao lưu";
                }
                else
                {
                    text = $"💾 Sao lưu gần nhất: {last.Value.ToLocalTime():dd/MM/yyyy HH:mm}";
                    if (NeedsBackup(username)) text += " (có thay đổi mới)";
                }

                if (!IsEnabled(username)) text += " • tự sao lưu: tắt";

                return (text, GetReminder(username) != null);
            }
            catch
            {
                return ("", false);
            }
        }

        public static string? GetReminder(string username)
        {
            try
            {
                if (CountAccounts(username) <= 0) return null;
                if (!NeedsBackup(username)) return null;

                DateTime? last = GetLastBackupUtc(username);
                if (last == null)
                {
                    return IsEnabled(username)
                        ? null
                        : "Bạn chưa từng sao lưu dữ liệu, và tính năng tự sao lưu khi thoát đang tắt.";
                }

                double days = (DateTime.UtcNow - last.Value).TotalDays;
                if (days >= RemindAfterDays)
                    return $"Đã khoảng {Math.Floor(days):0} ngày chưa sao lưu, và dữ liệu đã thay đổi " +
                           $"kể từ lần sao lưu gần nhất ({last.Value.ToLocalTime():dd/MM/yyyy HH:mm}).";

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string Tag(string username) => BackupCrypto.UserTag(username);

        private static int CountAccounts(string username)
        {
            try { return new AccountVaultService().Count(username); }
            catch { return -1; }
        }

        private static DateTime? NewestBackupFileUtc(string username)
        {
            try
            {
                string folder = GetFolder(username);
                if (!Directory.Exists(folder)) return null;

                string prefix = $"{FilePrefix}{Tag(username)}_";
                DateTime? newest = null;

                foreach (var f in Directory.GetFiles(folder, prefix + "*" + FileExt))
                {
                    DateTime t = File.GetLastWriteTimeUtc(f);
                    if (newest == null || t > newest) newest = t;
                }

                return newest;
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? Max(DateTime? a, DateTime? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return a > b ? a : b;
        }

        private static DateTime? ParseUtc(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToUniversalTime()
                : null;
        }

        private static string? ReadValue(string name)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                return key?.GetValue(name)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static void WriteValue(string name, string value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue(name, value);
            }
            catch { }
        }
    }
}
