using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using PersonalInfoManager.Models;
namespace PersonalInfoManager.Services
{
    public class VaultUnreadableException : Exception
    {
        public VaultUnreadableException(string message, Exception? inner = null)
            : base(message, inner) { }
    }
    public class AccountVaultService
    {
        public const int TrashRetentionDays = 30;
        private static readonly JsonSerializerOptions JsonOpts =
            new JsonSerializerOptions { WriteIndented = true };
        private sealed class CacheEntry
        {
            public List<SavedAccount> Accounts = new();
            public DateTime WriteTimeUtc;
            public long Length;
        }
        private static readonly Dictionary<string, CacheEntry> Cache =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new();
        private readonly string _root = Path.Combine(
            ApplicationDataPath.Root,
            "PersonalInfoManager");
        private string GetVaultFile(string username)
        {
            var userService = new UserService();
            var user = userService.FindByUsername(username)
                ?? throw new InvalidOperationException(LocalizationAuditService.Get("storage.vault.message001", "vi"));
            if (!user.IsActive)
                throw new UnauthorizedAccessException(LocalizationAuditService.Get("storage.vault.message002", "vi"));
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
        private List<SavedAccount> LoadShared(string username)
        {
            string file = GetVaultFile(username);
            var info = new FileInfo(file);
            if (!info.Exists)
            {
                DropCache(file);
                return new List<SavedAccount>();
            }
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
            DropCache(file);
            var fromBackup = TryRead(file + ".bak", out _);
            if (fromBackup != null)
            {
                Save(username, fromBackup);
                return fromBackup;
            }
            throw new VaultUnreadableException(
                "Không đọc được dữ liệu đã lưu. Dữ liệu cũ vẫn được giữ nguyên, " +
                "đừng xóa thư mục %AppData%\\PersonalInfoManager. " +
                (mainError != null ? "Chi tiết: " + mainError.Message : ""),
                mainError);
        }
        public List<SavedAccount> Load(string username) => CloneAll(LoadShared(username));
        public int PurgeExpiredTrash(string username)
        {
            var accounts = Load(username);
            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-TrashRetentionDays);
            int removed = accounts.RemoveAll(account => account.IsDeleted &&
                account.DeletedAt.HasValue && account.DeletedAt.Value.ToUniversalTime() <= cutoffUtc);
            foreach (var account in accounts)
            {
                account.DeletedNotes ??= new List<DeletedNoteEntry>();
                removed += account.DeletedNotes.RemoveAll(note => note.DeletedAt.ToUniversalTime() <= cutoffUtc);
            }
            if (removed > 0) Save(username, accounts);
            return removed;
        }
        public void Save(string username, List<SavedAccount> accounts)
        {
            using var storageLock = AesCrypto.AcquireStorageLock();
            ValidateAttachments(accounts);
            Directory.CreateDirectory(_root);
            string file = GetVaultFile(username);
            string json = JsonSerializer.Serialize(accounts, JsonOpts);
            string encrypted = AesCrypto.Encrypt(json);
            if (AesCrypto.Decrypt(encrypted) != json)
                throw new CryptographicException(
                    LocalizationAuditService.Get("storage.vault.message003", "vi"));
            if (File.Exists(file))
            {
                var previous = TryRead(file, out _);
                if (previous != null)
                {
                    string previousJson = JsonSerializer.Serialize(previous, JsonOpts);
                    WriteAtomically(file + ".bak", AesCrypto.Encrypt(previousJson));
                }
                else
                {
                    File.Copy(file,
                        file + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"),
                        overwrite: true);
                    WriteAtomically(file + ".bak", encrypted);
                }
            }
            WriteAtomically(file, encrypted);
            try { StoreCache(file, new FileInfo(file), CloneAll(accounts)); }
            catch { DropCache(file); }
        }
        internal static void ValidateAttachments(IEnumerable<SavedAccount> accounts)
        {
            foreach (var account in accounts)
            {
                account.Attachments ??= new List<AccountAttachment>();
                if (account.Attachments.Count > 50)
                    throw new InvalidDataException(LocalizationAuditService.Get("storage.vault.message004", "vi"));
                long total = 0;
                foreach (var attachment in account.Attachments)
                {
                    if (attachment == null || string.IsNullOrWhiteSpace(attachment.FileName) ||
                        attachment.FileName != Path.GetFileName(attachment.FileName) ||
                        attachment.FileName.Length > 255 || attachment.FileName.Any(char.IsControl) ||
                        attachment.Content == null || attachment.Content.Length > 10 * 1024 * 1024)
                        throw new InvalidDataException(LocalizationAuditService.Get("storage.vault.message005", "vi"));
                    total += attachment.Content.Length;
                    if (total > 25 * 1024 * 1024)
                        throw new InvalidDataException(LocalizationAuditService.Get("storage.vault.message006", "vi"));
                }
            }
        }
        private static void WriteAtomically(string path, string contents)
        {
            AtomicFileService.WriteAllText(path, contents);
        }
        public List<SavedAccount> Search(string username, string keyword)
        {
            var all = LoadShared(username);
            if (string.IsNullOrWhiteSpace(keyword)) return CloneAll(all.Where(a => !a.IsDeleted));
            keyword = keyword.Trim().ToLower();
            return CloneAll(all.Where(a => !a.IsDeleted && (
                (a.ServiceName ?? "").ToLower().Contains(keyword) ||
                (a.Username ?? "").ToLower().Contains(keyword) ||
                (a.Note ?? "").ToLower().Contains(keyword) ||
                (a.WebsiteUrl ?? "").ToLower().Contains(keyword) ||
                (a.Folder ?? "").ToLower().Contains(keyword) ||
                (a.Tags ?? new List<string>()).Any(tag => (tag ?? "").ToLower().Contains(keyword)) ||
                (a.CustomFields ?? new List<AccountCustomField>()).Any(field =>
                    field != null && ((field.Name ?? "").ToLower().Contains(keyword) ||
                    (!field.IsSensitive && (field.Value ?? "").ToLower().Contains(keyword))))
                )
            ));
        }
        public int Count(string username) => LoadShared(username).Count(account => !account.IsDeleted);
        public DateTime? GetLastWriteUtc(string username)
        {
            var info = new FileInfo(GetVaultFile(username));
            return info.Exists ? info.LastWriteTimeUtc : null;
        }
    }
}
