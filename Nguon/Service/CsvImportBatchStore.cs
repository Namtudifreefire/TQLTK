using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace PersonalInfoManager.Services;
public sealed class CsvImportBatchRecord
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<Guid> AccountIds { get; set; } = new();
}
public static class CsvImportBatchStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TQLTK.CsvImportUndo.v1");
    private static string GetPath(string username) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalInfoManager", "CsvImportUndo", BackupCrypto.UserTag(username) + ".dat");
    public static CsvImportBatchRecord? Load(string username)
    {
        string path = GetPath(username);
        if (!File.Exists(path)) return null;
        byte[] cipher = File.ReadAllBytes(path);
        if (cipher.Length is 0 or > 1024 * 1024)
            throw new InvalidDataException("Thông tin hoàn tác nhập CSV có kích thước không hợp lệ.");
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            var record = JsonSerializer.Deserialize<CsvImportBatchRecord>(plain);
            if (record == null || record.Id == Guid.Empty || record.AccountIds == null ||
                record.AccountIds.Count == 0 || record.AccountIds.Count > 100_000 ||
                record.AccountIds.Any(id => id == Guid.Empty) ||
                record.AccountIds.Distinct().Count() != record.AccountIds.Count)
                throw new InvalidDataException("Thông tin hoàn tác nhập CSV không hợp lệ.");
            return record;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cipher);
            if (plain != null) CryptographicOperations.ZeroMemory(plain);
        }
    }
    public static void Save(string username, CsvImportBatchRecord record)
    {
        if (record == null || record.Id == Guid.Empty || record.AccountIds == null ||
            record.AccountIds.Count == 0 || record.AccountIds.Count > 100_000 ||
            record.AccountIds.Any(id => id == Guid.Empty) ||
            record.AccountIds.Distinct().Count() != record.AccountIds.Count)
            throw new ArgumentException("Thông tin lô nhập CSV không hợp lệ.", nameof(record));
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(record);
        byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        try { AtomicFileService.WriteAllBytes(GetPath(username), cipher); }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(cipher);
        }
    }
    public static void Clear(string username)
    {
        string path = Path.GetFullPath(GetPath(username));
        string expectedParent = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "CsvImportUndo"));
        if (!string.Equals(Path.GetDirectoryName(path), expectedParent, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Đường dẫn trạng thái hoàn tác CSV không an toàn.");
        if (File.Exists(path)) File.Delete(path);
    }
}
