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
    /// <summary>
    /// Sao lưu / khôi phục kho "File cá nhân" của một người dùng (thư mục Storage\{user-id}).
    ///
    /// File cá nhân được lưu KHÔNG mã hóa trên máy, nên bản sao lưu luôn được nén rồi mã hóa bằng cụm mật khẩu sao lưu
    /// của người dùng (BackupCrypto.EncryptBytes, định dạng "TQFB") trước khi đưa lên Google Drive.
    ///
    /// Bên trong bản sao lưu là một file zip phẳng (mỗi file cá nhân một mục, không có thư mục) cùng một mục
    /// "__tqltk__/manifest.json" ghi người xuất, thời điểm xuất, số file và tổng dung lượng.
    ///
    /// Giới hạn: tổng dung lượng kho không quá MaxTotalBytes, vì việc nén và mã hóa làm trong bộ nhớ.
    /// Khôi phục không bao giờ ghi đè: file trùng tên và trùng nội dung thì bỏ qua,
    /// trùng tên nhưng khác nội dung thì lưu thêm với hậu tố (1), (2)... như khi nhập file thủ công.
    /// </summary>
    public static class FileBackupService
    {
        /// <summary>Tổng dung lượng tối đa của kho file được sao lưu (100 MB).</summary>
        public const long MaxTotalBytes = 100L * 1024 * 1024;

        private const int CurrentFormatVersion = 1;
        private const string ManifestEntryName = "__tqltk__/manifest.json";

        // Tên thiết bị đặc biệt của Windows (CON, NUL, COM1...) không được dùng làm tên file
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

        // ============================================================
        //  SAO LƯU
        // ============================================================

        /// <summary>Đếm số file và tổng dung lượng kho File cá nhân của người dùng.</summary>
        public static (int count, long bytes) Summarize(string username)
        {
            var files = new LocalFileService().ListFiles(username);
            return (files.Count, files.Sum(f => f.Length));
        }

        public static string FormatSize(long bytes) =>
            bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1048576.0:0.#} MB";

        /// <summary>
        /// Nén toàn bộ kho File cá nhân của người dùng, mã hóa bằng cụm mật khẩu và ghi ra destPath.
        /// Nặng về CPU và bộ nhớ, nên gọi từ luồng nền (Task.Run).
        /// </summary>
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

                            // FileShare.ReadWrite|Delete để không lỗi nếu file đang được app khác mở
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
                        CryptographicOperations.ZeroMemory(buffer);   // xóa bản nén chưa mã hóa khỏi bộ nhớ (tốt nhất có thể)
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

        // ============================================================
        //  KHÔI PHỤC
        // ============================================================

        /// <summary>
        /// Khôi phục các file trong một bản sao lưu vào kho File cá nhân của người dùng, không ghi đè file nào.
        /// Nặng về CPU và đĩa, nên gọi từ luồng nền (Task.Run); các hàm gọi lại có thể được gọi từ luồng đó.
        /// </summary>
        /// <param name="passphrase">Cụm mật khẩu để thử trước. Bỏ trống thì dùng cụm đã lưu của người dùng.</param>
        /// <param name="askPassphrase">
        /// Gọi khi cụm hiện có không mở được file (tối đa 3 lần). Nhận thông báo lỗi của lần trước (hoặc null),
        /// trả về cụm người dùng nhập, hoặc null nếu họ hủy.
        /// </param>
        /// <param name="confirmForeignOwner">
        /// Gọi khi bản sao lưu ghi người xuất khác người dùng hiện tại. Trả true để vẫn khôi phục.
        /// Bỏ trống thì từ chối bản sao lưu của người dùng khác.
        /// </param>
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

                // Bản sao lưu hợp lệ không thể lớn hơn giới hạn cộng phần nén và mã hóa; chặn file lạ quá lớn
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

                // Bản sao lưu ghi người xuất khác người dùng hiện tại: chỉ khôi phục khi được xác nhận rõ ràng
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

                // Chặn bản nén có dung lượng khai báo quá lớn (zip bomb)
                if (entries.Sum(e => e.Length) > MaxTotalBytes)
                    return (false, "Bản sao lưu có dung lượng lớn bất thường nên bị từ chối.", 0, 0);

                var fileService = new LocalFileService();
                string userFolder = fileService.GetUserFolder(username);

                tempDir = Path.Combine(Path.GetTempPath(), "tqltk_files_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                int index = 0;

                foreach (var entry in entries)
                {
                    // Mỗi mục có thư mục tạm riêng để tên file không bao giờ đụng nhau
                    string entryDir = Path.Combine(tempDir, (index++).ToString());
                    Directory.CreateDirectory(entryDir);
                    string tmp = Path.Combine(entryDir, entry.Name);

                    using (var src = entry.Open())
                    using (var dst = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        CopyBounded(src, dst, entry.Length);

                    try { File.SetLastWriteTime(tmp, entry.LastWriteTime.LocalDateTime); } catch { /* không quan trọng */ }

                    string existing = Path.Combine(userFolder, entry.Name);
                    if (File.Exists(existing) && SameContent(existing, tmp))
                    {
                        skipped++;
                        continue;
                    }

                    // ImportFile tự thêm (1), (2)... nếu trùng tên nhưng khác nội dung, không ghi đè
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
                    try { Directory.Delete(tempDir, recursive: true); } catch { /* bỏ qua */ }
                }
            }
        }

        // ============================================================
        //  HÀM TRỢ GIÚP
        // ============================================================

        /// <summary>Thử cụm đã có, rồi hỏi người dùng tối đa 3 lần. Trả về null nếu không mở được.</summary>
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

        /// <summary>
        /// Chỉ nhận mục là một file có tên phẳng hợp lệ (không thư mục, không đường dẫn, không ký tự cấm,
        /// không tên thiết bị Windows). Mục manifest và mọi mục lạ đều bị loại, chống path traversal.
        /// </summary>
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
