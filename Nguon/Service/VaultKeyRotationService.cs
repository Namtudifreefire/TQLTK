using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace PersonalInfoManager.Services
{
    public sealed record VaultKeyRotationResult(int ReencryptedFiles, int PreservedLegacyBackups);
    public static class VaultKeyRotationService
    {
        private const string JournalName = "key-rotation.journal";
        private static readonly byte[] JournalEntropy = Encoding.UTF8.GetBytes("TQLTK.VaultKeyRotation.v1");
#if SECURITY_TEST
        internal static Action<string>? TestCheckpoint { get; set; }
        internal static bool PreserveJournalAfterTestFailure { get; set; }
#endif
        private sealed class RotationJournal
        {
            public string Phase { get; set; } = "prepared";
            public string OldKeyHash { get; set; } = "";
            public string NewKeyHash { get; set; } = "";
            public string NewProtectedKey { get; set; } = "";
            public List<RotationFile> Files { get; set; } = new();
        }
        private sealed class RotationFile
        {
            public string Destination { get; set; } = "";
            public string Staged { get; set; } = "";
            public string RecoveryCopy { get; set; } = "";
            public string OriginalHash { get; set; } = "";
            public string NewHash { get; set; } = "";
        }
        public static VaultKeyRotationResult Rotate(string username) => RotateCore(username, null);
        internal static VaultKeyRotationResult RotateForTesting(
            string username, IReadOnlyCollection<string> backupDirectories) =>
            RotateCore(username, backupDirectories);
        private static VaultKeyRotationResult RotateCore(
            string username, IReadOnlyCollection<string>? backupDirectories)
        {
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException(LocalizationAuditService.Get("storage.rotation.message001", "vi"), nameof(username));
            using var storageLock = AesCrypto.AcquireStorageLock();
            string root = AesCrypto.StorageDirectory;
            Directory.CreateDirectory(root);
            RecoverIfNeeded(root);
            byte[] oldKey = AesCrypto.GetOrCreateKeyUnderLock();
            byte[] newKey = RandomNumberGenerator.GetBytes(32);
            var journal = new RotationJournal
            {
                OldKeyHash = HashFile(AesCrypto.StorageKeyFile),
                NewProtectedKey = Convert.ToBase64String(AesCrypto.ProtectKey(newKey)),
                Phase = "staging"
            };
            string journalPath = Path.Combine(root, JournalName);
            bool journalWritten = false;
            int legacyBackups = 0;
            try
            {
                byte[] protectedNewKey = Convert.FromBase64String(journal.NewProtectedKey);
                journal.NewKeyHash = HashBytes(protectedNewKey);
                CryptographicOperations.ZeroMemory(protectedNewKey);
                WriteJournal(journalPath, journal);
                journalWritten = true;
                WriteHistoricalKey(root, oldKey);
                foreach (string candidate in FindRotationCandidates(root, username, backupDirectories).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!File.Exists(candidate)) continue;
                    if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException($"Không xoay tệp liên kết ngoài kho: {Path.GetFileName(candidate)}.");
                    string encrypted = File.ReadAllText(candidate, Encoding.UTF8);
                    if (BackupCrypto.IsPassphraseFormat(encrypted))
                    {
                        legacyBackups++;
                        continue;
                    }
                    if (!AesCrypto.IsAuthenticatedV2Format(encrypted))
                        throw new CryptographicException(
                            $"Không thể xoay khóa an toàn vì '{Path.GetFileName(candidate)}' không dùng định dạng AES-GCM được hỗ trợ. File vẫn được giữ nguyên.");
                    string plaintext = AesCrypto.DecryptWithAvailableKeysUnderLock(
                        encrypted.Trim().Substring("v2:".Length), oldKey);
                    byte[] newEncrypted = AesCrypto.EncryptWithKey(newKey, plaintext);
                    string directory = Path.GetDirectoryName(candidate)!;
                    string id = Guid.NewGuid().ToString("N");
                    string staged = Path.Combine(directory, ".tqltk-rotation-" + id + ".tmp");
                    string recovery = Path.Combine(directory, ".tqltk-rotation-" + id + ".bak");
                    var record = new RotationFile
                    {
                        Destination = Path.GetFullPath(candidate),
                        Staged = Path.GetFullPath(staged),
                        RecoveryCopy = Path.GetFullPath(recovery),
                        OriginalHash = HashFile(candidate)
                    };
                    journal.Files.Add(record);
                    WriteJournal(journalPath, journal);
                    try
                    {
                        using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                        {
                            output.Write(newEncrypted);
                            output.Flush(flushToDisk: true);
                        }
                        string stagedText = Encoding.UTF8.GetString(newEncrypted);
                        if (!string.Equals(AesCrypto.DecryptWithKey(newKey, stagedText), plaintext, StringComparison.Ordinal))
                            throw new CryptographicException(LocalizationAuditService.Get("storage.rotation.message002", "vi"));
                        File.Copy(candidate, recovery, overwrite: false);
                        record.NewHash = HashBytes(newEncrypted);
                        WriteJournal(journalPath, journal);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(newEncrypted);
                    }
                }
                journal.Phase = "prepared";
                WriteJournal(journalPath, journal);
                ReachCheckpoint("prepared");
                CompleteRotation(root, journal);
                return new VaultKeyRotationResult(journal.Files.Count, legacyBackups);
            }
            catch
            {
#if SECURITY_TEST
                if (PreserveJournalAfterTestFailure) throw;
#endif
                if (!journalWritten)
                    Cleanup(journal);
                else
                {
                    try { RecoverIfNeeded(root); } catch { }
                }
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(oldKey);
                CryptographicOperations.ZeroMemory(newKey);
            }
        }
        internal static void RecoverIfNeeded(string root)
        {
            string path = Path.Combine(root, JournalName);
            if (!File.Exists(path)) return;
            RotationJournal journal = ReadJournal(path);
            CompleteRotation(root, journal);
        }
        private static IEnumerable<string> FindRotationCandidates(
            string root, string username, IReadOnlyCollection<string>? backupDirectories)
        {
            foreach (string path in Directory.EnumerateFiles(root, "vault_*.dat", SearchOption.TopDirectoryOnly))
                yield return path;
            foreach (string path in Directory.EnumerateFiles(root, "vault_*.dat.bak", SearchOption.TopDirectoryOnly))
                yield return path;
            string notes = Path.Combine(root, "notes.json");
            if (File.Exists(notes)) yield return notes;
            IEnumerable<string> folders;
            if (backupDirectories != null)
            {
                folders = backupDirectories;
            }
            else
            {
                IEnumerable<string> usernames = new[] { username }
                    .Concat(new UserService().GetAll().Select(user => user.Username))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                folders = usernames.Select(AutoBackupService.GetFolder)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            }
            foreach (string backupFolder in folders)
            {
                string fullFolder = Path.GetFullPath(backupFolder);
                if (!Directory.Exists(fullFolder)) continue;
                if ((File.GetAttributes(fullFolder) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(LocalizationAuditService.Get("storage.rotation.message003", "vi"));
                foreach (string path in Directory.EnumerateFiles(fullFolder, "*.vault", SearchOption.TopDirectoryOnly))
                    yield return path;
            }
        }
        private static void WriteHistoricalKey(string root, byte[] oldKey)
        {
            string directory = Path.Combine(root, "key-history");
            Directory.CreateDirectory(directory);
            byte[] protectedKey = AesCrypto.ProtectKey(oldKey);
            string destination = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".dat");
            try { AtomicFileService.WriteAllBytes(destination, protectedKey); }
            finally { CryptographicOperations.ZeroMemory(protectedKey); }
        }
        private static void CompleteRotation(string root, RotationJournal journal)
        {
            if (journal.Phase == "staging")
            {
                Cleanup(journal);
                AtomicFileService.TryDelete(Path.Combine(root, JournalName));
                return;
            }
            if (journal.Phase == "complete")
            {
                Cleanup(journal);
                AtomicFileService.TryDelete(Path.Combine(root, JournalName));
                return;
            }
            foreach (RotationFile file in journal.Files)
            {
                string actualHash = File.Exists(file.Destination) ? HashFile(file.Destination) : "";
                if (string.Equals(actualHash, file.NewHash, StringComparison.Ordinal)) continue;
                if (!string.Equals(actualHash, file.OriginalHash, StringComparison.Ordinal))
                    throw new IOException(
                        $"Phục hồi xoay khóa bị dừng vì '{Path.GetFileName(file.Destination)}' đã đổi ngoài giao dịch. File gốc và nhật ký vẫn được giữ nguyên.");
                if (!File.Exists(file.Staged) || !string.Equals(HashFile(file.Staged), file.NewHash, StringComparison.Ordinal))
                    throw new IOException($"Thiếu bản đã mã hóa để phục hồi '{Path.GetFileName(file.Destination)}'. File gốc vẫn được giữ nguyên.");
                if (!File.Exists(file.RecoveryCopy) || !string.Equals(HashFile(file.RecoveryCopy), file.OriginalHash, StringComparison.Ordinal))
                    throw new IOException($"Thiếu bản phục hồi trước khi xoay khóa cho '{Path.GetFileName(file.Destination)}'.");
                string replacement = Path.Combine(Path.GetDirectoryName(file.Destination)!, ".tqltk-rotation-commit-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.Copy(file.Staged, replacement, overwrite: false);
                    AtomicFileService.PromoteTemporary(replacement, file.Destination);
                    if (!string.Equals(HashFile(file.Destination), file.NewHash, StringComparison.Ordinal))
                        throw new CryptographicException(
                            $"Không xác minh được tệp sau khi thay thế '{Path.GetFileName(file.Destination)}'. Khóa mới chưa được kích hoạt.");
                    ReachCheckpoint("file-replaced");
                }
                finally { AtomicFileService.TryDelete(replacement); }
            }
            byte[] protectedKey = Convert.FromBase64String(journal.NewProtectedKey);
            try
            {
                string currentHash = File.Exists(AesCrypto.StorageKeyFile) ? HashFile(AesCrypto.StorageKeyFile) : "";
                if (!string.Equals(currentHash, journal.NewKeyHash, StringComparison.Ordinal))
                {
                    if (!string.Equals(currentHash, journal.OldKeyHash, StringComparison.Ordinal))
                        throw new IOException(LocalizationAuditService.Get("storage.rotation.message004", "vi"));
                    AtomicFileService.WriteAllBytes(AesCrypto.StorageKeyFile, protectedKey);
                    ReachCheckpoint("key-replaced");
                }
            }
            finally { CryptographicOperations.ZeroMemory(protectedKey); }
            journal.Phase = "complete";
            WriteJournal(Path.Combine(root, JournalName), journal);
            Cleanup(journal);
            AtomicFileService.TryDelete(Path.Combine(root, JournalName));
        }
        private static void WriteJournal(string path, RotationJournal journal)
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(journal);
            byte[] protectedData = ProtectedData.Protect(json, JournalEntropy, DataProtectionScope.CurrentUser);
            try { AtomicFileService.WriteAllBytes(path, protectedData); }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
                CryptographicOperations.ZeroMemory(protectedData);
            }
        }
        private static RotationJournal ReadJournal(string path)
        {
            byte[] protectedData = File.ReadAllBytes(path);
            byte[]? json = null;
            try
            {
                json = ProtectedData.Unprotect(protectedData, JournalEntropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<RotationJournal>(json)
                    ?? throw new CryptographicException(LocalizationAuditService.Get("storage.rotation.message005", "vi"));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedData);
                if (json != null) CryptographicOperations.ZeroMemory(json);
            }
        }
        private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        private static void ReachCheckpoint(string name)
        {
#if SECURITY_TEST
            TestCheckpoint?.Invoke(name);
#endif
        }
        private static void Cleanup(RotationJournal journal)
        {
            foreach (RotationFile file in journal.Files)
            {
                AtomicFileService.TryDelete(file.Staged);
                AtomicFileService.TryDelete(file.RecoveryCopy);
            }
        }
    }
}
