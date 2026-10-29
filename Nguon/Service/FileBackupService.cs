using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PersonalInfoManager.Services
{













    public static class FileBackupService
    {

        public const long MaxTotalBytes = 100L * 1024 * 1024;

        private const int CurrentFormatVersion = 1;
        private const string ManifestEntryName = "__tqltk__/manifest.json";


        private static readonly Regex ReservedName =
            new(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class ArchiveManifest
        {
            public int FormatVersion { get; set; }
            public string ExportedBy { get; set; } = "";
            public DateTime ExportedAt { get; set; }
            public int FileCount { get; set; }
            public long TotalBytes { get; set; }
        }






        public static (int count, long bytes) Summarize(string username)
        {
            var files = new LocalFileService().ListFiles(username);
            return (files.Count, files.Sum(f => f.Length));
        }

        public static string FormatSize(long bytes) =>
            bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1048576.0:0.#} MB";





        public static (bool ok, string message, int count, long bytes) CreateArchive(
            string username, string destPath, string passphrase)
        {
            try
            {
                var files = new LocalFileService().ListFiles(username);
                if (files.Count == 0)
                    return (false, "Kho File cá nhân đang trống, không có gì để sao lưu.", 0, 0);

                long total = files.Sum(f => f.Length);
                if (total > MaxTotalBytes)
                    return (false,
                        $"Kho File cá nhân hiện là {FormatSize(total)}, vượt giới hạn sao lưu {FormatSize(MaxTotalBytes)}.\n" +
                        "Hãy xóa bớt hoặc xuất bớt file ra ngoài rồi thử lại.",
                        files.Count, total);

                byte[] encrypted;

                using (var ms = new MemoryStream())
                {
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        var manifest = new ArchiveManifest
                        {
                            FormatVersion = CurrentFormatVersion,
                            ExportedBy = username,
                            ExportedAt = DateTime.Now,
                            FileCount = files.Count,
                            TotalBytes = total
                        };

                        var manifestEntry = zip.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                        using (var s = manifestEntry.Open())
                            JsonSerializer.Serialize(s, manifest);

                        foreach (var f in files)
                        {
                            var entry = zip.CreateEntry(f.Name, CompressionLevel.Optimal);
                            if (f.LastWriteTime.Year is >= 1980 and <= 2107) entry.LastWriteTime = f.LastWriteTime;


                            using var src = new FileStream(
                                f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            using var dst = entry.Open();
                            src.CopyTo(dst);
                        }
                    }

                    byte[] buffer = ms.GetBuffer();
                    try
                    {
                        encrypted = BackupCrypto.EncryptBytes(buffer.AsSpan(0, (int)ms.Length), passphrase);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(buffer);
                    }
                }

                File.WriteAllBytes(destPath, encrypted);
                return (true, $"Đã đóng gói {files.Count} file ({FormatSize(total)}).", files.Count, total);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi: {ex.Message}", 0, 0);
            }
        }


















        public static (bool ok, string message, int added, int skipped) RestoreFromFile(
            string username, string sourcePath, string? passphrase = null,
            Func<string?, string?>? askPassphrase = null,
            Func<string, bool>? confirmForeignOwner = null)
        {
            string? tempDir = null;
            byte[]? zipBytes = null;
            int added = 0, skipped = 0;

            try
            {
                if (!File.Exists(sourcePath))
                    return (false, "File không tồn tại.", 0, 0);


                if (new FileInfo(sourcePath).Length > MaxTotalBytes + 16L * 1024 * 1024)
                    return (false, "File quá lớn so với một bản sao lưu File cá nhân của TQLTK.", 0, 0);

                byte[] data = File.ReadAllBytes(sourcePath);
                if (!BackupCrypto.IsBinaryFormat(data))
                    return (false, "File này không phải bản sao lưu File cá nhân của TQLTK.", 0, 0);

                zipBytes = DecryptWithPrompts(data, username, passphrase, askPassphrase);
                if (zipBytes == null)
                    return (false, "Đã hủy: chưa nhập đúng cụm mật khẩu sao lưu.", 0, 0);

                using var ms = new MemoryStream(zipBytes, writable: false);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);


                string owner = ReadOwner(zip);
                bool foreignOwner = owner.Length > 0 &&
                                    !string.Equals(owner, username, StringComparison.OrdinalIgnoreCase);

                if (foreignOwner && (confirmForeignOwner == null || !confirmForeignOwner(owner)))
                    return (false,
                        $"Đã hủy khôi phục: bản sao lưu này thuộc về người dùng '{owner}', không phải '{username}'.", 0, 0);

                var entries = zip.Entries.Where(IsUserFileEntry).ToList();
                int invalid = zip.Entries.Count(e =>
                    e.Name.Length > 0 && !IsUserFileEntry(e) && e.FullName != ManifestEntryName);

                if (entries.Count == 0)
                    return (false, "Bản sao lưu không chứa file nào để khôi phục.", 0, 0);


                if (entries.Sum(e => e.Length) > MaxTotalBytes)
                    return (false, "Bản sao lưu có dung lượng lớn bất thường nên bị từ chối.", 0, 0);

                var fileService = new LocalFileService();
                string userFolder = fileService.GetUserFolder(username);

                tempDir = Path.Combine(Path.GetTempPath(), "tqltk_files_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                int index = 0;

                foreach (var entry in entries)
                {

                    string entryDir = Path.Combine(tempDir, (index++).ToString());
                    Directory.CreateDirectory(entryDir);
                    string tmp = Path.Combine(entryDir, entry.Name);

                    using (var src = entry.Open())
                    using (var dst = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        CopyBounded(src, dst, entry.Length);

                    try { File.SetLastWriteTime(tmp, entry.LastWriteTime.LocalDateTime); } catch {   }

                    string existing = Path.Combine(userFolder, entry.Name);
                    if (File.Exists(existing) && SameContent(existing, tmp))
                    {
                        skipped++;
                        continue;
                    }


                    fileService.ImportFile(username, tmp);
                    added++;
                }

                string msg = $"Đã khôi phục {added} file.";
                if (skipped > 0) msg += $"\nBỏ qua {skipped} file trùng (cùng tên và cùng nội dung).";
                if (invalid > 0) msg += $"\nBỏ qua {invalid} mục có tên không hợp lệ.";

                return (true, msg, added, skipped);
            }
            catch (Exception ex)
            {
                string partial = added > 0
                    ? $"\nTrước khi lỗi xảy ra, {added} file đã được khôi phục vào kho File cá nhân."
                    : "";
                return (false, $"Lỗi: {ex.Message}{partial}", added, skipped);
            }
            finally
            {
                if (zipBytes != null) CryptographicOperations.ZeroMemory(zipBytes);

                if (tempDir != null)
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch {   }
                }
            }
        }






        private static byte[]? DecryptWithPrompts(
            byte[] data, string username, string? passphrase, Func<string?, string?>? askPassphrase)
        {
            string? candidate = passphrase ?? BackupCrypto.GetStoredPassphrase(username);

            if (candidate != null && TryDecrypt(data, candidate, out byte[]? plain))
                return plain;

            string? error = candidate == null
                ? null
                : "Cụm mật khẩu đang lưu trên máy này không mở được bản sao lưu này.";

            for (int attempt = 0; attempt < 3 && askPassphrase != null; attempt++)
            {
                string? typed = askPassphrase(error);
                if (typed == null) return null;

                if (TryDecrypt(data, typed, out byte[]? typedPlain))
                    return typedPlain;

                error = "Cụm mật khẩu không đúng hoặc file bị hỏng. Hãy thử lại.";
            }

            return null;
        }

        private static bool TryDecrypt(byte[] data, string passphrase, out byte[]? plain)
        {
            try
            {
                plain = BackupCrypto.DecryptBytes(data, passphrase);
                return true;
            }
            catch (CryptographicException)
            {
                plain = null;
                return false;
            }
        }

        private static string ReadOwner(ZipArchive zip)
        {
            try
            {
                var entry = zip.GetEntry(ManifestEntryName);
                if (entry == null) return "";

                using var s = entry.Open();
                return JsonSerializer.Deserialize<ArchiveManifest>(s)?.ExportedBy.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }





        private static bool IsUserFileEntry(ZipArchiveEntry e)
        {
            string name = e.Name;

            return name.Length > 0
                   && e.FullName == name
                   && name != "." && name != ".."
                   && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                   && !name.EndsWith('.') && !name.EndsWith(' ')
                   && !ReservedName.IsMatch(name);
        }

        private static void CopyBounded(Stream src, Stream dst, long maxBytes)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;

            while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxBytes)
                    throw new InvalidDataException("Một file trong bản sao lưu lớn hơn dung lượng đã khai báo.");

                dst.Write(buffer, 0, read);
            }
        }

        private static bool SameContent(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;

            using var sa = fa.OpenRead();
            using var sb = fb.OpenRead();
            return SHA256.HashData(sa).SequenceEqual(SHA256.HashData(sb));
        }
    }
}
