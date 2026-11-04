using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace PersonalInfoManager.Services
{
    internal static class LocalFileEncryptionService
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TQLTKF01");
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TQLTK.LocalFiles.v1");
        private const int FixedHeaderSize = 8 + 8 + 8 + 4;
        private const int KeySize = 32;
        private const int NoncePrefixSize = 8;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int ChunkSize = 1024 * 1024;
        private const int MaxWrappedKeySize = 4096;
        public static bool IsEncryptedFile(string path)
        {
            if (!File.Exists(path)) return false;
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            Span<byte> prefix = stackalloc byte[Magic.Length];
            int read = ReadAtMost(input, prefix);
            return read == Magic.Length && prefix.SequenceEqual(Magic);
        }
        public static long GetPlaintextLength(string path)
        {
            if (!IsEncryptedFile(path)) return new FileInfo(path).Length;
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using HeaderContext header = ReadAuthenticatedHeader(input);
            return header.PlaintextLength;
        }
        public static void EncryptFileAtomic(
            string sourcePath, string destinationPath, bool overwrite,
            CancellationToken cancellationToken = default)
        {
            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(sourcePath);
            string fullDestination = Path.GetFullPath(destinationPath);
            string directory = Path.GetDirectoryName(fullDestination)!;
            Directory.CreateDirectory(directory);
            string tempPath = Path.Combine(directory, ".tqltk-encrypted-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                EncryptFile(sourcePath, tempPath, cancellationToken);
                VerifyEncryptedFile(tempPath, cancellationToken);
                File.Move(tempPath, fullDestination, overwrite);
                File.SetLastWriteTimeUtc(fullDestination, lastWriteUtc);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
        public static void EncryptStreamAtomic(Stream plaintext, string destinationPath, bool overwrite)
        {
            if (!plaintext.CanSeek)
                throw new ArgumentException("Luồng dữ liệu phải hỗ trợ đọc độ dài.", nameof(plaintext));
            EncryptStreamAtomic(plaintext, plaintext.Length - plaintext.Position, destinationPath, overwrite);
        }
        public static void EncryptStreamAtomic(
            Stream plaintext, long plaintextLength, string destinationPath, bool overwrite,
            DateTime? lastWriteTimeUtc = null, CancellationToken cancellationToken = default)
        {
            if (!plaintext.CanRead)
                throw new ArgumentException("Luồng dữ liệu không thể đọc.", nameof(plaintext));
            string fullDestination = Path.GetFullPath(destinationPath);
            string directory = Path.GetDirectoryName(fullDestination)!;
            Directory.CreateDirectory(directory);
            string tempPath = Path.Combine(directory, ".tqltk-encrypted-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    EncryptStream(plaintext, output, plaintextLength, cancellationToken);
                    output.Flush(flushToDisk: true);
                }
                VerifyEncryptedFile(tempPath, cancellationToken);
                File.Move(tempPath, fullDestination, overwrite);
                if (lastWriteTimeUtc.HasValue)
                    File.SetLastWriteTimeUtc(fullDestination, lastWriteTimeUtc.Value);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
        public static void DecryptFileAtomic(string sourcePath, string destinationPath, bool overwrite)
        {
            string fullSource = Path.GetFullPath(sourcePath);
            string fullDestination = Path.GetFullPath(destinationPath);
            if (string.Equals(fullSource, fullDestination, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Không thể xuất file đè lên bản mã hóa trong kho.");
            string directory = Path.GetDirectoryName(fullDestination)!;
            Directory.CreateDirectory(directory);
            string tempPath = Path.Combine(directory, ".tqltk-decrypted-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var input = new FileStream(fullSource, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    DecryptStream(input, output);
                    output.Flush(flushToDisk: true);
                }
                File.Move(tempPath, fullDestination, overwrite);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
        public static void CopyToPlaintext(string sourcePath, Stream destination)
        {
            using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (HasMagic(input))
            {
                input.Position = 0;
                DecryptStream(input, destination);
            }
            else
            {
                input.CopyTo(destination);
            }
        }
        public static bool ContentEqualsStored(
            string storedPath, Stream candidate, long candidateLength,
            CancellationToken cancellationToken = default)
        {
            if (candidateLength < 0) return false;
            using var input = new FileStream(storedPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using HeaderContext header = ReadAuthenticatedHeader(input);
            if (header.PlaintextLength != candidateLength) return false;
            var comparer = new ComparingWriteStream(candidate);
            DecryptStream(input, comparer, cancellationToken);
            return comparer.Matches && comparer.IsCandidateFullyConsumed();
        }
        public static bool ContentEqualsReadOnly(
            string storedPath, Stream candidate, long candidateLength,
            CancellationToken cancellationToken = default)
        {
            if (candidateLength < 0 || !File.Exists(storedPath)) return false;
            if (IsEncryptedFile(storedPath))
                return ContentEqualsStored(storedPath, candidate, candidateLength, cancellationToken);
            var info = new FileInfo(storedPath);
            if (info.Length != candidateLength) return false;
            using var input = new FileStream(storedPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var comparer = new ComparingWriteStream(candidate);
            byte[] buffer = new byte[81920];
            try
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    comparer.Write(buffer, 0, read);
                }
                return comparer.Matches && comparer.IsCandidateFullyConsumed();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
        private static void EncryptFile(
            string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            EncryptStream(input, output, input.Length - input.Position, cancellationToken);
            output.Flush(flushToDisk: true);
        }
        private static void EncryptStream(Stream plaintext, Stream encrypted)
        {
            if (!plaintext.CanRead || !plaintext.CanSeek)
                throw new ArgumentException("Luồng dữ liệu phải hỗ trợ đọc và tìm độ dài.", nameof(plaintext));
            long plaintextLength = plaintext.Length - plaintext.Position;
            EncryptStream(plaintext, encrypted, plaintextLength, CancellationToken.None);
        }
        private static void EncryptStream(
            Stream plaintext, Stream encrypted, long plaintextLength, CancellationToken cancellationToken)
        {
            if (!plaintext.CanRead)
                throw new ArgumentException("Luồng dữ liệu không thể đọc.", nameof(plaintext));
            if (plaintextLength < 0) throw new InvalidDataException("Độ dài file không hợp lệ.");
            if (plaintext.CanSeek && plaintext.Length - plaintext.Position != plaintextLength)
                throw new InvalidDataException("Độ dài luồng nguồn đã thay đổi.");
            long chunkCount = GetChunkCount(plaintextLength);
            if (chunkCount > uint.MaxValue - 1L)
                throw new InvalidDataException("File quá lớn để mã hóa an toàn.");
            byte[] key = RandomNumberGenerator.GetBytes(KeySize);
            byte[] wrappedKey = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
            byte[] noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
            byte[] header = BuildHeader(plaintextLength, noncePrefix, wrappedKey);
            byte[] headerTag = new byte[TagSize];
            byte[] nonce = new byte[NonceSize];
            byte[] plaintextBuffer = new byte[ChunkSize];
            byte[] ciphertextBuffer = new byte[ChunkSize];
            byte[] tag = new byte[TagSize];
            try
            {
                using var gcm = new AesGcm(key, TagSize);
                BuildNonce(noncePrefix, 0, nonce);
                gcm.Encrypt(nonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, headerTag, header);
                encrypted.Write(header);
                encrypted.Write(headerTag);
                long remaining = plaintextLength;
                uint chunkIndex = 1;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(ChunkSize, remaining);
                    plaintext.ReadExactly(plaintextBuffer.AsSpan(0, count));
                    byte[] aad = BuildChunkAad(header, chunkIndex, count);
                    try
                    {
                        BuildNonce(noncePrefix, chunkIndex, nonce);
                        gcm.Encrypt(nonce, plaintextBuffer.AsSpan(0, count),
                            ciphertextBuffer.AsSpan(0, count), tag, aad);
                        encrypted.Write(ciphertextBuffer, 0, count);
                        encrypted.Write(tag);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(aad);
                        CryptographicOperations.ZeroMemory(plaintextBuffer.AsSpan(0, count));
                        CryptographicOperations.ZeroMemory(ciphertextBuffer.AsSpan(0, count));
                        CryptographicOperations.ZeroMemory(tag);
                    }
                    remaining -= count;
                    chunkIndex++;
                }
                if (plaintext.ReadByte() != -1)
                    throw new IOException("File nguồn đã thay đổi trong lúc mã hóa.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(headerTag);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(plaintextBuffer);
                CryptographicOperations.ZeroMemory(ciphertextBuffer);
                CryptographicOperations.ZeroMemory(tag);
            }
        }
        private static void DecryptStream(
            Stream encrypted, Stream plaintext, CancellationToken cancellationToken = default)
        {
            using HeaderContext header = ReadAuthenticatedHeader(encrypted);
            byte[] nonce = new byte[NonceSize];
            byte[] ciphertextBuffer = new byte[ChunkSize];
            byte[] plaintextBuffer = new byte[ChunkSize];
            byte[] tag = new byte[TagSize];
            try
            {
                using var gcm = new AesGcm(header.Key, TagSize);
                long remaining = header.PlaintextLength;
                uint chunkIndex = 1;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(ChunkSize, remaining);
                    encrypted.ReadExactly(ciphertextBuffer.AsSpan(0, count));
                    encrypted.ReadExactly(tag);
                    byte[] aad = BuildChunkAad(header.AuthenticatedHeader, chunkIndex, count);
                    try
                    {
                        BuildNonce(header.NoncePrefix, chunkIndex, nonce);
                        gcm.Decrypt(nonce, ciphertextBuffer.AsSpan(0, count), tag,
                            plaintextBuffer.AsSpan(0, count), aad);
                        plaintext.Write(plaintextBuffer, 0, count);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(aad);
                        CryptographicOperations.ZeroMemory(ciphertextBuffer.AsSpan(0, count));
                        CryptographicOperations.ZeroMemory(plaintextBuffer.AsSpan(0, count));
                        CryptographicOperations.ZeroMemory(tag);
                    }
                    remaining -= count;
                    chunkIndex++;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(ciphertextBuffer);
                CryptographicOperations.ZeroMemory(plaintextBuffer);
                CryptographicOperations.ZeroMemory(tag);
            }
        }
        private static void VerifyEncryptedFile(string path, CancellationToken cancellationToken)
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            DecryptStream(input, Stream.Null, cancellationToken);
        }
        private static HeaderContext ReadAuthenticatedHeader(Stream input)
        {
            if (!input.CanRead || !input.CanSeek)
                throw new ArgumentException("File mã hóa không hỗ trợ đọc hoặc tìm kích thước.", nameof(input));
            input.Position = 0;
            byte[] fixedHeader = new byte[FixedHeaderSize];
            input.ReadExactly(fixedHeader);
            if (!fixedHeader.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                throw new CryptographicException("File không thuộc định dạng mã hóa của TQLTK.");
            long plaintextLength = BinaryPrimitives.ReadInt64LittleEndian(fixedHeader.AsSpan(8, 8));
            int wrappedKeyLength = BinaryPrimitives.ReadInt32LittleEndian(fixedHeader.AsSpan(24, 4));
            if (plaintextLength < 0 || wrappedKeyLength is < 1 or > MaxWrappedKeySize)
                throw new CryptographicException("Header file mã hóa không hợp lệ.");
            long chunkCount = GetChunkCount(plaintextLength);
            if (chunkCount > uint.MaxValue - 1L)
                throw new CryptographicException("File mã hóa vượt giới hạn hỗ trợ.");
            long expectedLength;
            try
            {
                expectedLength = checked(FixedHeaderSize + (long)wrappedKeyLength + TagSize +
                    plaintextLength + chunkCount * TagSize);
            }
            catch (OverflowException ex)
            {
                throw new CryptographicException("Kích thước file mã hóa không hợp lệ.", ex);
            }
            if (input.Length != expectedLength)
                throw new CryptographicException("File mã hóa bị thiếu dữ liệu hoặc có phần dư.");
            byte[] wrappedKey = new byte[wrappedKeyLength];
            input.ReadExactly(wrappedKey);
            byte[] authenticatedHeader = new byte[FixedHeaderSize + wrappedKeyLength];
            Buffer.BlockCopy(fixedHeader, 0, authenticatedHeader, 0, fixedHeader.Length);
            Buffer.BlockCopy(wrappedKey, 0, authenticatedHeader, fixedHeader.Length, wrappedKey.Length);
            byte[] headerTag = new byte[TagSize];
            input.ReadExactly(headerTag);
            byte[] key;
            try
            {
                key = ProtectedData.Unprotect(wrappedKey, Entropy, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("Không thể mở khóa mã hóa file trên tài khoản Windows này.", ex);
            }
            if (key.Length != KeySize)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new CryptographicException("Khóa mã hóa file không hợp lệ.");
            }
            byte[] noncePrefix = new byte[NoncePrefixSize];
            Buffer.BlockCopy(fixedHeader, 16, noncePrefix, 0, noncePrefix.Length);
            byte[] nonce = new byte[NonceSize];
            try
            {
                BuildNonce(noncePrefix, 0, nonce);
                using var gcm = new AesGcm(key, TagSize);
                gcm.Decrypt(nonce, ReadOnlySpan<byte>.Empty, headerTag,
                    Span<byte>.Empty, authenticatedHeader);
            }
            catch
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(noncePrefix);
                CryptographicOperations.ZeroMemory(authenticatedHeader);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(headerTag);
                CryptographicOperations.ZeroMemory(wrappedKey);
            }
            return new HeaderContext(key, noncePrefix, authenticatedHeader, plaintextLength);
        }
        private static byte[] BuildHeader(long plaintextLength, byte[] noncePrefix, byte[] wrappedKey)
        {
            byte[] header = new byte[FixedHeaderSize + wrappedKey.Length];
            Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8, 8), plaintextLength);
            Buffer.BlockCopy(noncePrefix, 0, header, 16, NoncePrefixSize);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24, 4), wrappedKey.Length);
            Buffer.BlockCopy(wrappedKey, 0, header, FixedHeaderSize, wrappedKey.Length);
            return header;
        }
        private static byte[] BuildChunkAad(byte[] header, uint chunkIndex, int length)
        {
            byte[] aad = new byte[header.Length + 8];
            Buffer.BlockCopy(header, 0, aad, 0, header.Length);
            BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(header.Length, 4), chunkIndex);
            BinaryPrimitives.WriteInt32BigEndian(aad.AsSpan(header.Length + 4, 4), length);
            return aad;
        }
        private static void BuildNonce(byte[] noncePrefix, uint chunkIndex, byte[] nonce)
        {
            Buffer.BlockCopy(noncePrefix, 0, nonce, 0, NoncePrefixSize);
            BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixSize, 4), chunkIndex);
        }
        private static long GetChunkCount(long length) =>
            length == 0 ? 0 : 1 + ((length - 1) / ChunkSize);
        private static bool HasMagic(Stream input)
        {
            long originalPosition = input.Position;
            try
            {
                input.Position = 0;
                Span<byte> prefix = stackalloc byte[Magic.Length];
                return ReadAtMost(input, prefix) == Magic.Length && prefix.SequenceEqual(Magic);
            }
            finally
            {
                input.Position = originalPosition;
            }
        }
        private static int ReadAtMost(Stream input, Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = input.Read(buffer[total..]);
                if (read == 0) break;
                total += read;
            }
            return total;
        }
        private sealed class HeaderContext : IDisposable
        {
            public byte[] Key { get; }
            public byte[] NoncePrefix { get; }
            public byte[] AuthenticatedHeader { get; }
            public long PlaintextLength { get; }
            public HeaderContext(byte[] key, byte[] noncePrefix, byte[] authenticatedHeader, long plaintextLength)
            {
                Key = key;
                NoncePrefix = noncePrefix;
                AuthenticatedHeader = authenticatedHeader;
                PlaintextLength = plaintextLength;
            }
            public void Dispose()
            {
                CryptographicOperations.ZeroMemory(Key);
                CryptographicOperations.ZeroMemory(NoncePrefix);
                CryptographicOperations.ZeroMemory(AuthenticatedHeader);
            }
        }
        private sealed class ComparingWriteStream : Stream
        {
            private readonly Stream _candidate;
            public bool Matches { get; private set; } = true;
            public ComparingWriteStream(Stream candidate) => _candidate = candidate;
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) =>
                Write(buffer.AsSpan(offset, count));
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                byte[] candidateBytes = new byte[buffer.Length];
                try
                {
                    _candidate.ReadExactly(candidateBytes);
                    if (!buffer.SequenceEqual(candidateBytes)) Matches = false;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(candidateBytes);
                }
            }
            public bool IsCandidateFullyConsumed() => _candidate.ReadByte() == -1;
        }
    }
}
