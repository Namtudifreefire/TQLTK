using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Liệt kê và xóa dữ liệu trên máy của một người dùng đã bị xóa khỏi danh sách tài khoản:
    /// vault (kèm .bak/.tmp/.corrupt-*), cấu hình 2FA, thư mục File cá nhân,
    /// cụm mật khẩu sao lưu và đăng nhập Google của người dùng đó.
    ///
    /// KHÔNG đụng tới: nhật ký hoạt động, bản sao lưu trên Google Drive, dữ liệu của người dùng khác.
    /// Quy tắc đặt tên file phải khớp với AccountVaultService, TwoFactorService, LocalFileService,
    /// BackupCrypto và CloudSyncService; nếu đổi cách đặt tên ở đó thì sửa cả ở đây.
    /// </summary>
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

        // ================================================================
        // ================== API ==================
        // ================================================================

        /// <summary>Các mục dữ liệu còn tồn tại của người dùng (để hiện cho quản trị viên xem trước khi xóa).</summary>
        public static List<string> Describe(string username)
            => BuildItems(username).Select(i => $"• {i.Label} — {FormatSize(i)}").ToList();

        /// <summary>Xóa các mục trên. Trả về danh sách đã xóa và danh sách lỗi (nếu có).</summary>
        public static (List<string> removed, List<string> errors) Delete(string username)
        {
            var removed = new List<string>();
            var errors = new List<string>();

            foreach (var item in BuildItems(username))
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

            // Ô "tên đăng nhập" ở màn hình đăng nhập không nên điền sẵn tên đã xóa
            try
            {
                if (string.Equals(RegistryService.GetLastUser(), username, StringComparison.OrdinalIgnoreCase))
                    RegistryService.ClearLastUser();
            }
            catch { }

            return (removed, errors);
        }

        // ================================================================
        // ================== NỘI BỘ ==================
        // ================================================================

        private static List<Item> BuildItems(string username)
        {
            var items = new List<Item>();

            string safe = SafeName(username);
            // Tên rỗng hoặc chỉ gồm dấu chấm/khoảng trắng: có thể trỏ ra chính thư mục cha → không đụng vào gì
            if (string.IsNullOrWhiteSpace(safe) || safe.All(c => c == '.' || c == ' '))
                return items;

            string tag = BackupCrypto.UserTag(username);

            // 1. Vault và các bản dự phòng
            var vault = new Item { Label = "Kho tài khoản đã lưu (vault) và bản dự phòng" };
            string vaultFile = Path.Combine(Root, $"vault_{safe}.dat");
            AddFile(vault, vaultFile);
            AddFile(vault, vaultFile + ".bak");
            AddFile(vault, vaultFile + ".tmp");
            if (Directory.Exists(Root))
            {
                string corruptPrefix = $"vault_{safe}.dat.corrupt-";
                foreach (var f in Directory.GetFiles(Root, corruptPrefix + "*"))
                {
                    // Chỉ nhận đúng hậu tố thời gian 14 chữ số, tránh nhặt nhầm file của người dùng khác
                    string suffix = Path.GetFileName(f).Substring(corruptPrefix.Length);
                    if (suffix.Length == 14 && suffix.All(char.IsDigit))
                        AddFile(vault, f);
                }
            }
            AddIfAny(items, vault);

            // 2. Xác thực 2 bước
            var twoFa = new Item { Label = "Cấu hình xác thực 2 bước (2FA)" };
            AddFile(twoFa, Path.Combine(Root, "2fa", safe + ".2fa"));
            AddIfAny(items, twoFa);

            // 3. Cụm mật khẩu sao lưu
            var pass = new Item { Label = "Cụm mật khẩu sao lưu đã lưu trên máy" };
            string passFile = Path.Combine(Root, $"backup_pass_{tag}.dat");
            AddFile(pass, passFile);
            AddFile(pass, passFile + ".tmp");
            AddIfAny(items, pass);

            // 4. File cá nhân
            string storageRoot = Path.Combine(Root, "Storage");
            string storageDir = Path.Combine(storageRoot, safe);
            if (Directory.Exists(storageDir) && IsDirectChild(storageRoot, storageDir))
            {
                var storage = new Item { Label = "File cá nhân", Dir = storageDir };
                AddDirStats(storage, storageDir);
                AddIfAny(items, storage);
            }

            // 5. Đăng nhập Google (token)
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

        /// <summary>Thay ký tự không hợp lệ trong tên file bằng '_' (khớp cách các service khác đặt tên).</summary>
        private static string SafeName(string username)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat((username ?? "").Select(c => invalid.Contains(c) ? '_' : c));
        }

        /// <summary>child phải là thư mục con TRỰC TIẾP của parent (chặn tên kiểu "..." trỏ ra ngoài).</summary>
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