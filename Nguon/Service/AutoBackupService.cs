using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        public const int RestoreDrillReminderDays = 90;
        public sealed record BackupHealthSummary(string Template, object?[] Arguments, bool Warn);
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
                    RecordBackupFailure(username, export.message);
                    return (false, export.message, null);
                }
                AtomicFileService.PromoteTemporary(tmpPath, finalPath);
                var old = Directory.GetFiles(folder, prefix + "*" + FileExt)
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .Skip(KeepCount);
                foreach (var f in old)
                {
                    try { File.Delete(f); } catch { }
                }
                RecordBackup(username, finalPath);
                try { ActivityLogger.Log(username, "AUTO_BACKUP", $"{reason}: {Path.GetFileName(finalPath)}"); } catch { }
                return (true, export.message, finalPath);
            }
            catch (Exception ex)
            {
                RecordBackupFailure(username, ex.Message);
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
                    RecordBackupFailure(username, r.message);
                    try { ActivityLogger.Log(username, "AUTO_BACKUP_FAILED", r.message); } catch { }
                }
            }
            catch (Exception ex)
            {
                RecordBackupFailure(username, ex.Message);
                try { ActivityLogger.Log(username, "AUTO_BACKUP_FAILED", ex.Message); } catch { }
            }
        }
        public static void RecordBackup(string username, string? path = null)
        {
            WriteValue("LastBackupUtc_" + Tag(username), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            WriteValue("LastBackupFailureUtc_" + Tag(username), "");
            WriteValue("LastBackupFailure_" + Tag(username), "");
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                string hash = HashFile(path);
                WriteValue("LastBackupPath_" + Tag(username), Path.GetFullPath(path));
                WriteValue("LastBackupHash_" + Tag(username), hash);
                WriteValue("LastBackupVerificationHash_" + Tag(username), hash);
                WriteValue("LastBackupVerificationUtc_" + Tag(username), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                WriteValue("LastBackupVerificationOk_" + Tag(username), "1");
            }
        }
        public static void RecordBackupFailure(string username, string message)
        {
            WriteValue("LastBackupFailureUtc_" + Tag(username), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            string safe = new string((message ?? "").Where(character => !char.IsControl(character)).Take(240).ToArray());
            WriteValue("LastBackupFailure_" + Tag(username), safe);
        }
        public static void RecordBackupVerification(string username, string path, bool succeeded)
        {
            WriteValue("LastBackupVerificationUtc_" + Tag(username), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            WriteValue("LastBackupVerificationOk_" + Tag(username), succeeded ? "1" : "0");
            string hash = succeeded && File.Exists(path) ? HashFile(path) : "";
            WriteValue("LastBackupVerificationHash_" + Tag(username), hash);
            if (hash.Length > 0)
            {
                WriteValue("LastBackupPath_" + Tag(username), Path.GetFullPath(path));
                WriteValue("LastBackupHash_" + Tag(username), hash);
            }
        }
        public static void RecordRestoreDrill(string username, string backupPath, bool succeeded, string message)
        {
            WriteValue("LastRestoreDrillUtc_" + Tag(username), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            WriteValue("LastRestoreDrillOk_" + Tag(username), succeeded ? "1" : "0");
            WriteValue("LastRestoreDrillHash_" + Tag(username),
                succeeded && File.Exists(backupPath) ? HashFile(backupPath) : "");
            string safe = new string((message ?? "").Where(character => !char.IsControl(character)).Take(240).ToArray());
            WriteValue("LastRestoreDrillMessage_" + Tag(username), safe);
        }
        public static BackupHealthSummary GetHealthSummary(string username)
        {
            DateTime? lastBackup = GetLastBackupUtc(username);
            DateTime? lastFailure = ParseUtc(ReadValue("LastBackupFailureUtc_" + Tag(username)));
            string backupHash = ReadValue("LastBackupHash_" + Tag(username)) ?? "";
            string backupPath = ReadValue("LastBackupPath_" + Tag(username)) ?? "";
            bool backupFileMatches = false;
            try
            {
                backupFileMatches = backupHash.Length > 0 && backupPath.Length > 0 &&
                    File.Exists(backupPath) && string.Equals(HashFile(backupPath), backupHash, StringComparison.Ordinal);
            }
            catch { }
            string verificationHash = ReadValue("LastBackupVerificationHash_" + Tag(username)) ?? "";
            bool verifiedOk = ReadValue("LastBackupVerificationOk_" + Tag(username)) == "1";
            DateTime? drill = ParseUtc(ReadValue("LastRestoreDrillUtc_" + Tag(username)));
            bool drillOk = ReadValue("LastRestoreDrillOk_" + Tag(username)) == "1";
            string drillHash = ReadValue("LastRestoreDrillHash_" + Tag(username)) ?? "";
            return EvaluateHealth(lastBackup, NeedsBackup(username), backupHash, backupFileMatches, verificationHash,
                verifiedOk, drill, drillOk, drillHash, lastFailure,
                ReadValue("LastBackupFailure_" + Tag(username)) ?? "", DateTime.UtcNow);
        }
        public static BackupHealthSummary EvaluateHealth(
            DateTime? lastBackupUtc,
            bool vaultChangedAfterBackup,
            string backupHash,
            bool backupFileMatches,
            string verificationHash,
            bool verificationSucceeded,
            DateTime? lastRestoreDrillUtc,
            bool restoreDrillSucceeded,
            string restoreDrillHash,
            DateTime? lastFailureUtc,
            string lastFailureMessage,
            DateTime nowUtc)
        {
            if (lastFailureUtc.HasValue && (!lastBackupUtc.HasValue || lastFailureUtc.Value > lastBackupUtc.Value))
                return new("Sao lưu gần nhất thất bại: {0}", new object?[] { lastFailureMessage }, true);
            if (!lastBackupUtc.HasValue)
                return new("Trạng thái sao lưu: chưa từng tạo bản sao lưu.", Array.Empty<object?>(), true);
            if (vaultChangedAfterBackup)
                return new("Trạng thái sao lưu: vault có thay đổi sau lần sao lưu {0}.",
                    new object?[] { lastBackupUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) }, true);
            if ((nowUtc - lastBackupUtc.Value).TotalDays >= 30)
                return new("Trạng thái sao lưu: bản gần nhất đã cũ ({0}).",
                    new object?[] { lastBackupUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) }, true);
            if (backupHash.Length == 0 || !backupFileMatches || !verificationSucceeded ||
                !string.Equals(backupHash, verificationHash, StringComparison.Ordinal))
                return new("Trạng thái sao lưu: bản gần nhất bị thiếu, thay đổi hoặc chưa xác minh.", Array.Empty<object?>(), true);
            if (!lastRestoreDrillUtc.HasValue || !restoreDrillSucceeded ||
                !string.Equals(backupHash, restoreDrillHash, StringComparison.Ordinal))
                return new("Trạng thái sao lưu: chưa có diễn tập khôi phục thành công.", Array.Empty<object?>(), true);
            if ((nowUtc - lastRestoreDrillUtc.Value).TotalDays >= RestoreDrillReminderDays)
                return new("Trạng thái sao lưu: cần diễn tập khôi phục lại; lần gần nhất {0}.",
                    new object?[] { lastRestoreDrillUtc.Value.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) }, true);
            return new("Trạng thái sao lưu tốt: đã xác minh và diễn tập gần đây ({0}).",
                new object?[] { lastRestoreDrillUtc.Value.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) }, false);
        }
        public static string GetHealthLine(string username)
        {
            BackupHealthSummary summary = GetHealthSummary(username);
            return string.Format(CultureInfo.CurrentCulture,
                LanguageService.Translate(summary.Template), summary.Arguments);
        }
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
                BackupHealthSummary healthSummary = GetHealthSummary(username);
                string health = GetHealthLine(username);
                if (!string.IsNullOrWhiteSpace(health)) text += " • " + health;
                return (text, GetReminder(username) != null || healthSummary.Warn);
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
        private static string HashFile(string path)
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            byte[] hash = SHA256.HashData(input);
            try { return Convert.ToHexString(hash); }
            finally { CryptographicOperations.ZeroMemory(hash); }
        }
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
