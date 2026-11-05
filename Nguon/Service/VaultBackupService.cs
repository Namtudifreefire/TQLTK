using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    public sealed class VaultImportPreview
    {
        internal VaultImportPreview(
            string owner, DateTime exportedAt, List<SavedAccount> accounts,
            List<BackupPreviewItem> items, int importedCount, int skippedCount,
            string targetVaultFingerprint)
        {
            Owner = owner;
            ExportedAt = exportedAt;
            Accounts = accounts;
            Items = items;
            ImportedCount = importedCount;
            SkippedCount = skippedCount;
            TargetVaultFingerprint = targetVaultFingerprint;
        }

        public string Owner { get; }
        public DateTime ExportedAt { get; }
        public int IncomingCount => Accounts.Count;
        public int ImportedCount { get; }
        public int SkippedCount { get; }
        public IReadOnlyList<BackupPreviewItem> Items { get; }
        internal List<SavedAccount> Accounts { get; }
        internal string TargetVaultFingerprint { get; }
    }

    public static class VaultBackupService
    {

        public static (bool ok, string message) ExportToFile(string username, string destPath)
            => ExportToFile(username, destPath, out _);

        public static (bool ok, string message) ExportToFile(
            string username, string destPath, out string snapshotFingerprint)
        {
            snapshotFingerprint = "";
            try
            {
                var vaultService = new AccountVaultService();
                var accounts = vaultService.Load(username);

                if (accounts.Count == 0)
                    return (false, "Vault trống, không có gì để xuất.");

                var package = new VaultPackage
                {
                    ExportedAt = DateTime.Now,
                    ExportedBy = username,
                    AppVersion = "3.6.7",
                    AccountCount = accounts.Count,
                    Accounts = accounts
                };

                string json = JsonSerializer.Serialize(package,
                    new JsonSerializerOptions { WriteIndented = true });

                string? passphrase = BackupCrypto.GetStoredPassphrase(username);
                string encrypted = passphrase != null
                    ? BackupCrypto.Encrypt(json, passphrase)
                    : AesCrypto.Encrypt(json);

                string stagedPath = AtomicFileService.WriteTemporary(
                    destPath, System.Text.Encoding.UTF8.GetBytes(encrypted));
                try
                {
                    var verification = InspectBackup(username, stagedPath, passphrase);
                    if (!verification.ok)
                        throw new InvalidDataException("Bản sao lưu tạo mới không qua kiểm tra: " + verification.message);

                    AtomicFileService.PromoteTemporary(stagedPath, destPath);
                    snapshotFingerprint = VaultSyncCheckpointStore.ComputeFingerprint(accounts);
                }
                finally
                {
                    AtomicFileService.TryDelete(stagedPath);
                }

                try
                {
                    ActivityLogger.Log(username, "EXPORT_VAULT",
                        $"Đã xuất {accounts.Count} tài khoản ra {Path.GetFileName(destPath)}");
                }
                catch { }

                string msg = $"Đã xuất {accounts.Count} tài khoản.";
                if (passphrase == null)
                    msg += "\n\nLưu ý: bạn chưa đặt cụm mật khẩu sao lưu nên file này chỉ mở được trên máy này " +
                           "(Tiện ích → Cụm mật khẩu sao lưu).";

                return (true, msg);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi: {ex.Message}");
            }
        }

        public static bool IsPassphraseProtected(string sourcePath)
        {
            try { return BackupCrypto.IsPassphraseFormat(File.ReadAllText(sourcePath)); }
            catch { return false; }
        }

        public static bool PassphraseWorks(string username, string sourcePath, string? passphrase = null)
        {
            try
            {
                string text = File.ReadAllText(sourcePath);
                if (!BackupCrypto.IsPassphraseFormat(text)) return true;

                passphrase ??= BackupCrypto.GetStoredPassphrase(username);
                if (passphrase == null) return false;

                BackupCrypto.Decrypt(text, passphrase);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static (bool ok, string message, int accountCount) InspectBackup(
            string username, string sourcePath, string? passphrase = null)
        {
            try
            {
                if (!File.Exists(sourcePath)) return (false, "File không tồn tại.", 0);

                string encrypted = File.ReadAllText(sourcePath);
                if (string.IsNullOrWhiteSpace(encrypted)) return (false, "File trống.", 0);

                string json;
                if (BackupCrypto.IsPassphraseFormat(encrypted))
                {
                    passphrase ??= BackupCrypto.GetStoredPassphrase(username);
                    if (passphrase == null)
                        return (false, "Bản sao lưu cần cụm mật khẩu sao lưu.", 0);
                    json = BackupCrypto.Decrypt(encrypted, passphrase);
                }
                else if (AesCrypto.IsAuthenticatedV2Format(encrypted))
                {
                    json = AesCrypto.Decrypt(encrypted);
                }
                else
                {
                    return (false, "Định dạng backup không được hỗ trợ hoặc không có kiểm tra toàn vẹn.", 0);
                }

                var package = JsonSerializer.Deserialize<VaultPackage>(json);
                if (package?.Accounts == null || package.Accounts.Count == 0)
                    return (false, "Backup không chứa danh sách tài khoản hợp lệ.", 0);
                if (package.Accounts.Any(account => account == null))
                    return (false, "Backup có mục tài khoản không hợp lệ.", 0);
                if (package.AccountCount > 0 && package.AccountCount != package.Accounts.Count)
                    return (false, "Số lượng tài khoản trong manifest không khớp dữ liệu.", 0);
                AccountVaultService.ValidateAttachments(package.Accounts);

                string owner = string.IsNullOrWhiteSpace(package.ExportedBy)
                    ? "không rõ người tạo"
                    : $"người tạo: {package.ExportedBy.Trim()}";
                string created = package.ExportedAt == default
                    ? "không có ngày xuất"
                    : $"ngày xuất: {package.ExportedAt:dd/MM/yyyy HH:mm}";
                return (true,
                    $"Bản sao lưu hợp lệ: {package.Accounts.Count} tài khoản, {owner}, {created}. " +
                    "Đã giải mã và kiểm tra cấu trúc; chưa thay đổi vault hiện tại.",
                    package.Accounts.Count);
            }
            catch (Exception ex)
            {
                return (false, $"Không thể xác minh bản sao lưu: {ex.Message}", 0);
            }
        }

        public static (bool ok, string message, int accountCount) DrillRestore(
            string username, string sourcePath, string? passphrase = null)
        {
            var stopwatch = Stopwatch.StartNew();
            string? drillDirectory = null;
            byte[]? drillKey = null;
            byte[]? serialized = null;
            byte[]? encrypted = null;
            try
            {
                var prepared = PrepareImport(username, sourcePath, passphrase);
                if (!prepared.ok || prepared.preview == null)
                    return (false, prepared.message, 0);

                var preview = prepared.preview;
                string tempRoot = Path.GetFullPath(Path.GetTempPath());
                drillDirectory = Path.GetFullPath(Path.Combine(tempRoot, "TQLTK-Vault-Drill-" + Guid.NewGuid().ToString("N")));
                if (!drillDirectory.StartsWith(tempRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Thư mục diễn tập không nằm trong thư mục tạm.");
                Directory.CreateDirectory(drillDirectory);
                if ((File.GetAttributes(drillDirectory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Thư mục diễn tập không được là liên kết hệ thống.");

                serialized = JsonSerializer.SerializeToUtf8Bytes(preview.Accounts);
                string json = Encoding.UTF8.GetString(serialized);
                drillKey = RandomNumberGenerator.GetBytes(32);
                encrypted = AesCrypto.EncryptWithKey(drillKey, json);
                string probePath = Path.Combine(drillDirectory, "vault-drill.v2");
                AtomicFileService.WriteAllBytes(probePath, encrypted);

                byte[] onDisk = File.ReadAllBytes(probePath);
                string probeCipher = Encoding.UTF8.GetString(onDisk);
                CryptographicOperations.ZeroMemory(onDisk);
                string restoredJson = AesCrypto.DecryptWithKey(drillKey, probeCipher);
                var restored = JsonSerializer.Deserialize<List<SavedAccount>>(restoredJson);
                if (restored == null || restored.Count != preview.IncomingCount ||
                    !string.Equals(
                        VaultSyncCheckpointStore.ComputeFingerprint(restored),
                        VaultSyncCheckpointStore.ComputeFingerprint(preview.Accounts),
                        StringComparison.Ordinal))
                    throw new InvalidDataException("Nội dung tài khoản, TOTP hoặc tệp đính kèm sau khôi phục thử không khớp bản sao lưu.");

                return (true,
                    $"Diễn tập khôi phục thành công: đã đọc, giải mã, ghi thử an toàn vào kho tạm, đọc lại và đối chiếu {restored.Count} tài khoản trong {stopwatch.Elapsed.TotalSeconds:0.0} giây. Vault thật không thay đổi.",
                    restored.Count);
            }
            catch (Exception ex)
            {
                return (false, $"Diễn tập khôi phục thất bại; vault thật không thay đổi. {ex.Message}", 0);
            }
            finally
            {
                if (serialized != null) CryptographicOperations.ZeroMemory(serialized);
                if (encrypted != null) CryptographicOperations.ZeroMemory(encrypted);
                if (drillKey != null) CryptographicOperations.ZeroMemory(drillKey);
                if (drillDirectory != null)
                {
                    try
                    {
                        string full = Path.GetFullPath(drillDirectory);
                        string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                            Path.GetFileName(full).StartsWith("TQLTK-Vault-Drill-", StringComparison.Ordinal))
                            Directory.Delete(full, recursive: true);
                    }
                    catch { }
                }
            }
        }

        public static (bool ok, string message, VaultImportPreview? preview) PrepareImport(
            string username, string sourcePath, string? passphrase = null)
        {
            try
            {
                if (!File.Exists(sourcePath))
                    return (false, "File không tồn tại.", null);

                string encrypted = File.ReadAllText(sourcePath);
                if (string.IsNullOrWhiteSpace(encrypted))
                    return (false, "File trống.", null);

                string json;
                if (BackupCrypto.IsPassphraseFormat(encrypted))
                {
                    passphrase ??= BackupCrypto.GetStoredPassphrase(username);
                    if (passphrase == null)
                        return (false, "File này được bảo vệ bằng cụm mật khẩu sao lưu. Hãy nhập cụm mật khẩu để mở.", null);
                    try { json = BackupCrypto.Decrypt(encrypted, passphrase); }
                    catch { return (false, "Không thể giải mã file. Cụm mật khẩu không đúng hoặc file bị hỏng.", null); }
                }
                else
                {
                    if (!AesCrypto.IsAuthenticatedV2Format(encrypted))
                        return (false,
                            "Bản sao lưu không dùng định dạng AES-GCM v2. CBC cũ không có kiểm tra toàn vẹn, " +
                            "nên ứng dụng từ chối mở file này và không thay đổi bản gốc.", null);
                    try { json = AesCrypto.Decrypt(encrypted); }
                    catch
                    {
                        return (false,
                            "Không thể giải mã file. File có thể bị hỏng hoặc không đúng định dạng.", null);
                    }
                }

                var package = JsonSerializer.Deserialize<VaultPackage>(json);
                if (package?.Accounts == null || package.Accounts.Count == 0)
                    return (false, "File không chứa tài khoản nào.", null);
                if (package.Accounts.Any(account => account == null))
                    return (false, "Bản sao lưu có mục tài khoản không hợp lệ.", null);
                if (package.AccountCount > 0 && package.AccountCount != package.Accounts.Count)
                    return (false, "Số lượng tài khoản trong manifest không khớp dữ liệu.", null);
                AccountVaultService.ValidateAttachments(package.Accounts);

                string owner = (package.ExportedBy ?? "").Trim();
                var existing = new AccountVaultService().Load(username);
                string targetVaultFingerprint = ComputeVaultFingerprint(existing);
                var previewItems = new List<BackupPreviewItem>(package.Accounts.Count);
                int imported = 0;
                int skipped = 0;

                foreach (var account in package.Accounts)
                {
                    bool duplicate = existing.Exists(current =>
                        current.Id == account.Id ||
                        (string.Equals(current.ServiceName, account.ServiceName, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(current.Username, account.Username, StringComparison.OrdinalIgnoreCase)));

                    if (duplicate)
                    {
                        skipped++;
                        previewItems.Add(new BackupPreviewItem(
                            account.ServiceName, "Bỏ qua trùng", account.Username));
                    }
                    else
                    {
                        imported++;
                        previewItems.Add(new BackupPreviewItem(
                            account.ServiceName, "Sẽ nhập", account.Username));
                        var simulated = account.Clone();
                        simulated.Id = Guid.NewGuid();
                        existing.Add(simulated);
                    }
                }

                return (true, "Đã giải mã bản sao lưu và tính các mục sẽ nhập.",
                    new VaultImportPreview(owner, package.ExportedAt, package.Accounts, previewItems,
                        imported, skipped, targetVaultFingerprint));
            }
            catch (Exception ex)
            {
                return (false, $"Không thể xem trước bản sao lưu: {ex.Message}", null);
            }
        }

        public static (bool ok, string message, int importedCount) CommitImport(
            string username, VaultImportPreview preview, Func<string, bool>? confirmForeignOwner = null)
        {
            try
            {
                if (preview.Accounts.Count == 0)
                    return (false, "Bản xem trước đã được sử dụng hoặc không còn dữ liệu.", 0);

                bool foreignOwner = preview.Owner.Length > 0 &&
                                    !string.Equals(preview.Owner, username, StringComparison.OrdinalIgnoreCase);
                if (foreignOwner && (confirmForeignOwner == null || !confirmForeignOwner(preview.Owner)))
                    return (false,
                        $"Đã hủy nhập: bản sao lưu này thuộc về người dùng '{preview.Owner}', không phải '{username}'.", 0);

                var vaultService = new AccountVaultService();
                var existing = vaultService.Load(username);
                if (!string.Equals(
                        preview.TargetVaultFingerprint,
                        ComputeVaultFingerprint(existing),
                        StringComparison.Ordinal))
                    return (false, "Vault đã thay đổi sau khi xem trước. Hãy tạo bản xem trước mới.", 0);

                int imported = 0;
                int skipped = 0;
                foreach (var account in preview.Accounts)
                {
                    bool duplicate = existing.Exists(current =>
                        current.Id == account.Id ||
                        (string.Equals(current.ServiceName, account.ServiceName, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(current.Username, account.Username, StringComparison.OrdinalIgnoreCase)));
                    if (duplicate) skipped++;
                    else
                    {
                        var simulated = account.Clone();
                        simulated.Id = Guid.NewGuid();
                        existing.Add(simulated);
                        imported++;
                    }
                }

                if (imported != preview.ImportedCount || skipped != preview.SkippedCount)
                    return (false, "Vault đã thay đổi sau khi xem trước. Hãy tạo bản xem trước mới.", 0);

                vaultService.Save(username, existing);
                try
                {
                    ActivityLogger.Log(username, "IMPORT_VAULT",
                        $"Đã nhập {imported} tài khoản (bỏ qua {skipped} trùng)" +
                        (foreignOwner ? $" (bản sao lưu của người dùng '{preview.Owner}')" : ""));
                }
                catch { }

                return (true,
                    skipped > 0 ? $"Đã nhập {imported} tài khoản.\nBỏ qua {skipped} tài khoản trùng." : $"Đã nhập {imported} tài khoản.",
                    imported);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi: {ex.Message}", 0);
            }
        }

        public static (bool ok, string message, int importedCount) ImportFromFile(
            string username, string sourcePath, string? passphrase = null,
            Func<string, bool>? confirmForeignOwner = null)
        {
            var prepared = PrepareImport(username, sourcePath, passphrase);
            if (!prepared.ok || prepared.preview == null)
                return (false, prepared.message, 0);
            return CommitImport(username, prepared.preview, confirmForeignOwner);
        }

        private class VaultPackage
        {
            public DateTime ExportedAt { get; set; }
            public string ExportedBy { get; set; } = "";
            public string AppVersion { get; set; } = "";
            public int AccountCount { get; set; }
            public List<SavedAccount> Accounts { get; set; } = new();
        }

        private static string ComputeVaultFingerprint(List<SavedAccount> accounts)
        {
            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(accounts.OrderBy(account => account.Id));
            try { return Convert.ToHexString(SHA256.HashData(serialized)); }
            finally { CryptographicOperations.ZeroMemory(serialized); }
        }
    }
}
