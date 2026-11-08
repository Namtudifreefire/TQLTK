using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace PersonalInfoManager.Services
{
    public sealed class FileRestorePreview
    {
        internal FileRestorePreview(string owner, int addedCount, int skippedCount,
            long totalBytes, int invalidCount, string fingerprint, string localFingerprint,
            List<BackupPreviewItem> items)
        {
            Owner = owner;
            AddedCount = addedCount;
            SkippedCount = skippedCount;
            TotalBytes = totalBytes;
            InvalidCount = invalidCount;
            Fingerprint = fingerprint;
            LocalFingerprint = localFingerprint;
            Items = items;
        }
        public string Owner { get; }
        public int AddedCount { get; }
        public int SkippedCount { get; }
        public long TotalBytes { get; }
        public int InvalidCount { get; }
        public IReadOnlyList<BackupPreviewItem> Items { get; }
        internal string Fingerprint { get; }
        internal string LocalFingerprint { get; }
    }
    public static class FileBackupService
    {
        public const long MaxTotalBytes = 100L * 1024 * 1024;
        private const int CurrentFormatVersion = 1;
        private const string ManifestEntryName = "__tqltk__/manifest.json";
        private static readonly Regex ReservedName =
            new(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private sealed class ArchiveManifest
        {
            public int FormatVersion { get; set; }
            public string ExportedBy { get; set; } = "";
            public DateTime ExportedAt { get; set; }
            public int FileCount { get; set; }
            public long TotalBytes { get; set; }
        }
        public static (int count, long bytes) Summarize(string username)
        {
            var fileService = new LocalFileService();
            var files = fileService.ListFiles(username);
            return (files.Count, files.Sum(f => fileService.GetPlaintextLength(f.FullName)));
        }
        public static bool IsBackupFile(string path)
        {
            if (!File.Exists(path)) return false;
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            byte[] prefix = new byte[4];
            int read = input.Read(prefix, 0, prefix.Length);
            return read == prefix.Length && BackupCrypto.IsBinaryFormat(prefix);
        }
        public static (bool ok, string message, int count, long bytes) InspectBackup(
            string username, string sourcePath, string? passphrase = null,
            Func<string?, string?>? askPassphrase = null,
            CancellationToken cancellationToken = default)
        {
            byte[]? zipBytes = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(sourcePath)) return (false, LocalizationAuditService.Get("backup.files.message001", "vi"), 0, 0);
                if (new FileInfo(sourcePath).Length > MaxTotalBytes + 16L * 1024 * 1024)
                    return (false, LocalizationAuditService.Get("backup.files.message002", "vi"), 0, 0);
                byte[] data = File.ReadAllBytes(sourcePath);
                if (!BackupCrypto.IsBinaryFormat(data))
                    return (false, LocalizationAuditService.Get("backup.files.message003", "vi"), 0, 0);
                zipBytes = DecryptWithPrompts(data, username, passphrase, askPassphrase);
                if (zipBytes == null)
                    return (false, LocalizationAuditService.Get("backup.files.message004", "vi"), 0, 0);
                using var ms = new MemoryStream(zipBytes, writable: false);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                var manifestEntry = zip.GetEntry(ManifestEntryName);
                if (manifestEntry == null || manifestEntry.Length > 64 * 1024)
                    return (false, LocalizationAuditService.Get("backup.files.message005", "vi"), 0, 0);
                var entries = zip.Entries.Where(IsUserFileEntry).ToList();
                if (entries.Count == 0)
                    return (false, LocalizationAuditService.Get("backup.files.message006", "vi"), 0, 0);
                long total = 0;
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    total = checked(total + entry.Length);
                    if (total > MaxTotalBytes)
                        return (false, LocalizationAuditService.Get("backup.files.message007", "vi"), 0, 0);
                    using var stream = entry.Open();
                    CopyBounded(stream, Stream.Null, entry.Length, cancellationToken);
                }
                if (!TryReadAndValidateManifest(zip, entries.Count, total, out var manifest))
                    return (false, LocalizationAuditService.Get("backup.files.message008", "vi"), 0, 0);
                int invalid = zip.Entries.Count(e =>
                    e.Name.Length > 0 && !IsUserFileEntry(e) && e.FullName != ManifestEntryName);
                string owner = manifest.ExportedBy.Trim();
                string ownerText = owner.Length == 0 ? "không rõ người tạo" : $"người tạo: {owner}";
                string message = $"Bản sao lưu hợp lệ: {entries.Count} file, {FormatSize(total)}, {ownerText}. " +
                                 "Đã kiểm tra giải mã và nội dung nén; chưa thay đổi dữ liệu trong kho.";
                if (invalid > 0) message += $" Bỏ qua {invalid} mục có tên không hợp lệ nếu khôi phục.";
                return (true, message, entries.Count, total);
            }
            catch (OperationCanceledException)
            {
                return (false, LocalizationAuditService.Get("backup.files.message009", "vi"), 0, 0);
            }
            catch (Exception ex)
            {
                return (false, $"Không thể xác minh bản sao lưu: {ex.Message}", 0, 0);
            }
            finally
            {
                if (zipBytes != null) CryptographicOperations.ZeroMemory(zipBytes);
            }
        }
        public static string FormatSize(long bytes) =>
            bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1048576.0:0.#} MB";
        public static (bool ok, string message, int count, long bytes) DrillRestore(
            string username, string sourcePath, string? passphrase = null,
            Func<string?, string?>? askPassphrase = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            byte[]? archiveBytes = null;
            string? drillDirectory = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(sourcePath)) return (false, LocalizationAuditService.Get("backup.files.message001", "vi"), 0, 0);
                if (new FileInfo(sourcePath).Length > MaxTotalBytes + 16L * 1024 * 1024)
                    return (false, LocalizationAuditService.Get("backup.files.message002", "vi"), 0, 0);
                byte[] encrypted = File.ReadAllBytes(sourcePath);
                if (!BackupCrypto.IsBinaryFormat(encrypted))
                    return (false, LocalizationAuditService.Get("backup.files.message003", "vi"), 0, 0);
                archiveBytes = DecryptWithPrompts(encrypted, username, passphrase, askPassphrase);
                CryptographicOperations.ZeroMemory(encrypted);
                if (archiveBytes == null)
                    return (false, LocalizationAuditService.Get("backup.files.message004", "vi"), 0, 0);
                using var memory = new MemoryStream(archiveBytes, writable: false);
                using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
                var entries = zip.Entries.Where(IsUserFileEntry).ToList();
                long total = 0;
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    total = checked(total + entry.Length);
                    if (entry.Length < 0 || total > MaxTotalBytes)
                        return (false, LocalizationAuditService.Get("backup.files.message007", "vi"), 0, 0);
                }
                if (entries.Count == 0 || !TryReadAndValidateManifest(zip, entries.Count, total, out _))
                    return (false, LocalizationAuditService.Get("backup.files.message008", "vi"), 0, 0);
                if (entries.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
                    return (false, LocalizationAuditService.Get("backup.files.message010", "vi"), 0, 0);
                string tempRoot = Path.GetFullPath(Path.GetTempPath());
                drillDirectory = Path.GetFullPath(Path.Combine(tempRoot, "TQLTK-Restore-Drill-" + Guid.NewGuid().ToString("N")));
                if (!drillDirectory.StartsWith(tempRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    throw new IOException(LocalizationAuditService.Get("backup.files.message011", "vi"));
                Directory.CreateDirectory(drillDirectory);
                if ((File.GetAttributes(drillDirectory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(LocalizationAuditService.Get("backup.files.message012", "vi"));
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string destination = Path.Combine(drillDirectory, entry.Name);
                    using Stream content = entry.Open();
                    LocalFileEncryptionService.EncryptStreamAtomic(
                        content, entry.Length, destination, overwrite: false,
                        lastWriteTimeUtc: null, cancellationToken);
                    using Stream comparison = entry.Open();
                    if (LocalFileEncryptionService.GetPlaintextLength(destination) != entry.Length ||
                        !LocalFileEncryptionService.ContentEqualsStored(
                            destination, comparison, entry.Length, cancellationToken))
                        throw new InvalidDataException($"Nội dung kiểm tra sau khôi phục không khớp: {entry.Name}");
                }
                return (true,
                    $"Diễn tập thành công: đã khôi phục thử {entries.Count} file ({FormatSize(total)}) vào kho tạm được mã hóa, xác minh từng file trong {stopwatch.Elapsed.TotalSeconds:0.0} giây rồi xóa kho tạm. Kho File cá nhân thật không thay đổi.",
                    entries.Count, total);
            }
            catch (OperationCanceledException)
            {
                return (false, LocalizationAuditService.Get("backup.files.message013", "vi"), 0, 0);
            }
            catch (Exception ex)
            {
                return (false, $"Diễn tập khôi phục thất bại; kho thật không thay đổi. {ex.Message}", 0, 0);
            }
            finally
            {
                if (archiveBytes != null) CryptographicOperations.ZeroMemory(archiveBytes);
                if (drillDirectory != null)
                {
                    try
                    {
                        string full = Path.GetFullPath(drillDirectory);
                        string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                            Path.GetFileName(full).StartsWith("TQLTK-Restore-Drill-", StringComparison.Ordinal))
                            Directory.Delete(full, recursive: true);
                    }
                    catch { }
                }
            }
        }
        public static (bool ok, string message, int count, long bytes) CreateArchive(
            string username, string destPath, string passphrase)
        {
            try
            {
                var fileService = new LocalFileService();
                var files = fileService.ListFiles(username);
                if (files.Count == 0)
                    return (false, LocalizationAuditService.Get("backup.files.message014", "vi"), 0, 0);
                long total = files.Sum(f => fileService.GetPlaintextLength(f.FullName));
                if (total > MaxTotalBytes)
                    return (false,
                        $"Kho File cá nhân hiện là {FormatSize(total)}, vượt giới hạn sao lưu {FormatSize(MaxTotalBytes)}.\n" +
                        "Hãy xóa bớt hoặc xuất bớt file ra ngoài rồi thử lại.",
                        files.Count, total);
                byte[] encrypted;
                using (var ms = new MemoryStream())
                {
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        var manifest = new ArchiveManifest
                        {
                            FormatVersion = CurrentFormatVersion,
                            ExportedBy = username,
                            ExportedAt = DateTime.Now,
                            FileCount = files.Count,
                            TotalBytes = total
                        };
                        var manifestEntry = zip.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                        using (var s = manifestEntry.Open())
                            JsonSerializer.Serialize(s, manifest);
                        foreach (var f in files)
                        {
                            var entry = zip.CreateEntry(f.Name, CompressionLevel.Optimal);
                            if (f.LastWriteTime.Year is >= 1980 and <= 2107) entry.LastWriteTime = f.LastWriteTime;
                            using var dst = entry.Open();
                            fileService.CopyToPlaintext(f.FullName, dst);
                        }
                    }
                    byte[] buffer = ms.GetBuffer();
                    try
                    {
                        encrypted = BackupCrypto.EncryptBytes(buffer.AsSpan(0, (int)ms.Length), passphrase);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(buffer);
                    }
                }
                string stagedPath;
                try
                {
                    stagedPath = AtomicFileService.WriteTemporary(destPath, encrypted);
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(encrypted);
                    throw;
                }
                try
                {
                    var verification = InspectBackup(username, stagedPath, passphrase);
                    if (!verification.ok)
                        throw new InvalidDataException("Bản sao lưu tạo mới không qua kiểm tra: " + verification.message);
                    AtomicFileService.PromoteTemporary(stagedPath, destPath);
                }
                finally
                {
                    AtomicFileService.TryDelete(stagedPath);
                    CryptographicOperations.ZeroMemory(encrypted);
                }
                return (true, $"Đã đóng gói {files.Count} file ({FormatSize(total)}).", files.Count, total);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi: {ex.Message}", 0, 0);
            }
        }
        public static (bool ok, string message, FileRestorePreview? preview) PreviewRestore(
            string username, string sourcePath, string? passphrase = null,
            Func<string?, string?>? askPassphrase = null,
            CancellationToken cancellationToken = default)
        {
            byte[]? zipBytes = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(sourcePath)) return (false, LocalizationAuditService.Get("backup.files.message001", "vi"), null);
                if (new FileInfo(sourcePath).Length > MaxTotalBytes + 16L * 1024 * 1024)
                    return (false, LocalizationAuditService.Get("backup.files.message002", "vi"), null);
                byte[] data = File.ReadAllBytes(sourcePath);
                if (!BackupCrypto.IsBinaryFormat(data))
                    return (false, LocalizationAuditService.Get("backup.files.message003", "vi"), null);
                byte[] fingerprintBytes = SHA256.HashData(data);
                string fingerprint;
                try { fingerprint = Convert.ToHexString(fingerprintBytes); }
                finally { CryptographicOperations.ZeroMemory(fingerprintBytes); }
                zipBytes = DecryptWithPrompts(data, username, passphrase, askPassphrase);
                if (zipBytes == null)
                    return (false, LocalizationAuditService.Get("backup.files.message004", "vi"), null);
                using var ms = new MemoryStream(zipBytes, writable: false);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                var manifestEntry = zip.GetEntry(ManifestEntryName);
                if (manifestEntry == null || manifestEntry.Length > 64 * 1024)
                    return (false, LocalizationAuditService.Get("backup.files.message005", "vi"), null);
                var entries = zip.Entries.Where(IsUserFileEntry).ToList();
                int invalid = zip.Entries.Count(entry =>
                    entry.Name.Length > 0 && !IsUserFileEntry(entry) && entry.FullName != ManifestEntryName);
                if (entries.Count == 0)
                    return (false, LocalizationAuditService.Get("backup.files.message006", "vi"), null);
                long totalBytes = 0;
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalBytes = checked(totalBytes + entry.Length);
                    if (entry.Length < 0 || totalBytes > MaxTotalBytes)
                        return (false, LocalizationAuditService.Get("backup.files.message007", "vi"), null);
                }
                if (!TryReadAndValidateManifest(zip, entries.Count, totalBytes, out _))
                    return (false, LocalizationAuditService.Get("backup.files.message008", "vi"), null);
                var fileService = new LocalFileService();
                string folder = fileService.GetExistingUserFolderReadOnly(username);
                string localFingerprint = ComputeFolderFingerprint(folder, cancellationToken);
                var names = Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder).Select(Path.GetFileName)
                        .Where(name => name != null).Cast<string>()
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var plannedEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                var items = new List<BackupPreviewItem>(entries.Count);
                int added = 0;
                int skipped = 0;
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var validationStream = entry.Open())
                        CopyBounded(validationStream, Stream.Null, entry.Length, cancellationToken);
                    string destinationName = entry.Name;
                    bool isSame = false;
                    if (names.Contains(entry.Name))
                    {
                        if (plannedEntries.TryGetValue(entry.Name, out var priorEntry))
                        {
                            using var prior = priorEntry.Open();
                            using var candidate = entry.Open();
                            isSame = StreamsEqual(prior, priorEntry.Length, candidate, entry.Length, cancellationToken);
                        }
                        else
                        {
                            string existingPath = Path.Combine(folder, entry.Name);
                            if (File.Exists(existingPath))
                            {
                                using var candidate = entry.Open();
                                isSame = fileService.ContentEqualsReadOnly(
                                    existingPath, candidate, entry.Length, cancellationToken);
                            }
                            else
                            {
                                isSame = false;
                            }
                        }
                    }
                    if (isSame)
                    {
                        skipped++;
                        items.Add(new BackupPreviewItem(entry.Name, BackupItemStatus.SkipDuplicate, "Nội dung đã có"));
                        continue;
                    }
                    if (names.Contains(destinationName))
                        destinationName = GetUniqueName(entry.Name, names);
                    names.Add(destinationName);
                    plannedEntries[destinationName] = entry;
                    added++;
                    items.Add(new BackupPreviewItem(
                        destinationName,
                        BackupItemStatus.Restore,
                        string.Equals(destinationName, entry.Name, StringComparison.OrdinalIgnoreCase)
                            ? FormatSize(entry.Length)
                            : $"Tên gốc: {entry.Name} • {FormatSize(entry.Length)}"));
                }
                string currentLocalFingerprint = ComputeFolderFingerprint(folder, cancellationToken);
                if (!string.Equals(localFingerprint, currentLocalFingerprint, StringComparison.Ordinal))
                    return (false, LocalizationAuditService.Get("backup.files.message015", "vi"), null);
                string owner = ReadOwner(zip);
                return (true, LocalizationAuditService.Get("backup.files.message016", "vi"),
                    new FileRestorePreview(owner, added, skipped, totalBytes, invalid,
                        fingerprint, localFingerprint, items));
            }
            catch (OperationCanceledException)
            {
                return (false, LocalizationAuditService.Get("backup.files.message017", "vi"), null);
            }
            catch (Exception ex)
            {
                return (false, $"Không thể xem trước bản sao lưu: {ex.Message}", null);
            }
            finally
            {
                if (zipBytes != null) CryptographicOperations.ZeroMemory(zipBytes);
            }
        }
        public static (bool ok, string message, int added, int skipped) RestoreFromFile(
            string username, string sourcePath, string? passphrase = null,
            Func<string?, string?>? askPassphrase = null,
            Func<string, bool>? confirmForeignOwner = null,
            CancellationToken cancellationToken = default,
            string? expectedFingerprint = null,
            string? expectedLocalFingerprint = null)
        {
            byte[]? zipBytes = null;
            int added = 0, skipped = 0;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(sourcePath))
                    return (false, LocalizationAuditService.Get("backup.files.message001", "vi"), 0, 0);
                if (new FileInfo(sourcePath).Length > MaxTotalBytes + 16L * 1024 * 1024)
                    return (false, LocalizationAuditService.Get("backup.files.message002", "vi"), 0, 0);
                byte[] data = File.ReadAllBytes(sourcePath);
                if (!BackupCrypto.IsBinaryFormat(data))
                    return (false, LocalizationAuditService.Get("backup.files.message003", "vi"), 0, 0);
                if (expectedFingerprint != null)
                {
                    byte[] fingerprintBytes = SHA256.HashData(data);
                    try
                    {
                        string currentFingerprint = Convert.ToHexString(fingerprintBytes);
                        if (!string.Equals(currentFingerprint, expectedFingerprint, StringComparison.Ordinal))
                            return (false, LocalizationAuditService.Get("backup.files.message018", "vi"), 0, 0);
                    }
                    finally { CryptographicOperations.ZeroMemory(fingerprintBytes); }
                }
                zipBytes = DecryptWithPrompts(data, username, passphrase, askPassphrase);
                if (zipBytes == null)
                    return (false, LocalizationAuditService.Get("backup.files.message019", "vi"), 0, 0);
                using var ms = new MemoryStream(zipBytes, writable: false);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                if (zip.GetEntry(ManifestEntryName) == null)
                    return (false, LocalizationAuditService.Get("backup.files.message020", "vi"), 0, 0);
                string owner = ReadOwner(zip);
                bool foreignOwner = owner.Length > 0 &&
                                    !string.Equals(owner, username, StringComparison.OrdinalIgnoreCase);
                if (foreignOwner && (confirmForeignOwner == null || !confirmForeignOwner(owner)))
                    return (false,
                        $"Đã hủy khôi phục: bản sao lưu này thuộc về người dùng '{owner}', không phải '{username}'.", 0, 0);
                var fileService = new LocalFileService();
                string readOnlyFolder = fileService.GetExistingUserFolderReadOnly(username);
                if (expectedLocalFingerprint != null &&
                    !string.Equals(ComputeFolderFingerprint(readOnlyFolder, cancellationToken),
                        expectedLocalFingerprint, StringComparison.Ordinal))
                    return (false, LocalizationAuditService.Get("backup.files.message021", "vi"), 0, 0);
                var entries = zip.Entries.Where(IsUserFileEntry).ToList();
                int invalid = zip.Entries.Count(e =>
                    e.Name.Length > 0 && !IsUserFileEntry(e) && e.FullName != ManifestEntryName);
                if (entries.Count == 0)
                    return (false, LocalizationAuditService.Get("backup.files.message022", "vi"), 0, 0);
                if (entries.Sum(e => e.Length) > MaxTotalBytes)
                    return (false, LocalizationAuditService.Get("backup.files.message023", "vi"), 0, 0);
                string userFolder = fileService.GetUserFolder(username);
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string existing = Path.Combine(userFolder, entry.Name);
                    if (File.Exists(existing))
                    {
                        using var candidate = entry.Open();
                        if (fileService.ContentEquals(existing, candidate, entry.Length, cancellationToken))
                        {
                            skipped++;
                            continue;
                        }
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    using var source = entry.Open();
                    fileService.ImportStream(username, entry.Name, source, entry.Length,
                        entry.LastWriteTime.UtcDateTime, cancellationToken);
                    added++;
                }
                string msg = $"Đã khôi phục {added} file.";
                if (skipped > 0) msg += $"\nBỏ qua {skipped} file trùng (cùng tên và cùng nội dung).";
                if (invalid > 0) msg += $"\nBỏ qua {invalid} mục có tên không hợp lệ.";
                return (true, msg, added, skipped);
            }
            catch (OperationCanceledException)
            {
                string partial = added > 0
                    ? $"\nTrước khi hủy, {added} file đã được khôi phục vào kho File cá nhân."
                    : "";
                return (false, $"Đã hủy khôi phục.{partial}", added, skipped);
            }
            catch (Exception ex)
            {
                string partial = added > 0
                    ? $"\nTrước khi lỗi xảy ra, {added} file đã được khôi phục vào kho File cá nhân."
                    : "";
                return (false, $"Lỗi: {ex.Message}{partial}", added, skipped);
            }
            finally
            {
                if (zipBytes != null) CryptographicOperations.ZeroMemory(zipBytes);
            }
        }
        private static byte[]? DecryptWithPrompts(
            byte[] data, string username, string? passphrase, Func<string?, string?>? askPassphrase)
        {
            string? candidate = passphrase ?? BackupCrypto.GetStoredPassphrase(username);
            if (candidate != null && TryDecrypt(data, candidate, out byte[]? plain))
                return plain;
            string? error = candidate == null
                ? null
                : "Cụm mật khẩu đang lưu trên máy này không mở được bản sao lưu này.";
            for (int attempt = 0; attempt < 3 && askPassphrase != null; attempt++)
            {
                string? typed = askPassphrase(error);
                if (typed == null) return null;
                if (TryDecrypt(data, typed, out byte[]? typedPlain))
                    return typedPlain;
                error = "Cụm mật khẩu không đúng hoặc file bị hỏng. Hãy thử lại.";
            }
            return null;
        }
        private static bool TryDecrypt(byte[] data, string passphrase, out byte[]? plain)
        {
            try
            {
                plain = BackupCrypto.DecryptBytes(data, passphrase);
                return true;
            }
            catch (CryptographicException)
            {
                plain = null;
                return false;
            }
        }
        private static string ReadOwner(ZipArchive zip)
        {
            try
            {
                var entry = zip.GetEntry(ManifestEntryName);
                if (entry == null) return "";
                using var s = entry.Open();
                return JsonSerializer.Deserialize<ArchiveManifest>(s)?.ExportedBy.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }
        private static bool TryReadAndValidateManifest(
            ZipArchive zip, int entryCount, long totalBytes, out ArchiveManifest manifest)
        {
            manifest = new ArchiveManifest();
            try
            {
                var entry = zip.GetEntry(ManifestEntryName);
                if (entry == null || entry.Length is < 1 or > 64 * 1024) return false;
                using var stream = entry.Open();
                var parsed = JsonSerializer.Deserialize<ArchiveManifest>(stream);
                if (parsed == null || parsed.FormatVersion != CurrentFormatVersion ||
                    parsed.FileCount != entryCount || parsed.TotalBytes != totalBytes ||
                    string.IsNullOrWhiteSpace(parsed.ExportedBy))
                    return false;
                manifest = parsed;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is InvalidDataException)
            {
                return false;
            }
        }
        private static string GetUniqueName(string originalName, HashSet<string> existingNames)
        {
            string stem = Path.GetFileNameWithoutExtension(originalName);
            string extension = Path.GetExtension(originalName);
            int suffix = 1;
            string candidate;
            do
            {
                candidate = $"{stem} ({suffix++}){extension}";
            } while (existingNames.Contains(candidate));
            return candidate;
        }
        private static bool StreamsEqual(
            Stream first, long firstLength, Stream second, long secondLength,
            CancellationToken cancellationToken)
        {
            byte[] firstBuffer = new byte[81920];
            byte[] secondBuffer = new byte[81920];
            try
            {
                if (firstLength != secondLength)
                {
                    CopyBounded(first, Stream.Null, firstLength, cancellationToken);
                    CopyBounded(second, Stream.Null, secondLength, cancellationToken);
                    return false;
                }
                long remaining = firstLength;
                bool equal = true;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(firstBuffer.Length, remaining);
                    first.ReadExactly(firstBuffer.AsSpan(0, count));
                    second.ReadExactly(secondBuffer.AsSpan(0, count));
                    if (!firstBuffer.AsSpan(0, count).SequenceEqual(secondBuffer.AsSpan(0, count)))
                        equal = false;
                    CryptographicOperations.ZeroMemory(firstBuffer.AsSpan(0, count));
                    CryptographicOperations.ZeroMemory(secondBuffer.AsSpan(0, count));
                    remaining -= count;
                }
                if (first.ReadByte() != -1 || second.ReadByte() != -1)
                    throw new InvalidDataException(LocalizationAuditService.Get("backup.files.message024", "vi"));
                return equal;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(firstBuffer);
                CryptographicOperations.ZeroMemory(secondBuffer);
            }
        }
        private static string ComputeFolderFingerprint(string folder, CancellationToken cancellationToken)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (Directory.Exists(folder))
            {
                var paths = Directory.EnumerateFiles(folder)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                    .ToList();
                byte[] buffer = new byte[81920];
                try
                {
                    foreach (string path in paths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(path));
                        byte[] lengthBytes = BitConverter.GetBytes(new FileInfo(path).Length);
                        try
                        {
                            hash.AppendData(nameBytes);
                            hash.AppendData(lengthBytes);
                            using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete);
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                hash.AppendData(buffer, 0, read);
                                CryptographicOperations.ZeroMemory(buffer.AsSpan(0, read));
                            }
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(nameBytes);
                            CryptographicOperations.ZeroMemory(lengthBytes);
                        }
                    }
                }
                finally { CryptographicOperations.ZeroMemory(buffer); }
            }
            byte[] digest = hash.GetHashAndReset();
            try { return Convert.ToHexString(digest); }
            finally { CryptographicOperations.ZeroMemory(digest); }
        }
        private static bool IsUserFileEntry(ZipArchiveEntry e)
        {
            string name = e.Name;
            return name.Length > 0
                   && e.FullName == name
                   && name != "." && name != ".."
                   && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                   && !name.EndsWith('.') && !name.EndsWith(' ')
                   && !ReservedName.IsMatch(name);
        }
        private static void CopyBounded(
            Stream src, Stream dst, long maxBytes, CancellationToken cancellationToken = default)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            try
            {
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    total += read;
                    if (total > maxBytes)
                        throw new InvalidDataException(LocalizationAuditService.Get("backup.files.message025", "vi"));
                    dst.Write(buffer, 0, read);
                }
                if (total != maxBytes)
                    throw new InvalidDataException(LocalizationAuditService.Get("backup.files.message026", "vi"));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
    }
}
