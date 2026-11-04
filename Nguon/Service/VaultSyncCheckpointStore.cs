using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
using PersonalInfoManager.Models;
namespace PersonalInfoManager.Services;
public sealed class VaultSyncCheckpoint
{
    public string RemoteBackupId { get; set; } = "";
    public string LocalFingerprint { get; set; } = "";
    public DateTime LastSyncAtUtc { get; set; }
}
public static class VaultSyncCheckpointStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TQLTK.VaultDriveSync.v1");
    private static string GetPath(string username) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalInfoManager", "VaultDriveSync", BackupCrypto.UserTag(username) + ".dat");
    public static VaultSyncCheckpoint? Load(string username)
    {
        string path = GetPath(username);
        if (!File.Exists(path)) return null;
        byte[] cipher = File.ReadAllBytes(path);
        byte[]? plain = null;
        try
        {
            if (cipher.Length is 0 or > 16 * 1024) throw new InvalidDataException("Trạng thái đồng bộ Drive không hợp lệ.");
            plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            var state = JsonSerializer.Deserialize<VaultSyncCheckpoint>(plain);
            if (state == null || string.IsNullOrWhiteSpace(state.RemoteBackupId) ||
                string.IsNullOrWhiteSpace(state.LocalFingerprint) || state.LocalFingerprint.Length != 64 ||
                !state.LocalFingerprint.All(Uri.IsHexDigit) || state.LastSyncAtUtc == default)
                throw new InvalidDataException("Trạng thái đồng bộ Drive không hợp lệ.");
            return state;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cipher);
            if (plain != null) CryptographicOperations.ZeroMemory(plain);
        }
    }
    public static void Save(string username, VaultSyncCheckpoint state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.RemoteBackupId) ||
            string.IsNullOrWhiteSpace(state.LocalFingerprint) || state.LocalFingerprint.Length != 64 ||
            !state.LocalFingerprint.All(Uri.IsHexDigit))
            throw new ArgumentException("Trạng thái đồng bộ Drive không hợp lệ.", nameof(state));
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(state);
        byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        try { AtomicFileService.WriteAllBytes(GetPath(username), cipher); }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(cipher);
        }
    }
    public static string ComputeFingerprint(System.Collections.Generic.IEnumerable<SavedAccount> accounts)
    {
        var ordered = accounts.OrderBy(account => account.Id).ToList();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(ordered);
        try { return Convert.ToHexString(SHA256.HashData(json)); }
        finally { CryptographicOperations.ZeroMemory(json); }
    }
}
