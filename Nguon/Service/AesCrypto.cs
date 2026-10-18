using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Mã hóa / giải mã AES-256.
    /// Key được sinh ngẫu nhiên và bảo vệ bằng DPAPI (gắn với tài khoản Windows hiện tại).
    /// Vị trí key: %AppData%\PersonalInfoManager\key.dat
    /// </summary>
    public static class AesCrypto
    {
        private static readonly string KeyFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "key.dat");

        /// <summary>
        /// Lấy key AES hiện có, hoặc tạo mới nếu chưa có.
        /// Key được bảo vệ bằng DPAPI (chỉ user hiện tại giải mã được).
        /// </summary>
        private static byte[] GetOrCreateKey()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyFile)!);

            if (File.Exists(KeyFile))
            {
                // Key đã bị mã hóa bằng DPAPI → giải mã
                byte[] protectedKey = File.ReadAllBytes(KeyFile);
                return ProtectedData.Unprotect(protectedKey, null,
                    DataProtectionScope.CurrentUser);
            }

            // Tạo key mới 32 byte (AES-256) và bảo vệ bằng DPAPI
            byte[] newKey = RandomNumberGenerator.GetBytes(32);
            byte[] protectedNew = ProtectedData.Protect(newKey, null,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(KeyFile, protectedNew);
            return newKey;
        }

        /// <summary>
        /// Mã hóa chuỗi plain text → Base64 (đã gộp IV + cipher).
        /// </summary>
        public static string Encrypt(string plainText)
        {
            byte[] key = GetOrCreateKey();

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            byte[] plain = Encoding.UTF8.GetBytes(plainText);
            byte[] cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

            // Ghép IV (16 byte) + cipher → Base64
            byte[] result = new byte[aes.IV.Length + cipher.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);

            return Convert.ToBase64String(result);
        }

        /// <summary>
        /// Giải mã chuỗi Base64 (gồm IV + cipher) trở lại plain text.
        /// </summary>
        public static string Decrypt(string cipherBase64)
        {
            byte[] key = GetOrCreateKey();
            byte[] data = Convert.FromBase64String(cipherBase64);

            if (data.Length < 16)
                throw new CryptographicException("Dữ liệu mã hóa không hợp lệ.");

            using var aes = Aes.Create();
            aes.Key = key;

            // Tách IV và cipher
            byte[] iv = new byte[16];
            byte[] cipher = new byte[data.Length - 16];
            Buffer.BlockCopy(data, 0, iv, 0, 16);
            Buffer.BlockCopy(data, 16, cipher, 0, cipher.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            byte[] plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plain);
        }
    }
}