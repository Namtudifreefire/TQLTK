using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Util.Store;

namespace PersonalInfoManager.Services
{

    public sealed class DpapiDataStore : IDataStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TQLTK.GoogleTokens.v1");
        private readonly string _folder;

        public DpapiDataStore(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("Token folder is required.", nameof(folder));

            _folder = folder;
        }

        public async Task StoreAsync<T>(string key, T value)
        {
            Directory.CreateDirectory(_folder);
            string path = GetPath(key);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(value);

            try
            {
                byte[] encrypted = ProtectedData.Protect(
                    plaintext, Entropy, DataProtectionScope.CurrentUser);

                try
                {
                    await File.WriteAllBytesAsync(temp, encrypted).ConfigureAwait(false);
                    File.Move(temp, path, overwrite: true);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(encrypted);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        public async Task<T> GetAsync<T>(string key)
        {
            string path = GetPath(key);
            if (!File.Exists(path)) return default!;

            byte[] encrypted = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            byte[]? plaintext = null;
            try
            {
                plaintext = ProtectedData.Unprotect(
                    encrypted, Entropy, DataProtectionScope.CurrentUser);

                return JsonSerializer.Deserialize<T>(plaintext)!;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encrypted);
                if (plaintext != null) CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        public Task DeleteAsync<T>(string key)
        {
            string path = GetPath(key);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            if (Directory.Exists(_folder))
            {
                foreach (string file in Directory.EnumerateFiles(_folder, "*.bin"))
                    File.Delete(file);
            }

            return Task.CompletedTask;
        }

        private string GetPath(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Token key is required.", nameof(key));

            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] hash = SHA256.HashData(keyBytes);
            try
            {
                return Path.Combine(_folder, Convert.ToHexString(hash) + ".bin");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
                CryptographicOperations.ZeroMemory(hash);
            }
        }
    }
}
