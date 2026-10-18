using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Xuất / Nhập toàn bộ vault (tài khoản đã lưu) của user.
    /// File backup được mã hóa AES.
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

                string encrypted = AesCrypto.Encrypt(json);
                File.WriteAllText(destPath, encrypted);

                try
                {
                    ActivityLogger.Log(username, "EXPORT_VAULT",
                        $"Đã xuất {accounts.Count} tài khoản ra {Path.GetFileName(destPath)}");
                }
                catch { }

                return (true, $"Đã xuất {accounts.Count} tài khoản.");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi: {ex.Message}");
            }
        }

        /// <summary>
        /// Nhập vault từ file backup.
        /// </summary>
        public static (bool ok, string message, int importedCount) ImportFromFile(
            string username, string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                    return (false, "File không tồn tại.", 0);

                string encrypted = File.ReadAllText(sourcePath);
                if (string.IsNullOrWhiteSpace(encrypted))
                    return (false, "File trống.", 0);

                string json;
                try
                {
                    json = AesCrypto.Decrypt(encrypted);
                }
                catch
                {
                    return (false,
                        "Không thể giải mã file.\n" +
                        "File có thể bị hỏng hoặc không đúng định dạng.", 0);
                }

                var package = JsonSerializer.Deserialize<VaultPackage>(json);
                if (package?.Accounts == null || package.Accounts.Count == 0)
                    return (false, "File không chứa tài khoản nào.", 0);

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
                        $"Đã nhập {imported} tài khoản (bỏ qua {skipped} trùng) từ {Path.GetFileName(sourcePath)}");
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