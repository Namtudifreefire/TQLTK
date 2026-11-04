using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalInfoManager.Models;
namespace PersonalInfoManager.Services;
public sealed class AccountSharePackage
{
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public SavedAccount Account { get; set; } = new();
}
public static class AccountShareService
{
    public const int MaximumPackageBytes = 40 * 1024 * 1024;
    private const int MaximumAttachmentBytes = 10 * 1024 * 1024;
    private const int MaximumAttachmentsTotalBytes = 25 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private const string PassphraseAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
    public static string GeneratePassphrase(int length = 28)
    {
        if (length is < 20 or > 64) throw new ArgumentOutOfRangeException(nameof(length));
        return string.Create(length, PassphraseAlphabet, (span, alphabet) =>
        {
            for (int index = 0; index < span.Length; index++)
                span[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        });
    }
    public static void CreateEncryptedPackage(
        SavedAccount source, DateTimeOffset expiresAtUtc, string passphrase, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(passphrase);
        if (passphrase.Length < BackupCrypto.MinPassphraseLength)
            throw new ArgumentException("Cụm mật khẩu chia sẻ quá ngắn.", nameof(passphrase));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        expiresAtUtc = expiresAtUtc.ToUniversalTime();
        if (expiresAtUtc <= now || expiresAtUtc > now.AddDays(30))
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Thời hạn gói chia sẻ phải trong vòng 30 ngày.");
        SavedAccount account = source.Clone();
        account.Id = Guid.NewGuid();
        account.PasswordHistory = new();
        account.DeletedNotes = new();
        account.EditHistory = new();
        account.ImportBatchId = null;
        account.ImportBatchAtUtc = null;
        account.ImportUndoable = false;
        account.CreatedInImportBatch = false;
        account.ImportUndoSnapshot = null;
        account.IsDeleted = false;
        account.DeletedAt = null;
        ValidateAccount(account);
        var package = new AccountSharePackage
        {
            Version = 1,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc,
            Account = account
        };
        string json = JsonSerializer.Serialize(package, JsonOptions);
        string encrypted = BackupCrypto.Encrypt(json, passphrase);
        if (Encoding.UTF8.GetByteCount(encrypted) > MaximumPackageBytes)
            throw new InvalidDataException("Gói chia sẻ vượt quá giới hạn 40 MB.");
        byte[] verify = Encoding.UTF8.GetBytes(encrypted);
        try
        {
            var check = JsonSerializer.Deserialize<AccountSharePackage>(BackupCrypto.Decrypt(encrypted, passphrase), JsonOptions);
            if (check?.Version != 1 || check.Account?.Id != account.Id || check.ExpiresAtUtc != expiresAtUtc)
                throw new CryptographicException("Không thể xác minh gói chia sẻ vừa tạo.");
            AtomicFileService.WriteAllBytes(destinationPath, verify);
        }
        finally { CryptographicOperations.ZeroMemory(verify); }
    }
    public static AccountSharePackage OpenEncryptedPackage(string sourcePath, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(passphrase);
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new FileNotFoundException("Không tìm thấy gói chia sẻ.", sourcePath);
        if (info.Length is <= 0 or > MaximumPackageBytes)
            throw new InvalidDataException("Kích thước gói chia sẻ không hợp lệ hoặc vượt quá 40 MB.");
        byte[] bytes;
        using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new MemoryStream())
        {
            if (input.Length > MaximumPackageBytes)
                throw new InvalidDataException("Kích thước gói chia sẻ vượt quá 40 MB.");
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaximumPackageBytes)
                    throw new InvalidDataException("Kích thước gói chia sẻ vượt quá 40 MB.");
                output.Write(buffer, 0, read);
            }
            bytes = output.ToArray();
            CryptographicOperations.ZeroMemory(buffer);
        }
        try
        {
            string encrypted = new UTF8Encoding(false, true).GetString(bytes);
            if (!BackupCrypto.IsPassphraseFormat(encrypted))
                throw new CryptographicException("Gói này không dùng định dạng AES-GCM được hỗ trợ.");
            string json = BackupCrypto.Decrypt(encrypted, passphrase);
            var package = JsonSerializer.Deserialize<AccountSharePackage>(json, JsonOptions)
                ?? throw new InvalidDataException("Gói chia sẻ trống hoặc sai cấu trúc.");
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (package.Version != 1 || package.Account == null || package.CreatedAtUtc == default ||
                package.CreatedAtUtc > now.AddMinutes(5) || package.ExpiresAtUtc <= now ||
                package.ExpiresAtUtc <= package.CreatedAtUtc ||
                package.ExpiresAtUtc > package.CreatedAtUtc.AddDays(30))
                throw new InvalidDataException("Gói chia sẻ đã hết hạn hoặc có thông tin thời hạn không hợp lệ.");
            ValidateAccount(package.Account);
            SavedAccount result = package.Account.Clone();
            result.Id = Guid.NewGuid();
            result.IsDeleted = false;
            result.DeletedAt = null;
            result.ImportBatchId = null;
            result.ImportUndoSnapshot = null;
            result.ImportUndoable = false;
            result.CreatedInImportBatch = false;
            package.Account = result;
            return package;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void ValidateAccount(SavedAccount account)
    {
        account.ServiceName ??= "";
        account.Username ??= "";
        account.Password ??= "";
        account.Note ??= "";
        account.WebsiteUrl ??= "";
        account.Folder ??= "";
        if (account.ServiceName.Length > 1024 || account.Username.Length > 4096 ||
            account.Password.Length > 16_384 || account.Note.Length > 100_000 ||
            account.WebsiteUrl.Length > 4096 || account.Folder.Length > 512)
            throw new InvalidDataException("Gói có trường tài khoản vượt quá giới hạn kích thước.");
        account.Tags ??= new();
        if (account.Tags.Count > 100 || account.Tags.Any(tag =>
                tag == null || tag.Length > 256 || tag.Any(char.IsControl)))
            throw new InvalidDataException("Gói có nhãn không hợp lệ.");
        account.CustomFields ??= new();
        if (account.CustomFields.Count > 100 || account.CustomFields.Any(field =>
                field == null || field.Name == null || field.Value == null ||
                field.Name.Length > 256 || field.Value.Length > 65_536))
            throw new InvalidDataException("Gói có trường tùy chỉnh không hợp lệ hoặc quá dài.");
        account.Attachments ??= new();
        if (account.Attachments.Count > 50)
            throw new InvalidDataException("Gói có quá nhiều tệp đính kèm.");
        long total = 0;
        foreach (var attachment in account.Attachments)
        {
            if (attachment == null || string.IsNullOrWhiteSpace(attachment.FileName) ||
                attachment.FileName != Path.GetFileName(attachment.FileName) ||
                attachment.FileName.Length > 255 || attachment.FileName.Any(char.IsControl) ||
                attachment.Content == null || attachment.Content.Length > MaximumAttachmentBytes)
                throw new InvalidDataException("Gói có tệp đính kèm không hợp lệ.");
            total += attachment.Content.Length;
            if (total > MaximumAttachmentsTotalBytes)
                throw new InvalidDataException("Tổng dung lượng tệp đính kèm vượt giới hạn 25 MB.");
        }
    }
}
