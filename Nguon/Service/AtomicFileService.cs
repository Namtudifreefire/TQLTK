using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PersonalInfoManager.Services
{
    public static class AtomicFileService
    {
        public static void WriteAllBytes(string destinationPath, ReadOnlySpan<byte> contents)
        {
            string temporaryPath = WriteTemporary(destinationPath, contents);
            try
            {
                PromoteTemporary(temporaryPath, destinationPath);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        public static void WriteAllText(string destinationPath, string contents, Encoding? encoding = null)
        {
            ArgumentNullException.ThrowIfNull(contents);
            encoding ??= new UTF8Encoding(false);
            byte[] bytes = encoding.GetBytes(contents);
            try
            {
                WriteAllBytes(destinationPath, bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        public static string WriteTemporary(string destinationPath, ReadOnlySpan<byte> contents)
        {
            string fullDestination = Path.GetFullPath(destinationPath);
            string directory = Path.GetDirectoryName(fullDestination)
                ?? throw new ArgumentException("Destination path must include a directory.", nameof(destinationPath));
            Directory.CreateDirectory(directory);

            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullDestination)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.WriteThrough);
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
                return temporaryPath;
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        public static void PromoteTemporary(string temporaryPath, string destinationPath)
        {
            string fullTemporary = Path.GetFullPath(temporaryPath);
            string fullDestination = Path.GetFullPath(destinationPath);
            string temporaryDirectory = Path.GetDirectoryName(fullTemporary)!;
            string destinationDirectory = Path.GetDirectoryName(fullDestination)!;

            if (!string.Equals(temporaryDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Atomic replacement requires the temporary file to be beside its destination.");
            if (!File.Exists(fullTemporary))
                throw new FileNotFoundException("Temporary file is missing.", fullTemporary);

            File.Move(fullTemporary, fullDestination, overwrite: true);
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
