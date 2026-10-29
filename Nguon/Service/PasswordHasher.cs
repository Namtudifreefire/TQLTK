using System;
using System.Globalization;
using System.Security.Cryptography;

namespace PersonalInfoManager.Services
{

    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 600_000;
        private const int MinAcceptedIterations = 100_000;
        private const int MaxAcceptedIterations = 1_000_000;

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

        public static bool Verify(string password, string stored)
        {
            try
            {
                if (string.IsNullOrEmpty(stored)) return false;

                var parts = stored.Split('.');
                if (parts.Length != 3) return false;

                if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int iter))
                    return false;

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

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }

        public static bool NeedsRehash(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return false;

            var parts = stored.Split('.');
            return parts.Length == 3 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations) &&
                iterations < Iterations;
        }
    }
}
