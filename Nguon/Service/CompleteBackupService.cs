using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using PersonalInfoManager.Models;
namespace PersonalInfoManager.Services;
public sealed record CompleteBackupInfo(
    string ExportedBy,
    DateTime ExportedAtUtc,
    string AppVersion,
    int AccountCount,
    int NoteCount,
    int PersonalFileCount,
    long PersonalFileBytes,
    bool IncludesSettings,
    long ArchiveBytes,
    string SnapshotFingerprint)
{
    public bool IncludesNotes { get; init; }
    public bool IncludesPersonalFiles { get; init; }
}
public sealed class CompleteBackupPreview
{
    internal CompleteBackupPreview(string path, string fileHash, string localStateHash,
        CompleteBackupInfo info, IReadOnlyList<BackupPreviewItem> items,
        int accountsToAdd, int notesToAdd, int filesToAdd)
    {
        Path = path;
        FileHash = fileHash;
        LocalStateHash = localStateHash;
        Info = info;
        Items = items;
        AccountsToAdd = accountsToAdd;
        NotesToAdd = notesToAdd;
        FilesToAdd = filesToAdd;
    }
    internal string Path { get; }
    internal string FileHash { get; }
    internal string LocalStateHash { get; }
    public CompleteBackupInfo Info { get; }
    public IReadOnlyList<BackupPreviewItem> Items { get; }
    public int AccountsToAdd { get; }
    public int NotesToAdd { get; }
    public int FilesToAdd { get; }
}
public sealed record BackupComparisonResult(
    CompleteBackupInfo Older,
    CompleteBackupInfo Newer,
    IReadOnlyList<BackupPreviewItem> Changes,
    int AddedCount,
    int ChangedCount,
    int RemovedCount)
{
    public string OlderPath { get; init; } = "";
    public string NewerPath { get; init; } = "";
    public string OlderFileHash { get; init; } = "";
    public string NewerFileHash { get; init; } = "";
    public string LocalStateHash { get; init; } = "";
    public IReadOnlyList<BackupComparisonEntry> Entries { get; init; } = Array.Empty<BackupComparisonEntry>();
}
public enum BackupRecoveryItemKind
{
    Account,
    Note,
    PersonalFile,
    Settings
}
public sealed class BackupComparisonEntry
{
    public required string Name { get; init; }
    public required BackupItemStatus Status { get; init; }
    public required string Details { get; init; }
    public required BackupRecoveryItemKind Kind { get; init; }
    public string? OlderKey { get; init; }
    public string? NewerKey { get; init; }
    public required IReadOnlyList<BackupRecoverySource> AvailableSources { get; init; }
    public BackupRecoverySource SelectedSource { get; set; } = BackupRecoverySource.None;
}
public sealed record BackupRecoverySelection(BackupRecoveryItemKind Kind, string Key, bool FromOlder);
public static class CompleteBackupService
{
#if SECURITY_TEST
    internal static Action<string>? RestoreTestCheckpoint { get; set; }
#endif
    public const string FileExtension = ".tqltk";
    public const long MaximumArchiveBytes = 160L * 1024 * 1024;
    private const int CurrentFormatVersion = 1;
    private const string ManifestEntry = "__tqltk__/manifest.json";
    private const string AccountsEntry = "data/accounts.json";
    private const string NotesEntry = "data/notes.json";
    private const string SettingsEntry = "data/settings.json";
    private const int MaximumEntries = 10005;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private sealed class PackageManifest
    {
        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public string ExportedBy { get; set; } = "";
        public DateTime ExportedAtUtc { get; set; }
        public string AppVersion { get; set; } = global::PersonalInfoManager.Services.AppVersion.Current;
        public int AccountCount { get; set; }
        public int NoteCount { get; set; }
        public int PersonalFileCount { get; set; }
        public long PersonalFileBytes { get; set; }
        public bool IncludesNotes { get; set; }
        public bool IncludesPersonalFiles { get; set; }
        public bool IncludesSettings { get; set; }
        public long ArchiveBytes { get; set; }
        public string SnapshotFingerprint { get; set; } = "";
        public List<PersonalFileManifest> PersonalFiles { get; set; } = new();
    }
    private sealed class PersonalFileManifest
    {
        public string EntryName { get; set; } = "";
        public string FileName { get; set; } = "";
        public long Length { get; set; }
        public long LastWriteTimeUtcTicks { get; set; }
        public string Sha256 { get; set; } = "";
    }
    private sealed class PortableSettings
    {
        public string Language { get; set; } = "vi";
        public string Theme { get; set; } = "Light";
        public int AutoLockMinutes { get; set; } = 5;
        public bool LockWhenMinimized { get; set; }
        public bool AutoThemeEnabled { get; set; }
        public int PwnedPasswordReminderDays { get; set; }
    }
    private sealed class LoadedPackage : IDisposable
    {
        public required byte[] ArchiveBytes { get; init; }
        public required PackageManifest Manifest { get; init; }
        public required List<SavedAccount> Accounts { get; init; }
        public required List<Note> Notes { get; init; }
        public PortableSettings? Settings { get; init; }
        public required Dictionary<string, PersonalFileManifest> Files { get; init; }
        public void Dispose() => CryptographicOperations.ZeroMemory(ArchiveBytes);
    }
    public static (bool ok, string message, CompleteBackupInfo? info) Create(
        string username, string destinationPath, string passphrase,
        bool includeNotes = true, bool includePersonalFiles = true, bool includeSettings = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            return (false, "Cần cụm mật khẩu sao lưu để tạo gói có thể mở trên máy khác.", null);
        if (passphrase.Length < BackupCrypto.MinPassphraseLength)
            return (false, $"Cụm mật khẩu cần ít nhất {BackupCrypto.MinPassphraseLength} ký tự.", null);
        byte[]? archiveBuffer = null;
        int archiveLength = 0;
        BoundedMemoryStream? archiveStream = null;
        byte[]? encrypted = null;
        string? stagedPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string snapshotFingerprint = GetCurrentStateFingerprint(username, cancellationToken);
            var accounts = new AccountVaultService().Load(username);
            AccountVaultService.ValidateAttachments(accounts);
            var notes = includeNotes ? new NoteService().Load() : new List<Note>();
            var fileService = new LocalFileService();
            var files = includePersonalFiles ? fileService.ListFiles(username) : new List<FileInfo>();
            long fileBytes = 0;
            foreach (FileInfo file in files)
                fileBytes = checked(fileBytes + fileService.GetPlaintextLength(file.FullName));
            if (fileBytes > FileBackupService.MaxTotalBytes)
                return (false, $"Tệp cá nhân vượt giới hạn sao lưu {FileBackupService.FormatSize(FileBackupService.MaxTotalBytes)}.", null);
            PortableSettings? settings = includeSettings ? CaptureSettings() : null;
            var manifest = new PackageManifest
            {
                ExportedBy = username,
                ExportedAtUtc = DateTime.UtcNow,
                AccountCount = accounts.Count,
                NoteCount = notes.Count,
                PersonalFileCount = files.Count,
                PersonalFileBytes = fileBytes,
                IncludesNotes = includeNotes,
                IncludesPersonalFiles = includePersonalFiles,
                IncludesSettings = includeSettings,
                SnapshotFingerprint = snapshotFingerprint
            };
            long estimatedBytes = MeasureJson(accounts) + MeasureJson(notes) +
                (settings == null ? 0 : MeasureJson(settings)) + fileBytes + 256 * 1024;
            if (estimatedBytes > MaximumArchiveBytes)
                return (false, $"Dữ liệu quá lớn cho một gói chuyển máy ({FileBackupService.FormatSize(estimatedBytes)}). Giới hạn hiện tại là {FileBackupService.FormatSize(MaximumArchiveBytes)}.", null);
            archiveStream = new BoundedMemoryStream(MaximumArchiveBytes);
            {
                using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteJson(zip, AccountsEntry, accounts);
                    if (includeNotes) WriteJson(zip, NotesEntry, notes);
                    if (settings != null) WriteJson(zip, SettingsEntry, settings);
                    for (int index = 0; index < files.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        FileInfo file = files[index];
                        string entryName = $"personal-files/{index:D5}.bin";
                        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                        if (file.LastWriteTimeUtc.Year is >= 1980 and <= 2107)
                            entry.LastWriteTime = file.LastWriteTimeUtc;
                        using var destination = entry.Open();
                        using var hashing = new HashingWriteStream(destination, leaveOpen: true);
                        fileService.CopyToPlaintext(file.FullName, hashing);
                        if (hashing.BytesWritten != fileService.GetPlaintextLength(file.FullName))
                            throw new InvalidDataException($"Tệp '{file.Name}' thay đổi trong lúc tạo gói.");
                        manifest.PersonalFiles.Add(new PersonalFileManifest
                        {
                            EntryName = entryName,
                            FileName = file.Name,
                            Length = hashing.BytesWritten,
                            LastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks,
                            Sha256 = hashing.GetHash()
                        });
                    }
                    manifest.ArchiveBytes = estimatedBytes;
                    WriteJson(zip, ManifestEntry, manifest);
                }
                if (archiveStream.Length > MaximumArchiveBytes)
                    throw new InvalidDataException("Gói sao lưu vượt giới hạn dung lượng cho phép.");
                if (!archiveStream.TryGetBuffer(out ArraySegment<byte> segment) || segment.Array == null)
                    throw new InvalidOperationException("Không thể đọc gói sao lưu vừa tạo.");
                archiveBuffer = segment.Array;
                archiveLength = checked((int)archiveStream.Length);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(snapshotFingerprint, GetCurrentStateFingerprint(username, cancellationToken), StringComparison.Ordinal))
                throw new InvalidOperationException("Dữ liệu thay đổi trong lúc tạo bản sao lưu. Hãy thử lại sau.");
            cancellationToken.ThrowIfCancellationRequested();
            encrypted = BackupCrypto.EncryptBytes(archiveBuffer.AsSpan(0, archiveLength), passphrase);
            stagedPath = AtomicFileService.WriteTemporary(destinationPath, encrypted);
            using (LoadedPackage verified = Load(stagedPath, passphrase))
            {
                if (verified.Manifest.AccountCount != accounts.Count ||
                    verified.Manifest.NoteCount != notes.Count ||
                    verified.Manifest.PersonalFileCount != files.Count)
                    throw new InvalidDataException("Gói vừa tạo không khớp số lượng dữ liệu ban đầu.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFileService.PromoteTemporary(stagedPath, destinationPath);
            stagedPath = null;
            try { ActivityLogger.LogMessage(username, "CREATE_COMPLETE_BACKUP", "log.detail.create_complete_backup.1", Path.GetFileName(destinationPath)); } catch { }
            return (true, LocalizationAuditService.Get("backup.complete.message001", "vi"), ToInfo(manifest, new FileInfo(destinationPath).Length));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, "Không tạo được gói sao lưu đầy đủ: " + ex.Message, null);
        }
        finally
        {
            if (archiveBuffer != null) CryptographicOperations.ZeroMemory(archiveBuffer);
            archiveStream?.Dispose();
            if (encrypted != null) CryptographicOperations.ZeroMemory(encrypted);
            if (stagedPath != null) AtomicFileService.TryDelete(stagedPath);
        }
    }
    public static (bool ok, string message, CompleteBackupInfo? info) Inspect(string path, string passphrase)
    {
        try
        {
            using LoadedPackage package = Load(path, passphrase);
            return (true, "Gói hợp lệ: nội dung đã giải mã và xác minh được; chưa thay đổi dữ liệu hiện có.",
                ToInfo(package.Manifest, new FileInfo(path).Length));
        }
        catch (Exception ex)
        {
            return (false, "Không thể xác minh gói sao lưu: " + ex.Message, null);
        }
    }
    public static (bool ok, string message, CompleteBackupPreview? preview) PrepareRestore(
        string username, string path, string passphrase)
    {
        try
        {
            using LoadedPackage package = Load(path, passphrase);
            var currentAccounts = new AccountVaultService().Load(username);
            var currentNotes = new NoteService().Load();
            var localService = new LocalFileService();
            var currentFiles = localService.ListFilesReadOnly(username);
            string stateHash = ComputeLocalStateHash(currentAccounts, currentNotes, currentFiles, CaptureSettings());
            var items = new List<BackupPreviewItem>();
            items.Add(new BackupPreviewItem("Tài khoản, TOTP, tệp đính kèm và trường tùy chỉnh", BackupItemStatus.AlwaysIncluded,
                LanguageService.Format("{0} tài khoản; mỗi mục giữ thông tin đăng nhập, TOTP, tệp đính kèm, trường tùy chỉnh và lịch sử.", package.Accounts.Count)));
            items.Add(new BackupPreviewItem("Ghi chú riêng", package.Manifest.IncludesNotes ? BackupItemStatus.Included : BackupItemStatus.Excluded,
                package.Manifest.IncludesNotes
                    ? LanguageService.Format("{0} ghi chú riêng.", package.Notes.Count)
                    : LanguageService.Translate("Bạn đã bỏ chọn ghi chú riêng khi tạo gói.")));
            items.Add(new BackupPreviewItem("Tệp cá nhân", package.Manifest.IncludesPersonalFiles ? BackupItemStatus.Included : BackupItemStatus.Excluded,
                package.Manifest.IncludesPersonalFiles
                    ? LanguageService.Format("{0} tệp, tổng dung lượng {1}.", package.Manifest.PersonalFileCount, FileBackupService.FormatSize(package.Manifest.PersonalFileBytes))
                    : LanguageService.Translate("Bạn đã bỏ chọn tệp cá nhân khi tạo gói.")));
            items.Add(new BackupPreviewItem("Cài đặt an toàn", package.Manifest.IncludesSettings ? BackupItemStatus.Included : BackupItemStatus.Excluded,
                LanguageService.Translate("Chỉ gồm ngôn ngữ, giao diện và một số tùy chọn khóa; không gồm thông tin đăng nhập, 2FA hay Windows Hello.")));
            int addAccounts = 0;
            foreach (SavedAccount account in package.Accounts)
            {
                bool exists = currentAccounts.Any(item => item.Id == account.Id || SameAccount(item, account));
                items.Add(new BackupPreviewItem(account.ServiceName,
                    exists ? BackupItemStatus.KeepExisting : BackupItemStatus.AddAccount, account.Username));
                if (!exists) addAccounts++;
            }
            int addNotes = 0;
            foreach (Note note in package.Notes)
            {
                bool exists = currentNotes.Any(item => item.Id == note.Id);
                items.Add(new BackupPreviewItem(note.Title, exists ? BackupItemStatus.KeepNote : BackupItemStatus.AddNote, ""));
                if (!exists) addNotes++;
            }
            int addFiles = 0;
            foreach (PersonalFileManifest file in package.Files.Values)
            {
                FileInfo? existing = currentFiles.FirstOrDefault(item =>
                    string.Equals(item.Name, file.FileName, StringComparison.OrdinalIgnoreCase));
                bool sameContent = existing != null && FileContentEquals(package.ArchiveBytes,
                    file.EntryName, localService, existing.FullName, file.Length);
                items.Add(new BackupPreviewItem(file.FileName,
                    sameContent ? BackupItemStatus.SkipDuplicateFile : existing != null ? BackupItemStatus.AddRenamedCopy : BackupItemStatus.AddFile,
                    FileBackupService.FormatSize(file.Length)));
                if (!sameContent) addFiles++;
            }
            var preview = new CompleteBackupPreview(Path.GetFullPath(path), HashFile(path), stateHash,
                ToInfo(package.Manifest, new FileInfo(path).Length), items, addAccounts, addNotes, addFiles);
            return (true, LocalizationAuditService.Get("backup.complete.message002", "vi"), preview);
        }
        catch (Exception ex)
        {
            return (false, "Không thể xem trước gói sao lưu: " + ex.Message, null);
        }
    }
    public static (bool ok, string message, int accountsAdded, int notesAdded, int filesAdded) Restore(
        string username, CompleteBackupPreview preview, string passphrase, bool restoreSettings,
        bool allowForeignOwner = false)
    {
        int accountsAdded = 0, notesAdded = 0, filesAdded = 0;
        AccountVaultService? vaultService = null;
        List<SavedAccount>? originalAccounts = null;
        List<Note>? originalNotes = null;
        LocalFileService? fileService = null;
        var importedFilePaths = new List<string>();
        bool accountsSaved = false;
        bool notesSaved = false;
        bool settingsMayHaveChanged = false;
        PortableSettings? originalSettings = null;
        try
        {
            if (!File.Exists(preview.Path) || !string.Equals(HashFile(preview.Path), preview.FileHash, StringComparison.Ordinal))
                return (false, LocalizationAuditService.Get("backup.complete.message003", "vi"), 0, 0, 0);
            using LoadedPackage package = Load(preview.Path, passphrase);
            if (!string.Equals(package.Manifest.ExportedBy, username, StringComparison.OrdinalIgnoreCase) && !allowForeignOwner)
                return (false, LocalizationAuditService.Get("backup.complete.message004", "vi"), 0, 0, 0);
            if (package.Manifest.AccountCount != preview.Info.AccountCount ||
                package.Manifest.NoteCount != preview.Info.NoteCount ||
                package.Manifest.PersonalFileCount != preview.Info.PersonalFileCount)
                return (false, "Số lượng dữ liệu không khớp bản xem trước. Hãy xem trước lại.", 0, 0, 0);
            using var storageLock = AesCrypto.AcquireStorageLock();
            vaultService = new AccountVaultService();
            var currentAccounts = vaultService.Load(username);
            var currentNotes = new NoteService().Load();
            fileService = new LocalFileService();
            originalAccounts = currentAccounts.Select(item => item.Clone()).ToList();
            originalNotes = JsonSerializer.Deserialize<List<Note>>(JsonSerializer.Serialize(currentNotes, JsonOptions), JsonOptions)
                ?? throw new InvalidDataException("Không thể tạo bản chụp ghi chú để khôi phục an toàn.");
            originalSettings = CaptureSettings();
            var currentFiles = fileService.ListFilesReadOnly(username);
            if (!string.Equals(ComputeLocalStateHash(currentAccounts, currentNotes, currentFiles, originalSettings),
                    preview.LocalStateHash, StringComparison.Ordinal))
                return (false, LocalizationAuditService.Get("backup.complete.message005", "vi"), 0, 0, 0);
            foreach (SavedAccount incoming in package.Accounts)
            {
                if (currentAccounts.Any(existing => existing.Id == incoming.Id || SameAccount(existing, incoming))) continue;
                currentAccounts.Add(incoming.Clone());
                accountsAdded++;
            }
            if (accountsAdded > 0)
            {
                vaultService.Save(username, currentAccounts);
                accountsSaved = true;
#if SECURITY_TEST
                RestoreTestCheckpoint?.Invoke("after-vault-save");
#endif
            }
            foreach (Note incoming in package.Notes)
            {
                if (currentNotes.Any(existing => existing.Id == incoming.Id)) continue;
                currentNotes.Add(incoming);
                notesAdded++;
            }
            if (notesAdded > 0)
            {
                new NoteService().Save(currentNotes);
                notesSaved = true;
            }
            currentFiles = fileService.ListFilesReadOnly(username);
            using var archiveStream = new MemoryStream(package.ArchiveBytes, writable: false);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            foreach (PersonalFileManifest file in package.Files.Values.OrderBy(item => item.EntryName, StringComparer.Ordinal))
            {
                ZipArchiveEntry entry = archive.GetEntry(file.EntryName)
                    ?? throw new InvalidDataException("Thiếu tệp trong gói sao lưu.");
                FileInfo? sameName = currentFiles.FirstOrDefault(item =>
                    string.Equals(item.Name, file.FileName, StringComparison.OrdinalIgnoreCase));
                if (sameName != null)
                {
                    using var incoming = entry.Open();
                    if (fileService.ContentEqualsReadOnly(sameName.FullName, incoming, entry.Length)) continue;
                }
                using var source = entry.Open();
                string importedPath = fileService.ImportStream(username, file.FileName, source, entry.Length,
                    new DateTime(file.LastWriteTimeUtcTicks, DateTimeKind.Utc));
                importedFilePaths.Add(importedPath);
                filesAdded++;
            }
            if (restoreSettings && package.Settings != null)
            {
                settingsMayHaveChanged = true;
                ApplySettings(package.Settings);
            }
            try { ActivityLogger.LogMessage(username, "RESTORE_COMPLETE_BACKUP", "log.detail.restore_complete_backup.1", accountsAdded, notesAdded, filesAdded); } catch { }
            return (true, $"Khôi phục xong: thêm {accountsAdded} tài khoản, {notesAdded} ghi chú và {filesAdded} tệp. Dữ liệu trùng hoặc cài đặt chưa chọn được giữ nguyên.", accountsAdded, notesAdded, filesAdded);
        }
        catch (Exception ex)
        {
            var rollbackErrors = new List<string>();
            try
            {
#if SECURITY_TEST
                RestoreTestCheckpoint?.Invoke("before-vault-rollback");
#endif
                if (accountsSaved && originalAccounts != null) vaultService!.Save(username, originalAccounts);
            }
            catch (Exception rollback) { rollbackErrors.Add("vault: " + rollback.Message); }
            try { if (notesSaved && originalNotes != null) new NoteService().Save(originalNotes); }
            catch (Exception rollback) { rollbackErrors.Add("notes: " + rollback.Message); }
            try
            {
                if (fileService != null)
                    foreach (string importedPath in importedFilePaths) fileService.DeleteFile(importedPath);
            }
            catch (Exception rollback) { rollbackErrors.Add("files: " + rollback.Message); }
            try { if (settingsMayHaveChanged && originalSettings != null) ApplySettings(originalSettings); }
            catch (Exception rollback) { rollbackErrors.Add("settings: " + rollback.Message); }
            string rollbackStatus = rollbackErrors.Count == 0
                ? "Các thay đổi của lần khôi phục này đã được hoàn tác."
                : "Không hoàn tác được hoàn toàn (" + string.Join("; ", rollbackErrors) + "). Hãy kiểm tra dữ liệu trước khi thử lại.";
            try { ActivityLogger.Log(username, "RESTORE_COMPLETE_BACKUP_FAILED", rollbackStatus); } catch { }
            return (false, $"Khôi phục chưa hoàn tất: {ex.Message}\n{rollbackStatus}", 0, 0, 0);
        }
    }
    public static (bool ok, string message, BackupComparisonResult? result) Compare(
        string username, string olderPath, string olderPassphrase, string newerPath, string newerPassphrase)
    {
        try
        {
            using LoadedPackage older = Load(olderPath, olderPassphrase);
            using LoadedPackage newer = Load(newerPath, newerPassphrase);
            var changes = new List<BackupPreviewItem>();
            var entries = new List<BackupComparisonEntry>();
            CompareAccounts(older.Accounts, newer.Accounts, changes, entries);
            if (older.Manifest.IncludesNotes && newer.Manifest.IncludesNotes)
                CompareNotes(older.Notes, newer.Notes, changes, entries);
            else
                AddNonComparableCategory("Ghi chú riêng", changes, entries);
            if (older.Manifest.IncludesPersonalFiles && newer.Manifest.IncludesPersonalFiles)
                CompareFiles(older, newer, changes, entries);
            else
                AddNonComparableCategory("Tệp cá nhân", changes, entries);
            if (older.Settings != null && newer.Settings != null &&
                !string.Equals(JsonSerializer.Serialize(older.Settings, JsonOptions),
                    JsonSerializer.Serialize(newer.Settings, JsonOptions), StringComparison.Ordinal))
            {
                changes.Add(new BackupPreviewItem("Cài đặt an toàn", BackupItemStatus.Changed, "Ngôn ngữ, giao diện hoặc tùy chọn khóa."));
                entries.Add(new BackupComparisonEntry
                {
                    Name = "Cài đặt an toàn",
                    Status = BackupItemStatus.Changed,
                    Details = "Ngôn ngữ, giao diện hoặc tùy chọn khóa. Chỉ khôi phục qua khôi phục toàn gói.",
                    Kind = BackupRecoveryItemKind.Settings,
                    AvailableSources = new[] { BackupRecoverySource.Unsupported },
                    SelectedSource = BackupRecoverySource.Unsupported
                });
            }
            else
                AddNonComparableCategory("Cài đặt an toàn", changes, entries);
            int added = changes.Count(item => item.Status == BackupItemStatus.Added);
            int changed = changes.Count(item => item.Status == BackupItemStatus.Modified);
            int removed = changes.Count(item => item.Status == BackupItemStatus.Removed);
            return (true, $"So sánh xong: thêm {added}, sửa {changed}, xóa {removed}. Bản mới không tự áp dụng thay đổi.",
                new BackupComparisonResult(ToInfo(older.Manifest, new FileInfo(olderPath).Length),
                    ToInfo(newer.Manifest, new FileInfo(newerPath).Length), changes, added, changed, removed)
                {
                    OlderPath = Path.GetFullPath(olderPath),
                    NewerPath = Path.GetFullPath(newerPath),
                    OlderFileHash = HashFile(olderPath),
                    NewerFileHash = HashFile(newerPath),
                    LocalStateHash = GetCurrentStateFingerprintReadOnly(username),
                    Entries = entries
                });
        }
        catch (Exception ex)
        {
            return (false, "Không thể so sánh hai bản sao lưu: " + ex.Message, null);
        }
    }
    public static (bool ok, string message, int accountsAdded, int notesAdded, int filesAdded) RecoverSelectedAsCopies(
        string username, BackupComparisonResult comparison, string olderPassphrase, string newerPassphrase,
        IReadOnlyList<BackupRecoverySelection> selections, bool allowForeignOwner = false)
    {
        if (selections.Count == 0)
            return (false, LocalizationAuditService.Get("backup.complete.message006", "vi"), 0, 0, 0);
        AccountVaultService? vaultService = null;
        LocalFileService? fileService = null;
        List<SavedAccount>? originalAccounts = null;
        List<Note>? originalNotes = null;
        var importedPaths = new List<string>();
        bool accountsSaved = false;
        bool notesSaved = false;
        try
        {
            if (!File.Exists(comparison.OlderPath) || !File.Exists(comparison.NewerPath) ||
                !string.Equals(HashFile(comparison.OlderPath), comparison.OlderFileHash, StringComparison.Ordinal) ||
                !string.Equals(HashFile(comparison.NewerPath), comparison.NewerFileHash, StringComparison.Ordinal))
                return (false, LocalizationAuditService.Get("backup.complete.message007", "vi"), 0, 0, 0);
            foreach (BackupRecoverySelection selection in selections)
            {
                bool offered = comparison.Entries.Any(entry => entry.Kind == selection.Kind &&
                    (selection.FromOlder
                        ? string.Equals(entry.OlderKey, selection.Key, StringComparison.Ordinal)
                        : string.Equals(entry.NewerKey, selection.Key, StringComparison.Ordinal)));
                if (!offered || selection.Kind == BackupRecoveryItemKind.Settings)
                    return (false, LocalizationAuditService.Get("backup.complete.message008", "vi"), 0, 0, 0);
            }
            using LoadedPackage older = Load(comparison.OlderPath, olderPassphrase);
            using LoadedPackage newer = Load(comparison.NewerPath, newerPassphrase);
            bool usesOlder = selections.Any(selection => selection.FromOlder);
            bool usesNewer = selections.Any(selection => !selection.FromOlder);
            if (((usesOlder && !string.Equals(older.Manifest.ExportedBy, username, StringComparison.OrdinalIgnoreCase)) ||
                 (usesNewer && !string.Equals(newer.Manifest.ExportedBy, username, StringComparison.OrdinalIgnoreCase))) && !allowForeignOwner)
                return (false, LocalizationAuditService.Get("backup.complete.message009", "vi"), 0, 0, 0);
            using var storageLock = AesCrypto.AcquireStorageLock();
            vaultService = new AccountVaultService();
            List<SavedAccount> currentAccounts = vaultService.Load(username);
            List<Note> currentNotes = new NoteService().Load();
            fileService = new LocalFileService();
            List<FileInfo> currentFiles = fileService.ListFilesReadOnly(username);
            if (!string.Equals(ComputeLocalStateHash(currentAccounts, currentNotes, currentFiles, CaptureSettings()),
                    comparison.LocalStateHash, StringComparison.Ordinal))
                return (false, LocalizationAuditService.Get("backup.complete.message010", "vi"), 0, 0, 0);
            originalAccounts = currentAccounts.Select(account => account.Clone()).ToList();
            originalNotes = JsonSerializer.Deserialize<List<Note>>(JsonSerializer.Serialize(currentNotes, JsonOptions), JsonOptions)
                ?? throw new InvalidDataException("Không thể tạo bản chụp ghi chú trước khi khôi phục.");
            int accountsAdded = 0, notesAdded = 0, filesAdded = 0;
            string recoveredTag = LanguageService.Translate("Bản phục hồi từ sao lưu");
            CultureInfo displayCulture = LanguageService.IsEnglish ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("vi-VN");
            string suffix = LocalizationAuditService.Format("backup.recovered_copy_suffix", DateTime.Now.ToString("yyyy-MM-dd HH-mm", displayCulture));
            foreach (BackupRecoverySelection selection in selections.Distinct())
            {
                LoadedPackage source = selection.FromOlder ? older : newer;
                switch (selection.Kind)
                {
                    case BackupRecoveryItemKind.Account:
                    {
                        if (!Guid.TryParse(selection.Key, out Guid id))
                            throw new InvalidDataException(LocalizationAuditService.Get("backup.complete.message011", "vi"));
                        SavedAccount original = source.Accounts.FirstOrDefault(account => account.Id == id)
                            ?? throw new InvalidDataException(LocalizationAuditService.Get("backup.complete.message012", "vi"));
                        SavedAccount copy = original.Clone();
                        copy.Id = Guid.NewGuid();
                        copy.CreatedAt = DateTime.Now;
                        copy.UpdatedAt = DateTime.Now;
                        copy.IsDeleted = false;
                        copy.DeletedAt = null;
                        copy.ImportBatchId = null;
                        copy.ImportBatchAtUtc = null;
                        copy.ImportUndoable = false;
                        copy.CreatedInImportBatch = false;
                        copy.ImportUndoSnapshot = null;
                        copy.Tags ??= new List<string>();
                        if (!copy.Tags.Contains(recoveredTag, StringComparer.OrdinalIgnoreCase)) copy.Tags.Add(recoveredTag);
                        foreach (AccountCustomField field in copy.CustomFields) field.Id = Guid.NewGuid();
                        foreach (AccountAttachment attachment in copy.Attachments) attachment.Id = Guid.NewGuid();
                        foreach (DeletedNoteEntry entry in copy.DeletedNotes) entry.Id = Guid.NewGuid();
                        foreach (AccountRevisionEntry revision in copy.EditHistory) revision.Id = Guid.NewGuid();
                        currentAccounts.Add(copy);
                        accountsAdded++;
                        break;
                    }
                    case BackupRecoveryItemKind.Note:
                    {
                        if (!Guid.TryParse(selection.Key, out Guid id))
                            throw new InvalidDataException(LocalizationAuditService.Get("backup.complete.message013", "vi"));
                        Note original = source.Notes.FirstOrDefault(note => note.Id == id)
                            ?? throw new InvalidDataException(LocalizationAuditService.Get("backup.complete.message014", "vi"));
                        currentNotes.Add(new Note
                        {
                            Id = Guid.NewGuid(),
                            Title = original.Title + " " + suffix,
                            Content = original.Content,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        });
                        notesAdded++;
                        break;
                    }
                    case BackupRecoveryItemKind.PersonalFile:
                    {
                        if (!source.Files.TryGetValue(selection.Key, out PersonalFileManifest? file))
                            throw new InvalidDataException(LocalizationAuditService.Get("backup.complete.message015", "vi"));
                        using var archiveStream = new MemoryStream(source.ArchiveBytes, writable: false);
                        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
                        ZipArchiveEntry entry = archive.GetEntry(file.EntryName)
                            ?? throw new InvalidDataException(LocalizationAuditService.Get("backup.complete.message016", "vi"));
                        using Stream content = entry.Open();
                        importedPaths.Add(fileService.ImportStream(username, file.FileName, content,
                            file.Length, new DateTime(file.LastWriteTimeUtcTicks, DateTimeKind.Utc)));
                        filesAdded++;
                        break;
                    }
                    default:
                        throw new InvalidDataException(LocalizationAuditService.Get("backup.complete.message017", "vi"));
                }
            }
            if (accountsAdded > 0)
            {
                vaultService.Save(username, currentAccounts);
                accountsSaved = true;
            }
            if (notesAdded > 0)
            {
                new NoteService().Save(currentNotes);
                notesSaved = true;
            }
            try { ActivityLogger.LogMessage(username, "RECOVER_SELECTED_BACKUP_COPIES", "log.detail.recover_selected_backup_copies.1", accountsAdded, notesAdded, filesAdded); } catch { }
            return (true, LocalizationAuditService.Format("backup.selective.success", accountsAdded, notesAdded, filesAdded), accountsAdded, notesAdded, filesAdded);
        }
        catch (Exception ex)
        {
            var rollbackErrors = new List<string>();
            try { if (accountsSaved && originalAccounts != null) vaultService!.Save(username, originalAccounts); }
            catch (Exception rollback) { rollbackErrors.Add("vault: " + rollback.Message); }
            try { if (notesSaved && originalNotes != null) new NoteService().Save(originalNotes); }
            catch (Exception rollback) { rollbackErrors.Add("notes: " + rollback.Message); }
            try { if (fileService != null) foreach (string path in importedPaths) fileService.DeleteFile(path); }
            catch (Exception rollback) { rollbackErrors.Add("files: " + rollback.Message); }
            string rollbackStatus = rollbackErrors.Count == 0
                ? LanguageService.Translate("Các mục đã thêm trong lần khôi phục này đã được hoàn tác.")
                : LanguageService.Format("Không khôi phục được hoàn toàn ({0}). Hãy kiểm tra dữ liệu trước khi thử lại.", string.Join("; ", rollbackErrors));
            try { ActivityLogger.Log(username, "RECOVER_SELECTED_BACKUP_COPIES_FAILED", rollbackStatus); } catch { }
            return (false, LanguageService.Format("Không thể khôi phục các mục đã chọn: {0}\n{1}", ex.Message, rollbackStatus), 0, 0, 0);
        }
    }
    public static IReadOnlyList<FileInfo> ListBackups(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<FileInfo>();
        return new DirectoryInfo(folder).EnumerateFiles("*" + FileExtension, SearchOption.TopDirectoryOnly)
            .Where(file => !file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
    }
    public static string GetCurrentStateFingerprint(string username, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var accounts = new AccountVaultService().Load(username);
        var notes = new NoteService().Load();
        var files = new LocalFileService().ListFiles(username);
        return ComputeLocalStateHash(accounts, notes, files, CaptureSettings(), cancellationToken);
    }
    private static string GetCurrentStateFingerprintReadOnly(string username)
    {
        var accounts = new AccountVaultService().Load(username);
        var notes = new NoteService().Load();
        var files = new LocalFileService().ListFilesReadOnly(username);
        return ComputeLocalStateHash(accounts, notes, files, CaptureSettings());
    }
    private static LoadedPackage Load(string path, string passphrase)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Không tìm thấy gói sao lưu.", path);
        var file = new FileInfo(path);
        if (file.Length <= 0 || file.Length > MaximumArchiveBytes + 128)
            throw new InvalidDataException("Gói sao lưu rỗng hoặc vượt giới hạn dung lượng.");
        byte[] encrypted = File.ReadAllBytes(path);
        byte[]? archiveBytes = null;
        try
        {
            archiveBytes = BackupCrypto.DecryptBytes(encrypted, passphrase);
            if (archiveBytes.Length > MaximumArchiveBytes)
                throw new InvalidDataException("Nội dung giải mã vượt giới hạn dung lượng.");
            using var stream = new MemoryStream(archiveBytes, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (zip.Entries.Count is < 2 or > MaximumEntries)
                throw new InvalidDataException("Số mục trong gói sao lưu không hợp lệ.");
            if (zip.Entries.Select(item => item.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
                throw new InvalidDataException("Gói chứa tên mục trùng lặp.");
            PackageManifest manifest = ReadJson<PackageManifest>(zip, ManifestEntry);
            if (manifest.FormatVersion != CurrentFormatVersion || manifest.AccountCount < 0 ||
                manifest.NoteCount < 0 || manifest.PersonalFileCount < 0 ||
                manifest.PersonalFileBytes < 0 || manifest.ArchiveBytes < 0 ||
                manifest.ArchiveBytes > MaximumArchiveBytes)
                throw new InvalidDataException("Manifest gói sao lưu không hợp lệ hoặc không được hỗ trợ.");
            if (manifest.SnapshotFingerprint.Length != 64 || !manifest.SnapshotFingerprint.All(Uri.IsHexDigit))
                throw new InvalidDataException("Dấu kiểm tra trạng thái dữ liệu trong manifest không hợp lệ.");
            List<SavedAccount> accounts = ReadJson<List<SavedAccount>>(zip, AccountsEntry);
            List<Note> notes = manifest.IncludesNotes ? ReadJson<List<Note>>(zip, NotesEntry) : new();
            PortableSettings? settings = manifest.IncludesSettings ? ReadJson<PortableSettings>(zip, SettingsEntry) : null;
            if (accounts.Count != manifest.AccountCount || notes.Count != manifest.NoteCount)
                throw new InvalidDataException("Số tài khoản hoặc ghi chú không khớp manifest.");
            if (accounts.Any(item => item == null) || notes.Any(item => item == null))
                throw new InvalidDataException("Gói có bản ghi tài khoản hoặc ghi chú không hợp lệ.");
            if (accounts.Select(item => item.Id).Distinct().Count() != accounts.Count ||
                notes.Select(item => item.Id).Distinct().Count() != notes.Count)
                throw new InvalidDataException("Gói chứa mã tài khoản hoặc ghi chú bị trùng.");
            AccountVaultService.ValidateAttachments(accounts);
            ValidateSettings(settings);
            var fileManifest = manifest.PersonalFiles ?? new();
            if (!manifest.IncludesPersonalFiles && fileManifest.Count != 0)
                throw new InvalidDataException("Manifest ghi có tệp nhưng cờ tệp cá nhân đang tắt.");
            if (fileManifest.Count != manifest.PersonalFileCount ||
                fileManifest.Select(item => item.EntryName).Distinct(StringComparer.Ordinal).Count() != fileManifest.Count ||
                fileManifest.Select(item => item.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fileManifest.Count)
                throw new InvalidDataException("Danh sách tệp trong manifest không hợp lệ.");
            long totalFiles = 0;
            var files = new Dictionary<string, PersonalFileManifest>(StringComparer.Ordinal);
            foreach (PersonalFileManifest item in fileManifest)
            {
                if (string.IsNullOrWhiteSpace(item.FileName) || item.FileName != Path.GetFileName(item.FileName) ||
                    item.FileName.Length > 255 || item.FileName.Any(char.IsControl) || item.Length < 0 ||
                    item.LastWriteTimeUtcTicks < DateTime.MinValue.Ticks || item.LastWriteTimeUtcTicks > DateTime.MaxValue.Ticks ||
                    item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit) ||
                    !item.EntryName.StartsWith("personal-files/", StringComparison.Ordinal) ||
                    !files.TryAdd(item.EntryName, item))
                    throw new InvalidDataException("Một mục tệp trong manifest không an toàn.");
                ZipArchiveEntry entry = zip.GetEntry(item.EntryName)
                    ?? throw new InvalidDataException("Thiếu tệp được khai báo trong manifest.");
                if (entry.Length != item.Length) throw new InvalidDataException("Kích thước tệp không khớp manifest.");
                totalFiles = checked(totalFiles + item.Length);
                using var content = entry.Open();
                if (!string.Equals(HashStream(content), item.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Nội dung tệp cá nhân không khớp mã kiểm tra.");
            }
            if (totalFiles != manifest.PersonalFileBytes || totalFiles > FileBackupService.MaxTotalBytes)
                throw new InvalidDataException("Tổng dung lượng tệp cá nhân không khớp hoặc vượt giới hạn.");
            var allowed = new HashSet<string>(StringComparer.Ordinal)
                { ManifestEntry, AccountsEntry };
            if (manifest.IncludesNotes) allowed.Add(NotesEntry);
            if (manifest.IncludesSettings) allowed.Add(SettingsEntry);
            foreach (string name in files.Keys) allowed.Add(name);
            if (zip.Entries.Any(entry => !allowed.Contains(entry.FullName)))
                throw new InvalidDataException("Gói chứa mục không được khai báo trong manifest.");
            byte[] owned = archiveBytes;
            archiveBytes = null;
            return new LoadedPackage
            {
                ArchiveBytes = owned,
                Manifest = manifest,
                Accounts = accounts,
                Notes = notes,
                Settings = settings,
                Files = files
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            if (archiveBytes != null) CryptographicOperations.ZeroMemory(archiveBytes);
        }
    }
    private static void CompareAccounts(List<SavedAccount> older, List<SavedAccount> newer,
        List<BackupPreviewItem> changes, List<BackupComparisonEntry> entries)
    {
        var oldMap = older.ToDictionary(item => item.Id);
        var newMap = newer.ToDictionary(item => item.Id);
        foreach (var item in newer)
        {
            if (!oldMap.TryGetValue(item.Id, out var prior))
            {
                changes.Add(new BackupPreviewItem(item.ServiceName, BackupItemStatus.Added, item.Username));
                entries.Add(CreateComparisonEntry(item.ServiceName, BackupItemStatus.Added, item.Username,
                    BackupRecoveryItemKind.Account, null, item.Id.ToString("D"), false, true));
                continue;
            }
            if (!string.Equals(HashJson(prior), HashJson(item), StringComparison.Ordinal))
            {
                changes.Add(new BackupPreviewItem(item.ServiceName, BackupItemStatus.Modified, item.Username));
                entries.Add(CreateComparisonEntry(item.ServiceName, BackupItemStatus.Modified, item.Username,
                    BackupRecoveryItemKind.Account, prior.Id.ToString("D"), item.Id.ToString("D"), true, true));
            }
        }
        foreach (var item in older.Where(item => !newMap.ContainsKey(item.Id)))
        {
            changes.Add(new BackupPreviewItem(item.ServiceName, BackupItemStatus.Removed, item.Username));
            entries.Add(CreateComparisonEntry(item.ServiceName, BackupItemStatus.Removed, item.Username,
                BackupRecoveryItemKind.Account, item.Id.ToString("D"), null, true, false));
        }
    }
    private static void CompareNotes(List<Note> older, List<Note> newer,
        List<BackupPreviewItem> changes, List<BackupComparisonEntry> entries)
    {
        var oldMap = older.ToDictionary(item => item.Id);
        var newMap = newer.ToDictionary(item => item.Id);
        foreach (var item in newer)
        {
            if (!oldMap.TryGetValue(item.Id, out var prior))
            {
                changes.Add(new BackupPreviewItem(item.Title, BackupItemStatus.Added, "Ghi chú riêng"));
                entries.Add(CreateComparisonEntry(item.Title, BackupItemStatus.Added, "Ghi chú riêng",
                    BackupRecoveryItemKind.Note, null, item.Id.ToString("D"), false, true));
            }
            else if (!string.Equals(HashJson(prior), HashJson(item), StringComparison.Ordinal))
            {
                changes.Add(new BackupPreviewItem(item.Title, BackupItemStatus.Modified, "Ghi chú riêng"));
                entries.Add(CreateComparisonEntry(item.Title, BackupItemStatus.Modified, "Ghi chú riêng",
                    BackupRecoveryItemKind.Note, prior.Id.ToString("D"), item.Id.ToString("D"), true, true));
            }
        }
        foreach (var item in older.Where(item => !newMap.ContainsKey(item.Id)))
        {
            changes.Add(new BackupPreviewItem(item.Title, BackupItemStatus.Removed, "Ghi chú riêng"));
            entries.Add(CreateComparisonEntry(item.Title, BackupItemStatus.Removed, "Ghi chú riêng",
                BackupRecoveryItemKind.Note, item.Id.ToString("D"), null, true, false));
        }
    }
    private static void CompareFiles(LoadedPackage older, LoadedPackage newer,
        List<BackupPreviewItem> changes, List<BackupComparisonEntry> entries)
    {
        var oldByName = older.Files.Values.ToDictionary(item => item.FileName, StringComparer.OrdinalIgnoreCase);
        var newByName = newer.Files.Values.ToDictionary(item => item.FileName, StringComparer.OrdinalIgnoreCase);
        foreach (var item in newer.Files.Values)
        {
            if (!oldByName.TryGetValue(item.FileName, out var prior))
            {
                changes.Add(new BackupPreviewItem(item.FileName, BackupItemStatus.Added, "Tệp cá nhân"));
                entries.Add(CreateComparisonEntry(item.FileName, BackupItemStatus.Added, "Tệp cá nhân",
                    BackupRecoveryItemKind.PersonalFile, null, item.EntryName, false, true));
            }
            else if (!string.Equals(prior.Sha256, item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new BackupPreviewItem(item.FileName, BackupItemStatus.Modified, "Tệp cá nhân"));
                entries.Add(CreateComparisonEntry(item.FileName, BackupItemStatus.Modified, "Tệp cá nhân",
                    BackupRecoveryItemKind.PersonalFile, prior.EntryName, item.EntryName, true, true));
            }
        }
        foreach (var item in older.Files.Values.Where(item => !newByName.ContainsKey(item.FileName)))
        {
            changes.Add(new BackupPreviewItem(item.FileName, BackupItemStatus.Removed, "Tệp cá nhân"));
            entries.Add(CreateComparisonEntry(item.FileName, BackupItemStatus.Removed, "Tệp cá nhân",
                BackupRecoveryItemKind.PersonalFile, item.EntryName, null, true, false));
        }
    }
    private static BackupComparisonEntry CreateComparisonEntry(string name, BackupItemStatus status, string details,
        BackupRecoveryItemKind kind, string? olderKey, string? newerKey, bool canUseOlder, bool canUseNewer)
    {
        var none = BackupRecoverySource.None;
        var sources = new List<BackupRecoverySource> { none };
        if (canUseOlder) sources.Add(BackupRecoverySource.Older);
        if (canUseNewer) sources.Add(BackupRecoverySource.Newer);
        return new BackupComparisonEntry
        {
            Name = name,
            Status = status,
            Details = details,
            Kind = kind,
            OlderKey = olderKey,
            NewerKey = newerKey,
            AvailableSources = sources,
            SelectedSource = none
        };
    }
    private static void AddNonComparableCategory(string name, List<BackupPreviewItem> changes,
        List<BackupComparisonEntry> entries)
    {
        const BackupItemStatus status = BackupItemStatus.NotComparable;
        string details = LanguageService.Translate("Một trong hai gói không có thành phần này nên không thể xác định mục đã thêm hoặc xóa.");
        changes.Add(new BackupPreviewItem(name, status, details));
        var unsupported = BackupRecoverySource.Unsupported;
        entries.Add(new BackupComparisonEntry
        {
            Name = name,
            Status = status,
            Details = details,
            Kind = BackupRecoveryItemKind.Settings,
            AvailableSources = new[] { unsupported },
            SelectedSource = unsupported
        });
    }
    private static string ComputeLocalStateHash(List<SavedAccount> accounts, List<Note> notes,
        List<FileInfo> files, PortableSettings settings, CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, HashJson(accounts.OrderBy(item => item.Id).ToArray()));
        Append(hash, HashJson(notes.OrderBy(item => item.Id).ToArray()));
        Append(hash, HashJson(settings));
        foreach (FileInfo file in files.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, file.Name);
            Append(hash, file.Length.ToString(CultureInfo.InvariantCulture));
            Append(hash, HashFile(file.FullName, cancellationToken));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        try { hash.AppendData(bytes); hash.AppendData(new byte[] { 0 }); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static bool SameAccount(SavedAccount left, SavedAccount right) =>
        string.Equals(left.ServiceName, right.ServiceName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Username, right.Username, StringComparison.OrdinalIgnoreCase);
    private static CompleteBackupInfo ToInfo(PackageManifest manifest, long archiveBytes) => new CompleteBackupInfo(
        manifest.ExportedBy, manifest.ExportedAtUtc, manifest.AppVersion, manifest.AccountCount,
        manifest.NoteCount, manifest.PersonalFileCount, manifest.PersonalFileBytes,
        manifest.IncludesSettings, archiveBytes, manifest.SnapshotFingerprint)
    {
        IncludesNotes = manifest.IncludesNotes,
        IncludesPersonalFiles = manifest.IncludesPersonalFiles
    };
    private static PortableSettings CaptureSettings() => new()
    {
        Language = RegistryService.GetLanguage(),
        Theme = RegistryService.GetTheme(),
        AutoLockMinutes = RegistryService.GetAutoLockMinutes(),
        LockWhenMinimized = RegistryService.GetLockWhenMinimized(),
        AutoThemeEnabled = RegistryService.GetAutoThemeEnabled(),
        PwnedPasswordReminderDays = RegistryService.GetPwnedPasswordReminderDays()
    };
    private static void ValidateSettings(PortableSettings? settings)
    {
        if (settings == null) return;
        if (settings.Language is not ("vi" or "en") || settings.Theme is not ("Light" or "Dark") ||
            settings.AutoLockMinutes is not (1 or 5 or 10 or 15) ||
            settings.PwnedPasswordReminderDays is not (0 or 30 or 90 or 180))
            throw new InvalidDataException("Cài đặt trong gói sao lưu không hợp lệ.");
    }
    private static void ApplySettings(PortableSettings settings)
    {
        ValidateSettings(settings);
        RegistryService.SetLanguage(settings.Language);
        RegistryService.SetTheme(settings.Theme);
        RegistryService.SetAutoLockMinutes(settings.AutoLockMinutes);
        RegistryService.SetLockWhenMinimized(settings.LockWhenMinimized);
        RegistryService.SetAutoThemeEnabled(settings.AutoThemeEnabled);
        RegistryService.SetPwnedPasswordReminderDays(settings.PwnedPasswordReminderDays);
        void ApplyAppearance()
        {
            LanguageService.SetLanguage(settings.Language);
            if (settings.Theme == "Dark") ThemeService.ApplyDark(save: false); else ThemeService.ApplyLight(save: false);
        }
        if (System.Windows.Application.Current is { } app && !app.Dispatcher.CheckAccess())
            app.Dispatcher.BeginInvoke(new Action(ApplyAppearance));
        else
            ApplyAppearance();
    }
    private static long MeasureJson<T>(T value)
    {
        using var stream = new CountingWriteStream();
        JsonSerializer.Serialize(stream, value, JsonOptions);
        return stream.Count;
    }
    private static void WriteJson<T>(ZipArchive zip, string name, T value)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        JsonSerializer.Serialize(stream, value, JsonOptions);
    }
    private static T ReadJson<T>(ZipArchive zip, string entryName)
    {
        ZipArchiveEntry entry = zip.GetEntry(entryName)
            ?? throw new InvalidDataException($"Gói sao lưu thiếu mục '{entryName}'.");
        if (entry.Length > MaximumArchiveBytes) throw new InvalidDataException("Mục dữ liệu vượt giới hạn.");
        using Stream stream = entry.Open();
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Mục '{entryName}' không đúng định dạng.");
    }
    private static string HashJson<T>(T value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try { return Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static string HashFile(string path, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return HashStream(stream, cancellationToken);
    }
    private static bool FileContentEquals(byte[] archiveBytes, string entryName,
        LocalFileService service, string existingPath, long expectedLength)
    {
        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        ZipArchiveEntry entry = zip.GetEntry(entryName)
            ?? throw new InvalidDataException("Thiếu tệp trong gói sao lưu.");
        using var incoming = entry.Open();
        return service.ContentEqualsReadOnly(existingPath, incoming, expectedLength);
    }
    private static string HashStream(Stream stream, CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private sealed class CountingWriteStream : Stream
    {
        public long Count { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Count;
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Count = checked(Count + count);
        public override void Write(ReadOnlySpan<byte> buffer) => Count = checked(Count + buffer.Length);
        public override void WriteByte(byte value) => Count = checked(Count + 1);
    }
    private sealed class BoundedMemoryStream(long limit) : MemoryStream
    {
        private void EnsureCapacity(long additional)
        {
            if (additional > limit - Length) throw new InvalidDataException("Dữ liệu sao lưu vượt giới hạn dung lượng.");
        }
        public override void Write(byte[] buffer, int offset, int count) { EnsureCapacity(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { EnsureCapacity(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { EnsureCapacity(1); base.WriteByte(value); }
        public override void SetLength(long value)
        {
            if (value > limit) throw new InvalidDataException("Dữ liệu sao lưu vượt giới hạn dung lượng.");
            base.SetLength(value);
        }
    }
    private sealed class HashingWriteStream(Stream inner, bool leaveOpen) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public long BytesWritten { get; private set; }
        public string GetHash() => Convert.ToHexString(_hash.GetHashAndReset());
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            _hash.AppendData(buffer, offset, count);
            BytesWritten = checked(BytesWritten + count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            _hash.AppendData(buffer);
            BytesWritten = checked(BytesWritten + buffer.Length);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _hash.Dispose(); if (!leaveOpen) inner.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
