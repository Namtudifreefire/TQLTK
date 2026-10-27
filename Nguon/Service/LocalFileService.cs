using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Quản lý kho file local của từng user.
    /// Mỗi user có thư mục riêng: %AppData%\PersonalInfoManager\Storage\&lt;user-id&gt;\
    /// </summary>
    public class LocalFileService
    {
        private readonly string _storageRoot;

        public LocalFileService()
        {
            _storageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PersonalInfoManager", "Storage");

            Directory.CreateDirectory(_storageRoot);
        }

        /// <summary>
        /// Lấy đường dẫn thư mục riêng của user (tự tạo nếu chưa có).
        /// </summary>
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

        /// <summary>
        /// Import file từ ngoài vào kho local (copy).
        /// Trả về đường dẫn đích.
        /// </summary>
        public string ImportFile(string username, string sourcePath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("File nguồn không tồn tại.", sourcePath);

            string userFolder = GetUserFolder(username);
            string fileName = Path.GetFileName(sourcePath);
            string destPath = EnsureUniquePath(Path.Combine(userFolder, fileName));

            File.Copy(sourcePath, destPath, overwrite: false);
            return destPath;
        }

        /// <summary>
        /// Lưu nội dung text trực tiếp thành file trong kho local.
        /// </summary>
        public string SaveTextFile(string username, string fileName, string content)
        {
            string userFolder = GetUserFolder(username);
            string destPath = EnsureUniquePath(Path.Combine(userFolder, fileName));

            File.WriteAllText(destPath, content, System.Text.Encoding.UTF8);
            return destPath;
        }

        /// <summary>
        /// Liệt kê tất cả file trong kho của user (mới nhất trước).
        /// </summary>
        public List<FileInfo> ListFiles(string username)
        {
            string userFolder = GetUserFolder(username);
            return new DirectoryInfo(userFolder)
                .GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
        }

        /// <summary>
        /// Xóa file khỏi kho local.
        /// </summary>
        public void DeleteFile(string filePath)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        /// <summary>
        /// Export file ra ngoài (copy tới vị trí user chọn).
        /// </summary>
        public void ExportFile(string sourcePath, string destPath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("File nguồn không tồn tại.", sourcePath);

            File.Copy(sourcePath, destPath, overwrite: true);
        }

        /// <summary>
        /// Tính tổng dung lượng file user đã lưu (byte).
        /// </summary>
        public long GetUsedBytes(string username)
        {
            string folder = GetUserFolder(username);
            return new DirectoryInfo(folder).GetFiles().Sum(f => f.Length);
        }

        // ================== HELPERS ==================

        /// <summary>
        /// Nếu file đã tồn tại → thêm hậu tố (1), (2), ... để tránh ghi đè.
        /// </summary>
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
