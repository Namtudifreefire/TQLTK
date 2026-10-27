using System;
using System.Globalization;
using System.Security.Cryptography;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Hash mật khẩu bằng PBKDF2 (SHA256, 100.000 vòng lặp).
    /// Định dạng lưu: iterations.salt.hash (Base64)
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;        // 128-bit
        private const int HashSize = 32;        // 256-bit
        private const int Iterations = 100_000; // cấu hình hiện tại, giữ tương thích hash cũ
        private const int MinAcceptedIterations = 100_000;
        private const int MaxAcceptedIterations = 1_000_000;

        /// <summary>
        /// Tạo hash từ mật khẩu plain text.
        /// </summary>
        public static string Hash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password: password,
                salt: salt,
                iterations: Iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: HashSize);

            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// Kiểm tra mật khẩu có khớp với hash đã lưu không.
        /// </summary>
        public static bool Verify(string password, string stored)
        {
            try
            {
                if (string.IsNullOrEmpty(stored)) return false;

                var parts = stored.Split('.');
                if (parts.Length != 3) return false;

                if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int iter))
                    return false;

                // Reject corrupted/tampered parameters before running an expensive KDF.
                if (iter < MinAcceptedIterations || iter > MaxAcceptedIterations)
                    return false;

                byte[] salt = Convert.FromBase64String(parts[1]);
                byte[] expected = Convert.FromBase64String(parts[2]);
                if (salt.Length != SaltSize || expected.Length != HashSize)
                    return false;

                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    password: password,
                    salt: salt,
                    iterations: iter,
                    hashAlgorithm: HashAlgorithmName.SHA256,
                    outputLength: HashSize);

                // So sánh an toàn (chống timing attack)
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }
    }
}