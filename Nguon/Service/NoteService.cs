using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Quản lý ghi chú cá nhân.
    /// Dữ liệu được mã hóa AES trước khi lưu xuống file.
    /// Vị trí: %AppData%\PersonalInfoManager\notes.json
    /// </summary>
    public class NoteService
    {
        private readonly string _dataFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "notes.json");

        /// <summary>
        /// Đọc danh sách ghi chú từ file (giải mã AES → JSON → List).
        /// </summary>
        public List<Note> Load()
        {
            if (!File.Exists(_dataFile)) return new List<Note>();

            try
            {
                string encrypted = File.ReadAllText(_dataFile);
                if (string.IsNullOrWhiteSpace(encrypted)) return new List<Note>();

                string json = AesCrypto.Decrypt(encrypted);
                return JsonSerializer.Deserialize<List<Note>>(json) ?? new List<Note>();
            }
            catch
            {
                // File hỏng, sai key, hoặc giải mã thất bại → trả về rỗng
                return new List<Note>();
            }
        }

        /// <summary>
        /// Lưu danh sách ghi chú (JSON → mã hóa AES → file).
        /// </summary>
        public void Save(List<Note> notes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dataFile)!);

            string json = JsonSerializer.Serialize(notes,
                new JsonSerializerOptions { WriteIndented = true });

            string encrypted = AesCrypto.Encrypt(json);
            File.WriteAllText(_dataFile, encrypted);
        }
    }
}