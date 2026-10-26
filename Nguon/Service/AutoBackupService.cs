using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Sao lưu vault tự động ra một thư mục trên máy (mặc định Documents\TQLTK Backup), và theo dõi
    /// "lần sao lưu gần nhất" để hiện trên thanh trạng thái và nhắc khi quá lâu chưa sao lưu.
    ///
    /// - Bản sao lưu chính là file xuất của VaultBackupService.ExportToFile (mã hóa bằng cụm mật khẩu sao lưu
    ///   nếu người dùng đã đặt, nếu chưa thì bằng khóa của máy và chỉ mở được trên máy này).
    ///   Khôi phục: Tiện ích → Nhập vault (Restore) rồi chọn file trong thư mục sao lưu.
    /// - Tên file: vault_auto_{nhãn người dùng}_{yyyyMMdd_HHmmss}.vault; mỗi người dùng chỉ giữ KeepCount bản gần nhất.
    /// - Ghi qua file .tmp rồi đổi tên, nên tiến trình bị tắt giữa chừng không để lại file sao lưu hỏng.
    /// - "Cần sao lưu" = file vault được ghi SAU lần sao lưu gần nhất.
    /// - "Lần sao lưu gần nhất" = mốc mới hơn giữa (bản trong thư mục) và (mốc ghi nhận qua RecordBackup,
    ///   dùng cho xuất vault thủ công và đồng bộ Drive).
    /// - Cài đặt và mốc thời gian lưu ở Registry (HKCU\Software\TQLTK), riêng từng người dùng.
    ///   "Reset cài đặt" xóa luôn các giá trị này: lúc đó quay về mặc định (bật, thư mục mặc định).
    /// </summary>
    public static class AutoBackupService
    {
        private const string AppKey = @"Software\TQLTK";   // phải khớp RegistryService.AppKey
        private const string FilePrefix = "vault_auto_";
        private const string FileExt = ".vault";

        /// <summary>Số bản sao lưu tự động gần nhất được giữ lại cho mỗi người dùng.</summary>
        public const int KeepCount = 7;

        /// <summary>Quá bấy nhiêu ngày chưa sao lưu (mà dữ liệu đã đổi) thì nhắc khi đăng nhập.</summary>
        public const int RemindAfterDays = 7;

        // ================================================================
        // ================== CÀI ĐẶT ==================
        // ================================================================

        /// <summary>Tự sao lưu khi thoát. Mặc định: bật.</summary>
        public static bool IsEnabled(string username) =>
            ReadValue("AutoBackupEnabled_" + Tag(username)) != "0";

        public static void SetEnabled(string username, bool enabled) =>
            WriteValue("AutoBackupEnabled_" + Tag(username), enabled ? "1" : "0");

        /// <summary>Thư mục sao lưu của người dùng (mặc định: Documents\TQLTK Backup).</summary>
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

        /// <summary>
        /// Đặt thư mục sao lưu sau khi thử tạo thư mục và ghi thử một file.
        /// Trả về null nếu thành công, hoặc thông báo lỗi (khi đó không đổi gì).
        /// </summary>
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

        // ================================================================
        // ================== SAO LƯU ==================
        // ================================================================

        /// <summary>
        /// Sao lưu vault của người dùng ngay bây giờ (không kiểm tra có thay đổi hay không).
        /// Nặng về CPU một chút (mã hóa bằng cụm mật khẩu), nên gọi từ luồng nền nếu có thể.
        /// </summary>
        public static (bool ok, string message, string? path) BackupNow(string username, string reason = "Thủ công")
        {
            try
            {
                string folder = GetFolder(username);
                Directory.CreateDirectory(folder);

                string prefix = $"{FilePrefix}{Tag(username)}_";

                // File .tmp sót lại từ lần bị tắt giữa chừng
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

                // Chỉ giữ KeepCount bản gần nhất của người dùng này (tên có dấu thời gian nên xếp theo tên là xếp theo ngày)
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

        /// <summary>
        /// Chạy khi thoát / đăng xuất / tự khóa: sao lưu nếu đang bật, có tài khoản và dữ liệu đã đổi
        /// kể từ lần sao lưu gần nhất. Không bao giờ ném lỗi (lỗi chỉ được ghi vào nhật ký).
        /// </summary>
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

        /// <summary>
        /// Ghi nhận vừa có một bản sao lưu ở nơi khác (xuất vault thủ công, đồng bộ Drive)
        /// để thanh trạng thái và lời nhắc tính cả các bản đó.
        /// </summary>
        public static void RecordBackup(string username) =>
            WriteValue("LastBackupUtc_" + Tag(username), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        // ================================================================
        // ================== TRẠNG THÁI / NHẮC ==================
        // ================================================================

        /// <summary>Thời điểm (UTC) của lần sao lưu gần nhất, null nếu chưa từng.</summary>
        public static DateTime? GetLastBackupUtc(string username)
        {
            DateTime? recorded = ParseUtc(ReadValue("LastBackupUtc_" + Tag(username)));
            return Max(recorded, NewestBackupFileUtc(username));
        }

        /// <summary>true nếu file vault được ghi sau lần sao lưu gần nhất (hoặc chưa từng sao lưu).</summary>
        public static bool NeedsBackup(string username)
        {
            DateTime? vault = new AccountVaultService().GetLastWriteUtc(username);
            if (vault == null) return false;

            DateTime? last = GetLastBackupUtc(username);
            return last == null || vault.Value > last.Value;
        }

        /// <summary>Dòng hiển thị trên thanh trạng thái, và có nên tô màu cảnh báo không.</summary>
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

        /// <summary>
        /// Nội dung nhắc sao lưu, hoặc null nếu chưa cần nhắc. Chỉ nhắc khi dữ liệu đã đổi kể từ lần sao lưu
        /// gần nhất VÀ (đã quá RemindAfterDays ngày, hoặc chưa từng sao lưu mà tính năng tự sao lưu đang tắt).
        /// </summary>
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
                        ? null   // sẽ tự sao lưu ở lần thoát tới
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

        // ================================================================
        // ================== NỘI BỘ ==================
        // ================================================================

        private static string Tag(string username) => BackupCrypto.UserTag(username);

        /// <summary>Số tài khoản trong vault; -1 nếu không đọc được.</summary>
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
