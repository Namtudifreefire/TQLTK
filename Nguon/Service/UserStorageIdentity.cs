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

    /// <summary>
    /// Gives each account's new local files a key based on its stable user id, while
    /// providing guarded, one-time migration from the old username-derived paths.
    /// </summary>
    internal static class UserStorageIdentity
    {
        public static string GetDirectoryName(Guid userId) => "user_" + userId.ToString("N");

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

        public static void MoveLegacyFile(string source, string destination, Guid userId)
        {
            if (!File.Exists(source)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (!File.Exists(destination))
                File.Move(source, destination);
            else
                File.Move(source, GetArchivePath(source, userId));
        }

        public static void MoveLegacyDirectory(string source, string destination, Guid userId)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (!Directory.Exists(destination))
                Directory.Move(source, destination);
            else
                Directory.Move(source, GetArchivePath(source, userId));
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

            // Win32 treats trailing dots and spaces in directory names as aliases.
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
