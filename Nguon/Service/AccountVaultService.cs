using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Ném ra khi file vault tồn tại nhưng không đọc được (sai key, file hỏng...).
    /// Giao diện cần bắt lỗi này, báo cho người dùng và KHÔNG gọi Save,
    /// để dữ liệu cũ không bị ghi đè.
    /// </summary>
    public class VaultUnreadableException : Exception
    {
        public VaultUnreadableException(string message, Exception? inner = null)
            : base(message, inner) { }
    }

    /// <summary>
    /// Đọc / ghi vault tài khoản (mã hóa AES-GCM). Định dạng CBC cũ không được mở;
    /// file gốc được giữ nguyên để phục hồi riêng.
    ///
    /// CACHE: danh sách đã giải mã được giữ trong bộ nhớ (dùng chung cho mọi instance, theo từng file vault)
    /// để Search / Count / Load không phải đọc file và giải mã lại mỗi lần.
    /// - Cache chỉ dùng được khi file trên đĩa còn y nguyên (so ngày sửa + dung lượng); file đổi bởi bất kỳ
    ///   instance nào (Save) hay bởi bên ngoài thì lần đọc sau tự nạp lại.
    /// - Load / Search luôn trả về BẢN SAO, nên nơi gọi sửa danh sách thoải mái mà không làm hỏng cache.
    /// - Gọi ClearCache() khi đăng xuất / tự khóa / thoát để không giữ mật khẩu đã giải mã trong bộ nhớ.
    /// </summary>
    public class AccountVaultService
    {
        private static readonly JsonSerializerOptions JsonOpts =
            new JsonSerializerOptions { WriteIndented = true };

        private sealed class CacheEntry
        {
            public List<SavedAccount> Accounts = new();   // không bao giờ bị sửa tại chỗ, chỉ bị thay cả entry
            public DateTime WriteTimeUtc;
            public long Length;
        }

        private static readonly Dictionary<string, CacheEntry> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly object CacheLock = new();

        private readonly string _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager");

        private string GetVaultFile(string username)
        {
            var userService = new UserService();
            var user = userService.FindByUsername(username)
                ?? throw new InvalidOperationException("Không tìm thấy tài khoản để xác định kho dữ liệu riêng.");

            string isolatedPath = Path.Combine(
                _root, $"vault_{UserStorageIdentity.GetDirectoryName(user.Id)}.dat");
            string legacyPath = UserStorageIdentity.GetLegacyPath(
                _root, user.Username, LegacyUserStorageKind.Vault);

            if (string.Equals(isolatedPath, legacyPath, StringComparison.OrdinalIgnoreCase) ||
                (!File.Exists(legacyPath) && !File.Exists(legacyPath + ".bak")))
                return isolatedPath;

            using var migrationLock = UserStorageIdentity.AcquireMigrationLock(user.Id);
            if (!File.Exists(legacyPath) && !File.Exists(legacyPath + ".bak"))
                return isolatedPath;

            if (userService.HasLegacyStoragePathCollision(
                user.Id, user.Username, LegacyUserStorageKind.Vault))
                throw new VaultUnreadableException(
                    "File vault cũ trùng đường dẫn với tài khoản khác. File cũ được giữ nguyên; " +
                    "không thể tự chọn chủ sở hữu an toàn.");

            UserStorageIdentity.MoveLegacyFile(legacyPath, isolatedPath, user.Id);
            UserStorageIdentity.MoveLegacyFile(
                legacyPath + ".bak", isolatedPath + ".bak", user.Id);
            return isolatedPath;
        }

        // ================================================================
        // ================== CACHE ==================
        // ================================================================

        /// <summary>Xóa toàn bộ danh sách đã giải mã khỏi bộ nhớ (gọi khi đăng xuất / tự khóa / thoát).</summary>
        public static void ClearCache()
        {
            lock (CacheLock) Cache.Clear();
        }

        private static List<SavedAccount> CloneAll(IEnumerable<SavedAccount> source) =>
            source.Select(a => a.Clone()).ToList();

        private static void StoreCache(string file, FileInfo info, List<SavedAccount> ownedList)
        {
            lock (CacheLock)
            {
                Cache[file] = new CacheEntry
                {
                    Accounts = ownedList,
                    WriteTimeUtc = info.LastWriteTimeUtc,
                    Length = info.Length
                };
            }
        }

        private static void DropCache(string file)
        {
            lock (CacheLock) Cache.Remove(file);
        }

        // ================================================================
        // ================== ĐỌC ==================
        // ================================================================

        /// <summary>
        /// Đọc và giải mã một file vault. Trả về null nếu file không tồn tại hoặc không giải mã được
        /// (lý do nằm trong "error"). Lỗi I/O (file đang bị khóa...) vẫn được ném ra ngoài.
        /// Hàm này KHÔNG dùng cache (Save dựa vào nó để kiểm tra file hiện tại còn đọc được không).
        /// </summary>
        private static List<SavedAccount>? TryRead(string file, out Exception? error)
        {
            error = null;

            if (!File.Exists(file)) return null;

            string encrypted = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(encrypted)) return new List<SavedAccount>();

            try
            {
                string json = AesCrypto.Decrypt(encrypted);
                return JsonSerializer.Deserialize<List<SavedAccount>>(json)
                       ?? new List<SavedAccount>();
            }
            catch (Exception ex) when (
                ex is CryptographicException ||
                ex is FormatException ||
                ex is JsonException)
            {
                error = ex;
                return null;
            }
        }

        /// <summary>
        /// Lấy danh sách dùng chung (từ cache nếu file chưa đổi, không thì đọc và giải mã rồi nạp cache).
        /// CHỈ ĐỌC: không được sửa danh sách trả về, vì nó chính là bản trong cache.
        /// </summary>
        private List<SavedAccount> LoadShared(string username)
        {
            string file = GetVaultFile(username);

            var info = new FileInfo(file);
            if (!info.Exists)
            {
                DropCache(file);
                return new List<SavedAccount>();
            }

            // Lấy dấu file TRƯỚC khi đọc: nếu file đổi giữa chừng thì lần sau dấu lệch và tự nạp lại
            DateTime stamp = info.LastWriteTimeUtc;
            long length = info.Length;

            lock (CacheLock)
            {
                if (Cache.TryGetValue(file, out var hit) &&
                    hit.WriteTimeUtc == stamp && hit.Length == length)
                    return hit.Accounts;
            }

            var accounts = TryRead(file, out Exception? mainError);
            if (accounts != null)
            {
                StoreCache(file, info, accounts);
                return accounts;
            }

            // File chính hỏng → thử bản dự phòng gần nhất (không nạp cache, trường hợp hiếm)
            DropCache(file);
            var fromBackup = TryRead(file + ".bak", out _);
            if (fromBackup != null)
            {
                // Restore the last valid backup and write it in the authenticated format.
                Save(username, fromBackup);
                return fromBackup;
            }

            throw new VaultUnreadableException(
                "Không đọc được dữ liệu đã lưu. Dữ liệu cũ vẫn được giữ nguyên, " +
                "đừng xóa thư mục %AppData%\\PersonalInfoManager. " +
                (mainError != null ? "Chi tiết: " + mainError.Message : ""),
                mainError);
        }

        /// <summary>
        /// Đọc danh sách tài khoản. Chưa có file → danh sách rỗng.
        /// File có nhưng không đọc được → thử bản .bak, nếu vẫn không được thì ném VaultUnreadableException.
        /// Trả về BẢN SAO: được phép sửa / thêm / xóa rồi gọi Save.
        /// </summary>
        public List<SavedAccount> Load(string username) => CloneAll(LoadShared(username));

        // ================================================================
        // ================== GHI ==================
        // ================================================================

        /// <summary>
        /// Ghi danh sách tài khoản. Ghi qua file tạm, giữ bản .bak của lần ghi trước,
        /// và kiểm tra giải mã lại được trước khi thay file chính.
        /// Ghi xong thì cache được cập nhật luôn bằng danh sách vừa ghi.
        /// </summary>
        public void Save(string username, List<SavedAccount> accounts)
        {
            Directory.CreateDirectory(_root);
            string file = GetVaultFile(username);

            string json = JsonSerializer.Serialize(accounts, JsonOpts);
            string encrypted = AesCrypto.Encrypt(json);

            // Kiểm tra bản vừa mã hóa đọc lại được thì mới ghi
            if (AesCrypto.Decrypt(encrypted) != json)
                throw new CryptographicException(
                    "Kiểm tra sau mã hóa thất bại, dữ liệu chưa được ghi.");

            if (File.Exists(file))
            {
                var previous = TryRead(file, out _);
                if (previous != null)
                {
                    // Keep the previous readable snapshot as an authenticated AES-GCM backup.
                    string previousJson = JsonSerializer.Serialize(previous, JsonOpts);
                    WriteAtomically(file + ".bak", AesCrypto.Encrypt(previousJson));
                }
                else
                {
                    // File hiện tại không đọc được → giữ lại một bản, không để mất hẳn
                    File.Copy(file,
                        file + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"),
                        overwrite: true);
                    // Preserve a protected recovery copy of the validated in-memory data.
                    WriteAtomically(file + ".bak", encrypted);
                }
            }

            WriteAtomically(file, encrypted);

            // Cache giữ BẢN SAO của danh sách vừa ghi (nơi gọi có thể còn sửa danh sách của họ)
            try { StoreCache(file, new FileInfo(file), CloneAll(accounts)); }
            catch { DropCache(file); }
        }

        private static void WriteAtomically(string path, string contents)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, contents);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        // ================================================================
        // ================== TÌM KIẾM / ĐẾM ==================
        // ================================================================

        public List<SavedAccount> Search(string username, string keyword)
        {
            var all = LoadShared(username);
            if (string.IsNullOrWhiteSpace(keyword)) return CloneAll(all);

            keyword = keyword.Trim().ToLower();
            return CloneAll(all.Where(a =>
                (a.ServiceName ?? "").ToLower().Contains(keyword) ||
                (a.Username ?? "").ToLower().Contains(keyword) ||
                (a.Note ?? "").ToLower().Contains(keyword)
            ));
        }

        /// <summary>Đếm số tài khoản, không cần tạo bản sao.</summary>
        public int Count(string username) => LoadShared(username).Count;

        /// <summary>Thời điểm (UTC) file vault của người dùng được ghi lần cuối; null nếu chưa có file.</summary>
        public DateTime? GetLastWriteUtc(string username)
        {
            var info = new FileInfo(GetVaultFile(username));
            return info.Exists ? info.LastWriteTimeUtc : null;
        }
    }
}
