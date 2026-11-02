using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;

namespace PersonalInfoManager.Services
{
        public class LocalFileService
    {
        private readonly string _storageRoot;

        public LocalFileService()
        {
            _storageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PersonalInfoManager", "Storage");
        }

                public string GetUserFolder(string username)
        {
            var userService = new UserService();
            var user = userService.FindByUsername(username)
                ?? throw new InvalidOperationException("Không tìm thấy tài khoản để xác định kho file riêng.");

            string folder = Path.Combine(
                _storageRoot, UserStorageIdentity.GetDirectoryName(user.Id));
            string legacyFolder = UserStorageIdentity.GetLegacyPath(
                Path.GetDirectoryName(_storageRoot)!, user.Username,
                LegacyUserStorageKind.LocalFiles);

            if (!string.Equals(folder, legacyFolder, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(legacyFolder))
            {
                using var migrationLock = UserStorageIdentity.AcquireMigrationLock(user.Id);
                if (Directory.Exists(legacyFolder))
                {
                    if (userService.HasLegacyStoragePathCollision(
                        user.Id, user.Username, LegacyUserStorageKind.LocalFiles))
                        throw new AmbiguousLegacyUserStorageException(
                            "Thư mục file cũ trùng đường dẫn với tài khoản khác. Thư mục vẫn được giữ nguyên; " +
                            "không thể tự chọn chủ sở hữu an toàn.");

                    UserStorageIdentity.MoveLegacyDirectory(legacyFolder, folder, user.Id);
                }
            }

            Directory.CreateDirectory(folder);
            return folder;
        }

        public string GetExistingUserFolderReadOnly(string username)
        {
            var userService = new UserService();
            var user = userService.FindByUsername(username)
                ?? throw new InvalidOperationException("Không tìm thấy tài khoản để xác định kho file riêng.");

            string currentFolder = Path.Combine(_storageRoot, UserStorageIdentity.GetDirectoryName(user.Id));
            string legacyFolder = UserStorageIdentity.GetLegacyPath(
                Path.GetDirectoryName(_storageRoot)!, user.Username, LegacyUserStorageKind.LocalFiles);

            if (!string.Equals(currentFolder, legacyFolder, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(legacyFolder) &&
                userService.HasLegacyStoragePathCollision(
                    user.Id, user.Username, LegacyUserStorageKind.LocalFiles))
                throw new AmbiguousLegacyUserStorageException(
                    "Thư mục file cũ trùng đường dẫn với tài khoản khác. Không thể xem trước khôi phục an toàn.");

            if (Directory.Exists(currentFolder)) return currentFolder;
            if (Directory.Exists(legacyFolder))
                return legacyFolder;

            return currentFolder;
        }

                public string ImportFile(string username, string sourcePath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("File nguồn không tồn tại.", sourcePath);

            string userFolder = GetUserFolder(username);
            string fileName = Path.GetFileName(sourcePath);
            string destPath = EnsureUniquePath(Path.Combine(userFolder, fileName));

            LocalFileEncryptionService.EncryptFileAtomic(sourcePath, destPath, overwrite: false);
            return destPath;
        }

        public string ImportStream(
            string username, string fileName, Stream plaintext, long plaintextLength,
            DateTime? lastWriteTimeUtc = null, CancellationToken cancellationToken = default)
        {
            string userFolder = GetUserFolder(username);
            string safeName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName) || safeName != fileName)
                throw new InvalidDataException("Tên file không hợp lệ.");

            string destPath = EnsureUniquePath(Path.Combine(userFolder, safeName));
            LocalFileEncryptionService.EncryptStreamAtomic(
                plaintext, plaintextLength, destPath, overwrite: false,
                lastWriteTimeUtc: lastWriteTimeUtc, cancellationToken: cancellationToken);
            return destPath;
        }

                public string SaveTextFile(string username, string fileName, string content)
        {
            string userFolder = GetUserFolder(username);
            string destPath = EnsureUniquePath(Path.Combine(userFolder, fileName));

            byte[] plaintext = Encoding.UTF8.GetBytes(content);
            try
            {
                using var input = new MemoryStream(plaintext, writable: false);
                LocalFileEncryptionService.EncryptStreamAtomic(input, destPath, overwrite: false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            return destPath;
        }

                public List<FileInfo> ListFiles(string username, CancellationToken cancellationToken = default)
        {
            string userFolder = GetUserFolder(username);
            var directory = new DirectoryInfo(userFolder);
            foreach (var file in GetStoredFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsTemporaryFile(file.Name)) continue;

                if (!LocalFileEncryptionService.IsEncryptedFile(file.FullName))
                    LocalFileEncryptionService.EncryptFileAtomic(
                        file.FullName, file.FullName, overwrite: true, cancellationToken: cancellationToken);
                else
                {
                    try { _ = LocalFileEncryptionService.GetPlaintextLength(file.FullName); }
                    catch (Exception ex) when (ex is CryptographicException || ex is IOException) { }
                }
            }

            return GetStoredFiles(directory).Where(f => !IsTemporaryFile(f.Name))
                .OrderByDescending(f => f.LastWriteTime).ToList();
        }

        public (int total, int encrypted, int legacy, int invalid) GetEncryptionStatus(string username)
        {
            string userFolder = GetUserFolder(username);
            var files = GetStoredFiles(new DirectoryInfo(userFolder))
                .Where(f => !IsTemporaryFile(f.Name)).ToList();
            int encrypted = 0, legacy = 0, invalid = 0;
            foreach (var file in files)
            {
                if (!LocalFileEncryptionService.IsEncryptedFile(file.FullName))
                {
                    legacy++;
                    continue;
                }

                try
                {
                    _ = LocalFileEncryptionService.GetPlaintextLength(file.FullName);
                    encrypted++;
                }
                catch (Exception ex) when (ex is CryptographicException || ex is IOException)
                {
                    invalid++;
                }
            }

            return (files.Count, encrypted, legacy, invalid);
        }

        public long GetPlaintextLength(string storedPath) =>
            LocalFileEncryptionService.GetPlaintextLength(storedPath);

        public void CopyToPlaintext(string storedPath, Stream destination)
        {
            EnsureEncrypted(storedPath);
            LocalFileEncryptionService.CopyToPlaintext(storedPath, destination);
        }

        public bool ContentEquals(
            string storedPath, Stream plaintext, long plaintextLength,
            CancellationToken cancellationToken = default)
        {
            EnsureEncrypted(storedPath, cancellationToken);
            return LocalFileEncryptionService.ContentEqualsStored(
                storedPath, plaintext, plaintextLength, cancellationToken);
        }

        public bool ContentEqualsReadOnly(
            string storedPath, Stream plaintext, long plaintextLength,
            CancellationToken cancellationToken = default) =>
            LocalFileEncryptionService.ContentEqualsReadOnly(
                storedPath, plaintext, plaintextLength, cancellationToken);

                public void DeleteFile(string filePath)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

                public void ExportFile(string sourcePath, string destPath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("File nguồn không tồn tại.", sourcePath);

            EnsureEncrypted(sourcePath);
            LocalFileEncryptionService.DecryptFileAtomic(sourcePath, destPath, overwrite: true);
        }

                public long GetUsedBytes(string username)
        {
            string folder = GetUserFolder(username);
            return GetStoredFiles(new DirectoryInfo(folder)).Where(f => !IsTemporaryFile(f.Name)).Sum(f => f.Length);
        }

        private static void EnsureEncrypted(string path, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("File không tồn tại trong kho.", path);
            if (!LocalFileEncryptionService.IsEncryptedFile(path))
                LocalFileEncryptionService.EncryptFileAtomic(
                    path, path, overwrite: true, cancellationToken: cancellationToken);
            else
                _ = LocalFileEncryptionService.GetPlaintextLength(path);
        }

        private static FileInfo[] GetStoredFiles(DirectoryInfo directory) => directory.GetFiles();

        private static bool IsTemporaryFile(string name) =>
            name.StartsWith(".tqltk-encrypted-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(".tqltk-decrypted-", StringComparison.OrdinalIgnoreCase);

                private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path)!;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            int i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                i++;
            } while (File.Exists(candidate));

            return candidate;
        }
    }
}
