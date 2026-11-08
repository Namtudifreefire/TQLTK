using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services;

public sealed class EditAccountDraft
{
    public int Version { get; set; } = 1;
    public string ServiceName { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Tags { get; set; } = "";
    public bool IsFavorite { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Note { get; set; } = "";
    public List<DeletedNoteEntry> DeletedNotes { get; set; } = new();
    public List<AccountCustomField> CustomFields { get; set; } = new();
    public bool NoteMovedToTrash { get; set; }
    public string TotpSecret { get; set; } = "";
    public string TotpIssuer { get; set; } = "";
    public string TotpAlgorithm { get; set; } = "SHA1";
    public int TotpDigits { get; set; } = 6;
    public int TotpPeriodSeconds { get; set; } = 30;
}

public static class EditAccountDraftService
{
    private const int MaxDraftBytes = 2 * 1024 * 1024;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TQLTK.EditAccountDraft.v1.CurrentWindowsUser");
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 32 };

    public static string GetDraftPath(string userScope, Guid? accountId)
    {
        string identity = userScope.Trim().ToUpperInvariant() + "|" + (accountId?.ToString("N") ?? "new-account");
        byte[] identityBytes = Encoding.UTF8.GetBytes(identity);
        try
        {
            string name = Convert.ToHexString(SHA256.HashData(identityBytes));
            return Path.Combine(ApplicationDataPath.Root, "PersonalInfoManager", "EditDrafts", name + ".dpapi");
        }
        finally { CryptographicOperations.ZeroMemory(identityBytes); }
    }

    public static bool TryLoad(string userScope, Guid? accountId, out EditAccountDraft? draft, out string? error)
    {
        draft = null;
        error = null;
        string path = GetDraftPath(userScope, accountId);
        if (!File.Exists(path)) return false;

        byte[]? protectedBytes = null;
        byte[]? plaintext = null;
        try
        {
            var file = new FileInfo(path);
            if (file.Length <= 0 || file.Length > MaxDraftBytes)
                throw new InvalidDataException("The encrypted draft is empty or exceeds its size limit.");

            protectedBytes = File.ReadAllBytes(path);
            plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            if (plaintext.Length > MaxDraftBytes)
                throw new InvalidDataException("The decrypted draft exceeds its size limit.");
            draft = JsonSerializer.Deserialize<EditAccountDraft>(plaintext, JsonOptions)
                ?? throw new InvalidDataException("The encrypted draft has an invalid format.");
            Validate(draft);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            error = ex.Message;
            draft = null;
            return false;
        }
        finally
        {
            if (protectedBytes != null) CryptographicOperations.ZeroMemory(protectedBytes);
            if (plaintext != null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static void Save(string userScope, Guid? accountId, EditAccountDraft draft)
    {
        Validate(draft);
        byte[]? plaintext = null;
        byte[]? protectedBytes = null;
        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(draft, JsonOptions);
            if (plaintext.Length > MaxDraftBytes)
                throw new InvalidDataException("The encrypted draft exceeds its size limit.");
            protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            if (protectedBytes.Length > MaxDraftBytes)
                throw new InvalidDataException("The encrypted draft exceeds its size limit.");
            AtomicFileService.WriteAllBytes(GetDraftPath(userScope, accountId), protectedBytes);
        }
        finally
        {
            if (plaintext != null) CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes != null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public static void Delete(string userScope, Guid? accountId) => AtomicFileService.TryDelete(GetDraftPath(userScope, accountId));

    private static void Validate(EditAccountDraft draft)
    {
        if (draft.ServiceName == null || draft.WebsiteUrl == null || draft.Folder == null || draft.Tags == null ||
            draft.Username == null || draft.Password == null || draft.Note == null || draft.TotpSecret == null ||
            draft.TotpIssuer == null || draft.DeletedNotes == null || draft.CustomFields == null ||
            draft.DeletedNotes.Any(item => item == null) || draft.CustomFields.Any(item => item == null) ||
            draft.Version != 1 || draft.TotpDigits is < 6 or > 8 || draft.TotpPeriodSeconds is < 1 or > 300 ||
            draft.TotpAlgorithm is not ("SHA1" or "SHA256" or "SHA512") ||
            draft.ServiceName.Length > 4096 || draft.WebsiteUrl.Length > 4096 || draft.Folder.Length > 4096 ||
            draft.Tags.Length > 8192 || draft.Username.Length > 16384 || draft.Password.Length > 65536 ||
            draft.Note.Length > 262144 || draft.TotpSecret.Length > 4096 || draft.TotpIssuer.Length > 4096 ||
            (draft.DeletedNotes?.Count ?? 0) > 100 || (draft.CustomFields?.Count ?? 0) > 100)
            throw new InvalidDataException("The encrypted draft contains invalid or oversized values.");
    }
}
