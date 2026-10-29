using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PersonalInfoManager.Services
{















    public static class BackupCrypto
    {
        public const string Prefix = "v3p:";
        public const int MinPassphraseLength = 10;

        private const int SaltSize = 16;
        private const int IterationsSize = 4;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;

        private const int DefaultIterations = 600_000;
        private const int MinIterations = 100_000;
        private const int MaxIterations = 5_000_000;

        private static readonly string PassDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager");


        private static readonly string LegacyPassFile = Path.Combine(PassDir, "backup_pass.dat");






        public static bool IsPassphraseFormat(string text) =>
            text.TrimStart().StartsWith(Prefix, StringComparison.Ordinal);





        public static string Encrypt(string plainText, string passphrase)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] header = BuildHeader(salt, DefaultIterations);
            byte[] key = DeriveKey(passphrase, salt, DefaultIterations);
            byte[] plain = Encoding.UTF8.GetBytes(plainText);

            try
            {
                byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
                byte[] cipher = new byte[plain.Length];
                byte[] tag = new byte[TagSize];

                using (var gcm = new AesGcm(key, TagSize))
                {
                    gcm.Encrypt(nonce, plain, cipher, tag, header);
                }

                byte[] result = new byte[header.Length + NonceSize + TagSize + cipher.Length];
                Buffer.BlockCopy(header, 0, result, 0, header.Length);
                Buffer.BlockCopy(nonce, 0, result, header.Length, NonceSize);
                Buffer.BlockCopy(tag, 0, result, header.Length + NonceSize, TagSize);
                Buffer.BlockCopy(cipher, 0, result, header.Length + NonceSize + TagSize, cipher.Length);

                return Prefix + Convert.ToBase64String(result);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plain);
            }
        }





        public static string Decrypt(string text, string passphrase)
        {
            text = text.Trim();
            if (!text.StartsWith(Prefix, StringComparison.Ordinal))
                throw new FormatException("Không phải định dạng sao lưu có cụm mật khẩu.");

            byte[] data = Convert.FromBase64String(text.Substring(Prefix.Length));

            int headerLen = SaltSize + IterationsSize;
            if (data.Length < headerLen + NonceSize + TagSize)
                throw new CryptographicException("Dữ liệu mã hóa không hợp lệ.");

            ReadOnlySpan<byte> span = data;
            ReadOnlySpan<byte> header = span.Slice(0, headerLen);
            ReadOnlySpan<byte> salt = span.Slice(0, SaltSize);
            int iterations = BinaryPrimitives.ReadInt32BigEndian(span.Slice(SaltSize, IterationsSize));

            if (iterations < MinIterations || iterations > MaxIterations)
                throw new CryptographicException("Tham số mã hóa không hợp lệ.");

            ReadOnlySpan<byte> nonce = span.Slice(headerLen, NonceSize);
            ReadOnlySpan<byte> tag = span.Slice(headerLen + NonceSize, TagSize);
            ReadOnlySpan<byte> cipher = span.Slice(headerLen + NonceSize + TagSize);

            byte[] key = DeriveKey(passphrase, salt, iterations);
            byte[] plain = new byte[cipher.Length];

            try
            {
                using (var gcm = new AesGcm(key, TagSize))
                {
                    gcm.Decrypt(nonce, cipher, tag, plain, header);
                }

                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        private static byte[] BuildHeader(byte[] salt, int iterations)
        {
            byte[] header = new byte[SaltSize + IterationsSize];
            Buffer.BlockCopy(salt, 0, header, 0, SaltSize);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(SaltSize, IterationsSize), iterations);
            return header;
        }

        private static byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt, int iterations)
        {


            string normalized = passphrase.Normalize(NormalizationForm.FormC);

            return Rfc2898DeriveBytes.Pbkdf2(
                normalized.AsSpan(), salt, iterations, HashAlgorithmName.SHA256, KeySize);
        }







        private static readonly byte[] BinaryMagic = { (byte)'T', (byte)'Q', (byte)'F', (byte)'B', 1 };


        public static bool IsBinaryFormat(byte[] data)
        {
            if (data.Length < BinaryMagic.Length) return false;

            for (int i = 0; i < BinaryMagic.Length; i++)
                if (data[i] != BinaryMagic[i]) return false;

            return true;
        }




        public static byte[] EncryptBytes(ReadOnlySpan<byte> plain, string passphrase)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] header = BuildHeader(salt, DefaultIterations);
            byte[] key = DeriveKey(passphrase, salt, DefaultIterations);

            try
            {
                byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
                int prefixLen = BinaryMagic.Length + header.Length;
                byte[] result = new byte[prefixLen + NonceSize + TagSize + plain.Length];

                Buffer.BlockCopy(BinaryMagic, 0, result, 0, BinaryMagic.Length);
                Buffer.BlockCopy(header, 0, result, BinaryMagic.Length, header.Length);
                Buffer.BlockCopy(nonce, 0, result, prefixLen, NonceSize);

                Span<byte> tag = result.AsSpan(prefixLen + NonceSize, TagSize);
                Span<byte> cipher = result.AsSpan(prefixLen + NonceSize + TagSize, plain.Length);

                using (var gcm = new AesGcm(key, TagSize))
                {
                    gcm.Encrypt(nonce, plain, cipher, tag, result.AsSpan(0, prefixLen));
                }

                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }





        public static byte[] DecryptBytes(byte[] data, string passphrase)
        {
            if (!IsBinaryFormat(data))
                throw new CryptographicException("Không phải định dạng sao lưu nhị phân của TQLTK.");

            int headerLen = SaltSize + IterationsSize;
            int prefixLen = BinaryMagic.Length + headerLen;

            if (data.Length < prefixLen + NonceSize + TagSize)
                throw new CryptographicException("Dữ liệu mã hóa không hợp lệ.");

            ReadOnlySpan<byte> span = data;
            ReadOnlySpan<byte> aad = span.Slice(0, prefixLen);
            ReadOnlySpan<byte> salt = span.Slice(BinaryMagic.Length, SaltSize);
            int iterations = BinaryPrimitives.ReadInt32BigEndian(
                span.Slice(BinaryMagic.Length + SaltSize, IterationsSize));

            if (iterations < MinIterations || iterations > MaxIterations)
                throw new CryptographicException("Tham số mã hóa không hợp lệ.");

            ReadOnlySpan<byte> nonce = span.Slice(prefixLen, NonceSize);
            ReadOnlySpan<byte> tag = span.Slice(prefixLen + NonceSize, TagSize);
            ReadOnlySpan<byte> cipher = span.Slice(prefixLen + NonceSize + TagSize);

            byte[] key = DeriveKey(passphrase, salt, iterations);
            byte[] plain = new byte[cipher.Length];

            try
            {
                using (var gcm = new AesGcm(key, TagSize))
                {
                    gcm.Decrypt(nonce, cipher, tag, plain, aad);
                }

                return plain;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(plain);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }










        public static string UserTag(string username)
        {
            string normalized = (username ?? "").Trim().ToLowerInvariant();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return "u" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        }





        private static string PassFileFor(string username) =>
            Path.Combine(PassDir, $"backup_pass_{UserTag(username)}.dat");


        public static string? GetStoredPassphrase(string username) => ReadPassFile(PassFileFor(username));

        public static bool HasStoredPassphrase(string username) => GetStoredPassphrase(username) != null;

        public static void SaveStoredPassphrase(string username, string passphrase) =>
            WritePassFile(PassFileFor(username), passphrase);


        public static bool HasLegacyPassphrase() => File.Exists(LegacyPassFile);





        public static bool TryAdoptLegacyPassphrase(string username)
        {
            string? legacy = ReadPassFile(LegacyPassFile);
            if (legacy == null) return false;

            WritePassFile(PassFileFor(username), legacy);
            try { File.Delete(LegacyPassFile); } catch {   }
            return true;
        }

        private static string? ReadPassFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                byte[] protectedBytes = File.ReadAllBytes(path);
                byte[] raw = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);

                try { return Encoding.UTF8.GetString(raw); }
                finally { CryptographicOperations.ZeroMemory(raw); }
            }
            catch
            {
                return null;
            }
        }

        private static void WritePassFile(string path, string passphrase)
        {
            Directory.CreateDirectory(PassDir);

            byte[] raw = Encoding.UTF8.GetBytes(passphrase);
            try
            {
                byte[] protectedBytes = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);


                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, protectedBytes);
                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(raw);
            }
        }
    }
}