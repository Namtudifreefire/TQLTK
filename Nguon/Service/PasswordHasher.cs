using System;
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
        private const int Iterations = 100_000; // càng cao càng khó brute-force

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
                var parts = stored.Split('.');
                if (parts.Length != 3) return false;

                int iter = int.Parse(parts[0]);
                byte[] salt = Convert.FromBase64String(parts[1]);
                byte[] expected = Convert.FromBase64String(parts[2]);

                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    password: password,
                    salt: salt,
                    iterations: iter,
                    hashAlgorithm: HashAlgorithmName.SHA256,
                    outputLength: expected.Length);

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