using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Mã hóa / giải mã dữ liệu vault.
    /// - Định dạng mới (v2): AES-256-GCM, có kiểm tra toàn vẹn. Chuỗi bắt đầu bằng "v2:".
    /// - Định dạng cũ: AES-256-CBC (IV 16 byte + cipher, Base64). Vẫn đọc được để không mất dữ liệu cũ;
    ///   vault và ghi chú cũ được ghi lại bằng AES-GCM ngay lần đọc thành công đầu tiên.
    /// Key sinh ngẫu nhiên, bảo vệ bằng DPAPI (gắn với tài khoản Windows hiện tại).
    /// Vị trí key: %AppData%\PersonalInfoManager\key.dat
    /// LƯU Ý: không đổi tham số DPAPI (entropy/scope), nếu không key.dat cũ sẽ không đọc được.
    /// </summary>
    public static class AesCrypto
    {
        private const string V2Prefix = "v2:";
        private const int KeySize = 32;        // AES-256
        private const int NonceSize = 12;      // GCM
        private const int TagSize = 16;        // GCM
        private const int LegacyIvSize = 16;   // CBC cũ

        private static readonly string KeyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager");

        private static readonly string KeyFile = Path.Combine(KeyDir, "key.dat");

        /// <summary>True when stored vault data uses the legacy unauthenticated CBC format.</summary>
        public static bool IsLegacyCbcFormat(string cipherText)
            => !cipherText.TrimStart().StartsWith(V2Prefix, StringComparison.Ordinal);

        /// <summary>
        /// Lấy key AES hiện có, hoặc tạo mới nếu đây là lần chạy đầu tiên.
        /// Nếu key.dat mất nhưng vẫn còn file vault_*.dat thì KHÔNG tạo key mới
        /// (key mới sẽ không giải mã được dữ liệu cũ và dữ liệu có thể bị ghi đè).
        /// </summary>
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

            // Ghi qua file tạm rồi đổi tên, tránh để lại key.dat ghi dở
            string tmp = KeyFile + ".tmp";
            File.WriteAllBytes(tmp, protectedNew);
            File.Move(tmp, KeyFile, overwrite: true);

            return newKey;
        }

        /// <summary>
        /// Mã hóa chuỗi plain text → "v2:" + Base64(nonce 12 + tag 16 + cipher).
        /// </summary>
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

        /// <summary>
        /// Giải mã chuỗi do Encrypt tạo ra (v2), hoặc định dạng CBC cũ.
        /// Ném CryptographicException / FormatException nếu dữ liệu hỏng hoặc sai key.
        /// </summary>
        public static string Decrypt(string cipherText)
        {
            string text = cipherText.Trim();
            byte[] key = GetOrCreateKey();

            try
            {
                if (text.StartsWith(V2Prefix, StringComparison.Ordinal))
                    return DecryptV2(key, text.Substring(V2Prefix.Length));

                return DecryptLegacyCbc(key, text);
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
                    gcm.Decrypt(nonce, cipher, tag, plain); // sai key / bị sửa → ném lỗi
                }

                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        private static string DecryptLegacyCbc(byte[] key, string base64)
        {
            byte[] data = Convert.FromBase64String(base64);

            // IV (16) + ít nhất 1 block (16), và phần cipher phải chia hết cho 16
            if (data.Length < LegacyIvSize * 2 || (data.Length - LegacyIvSize) % 16 != 0)
                throw new CryptographicException("Dữ liệu mã hóa không hợp lệ.");

            byte[] iv = data[..LegacyIvSize];
            byte[] cipher = data[LegacyIvSize..];

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            byte[] plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }
}
