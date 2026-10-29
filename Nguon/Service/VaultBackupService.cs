using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Xuất / Nhập toàn bộ vault (tài khoản đã lưu) của user.
    /// File backup được mã hóa AES:
    /// - Đã đặt cụm mật khẩu sao lưu của người dùng này: mã hóa bằng cụm đó (BackupCrypto, "v3p:"), mở được trên máy khác.
    /// - Chưa đặt: mã hóa bằng khóa của máy (AesCrypto), chỉ mở được trên máy này.
    /// Khi nhập, định dạng được nhận biết từ phần đầu file nên file cũ vẫn đọc được.
    /// </summary>
    public static class VaultBackupService
    {
        /// <summary>
        /// Xuất vault ra file backup (đã mã hóa).
        /// </summary>
        public static (bool ok, string message) ExportToFile(string username, string destPath)
        {
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
                    AppVersion = "1.0.0",
                    AccountCount = accounts.Count,
                    Accounts = accounts
                };

                string json = JsonSerializer.Serialize(package,
                    new JsonSerializerOptions { WriteIndented = true });

                string? passphrase = BackupCrypto.GetStoredPassphrase(username);
                string encrypted = passphrase != null
                    ? BackupCrypto.Encrypt(json, passphrase)
                    : AesCrypto.Encrypt(json);

                File.WriteAllText(destPath, encrypted);

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

        /// <summary>
        /// true nếu file cần cụm mật khẩu để mở (định dạng "v3p:").
        /// </summary>
        public static bool IsPassphraseProtected(string sourcePath)
        {
            try { return BackupCrypto.IsPassphraseFormat(File.ReadAllText(sourcePath)); }
            catch { return false; }
        }

        /// <summary>
        /// Kiểm tra file mở được bằng cụm mật khẩu này không (passphrase = null thì dùng cụm đã lưu của người dùng này).
        /// File kiểu cũ không cần cụm nên trả về true.
        /// </summary>
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

        /// <summary>
        /// Nhập vault từ file backup.
        /// </summary>
        /// <param name="passphrase">
        /// Cụm mật khẩu để mở file định dạng "v3p:". Bỏ trống thì dùng cụm đã lưu của người dùng này.
        /// File kiểu cũ (khóa của máy) không dùng tham số này.
        /// </param>
        /// <param name="confirmForeignOwner">
        /// Gọi khi file ghi người xuất (ExportedBy) khác người dùng hiện tại, nhận tên người xuất.
        /// Trả true để vẫn nhập. Bỏ trống thì từ chối nhập file của người dùng khác.
        /// </param>
        public static (bool ok, string message, int importedCount) ImportFromFile(
            string username, string sourcePath, string? passphrase = null,
            Func<string, bool>? confirmForeignOwner = null)
        {
            try
            {
                if (!File.Exists(sourcePath))
                    return (false, "File không tồn tại.", 0);

                string encrypted = File.ReadAllText(sourcePath);
                if (string.IsNullOrWhiteSpace(encrypted))
                    return (false, "File trống.", 0);

                string json;
                if (BackupCrypto.IsPassphraseFormat(encrypted))
                {
                    passphrase ??= BackupCrypto.GetStoredPassphrase(username);
                    if (passphrase == null)
                        return (false, "File này được bảo vệ bằng cụm mật khẩu sao lưu. Hãy nhập cụm mật khẩu để mở.", 0);

                    try
                    {
                        json = BackupCrypto.Decrypt(encrypted, passphrase);
                    }
                    catch
                    {
                        return (false,
                            "Không thể giải mã file.\n" +
                            "Cụm mật khẩu không đúng, hoặc file bị hỏng.", 0);
                    }
                }
                else
                {
                    if (!AesCrypto.IsAuthenticatedV2Format(encrypted))
                    {
                        return (false,
                            "Bản sao lưu không dùng định dạng AES-GCM v2. CBC cũ không có kiểm tra toàn vẹn, " +
                            "nên ứng dụng từ chối mở file này và không thay đổi bản gốc.", 0);
                    }

                    try
                    {
                        json = AesCrypto.Decrypt(encrypted);
                    }
                    catch
                    {
                        return (false,
                            "Không thể giải mã file.\n" +
                            "File có thể bị hỏng hoặc không đúng định dạng.\n\n" +
                            "Nếu file được tạo trên máy khác trước khi đặt cụm mật khẩu sao lưu, " +
                            "nó chỉ mở được trên máy đã tạo ra nó.", 0);
                    }
                }

                var package = JsonSerializer.Deserialize<VaultPackage>(json);
                if (package?.Accounts == null || package.Accounts.Count == 0)
                    return (false, "File không chứa tài khoản nào.", 0);

                // File ghi người xuất khác người dùng hiện tại: chỉ nhập khi được xác nhận rõ ràng
                string owner = (package.ExportedBy ?? "").Trim();
                bool foreignOwner = owner.Length > 0 &&
                                    !string.Equals(owner, username, StringComparison.OrdinalIgnoreCase);

                if (foreignOwner && (confirmForeignOwner == null || !confirmForeignOwner(owner)))
                    return (false,
                        $"Đã hủy nhập: bản sao lưu này thuộc về người dùng '{owner}', không phải '{username}'.", 0);

                var vaultService = new AccountVaultService();
                var existing = vaultService.Load(username);

                int imported = 0;
                int skipped = 0;

                foreach (var acc in package.Accounts)
                {
                    bool isDuplicate = existing.Exists(e =>
                        e.Id == acc.Id ||
                        (string.Equals(e.ServiceName, acc.ServiceName,
                            StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(e.Username, acc.Username,
                            StringComparison.OrdinalIgnoreCase)));

                    if (isDuplicate)
                    {
                        skipped++;
                        continue;
                    }

                    acc.Id = Guid.NewGuid();
                    existing.Add(acc);
                    imported++;
                }

                vaultService.Save(username, existing);

                try
                {
                    ActivityLogger.Log(username, "IMPORT_VAULT",
                        $"Đã nhập {imported} tài khoản (bỏ qua {skipped} trùng) từ {Path.GetFileName(sourcePath)}" +
                        (foreignOwner ? $" (bản sao lưu của người dùng '{owner}')" : ""));
                }
                catch { }

                string msg = $"Đã nhập {imported} tài khoản.";
                if (skipped > 0)
                    msg += $"\nBỏ qua {skipped} tài khoản trùng.";

                return (true, msg, imported);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi: {ex.Message}", 0);
            }
        }

        /// <summary>
        /// Package dùng để đóng gói khi xuất/nhập.
        /// </summary>
        private class VaultPackage
        {
            public DateTime ExportedAt { get; set; }
            public string ExportedBy { get; set; } = "";
            public string AppVersion { get; set; } = "";
            public int AccountCount { get; set; }
            public List<SavedAccount> Accounts { get; set; } = new();
        }
    }
}
