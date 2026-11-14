using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    internal enum LegacyUserStorageKind
    {
        Vault,
        TwoFactor,
        LocalFiles
    }

    internal sealed class AmbiguousLegacyUserStorageException : InvalidOperationException
    {
        public AmbiguousLegacyUserStorageException(string message) : base(message) { }
    }

    internal static class UserStorageIdentity
    {
        public static string GetDirectoryName(Guid userId) => "user_" + userId.ToString("N");

        // This preflight detects existing reparse points; it cannot eliminate TOCTOU.
        // ApplicationData itself is supplied by Windows and its ancestors are not rejected.
        internal static void ValidateMigrationPaths(string root, params string[] paths)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            foreach (string path in paths.Prepend(fullRoot))
            {
                string fullPath = Path.GetFullPath(path);
                string relative = Path.GetRelativePath(fullRoot, fullPath);
                if (Path.IsPathRooted(relative) || relative == ".." ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new IOException("Đường dẫn migration nằm ngoài kho dữ liệu ứng dụng.");
                string current = fullRoot;
                if (!ValidateExistingComponent(current, mustBeDirectory: true)) continue;
                if (relative == ".") continue;
                string[] parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                for (int index = 0; index < parts.Length; index++)
                {
                    current = Path.Combine(current, parts[index]);
                    if (!ValidateExistingComponent(current, mustBeDirectory: index < parts.Length - 1)) break;
                }
            }
        }

        private static bool ValidateExistingComponent(string path, bool mustBeDirectory)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Migration không hỗ trợ đường dẫn chứa junction hoặc reparse point.");
                if (mustBeDirectory && (attributes & FileAttributes.Directory) == 0)
                    throw new IOException("Thành phần cha của đường dẫn migration không phải thư mục.");
                return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        internal static bool MigrationPathExists(string path, bool directory = false)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if (((attributes & FileAttributes.Directory) != 0) != directory)
                    throw new IOException("Loại đường dẫn migration không hợp lệ.");
                return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        public static string GetLegacyPath(
            string root, string username, LegacyUserStorageKind kind)
        {
            string safeName = kind == LegacyUserStorageKind.Vault
                ? string.Join("_", username.Split(Path.GetInvalidFileNameChars()))
                : SanitizeName(username);

            return kind switch
            {
                LegacyUserStorageKind.Vault => Path.Combine(root, $"vault_{safeName}.dat"),
                LegacyUserStorageKind.TwoFactor => Path.Combine(root, "2fa", safeName + ".2fa"),
                LegacyUserStorageKind.LocalFiles => Path.Combine(root, "Storage", safeName),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        public static bool HasLegacyPathCollision(
            User target, IEnumerable<User> users, LegacyUserStorageKind kind)
        {
            string targetKey = GetLegacyCollisionKey(target.Username, kind);
            return users.Any(other =>
                other.Id != target.Id &&
                string.Equals(targetKey, GetLegacyCollisionKey(other.Username, kind),
                    StringComparison.OrdinalIgnoreCase));
        }

        public static bool HasAmbiguousLegacyData(
            User target, IEnumerable<User> users, string root)
        {
            foreach (var other in users)
            {
                if (other.Id == target.Id) continue;

                foreach (LegacyUserStorageKind kind in Enum.GetValues<LegacyUserStorageKind>())
                {
                    if (!string.Equals(
                        GetLegacyCollisionKey(target.Username, kind),
                        GetLegacyCollisionKey(other.Username, kind),
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (HasLegacyData(root, target.Username, kind)) return true;
                }
            }

            return false;
        }

        public static bool HasLegacyDataForUsername(string root, string username)
            => Enum.GetValues<LegacyUserStorageKind>()
                .Any(kind => HasLegacyData(root, username, kind));

        public static IDisposable AcquireMigrationLock(Guid userId)
        {
            var mutex = new Mutex(
                initiallyOwned: false,
                name: @"Local\TQLTK.UserStorage." + userId.ToString("N"));

            try
            {
                bool acquired = false;
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
                catch (AbandonedMutexException) { acquired = true; }

                if (!acquired)
                    throw new TimeoutException("Hết thời gian chờ khóa di chuyển dữ liệu người dùng.");

                return new MutexLease(mutex);
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
        }

        public static void MoveLegacyFile(string source, string destination, Guid userId, string? root = null)
        {
            if (root != null) ValidateMigrationPaths(root, source, destination);
            if (!(root == null ? File.Exists(source) : MigrationPathExists(source))) return;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (!(root == null ? File.Exists(destination) : MigrationPathExists(destination)))
                File.Move(source, destination);
            else
            {
                string archive = GetArchivePath(source, userId);
                if (root != null) ValidateMigrationPaths(root, source, destination, archive);
                File.Move(source, archive);
            }
        }

        public static void MoveLegacyDirectory(string source, string destination, Guid userId, string? root = null)
        {
            if (root != null) ValidateMigrationPaths(root, source, destination);
            if (!(root == null ? Directory.Exists(source) : MigrationPathExists(source, directory: true))) return;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (!(root == null ? Directory.Exists(destination) : MigrationPathExists(destination, directory: true)))
                Directory.Move(source, destination);
            else
            {
                string archive = GetArchivePath(source, userId);
                if (root != null) ValidateMigrationPaths(root, source, destination, archive);
                Directory.Move(source, archive);
            }
        }

        private static string GetArchivePath(string source, Guid userId)
            => source + ".migrated-" + userId.ToString("N") + "-" + Guid.NewGuid().ToString("N");

        internal static bool HasLegacyData(
            string root, string username, LegacyUserStorageKind kind)
        {
            string path = GetLegacyPath(root, username, kind);
            return kind switch
            {
                LegacyUserStorageKind.Vault => File.Exists(path) || File.Exists(path + ".bak"),
                LegacyUserStorageKind.TwoFactor => File.Exists(path),
                LegacyUserStorageKind.LocalFiles => Directory.Exists(path),
                _ => false
            };
        }

        private static string GetLegacyCollisionKey(
            string username, LegacyUserStorageKind kind)
        {
            string safeName = kind == LegacyUserStorageKind.Vault
                ? string.Join("_", username.Split(Path.GetInvalidFileNameChars()))
                : SanitizeName(username);

            return kind == LegacyUserStorageKind.LocalFiles
                ? safeName.TrimEnd(' ', '.')
                : safeName;
        }

        private static string SanitizeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private sealed class MutexLease : IDisposable
        {
            private readonly Mutex _mutex;
            private bool _disposed;

            public MutexLease(Mutex mutex) => _mutex = mutex;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { _mutex.ReleaseMutex(); }
                finally { _mutex.Dispose(); }
            }
        }
    }
}
