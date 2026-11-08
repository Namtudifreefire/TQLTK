using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
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
        private const string CompletePrefix = "complete_auto_";
        private const string CompleteExtension = CompleteBackupService.FileExtension;
        private const string PinSuffix = ".pinned";
        private const int DefaultPeriodicIntervalHours = 24;
        private const int DefaultRetentionCount = 7;
        private const int DefaultRetentionDays = 90;
        private const int DefaultStorageLimitMb = 2048;
        private static readonly TimeSpan FailedBackupRetryDelay = TimeSpan.FromHours(6);
#if SECURITY_TEST
        private static readonly object TestSettingsLock = new();
        private static string TestSettingsPath => Path.Combine(ApplicationDataPath.Root, "auto-backup-test-settings.json");
#endif
        public sealed record BackupHealthSummary(string Template, object?[] Arguments, bool Warn);
        public sealed record ScheduledBackupResult(bool Attempted, bool Succeeded, string Message, string? Path);
        public sealed record RetentionResult(int Removed, long CurrentBytes, bool LimitReached, string Message);
        public static bool IsPeriodicEnabled(string username) =>
            ReadValue("PeriodicBackupEnabled_" + Tag(username)) == "1";
        public static void SetPeriodicEnabled(string username, bool enabled) =>
            WriteValue("PeriodicBackupEnabled_" + Tag(username), enabled ? "1" : "0");
        public static int GetPeriodicIntervalHours(string username) =>
            ReadInt("PeriodicBackupIntervalHours_" + Tag(username), DefaultPeriodicIntervalHours,
                24, 168, 720);
        public static void SetPeriodicIntervalHours(string username, int hours)
        {
            if (hours is not (24 or 168 or 720)) throw new ArgumentOutOfRangeException(nameof(hours));
            WriteValue("PeriodicBackupIntervalHours_" + Tag(username), hours.ToString(CultureInfo.InvariantCulture));
        }
        public static int GetRetentionCount(string username) =>
            ReadInt("BackupRetentionCount_" + Tag(username), DefaultRetentionCount, 3, 7, 14, 30);
        public static void SetRetentionCount(string username, int count)
        {
            if (count is not (3 or 7 or 14 or 30)) throw new ArgumentOutOfRangeException(nameof(count));
            WriteValue("BackupRetentionCount_" + Tag(username), count.ToString(CultureInfo.InvariantCulture));
        }
        public static int GetRetentionDays(string username) =>
            ReadInt("BackupRetentionDays_" + Tag(username), DefaultRetentionDays, 30, 90, 180, 365);
        public static void SetRetentionDays(string username, int days)
        {
            if (days is not (30 or 90 or 180 or 365)) throw new ArgumentOutOfRangeException(nameof(days));
            WriteValue("BackupRetentionDays_" + Tag(username), days.ToString(CultureInfo.InvariantCulture));
        }
        public static int GetDailyRetention(string username) =>
            ReadInt("BackupDailyRetention_" + Tag(username), 0, 0, 7, 14, 30);
        public static int GetWeeklyRetention(string username) =>
            ReadInt("BackupWeeklyRetention_" + Tag(username), 0, 0, 4, 8, 12);
        public static int GetMonthlyRetention(string username) =>
            ReadInt("BackupMonthlyRetention_" + Tag(username), 0, 0, 3, 6, 12);
        public static void SetCalendarRetention(string username, int daily, int weekly, int monthly)
        {
            if (daily is not (0 or 7 or 14 or 30) || weekly is not (0 or 4 or 8 or 12) || monthly is not (0 or 3 or 6 or 12))
                throw new ArgumentOutOfRangeException(nameof(daily));
            WriteValue("BackupDailyRetention_" + Tag(username), daily.ToString(CultureInfo.InvariantCulture));
            WriteValue("BackupWeeklyRetention_" + Tag(username), weekly.ToString(CultureInfo.InvariantCulture));
            WriteValue("BackupMonthlyRetention_" + Tag(username), monthly.ToString(CultureInfo.InvariantCulture));
        }
        internal static HashSet<string> SelectCalendarRetention(
            IEnumerable<(string Path, DateTime CreatedUtc)> snapshots, DateTime nowUtc,
            int daily, int weekly, int monthly)
        {
            DateTime today = nowUtc.ToLocalTime().Date;
            DateTime currentWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var days = new HashSet<DateTime>();
            var weeks = new HashSet<DateTime>();
            var months = new HashSet<(int Year, int Month)>();
            foreach (var snapshot in snapshots.OrderByDescending(item => item.CreatedUtc))
            {
                DateTime date = snapshot.CreatedUtc.ToLocalTime().Date;
                if (date > today) continue;
                DateTime week = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
                int ageDays = (today - date).Days;
                int ageWeeks = (currentWeek - week).Days / 7;
                int ageMonths = ((today.Year - date.Year) * 12) + today.Month - date.Month;
                bool keepDay = ageDays >= 0 && ageDays < daily && days.Add(date);
                bool keepWeek = ageWeeks >= 0 && ageWeeks < weekly && weeks.Add(week);
                bool keepMonth = ageMonths >= 0 && ageMonths < monthly && months.Add((date.Year, date.Month));
                if (keepDay || keepWeek || keepMonth) selected.Add(snapshot.Path);
            }
            return selected;
        }
        public static int GetStorageLimitMb(string username) =>
            ReadInt("BackupStorageLimitMb_" + Tag(username), DefaultStorageLimitMb, 512, 1024, 2048, 4096);
        public static void SetStorageLimitMb(string username, int megabytes)
        {
            if (megabytes is not (512 or 1024 or 2048 or 4096)) throw new ArgumentOutOfRangeException(nameof(megabytes));
            WriteValue("BackupStorageLimitMb_" + Tag(username), megabytes.ToString(CultureInfo.InvariantCulture));
        }
        public static IReadOnlyList<FileInfo> ListCompleteBackups(string username)
        {
            string folder = GetFolder(username);
            string prefix = CompletePrefix + Tag(username) + "_";
            return CompleteBackupService.ListBackups(folder)
                .Where(file => file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        public static bool IsPinned(string backupPath)
        {
            string marker = backupPath + PinSuffix;
            try
            {
                if (!File.Exists(backupPath) || !File.Exists(marker)) return false;
                string savedHash = File.ReadAllText(marker, Encoding.ASCII).Trim();
                if (savedHash.Length != 64 || !savedHash.All(Uri.IsHexDigit)) return false;
                byte[] saved = Convert.FromHexString(savedHash);
                byte[] current = Convert.FromHexString(HashFile(backupPath));
                try { return CryptographicOperations.FixedTimeEquals(saved, current); }
                finally
                {
                    CryptographicOperations.ZeroMemory(saved);
                    CryptographicOperations.ZeroMemory(current);
                }
            }
            catch { return false; }
        }
        public static (bool ok, string message) SetPinned(string backupPath, string passphrase, bool pinned)
        {
            try
            {
                if (!File.Exists(backupPath)) return (false, "Không tìm thấy bản sao lưu.");
                string marker = backupPath + PinSuffix;
                if (pinned)
                {
                    string before = HashFile(backupPath);
                    var verification = CompleteBackupService.Inspect(backupPath, passphrase);
                    if (!verification.ok) return (false, verification.message);
                    string after = HashFile(backupPath);
                    if (!string.Equals(before, after, StringComparison.Ordinal))
                        return (false, "Bản sao lưu đã thay đổi trong lúc xác minh; hãy xác minh lại.");
                    AtomicFileService.WriteAllBytes(marker, Encoding.ASCII.GetBytes(after));
                    return (true, "Đã ghim bản sao lưu đã xác minh.");
                }
                if (File.Exists(marker)) File.Delete(marker);
                return (true, "Đã bỏ ghim bản sao lưu.");
            }
            catch (Exception ex) { return (false, "Không cập nhật được trạng thái ghim: " + ex.Message); }
        }
        public static ScheduledBackupResult RunPeriodicIfDue(string username, CancellationToken cancellationToken = default)
            => RunPeriodicIfDue(username, cancellationToken, DateTime.UtcNow);
        internal static ScheduledBackupResult RunPeriodicIfDue(string username, CancellationToken cancellationToken, DateTime nowUtc)
        {
            if (!IsPeriodicEnabled(username)) return new(false, true, "Sao lưu định kỳ đang tắt.", null);
            string folder = GetFolder(username);
            Directory.CreateDirectory(folder);
            FileStream runLock;
            try
            {
                string lockPath = Path.Combine(folder, ".tqltk-periodic-" + Tag(username) + ".lock");
                runLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return new(false, true, "Một bản sao lưu định kỳ khác đang chạy.", null);
            }
            using (runLock)
                return RunPeriodicLocked(username, folder, cancellationToken, nowUtc);
        }
        private static ScheduledBackupResult RunPeriodicLocked(string username, string folder, CancellationToken cancellationToken, DateTime now)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime? lastFailure = ParseUtc(ReadValue("LastCompleteBackupFailureUtc_" + Tag(username)));
            if (lastFailure.HasValue && now - lastFailure.Value < FailedBackupRetryDelay)
                return new(false, false, "Sao lưu định kỳ chờ lần thử lại sau.", null);
            DateTime? last = ParseUtc(ReadValue("LastCompleteBackupUtc_" + Tag(username)));
            int interval = GetPeriodicIntervalHours(username);
            if (last.HasValue && now - last.Value < TimeSpan.FromHours(interval))
                return new(false, true, "Chưa đến kỳ sao lưu tiếp theo.", null);
            string fingerprint;
            try { fingerprint = CompleteBackupService.GetCurrentStateFingerprint(username, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                RecordCompleteBackupFailure(username, ex.Message);
                return new(true, false, "Không đọc được dữ liệu để kiểm tra sao lưu: " + ex.Message, null);
            }
            if (string.Equals(fingerprint, ReadValue("LastCompleteBackupState_" + Tag(username)), StringComparison.Ordinal))
                return new(false, true, "Không có thay đổi mới; bỏ qua bản sao lưu lần này.", null);
            string? passphrase = BackupCrypto.GetStoredPassphrase(username);
            if (passphrase == null)
            {
                const string message = "Hãy đặt cụm mật khẩu sao lưu trước khi bật sao lưu định kỳ.";
                RecordCompleteBackupFailure(username, message);
                return new(true, false, message, null);
            }
            string path = Path.Combine(folder,
                $"{CompletePrefix}{Tag(username)}_{now:yyyyMMdd_HHmmss}{CompleteExtension}");
            cancellationToken.ThrowIfCancellationRequested();
            var created = CompleteBackupService.Create(username, path, passphrase, cancellationToken: cancellationToken);
            if (!created.ok || created.info == null)
            {
                RecordCompleteBackupFailure(username, created.message);
                return new(true, false, created.message, null);
            }
            RecordCompleteBackup(username, path, created.info);
            RetentionResult retention = PruneCompleteBackups(username);
            string resultMessage = created.message;
            if (retention.LimitReached)
                resultMessage += " " + retention.Message;
            try { ActivityLogger.LogMessage(username, "AUTO_BACKUP", "log.detail.auto_backup.1", Path.GetFileName(path)); } catch { }
            return new(true, true, resultMessage, path);
        }
        public static void RecordCompleteBackup(string username, string path, CompleteBackupInfo info)
        {
            WriteValue("LastCompleteBackupUtc_" + Tag(username), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            WriteValue("LastCompleteBackupPath_" + Tag(username), Path.GetFullPath(path));
            WriteValue("LastCompleteBackupHash_" + Tag(username), HashFile(path));
            WriteValue("LastCompleteBackupState_" + Tag(username), info.SnapshotFingerprint);
            WriteValue("LastCompleteBackupFailureUtc_" + Tag(username), "");
            WriteValue("LastCompleteBackupFailure_" + Tag(username), "");
        }
        public static void RecordCompleteBackupFailure(string username, string message)
        {
            WriteValue("LastCompleteBackupFailureUtc_" + Tag(username), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            string safe = new string((message ?? "").Where(character => !char.IsControl(character)).Take(240).ToArray());
            WriteValue("LastCompleteBackupFailure_" + Tag(username), safe);
        }
        public static RetentionResult PruneCompleteBackups(string username)
        {
            try
            {
                List<FileInfo> files = ListCompleteBackups(username).OrderByDescending(file => file.LastWriteTimeUtc).ToList();
                if (files.Count == 0)
                {
                    SaveRetentionStatus(username, false, 0, 0);
                    return new(0, 0, false, "");
                }
                FileInfo newest = files[0];
                DateTime cutoff = DateTime.UtcNow.AddDays(-GetRetentionDays(username));
                HashSet<string> calendarCopies = SelectCalendarRetention(
                    files.Select(file => (file.FullName, file.LastWriteTimeUtc)), DateTime.UtcNow,
                    GetDailyRetention(username), GetWeeklyRetention(username), GetMonthlyRetention(username));
                long limit = checked((long)GetStorageLimitMb(username) * 1024 * 1024);
                long total = files.Sum(file => file.Length);
                int regularCopies = files.Count(file => !calendarCopies.Contains(file.FullName));
                int removed = 0;
                foreach (FileInfo file in files.OrderBy(item => item.LastWriteTimeUtc))
                {
                    if (file.FullName.Equals(newest.FullName, StringComparison.OrdinalIgnoreCase) || IsPinned(file.FullName)) continue;
                    bool isCalendarCopy = calendarCopies.Contains(file.FullName);
                    bool tooOld = !isCalendarCopy && file.LastWriteTimeUtc < cutoff;
                    bool tooMany = !isCalendarCopy && regularCopies > GetRetentionCount(username);
                    bool overLimit = total > limit;
                    if (!tooOld && !tooMany && !overLimit) continue;
                    total -= file.Length;
                    try
                    {
                        file.Delete();
                        AtomicFileService.TryDelete(file.FullName + PinSuffix);
                        if (!isCalendarCopy) regularCopies--;
                        removed++;
                    }
                    catch { total += file.Length; }
                }
                List<FileInfo> remaining = files.Where(file => File.Exists(file.FullName)).ToList();
                bool countLimitReached = regularCopies > GetRetentionCount(username);
                bool ageLimitReached = remaining.Any(file =>
                    !calendarCopies.Contains(file.FullName) &&
                    !file.FullName.Equals(newest.FullName, StringComparison.OrdinalIgnoreCase) && file.LastWriteTimeUtc < cutoff);
                bool limitReached = total > limit || countLimitReached || ageLimitReached;
                SaveRetentionStatus(username, limitReached, remaining.Count, total);
                string message = limitReached ? FormatRetentionWarning(username, remaining.Count, total) : "";
                return new(removed, total, limitReached, message);
            }
            catch
            {
                WriteValue("LastCompleteBackupRetentionState_" + Tag(username), "failed");
                return new(0, 0, true, LocalizationAuditService.Get("backup.retention.failed"));
            }
        }
        public static string GetPeriodicStatusLine(string username)
        {
            string status;
            if (!IsPeriodicEnabled(username))
            {
                status = LocalizationAuditService.Get("backup.periodic.disabled");
            }
            else
            {
                string failure = ReadValue("LastCompleteBackupFailure_" + Tag(username)) ?? "";
                if (!string.IsNullOrWhiteSpace(failure))
                    status = LocalizationAuditService.Format("backup.periodic.failure", LanguageService.Translate(failure));
                else
                {
                    DateTime? last = ParseUtc(ReadValue("LastCompleteBackupUtc_" + Tag(username)));
                    status = last.HasValue
                        ? LocalizationAuditService.Format("backup.periodic.last_success", LocalizationAuditService.FormatDateTime(last.Value))
                        : LocalizationAuditService.Get("backup.periodic.enabled_empty");
                }
            }
            string retentionState = ReadValue("LastCompleteBackupRetentionState_" + Tag(username)) ?? "";
            if (retentionState == "failed")
                status += Environment.NewLine + LocalizationAuditService.Get("backup.retention.failed");
            else if (retentionState == "warning")
            {
                int copies = int.TryParse(ReadValue("LastCompleteBackupRetentionCopies_" + Tag(username)), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int parsedCopies) ? Math.Max(0, parsedCopies) : 0;
                long bytes = long.TryParse(ReadValue("LastCompleteBackupRetentionBytes_" + Tag(username)), NumberStyles.None,
                    CultureInfo.InvariantCulture, out long parsedBytes) ? Math.Max(0, parsedBytes) : 0;
                status += Environment.NewLine + FormatRetentionWarning(username, copies, bytes);
            }
            return status;
        }
        private static void SaveRetentionStatus(string username, bool warning, int copies, long bytes)
        {
            string tag = Tag(username);
            WriteValue("LastCompleteBackupRetentionState_" + tag, warning ? "warning" : "ok");
            WriteValue("LastCompleteBackupRetentionCopies_" + tag, copies.ToString(CultureInfo.InvariantCulture));
            WriteValue("LastCompleteBackupRetentionBytes_" + tag, bytes.ToString(CultureInfo.InvariantCulture));
        }
        private static string FormatRetentionWarning(string username, int copies, long bytes) =>
            LocalizationAuditService.Format("backup.retention.warning", copies,
                FileBackupService.FormatSize(bytes), GetRetentionCount(username), GetRetentionDays(username),
                FileBackupService.FormatSize((long)GetStorageLimitMb(username) * 1024 * 1024));
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
            SaveRetentionStatus(username, false, 0, 0);
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
                    new object?[] { LocalizationAuditService.FormatDateTime(lastBackupUtc.Value) }, true);
            if ((nowUtc - lastBackupUtc.Value).TotalDays >= 30)
                return new("Trạng thái sao lưu: bản gần nhất đã cũ ({0}).",
                    new object?[] { LocalizationAuditService.FormatDateTime(lastBackupUtc.Value) }, true);
            if (backupHash.Length == 0 || !backupFileMatches || !verificationSucceeded ||
                !string.Equals(backupHash, verificationHash, StringComparison.Ordinal))
                return new("Trạng thái sao lưu: bản gần nhất bị thiếu, thay đổi hoặc chưa xác minh.", Array.Empty<object?>(), true);
            if (!lastRestoreDrillUtc.HasValue || !restoreDrillSucceeded ||
                !string.Equals(backupHash, restoreDrillHash, StringComparison.Ordinal))
                return new("Trạng thái sao lưu: chưa có diễn tập khôi phục thành công.", Array.Empty<object?>(), true);
            if ((nowUtc - lastRestoreDrillUtc.Value).TotalDays >= RestoreDrillReminderDays)
                return new("Trạng thái sao lưu: cần diễn tập khôi phục lại; lần gần nhất {0}.",
                    new object?[] { LocalizationAuditService.FormatDate(lastRestoreDrillUtc.Value) }, true);
            return new("Trạng thái sao lưu tốt: đã xác minh và diễn tập gần đây ({0}).",
                new object?[] { LocalizationAuditService.FormatDate(lastRestoreDrillUtc.Value) }, false);
        }
        public static string GetHealthLine(string username)
        {
            BackupHealthSummary summary = GetHealthSummary(username);
            return LanguageService.Format(summary.Template, summary.Arguments);
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
#if SECURITY_TEST
                lock (TestSettingsLock)
                {
                    if (!File.Exists(TestSettingsPath)) return null;
                    Dictionary<string, string> values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        File.ReadAllText(TestSettingsPath)) ?? new(StringComparer.Ordinal);
                    return values.TryGetValue(name, out string? value) ? value : null;
                }
#else
                using var key = Registry.CurrentUser.OpenSubKey(AppKey);
                return key?.GetValue(name)?.ToString();
#endif
            }
            catch
            {
                return null;
            }
        }
        private static int ReadInt(string name, int fallback, params int[] allowed)
        {
            string? raw = ReadValue(name);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
                   allowed.Contains(value)
                ? value
                : fallback;
        }
        private static void WriteValue(string name, string value)
        {
            try
            {
#if SECURITY_TEST
                lock (TestSettingsLock)
                {
                    Dictionary<string, string> values = File.Exists(TestSettingsPath)
                        ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(TestSettingsPath))
                            ?? new(StringComparer.Ordinal)
                        : new(StringComparer.Ordinal);
                    values[name] = value;
                    AtomicFileService.WriteAllText(TestSettingsPath, JsonSerializer.Serialize(values));
                }
#else
                using var key = Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue(name, value);
#endif
            }
            catch { }
        }
    }
}
