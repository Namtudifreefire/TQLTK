using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
namespace PersonalInfoManager.Services
{
    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Pbkdf2Iterations = 600_000;
        private const int MinAcceptedIterations = 100_000;
        private const int MaxAcceptedIterations = 1_000_000;
        private const int ArgonVersion = 19;
        private const int ArgonMemoryKiB = 19_456;
        private const int ArgonIterations = 2;
        private const int ArgonParallelism = 1;
        public static string Hash(string password) => HashForCurrentPolicy(password);
        public static string HashForCurrentPolicy(string password)
        {
            if (RegistryService.GetFipsCompatiblePasswordHash())
                return HashPbkdf2Fips(password);
            return HashArgon2id(password);
        }
        public static string HashPbkdf2Fips(string password)
        {
            ArgumentNullException.ThrowIfNull(password);
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations,
                HashAlgorithmName.SHA256, HashSize);
            try
            {
                return $"pbkdf2-sha256$i={Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(hash);
            }
        }
        private static string HashArgon2id(string password)
        {
            ArgumentNullException.ThrowIfNull(password);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[]? hash = null;
            try
            {
                using var argon2 = new Argon2id(passwordBytes)
                {
                    Salt = salt,
                    DegreeOfParallelism = ArgonParallelism,
                    Iterations = ArgonIterations,
                    MemorySize = ArgonMemoryKiB
                };
                hash = argon2.GetBytes(HashSize);
                return $"argon2id$v={ArgonVersion}$m={ArgonMemoryKiB},t={ArgonIterations},p={ArgonParallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
                CryptographicOperations.ZeroMemory(salt);
                if (hash != null) CryptographicOperations.ZeroMemory(hash);
            }
        }
#if SECURITY_TEST
        internal static string HashArgon2idForTesting(string password) => HashArgon2id(password);
#endif
        public static bool Verify(string password, string stored)
        {
            try
            {
                if (string.IsNullOrEmpty(stored)) return false;
                if (stored.StartsWith("argon2id$", StringComparison.Ordinal))
                    return VerifyArgon2id(password, stored);
                if (stored.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal))
                    return VerifyVersionedPbkdf2(password, stored);
                return VerifyLegacyPbkdf2(password, stored);
            }
            catch
            {
                return false;
            }
        }
        public static bool NeedsRehash(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return false;
            if (stored.StartsWith("argon2id$", StringComparison.Ordinal))
            {
                string expected = $"argon2id$v={ArgonVersion}$m={ArgonMemoryKiB},t={ArgonIterations},p={ArgonParallelism}$";
                return !stored.StartsWith(expected, StringComparison.Ordinal) ||
                    RegistryService.GetFipsCompatiblePasswordHash();
            }
            if (stored.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal))
            {
                string[] versioned = stored.Split('$');
                if (versioned.Length != 4 || !versioned[1].StartsWith("i=", StringComparison.Ordinal) ||
                    !int.TryParse(versioned[1].AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int versionIterations))
                    return true;
                return !RegistryService.GetFipsCompatiblePasswordHash() || versionIterations != Pbkdf2Iterations;
            }
            string[] parts = stored.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None,
                    CultureInfo.InvariantCulture, out _))
                return false;
            return true;
        }
        private static bool VerifyArgon2id(string password, string stored)
        {
            string[] parts = stored.Split('$');
            if (parts.Length != 5 || parts[0] != "argon2id" || parts[1] != $"v={ArgonVersion}" ||
                parts[2] != $"m={ArgonMemoryKiB},t={ArgonIterations},p={ArgonParallelism}")
                return false;
            byte[] salt = Convert.FromBase64String(parts[3]);
            byte[] expected = Convert.FromBase64String(parts[4]);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[]? actual = null;
            try
            {
                if (salt.Length != SaltSize || expected.Length != HashSize) return false;
                using var argon2 = new Argon2id(passwordBytes)
                {
                    Salt = salt,
                    DegreeOfParallelism = ArgonParallelism,
                    Iterations = ArgonIterations,
                    MemorySize = ArgonMemoryKiB
                };
                actual = argon2.GetBytes(HashSize);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(expected);
                CryptographicOperations.ZeroMemory(passwordBytes);
                if (actual != null) CryptographicOperations.ZeroMemory(actual);
            }
        }
        private static bool VerifyVersionedPbkdf2(string password, string stored)
        {
            string[] parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" ||
                !parts[1].StartsWith("i=", StringComparison.Ordinal) ||
                !int.TryParse(parts[1].AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int iterations) ||
                iterations is < MinAcceptedIterations or > MaxAcceptedIterations)
                return false;
            return VerifyPbkdf2(password, parts[2], parts[3], iterations);
        }
        private static bool VerifyLegacyPbkdf2(string password, string stored)
        {
            string[] parts = stored.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int iterations) ||
                iterations is < MinAcceptedIterations or > MaxAcceptedIterations)
                return false;
            return VerifyPbkdf2(password, parts[1], parts[2], iterations);
        }
        private static bool VerifyPbkdf2(string password, string saltText, string hashText, int iterations)
        {
            byte[] salt = Convert.FromBase64String(saltText);
            byte[] expected = Convert.FromBase64String(hashText);
            byte[]? actual = null;
            try
            {
                if (salt.Length != SaltSize || expected.Length != HashSize) return false;
                actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations,
                    HashAlgorithmName.SHA256, HashSize);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(expected);
                if (actual != null) CryptographicOperations.ZeroMemory(actual);
            }
        }
    }
}
