using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Quản lý danh sách tài khoản đã lưu của từng user.
    /// Toàn bộ dữ liệu được mã hóa AES trước khi lưu xuống file.
    /// Vị trí: %AppData%\PersonalInfoManager\vault_<username>.dat
    /// </summary>
    public class AccountVaultService
    {
        private readonly string _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager");

        private string GetVaultFile(string username)
        {
            string safe = string.Join("_",
                username.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_root, $"vault_{safe}.dat");
        }

        public List<SavedAccount> Load(string username)
        {
            string file = GetVaultFile(username);
            if (!File.Exists(file)) return new List<SavedAccount>();

            try
            {
                string encrypted = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(encrypted)) return new List<SavedAccount>();

                string json = AesCrypto.Decrypt(encrypted);
                return JsonSerializer.Deserialize<List<SavedAccount>>(json)
                       ?? new List<SavedAccount>();
            }
            catch
            {
                return new List<SavedAccount>();
            }
        }

        public void Save(string username, List<SavedAccount> accounts)
        {
            Directory.CreateDirectory(_root);
            string json = JsonSerializer.Serialize(accounts,
                new JsonSerializerOptions { WriteIndented = true });
            string encrypted = AesCrypto.Encrypt(json);
            File.WriteAllText(GetVaultFile(username), encrypted);
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