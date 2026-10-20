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

    public class AccountVaultService
    {
        private static readonly JsonSerializerOptions JsonOpts =
            new JsonSerializerOptions { WriteIndented = true };

        private readonly string _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager");

        private string GetVaultFile(string username)
        {
            string safe = string.Join("_",
                username.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_root, $"vault_{safe}.dat");
        }

        /// <summary>
        /// Đọc và giải mã một file vault. Trả về null nếu file không tồn tại hoặc không giải mã được
        /// (lý do nằm trong "error"). Lỗi I/O (file đang bị khóa...) vẫn được ném ra ngoài.
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
        /// Đọc danh sách tài khoản. Chưa có file → danh sách rỗng.
        /// File có nhưng không đọc được → thử bản .bak, nếu vẫn không được thì ném VaultUnreadableException.
        /// </summary>
        public List<SavedAccount> Load(string username)
        {
            string file = GetVaultFile(username);
            if (!File.Exists(file)) return new List<SavedAccount>();

            var accounts = TryRead(file, out Exception? mainError);
            if (accounts != null) return accounts;

            // File chính hỏng → thử bản dự phòng gần nhất
            var fromBackup = TryRead(file + ".bak", out _);
            if (fromBackup != null) return fromBackup;

            throw new VaultUnreadableException(
                "Không đọc được dữ liệu đã lưu. Dữ liệu cũ vẫn được giữ nguyên, " +
                "đừng xóa thư mục %AppData%\\PersonalInfoManager. " +
                (mainError != null ? "Chi tiết: " + mainError.Message : ""),
                mainError);
        }

        /// <summary>
        /// Ghi danh sách tài khoản. Ghi qua file tạm, giữ bản .bak của lần ghi trước,
        /// và kiểm tra giải mã lại được trước khi thay file chính.
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

            string tmp = file + ".tmp";
            File.WriteAllText(tmp, encrypted);

            if (File.Exists(file))
            {
                if (TryRead(file, out _) != null)
                {
                    // File hiện tại đọc được → lưu làm bản dự phòng
                    File.Copy(file, file + ".bak", overwrite: true);
                }
                else
                {
                    // File hiện tại không đọc được → giữ lại một bản, không để mất hẳn
                    File.Copy(file,
                        file + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
                        overwrite: true);
                }
            }

            File.Move(tmp, file, overwrite: true);
        }

        public List<SavedAccount> Search(string username, string keyword)
        {
            var all = Load(username);
            if (string.IsNullOrWhiteSpace(keyword)) return all;

            keyword = keyword.Trim().ToLower();
            return all.Where(a =>
                (a.ServiceName ?? "").ToLower().Contains(keyword) ||
                (a.Username ?? "").ToLower().Contains(keyword) ||
                (a.Note ?? "").ToLower().Contains(keyword)
            ).ToList();
        }

        public int Count(string username) => Load(username).Count;
    }
}