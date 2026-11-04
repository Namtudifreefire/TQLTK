using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
namespace PersonalInfoManager.Services
{
    public static class AesCrypto
    {
        private const string V2Prefix = "v2:";
        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private static readonly string KeyDir = Path.Combine(
            ApplicationDataPath.Root,
            "PersonalInfoManager");
        private static readonly string KeyFile = Path.Combine(KeyDir, "key.dat");
        public static bool IsAuthenticatedV2Format(string cipherText)
            => cipherText.TrimStart().StartsWith(V2Prefix, StringComparison.Ordinal);
        internal static string StorageDirectory => KeyDir;
        internal static string StorageKeyFile => KeyFile;
        internal static IDisposable AcquireStorageLock()
        {
            string identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName.ToUpperInvariant();
            byte[] identityBytes = Encoding.UTF8.GetBytes(identity);
            byte[] identityHash = SHA256.HashData(identityBytes);
            string mutexName = @"Local\TQLTK-AesCrypto-" + Convert.ToHexString(identityHash);
            CryptographicOperations.ZeroMemory(identityBytes);
            CryptographicOperations.ZeroMemory(identityHash);
            var mutex = new Mutex(false, mutexName);
            try
            {
                try
                {
                    if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
                        throw new IOException("Đang có thao tác khóa vault khác; hãy thử lại sau.");
                }
                catch (AbandonedMutexException) { }
                return new MutexRelease(mutex);
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
        }
        private sealed class MutexRelease : IDisposable
        {
            private Mutex? _mutex;
            public MutexRelease(Mutex mutex) => _mutex = mutex;
            public void Dispose()
            {
                Mutex? mutex = Interlocked.Exchange(ref _mutex, null);
                if (mutex == null) return;
                try { mutex.ReleaseMutex(); } finally { mutex.Dispose(); }
            }
        }
        internal static byte[] GetOrCreateKeyUnderLock()
        {
            Directory.CreateDirectory(KeyDir);
            VaultKeyRotationService.RecoverIfNeeded(KeyDir);
            if (File.Exists(KeyFile))
            {
                byte[] protectedKey = File.ReadAllBytes(KeyFile);
                try
                {
                    byte[] key = ProtectedData.Unprotect(protectedKey, null,
                        DataProtectionScope.CurrentUser);
                    if (key.Length != KeySize)
                    {
                        CryptographicOperations.ZeroMemory(key);
                        throw new CryptographicException("key.dat không hợp lệ.");
                    }
                    return key;
                }
                finally { CryptographicOperations.ZeroMemory(protectedKey); }
            }
            bool hasVault = Directory.EnumerateFiles(KeyDir, "vault_*")
                .Any(f => f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));
            if (hasVault)
                throw new CryptographicException(
                    "Không tìm thấy key.dat nhưng vẫn còn dữ liệu vault_*.dat. " +
                    "Hãy khôi phục key.dat từ bản sao lưu. " +
                    "Ứng dụng không tự tạo key mới để tránh mất dữ liệu.");
            byte[] newKey = RandomNumberGenerator.GetBytes(KeySize);
            byte[] protectedNew = ProtectedData.Protect(newKey, null,
                DataProtectionScope.CurrentUser);
            try
            {
                AtomicFileService.WriteAllBytes(KeyFile, protectedNew);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedNew);
            }
            return newKey;
        }
        public static string Encrypt(string plainText)
        {
            using var storageLock = AcquireStorageLock();
            byte[] key = GetOrCreateKeyUnderLock();
            byte[] plain = Encoding.UTF8.GetBytes(plainText);
            try
            {
                byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
                byte[] cipher = new byte[plain.Length];
                byte[] tag = new byte[TagSize];
                using (var gcm = new AesGcm(key, TagSize))
                {
                    gcm.Encrypt(nonce, plain, cipher, tag);
                }
                byte[] result = new byte[NonceSize + TagSize + cipher.Length];
                Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
                Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
                Buffer.BlockCopy(cipher, 0, result, NonceSize + TagSize, cipher.Length);
                return V2Prefix + Convert.ToBase64String(result);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        public static string Decrypt(string cipherText)
        {
            string text = cipherText.Trim();
            if (!IsAuthenticatedV2Format(text))
            {
                throw new CryptographicException(
                    "Định dạng CBC cũ hoặc định dạng không được hỗ trợ không thể được mở an toàn. " +
                    "File gốc được giữ nguyên để phục hồi riêng.");
            }
            using var storageLock = AcquireStorageLock();
            byte[] key = GetOrCreateKeyUnderLock();
            try { return DecryptWithAvailableKeys(text.Substring(V2Prefix.Length), key); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        internal static byte[] ProtectKey(byte[] key) => ProtectedData.Protect(
            key, null, DataProtectionScope.CurrentUser);
        internal static byte[] UnprotectKey(byte[] protectedKey) => ProtectedData.Unprotect(
            protectedKey, null, DataProtectionScope.CurrentUser);
        internal static byte[] EncryptWithKey(byte[] key, string plainText)
        {
            byte[] plain = Encoding.UTF8.GetBytes(plainText);
            try
            {
                byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
                byte[] cipher = new byte[plain.Length];
                byte[] tag = new byte[TagSize];
                using (var gcm = new AesGcm(key, TagSize))
                    gcm.Encrypt(nonce, plain, cipher, tag);
                byte[] result = new byte[NonceSize + TagSize + cipher.Length];
                Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
                Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
                Buffer.BlockCopy(cipher, 0, result, NonceSize + TagSize, cipher.Length);
                return Encoding.ASCII.GetBytes(V2Prefix + Convert.ToBase64String(result));
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        internal static string DecryptWithKey(byte[] key, string cipherText)
        {
            if (!IsAuthenticatedV2Format(cipherText))
                throw new CryptographicException("Định dạng mã hóa không được hỗ trợ.");
            return DecryptV2(key, cipherText.Trim().Substring(V2Prefix.Length));
        }
        private static string DecryptWithAvailableKeys(string base64, byte[] currentKey)
        {
            try { return DecryptV2(currentKey, base64); }
            catch (CryptographicException)
            {
                string historyDirectory = Path.Combine(KeyDir, "key-history");
                if (!Directory.Exists(historyDirectory)) throw;
                foreach (string keyPath in Directory.EnumerateFiles(historyDirectory, "*.dat"))
                {
                    byte[]? oldKey = null;
                    try
                    {
                        oldKey = UnprotectKey(File.ReadAllBytes(keyPath));
                        if (oldKey.Length != KeySize) continue;
                        return DecryptV2(oldKey, base64);
                    }
                    catch (CryptographicException) { }
                    catch (IOException) { }
                    finally
                    {
                        if (oldKey != null) CryptographicOperations.ZeroMemory(oldKey);
                    }
                }
                throw new CryptographicException(
                    "Không thể giải mã bằng khóa hiện tại hoặc khóa lịch sử. File được giữ nguyên để khôi phục.");
            }
        }
        internal static string DecryptWithAvailableKeysUnderLock(string base64, byte[] currentKey) =>
            DecryptWithAvailableKeys(base64, currentKey);
        private static string DecryptV2(byte[] key, string base64)
        {
            byte[] data = Convert.FromBase64String(base64);
            if (data.Length < NonceSize + TagSize)
                throw new CryptographicException("Dữ liệu mã hóa không hợp lệ.");
            ReadOnlySpan<byte> span = data;
            ReadOnlySpan<byte> nonce = span.Slice(0, NonceSize);
            ReadOnlySpan<byte> tag = span.Slice(NonceSize, TagSize);
            ReadOnlySpan<byte> cipher = span.Slice(NonceSize + TagSize);
            byte[] plain = new byte[cipher.Length];
            try
            {
                using (var gcm = new AesGcm(key, TagSize))
                {
                    gcm.Decrypt(nonce, cipher, tag, plain);
                }
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }
}
