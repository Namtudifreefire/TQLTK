using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PersonalInfoManager.Services
{

    public static class UserDataCleaner
    {
        private static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager");

        private sealed class Item
        {
            public string Label = "";
            public List<string> Files = new();
            public string? Dir;
            public long Bytes;
            public int FileCount;
        }

        public static List<string> Describe(Guid userId, string username)
        {
            var warnings = new List<string>();
            var descriptions = BuildItems(userId, username, warnings)
                .Select(i => $"• {i.Label} — {FormatSize(i)}")
                .ToList();
            descriptions.AddRange(warnings.Select(w => "⚠ " + w));
            return descriptions;
        }

        public static (List<string> removed, List<string> errors) Delete(Guid userId, string username)
        {
            var removed = new List<string>();
            var errors = new List<string>();
            var warnings = new List<string>();

            foreach (var item in BuildItems(userId, username, warnings))
            {
                try
                {
                    foreach (var f in item.Files) File.Delete(f);

                    if (item.Dir != null && Directory.Exists(item.Dir))
                        Directory.Delete(item.Dir, recursive: true);

                    removed.Add(item.Label);
                }
                catch (Exception ex)
                {
                    errors.Add($"{item.Label}: {ex.Message}");
                }
            }

            errors.AddRange(warnings);

            try
            {
                if (string.Equals(RegistryService.GetLastUser(), username, StringComparison.OrdinalIgnoreCase))
                    RegistryService.ClearLastUser();
            }
            catch { }

            return (removed, errors);
        }

        private static List<Item> BuildItems(Guid userId, string username, List<string> warnings)
        {
            var items = new List<Item>();
            var userService = new UserService();
            string isolatedName = UserStorageIdentity.GetDirectoryName(userId);

            string tag = BackupCrypto.UserTag(username);

            var vault = new Item { Label = "Kho tài khoản đã lưu (vault) và bản dự phòng" };
            string vaultFile = Path.Combine(Root, $"vault_{isolatedName}.dat");
            AddFile(vault, vaultFile);
            AddFile(vault, vaultFile + ".bak");
            AddFile(vault, vaultFile + ".tmp");
            AddCorruptVaultFiles(vault, vaultFile);

            string legacyVault = UserStorageIdentity.GetLegacyPath(
                Root, username, LegacyUserStorageKind.Vault);
            if (!userService.HasLegacyStoragePathCollision(
                userId, username, LegacyUserStorageKind.Vault))
            {
                AddFile(vault, legacyVault);
                AddFile(vault, legacyVault + ".bak");
                AddFile(vault, legacyVault + ".tmp");
                AddCorruptVaultFiles(vault, legacyVault);
            }
            else if (UserStorageIdentity.HasLegacyData(
                Root, username, LegacyUserStorageKind.Vault))
                warnings.Add("Vault cũ có đường dẫn trùng với tài khoản khác nên được giữ nguyên.");
            AddIfAny(items, vault);

            var twoFa = new Item { Label = "Cấu hình xác thực 2 bước (2FA)" };
            AddFile(twoFa, Path.Combine(
                Root, "2fa", UserStorageIdentity.GetDirectoryName(userId) + ".2fa"));
            string legacyTwoFa = UserStorageIdentity.GetLegacyPath(
                Root, username, LegacyUserStorageKind.TwoFactor);
            if (!userService.HasLegacyStoragePathCollision(
                userId, username, LegacyUserStorageKind.TwoFactor))
                AddFile(twoFa, legacyTwoFa);
            else if (UserStorageIdentity.HasLegacyData(
                Root, username, LegacyUserStorageKind.TwoFactor))
                warnings.Add("File 2FA cũ có đường dẫn trùng với tài khoản khác nên được giữ nguyên.");
            AddIfAny(items, twoFa);

            var pass = new Item { Label = "Cụm mật khẩu sao lưu đã lưu trên máy" };
            string passFile = Path.Combine(Root, $"backup_pass_{tag}.dat");
            AddFile(pass, passFile);
            AddFile(pass, passFile + ".tmp");
            AddIfAny(items, pass);

            string storageRoot = Path.Combine(Root, "Storage");
            string storageDir = Path.Combine(storageRoot, isolatedName);
            AddStorageDirectory(items, storageDir);

            string legacyStorageDir = UserStorageIdentity.GetLegacyPath(
                Root, username, LegacyUserStorageKind.LocalFiles);
            if (!userService.HasLegacyStoragePathCollision(
                userId, username, LegacyUserStorageKind.LocalFiles))
                AddStorageDirectory(items, legacyStorageDir);
            else if (UserStorageIdentity.HasLegacyData(
                Root, username, LegacyUserStorageKind.LocalFiles))
                warnings.Add("Thư mục file cũ có đường dẫn trùng với tài khoản khác nên được giữ nguyên.");

            string tokenRoot = Path.Combine(Root, "GoogleTokens");
            string tokenDir = Path.Combine(tokenRoot, tag);
            if (Directory.Exists(tokenDir) && IsDirectChild(tokenRoot, tokenDir))
            {
                var token = new Item { Label = "Đăng nhập Google (token đồng bộ Drive)", Dir = tokenDir };
                AddDirStats(token, tokenDir);
                AddIfAny(items, token);
            }

            return items;
        }

        private static void AddCorruptVaultFiles(Item item, string vaultFile)
        {
            if (!Directory.Exists(Root)) return;
            string corruptPrefix = Path.GetFileName(vaultFile) + ".corrupt-";
            foreach (var file in Directory.GetFiles(Root, corruptPrefix + "*"))
                AddFile(item, file);
        }

        private static void AddStorageDirectory(List<Item> items, string storageDir)
        {
            string storageRoot = Path.Combine(Root, "Storage");
            if (!Directory.Exists(storageDir) || !IsDirectChild(storageRoot, storageDir)) return;

            var storage = new Item { Label = "File cá nhân", Dir = storageDir };
            AddDirStats(storage, storageDir);
            AddIfAny(items, storage);
        }

        private static bool IsDirectChild(string parent, string child)
        {
            string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            string c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(Path.GetDirectoryName(c), p, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddFile(Item item, string path)
        {
            if (!File.Exists(path)) return;

            item.Files.Add(path);
            item.FileCount++;
            try { item.Bytes += new FileInfo(path).Length; } catch { }
        }

        private static void AddDirStats(Item item, string dir)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    item.FileCount++;
                    try { item.Bytes += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
        }

        private static void AddIfAny(List<Item> items, Item item)
        {
            if (item.FileCount > 0) items.Add(item);
        }

        private static string FormatSize(Item i)
        {
            string size = i.Bytes < 1024 * 1024
                ? $"{i.Bytes / 1024.0:0.#} KB"
                : $"{i.Bytes / 1048576.0:0.#} MB";

            return i.FileCount == 1 ? size : $"{i.FileCount} file, {size}";
        }
    }
}
