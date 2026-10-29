using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager");

        private static readonly string KeyFile = Path.Combine(KeyDir, "key.dat");

        public static bool IsAuthenticatedV2Format(string cipherText)
            => cipherText.TrimStart().StartsWith(V2Prefix, StringComparison.Ordinal);

        private static byte[] GetOrCreateKey()
        {
            Directory.CreateDirectory(KeyDir);

            if (File.Exists(KeyFile))
            {
                byte[] protectedKey = File.ReadAllBytes(KeyFile);
                byte[] key = ProtectedData.Unprotect(protectedKey, null,
                    DataProtectionScope.CurrentUser);

                if (key.Length != KeySize)
                    throw new CryptographicException("key.dat không hợp lệ.");

                return key;
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

            string tmp = KeyFile + ".tmp";
            File.WriteAllBytes(tmp, protectedNew);
            File.Move(tmp, KeyFile, overwrite: true);

            return newKey;
        }

        public static string Encrypt(string plainText)
        {
            byte[] key = GetOrCreateKey();
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

            byte[] key = GetOrCreateKey();

            try
            {
                return DecryptV2(key, text.Substring(V2Prefix.Length));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

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
