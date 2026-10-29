using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Quản lý ghi chú cá nhân.
    /// Dữ liệu được mã hóa AES-GCM trước khi lưu xuống file. Ghi chú CBC cũ không được mở;
    /// file gốc được giữ nguyên để không âm thầm chấp nhận dữ liệu chưa xác thực.
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

            return DecryptNotes(File.ReadAllText(_dataFile));
        }

        private static List<Note> DecryptNotes(string encrypted)
        {
            if (string.IsNullOrWhiteSpace(encrypted)) return new List<Note>();

            if (!AesCrypto.IsAuthenticatedV2Format(encrypted))
            {
                throw new CryptographicException(
                    "Ghi chú không dùng định dạng AES-GCM v2. CBC cũ không có kiểm tra toàn vẹn, " +
                    "nên ứng dụng từ chối mở và giữ nguyên file để phục hồi riêng.");
            }

            string json;
            try { json = AesCrypto.Decrypt(encrypted); }
            catch (FormatException ex)
            {
                throw new CryptographicException(
                    "Dữ liệu ghi chú AES-GCM không hợp lệ; file được giữ nguyên.", ex);
            }

            try
            {
                return JsonSerializer.Deserialize<List<Note>>(json)
                    ?? throw new CryptographicException("Nội dung ghi chú không đúng định dạng danh sách.");
            }
            catch (JsonException ex)
            {
                throw new CryptographicException(
                    "Nội dung ghi chú không hợp lệ; file được giữ nguyên.", ex);
            }
        }

        /// <summary>
        /// Lưu danh sách ghi chú (JSON → mã hóa AES → file).
        /// </summary>
        public void Save(List<Note> notes)
        {
            if (File.Exists(_dataFile))
            {
                string existing = File.ReadAllText(_dataFile);
                _ = DecryptNotes(existing);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_dataFile)!);

            string json = JsonSerializer.Serialize(notes,
                new JsonSerializerOptions { WriteIndented = true });

            string encrypted = AesCrypto.Encrypt(json);
            string temp = _dataFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, encrypted);
                File.Move(temp, _dataFile, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }
}
