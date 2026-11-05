using System;
using System.Collections.Generic;
using System.Linq;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services;

public static class AccountRevisionService
{
    public const int MaximumEntries = 20;

    public static AccountRevisionSnapshot Capture(SavedAccount account) => new()
    {
        ServiceName = account.ServiceName ?? "",
        Username = account.Username ?? "",
        WebsiteUrl = account.WebsiteUrl ?? "",
        Folder = account.Folder ?? "",
        Tags = (account.Tags ?? new()).ToList(),
        IsFavorite = account.IsFavorite,
        Note = account.Note ?? "",
        CustomFields = (account.CustomFields ?? new()).Where(field => field != null).Select(field => field.Clone()).ToList()
    };

    public static IReadOnlyList<string> GetChangedFields(SavedAccount account, AccountRevisionSnapshot snapshot)
    {
        var fields = new List<string>();
        if (!string.Equals(account.ServiceName, snapshot.ServiceName, StringComparison.Ordinal)) fields.Add("ServiceName");
        if (!string.Equals(account.Username, snapshot.Username, StringComparison.Ordinal)) fields.Add("Username");
        if (!string.Equals(account.WebsiteUrl, snapshot.WebsiteUrl, StringComparison.Ordinal)) fields.Add("WebsiteUrl");
        if (!string.Equals(account.Folder, snapshot.Folder, StringComparison.Ordinal)) fields.Add("Folder");
        if (!(account.Tags ?? new()).SequenceEqual(snapshot.Tags ?? new(), StringComparer.Ordinal)) fields.Add("Tags");
        if (account.IsFavorite != snapshot.IsFavorite) fields.Add("IsFavorite");
        if (!string.Equals(account.Note, snapshot.Note, StringComparison.Ordinal)) fields.Add("Note");
        if (!CustomFieldsEqual(account.CustomFields, snapshot.CustomFields)) fields.Add("CustomFields");
        return fields;
    }

    public static AccountRevisionEntry? RecordPreviousIfChanged(
        SavedAccount account, AccountRevisionSnapshot previous, DateTime changedAtUtc, Guid? importBatchId = null)
    {
        var changed = GetChangedFields(account, previous);
        if (changed.Count == 0) return null;
        var entry = new AccountRevisionEntry
        {
            ChangedAtUtc = changedAtUtc,
            ChangedFields = string.Join("|", changed),
            Snapshot = previous,
            ImportBatchId = importBatchId
        };
        account.EditHistory ??= new List<AccountRevisionEntry>();
        account.EditHistory.Insert(0, entry);
        if (account.EditHistory.Count > MaximumEntries)
            account.EditHistory = account.EditHistory.Take(MaximumEntries).ToList();
        return entry;
    }

    public static void Apply(SavedAccount account, AccountRevisionSnapshot snapshot)
    {
        account.ServiceName = snapshot.ServiceName ?? "";
        account.Username = snapshot.Username ?? "";
        account.WebsiteUrl = snapshot.WebsiteUrl ?? "";
        account.Folder = snapshot.Folder ?? "";
        account.Tags = (snapshot.Tags ?? new()).ToList();
        account.IsFavorite = snapshot.IsFavorite;
        account.Note = snapshot.Note ?? "";
        account.CustomFields = (snapshot.CustomFields ?? new()).Where(field => field != null).Select(field => field.Clone()).ToList();
    }

    public static AccountImportUndoSnapshot CaptureForImportUndo(SavedAccount account) => new()
    {
        ServiceName = account.ServiceName,
        Username = account.Username,
        Password = account.Password,
        Note = account.Note,
        WebsiteUrl = account.WebsiteUrl,
        Folder = account.Folder,
        Tags = (account.Tags ?? new()).ToList(),
        IsFavorite = account.IsFavorite,
        CustomFields = (account.CustomFields ?? new()).Where(field => field != null).Select(field => field.Clone()).ToList(),
        Attachments = (account.Attachments ?? new()).Where(attachment => attachment != null).Select(attachment => attachment.Clone()).ToList(),
        TotpSecret = account.TotpSecret,
        TotpIssuer = account.TotpIssuer,
        TotpAlgorithm = account.TotpAlgorithm,
        TotpDigits = account.TotpDigits,
        TotpPeriodSeconds = account.TotpPeriodSeconds,
        PasswordHistory = (account.PasswordHistory ?? new()).Select(entry => entry.Clone()).ToList(),
        PasswordChangedAt = account.PasswordChangedAt,
        UpdatedAt = account.UpdatedAt
    };

    public static void RestoreImportUndo(SavedAccount account, AccountImportUndoSnapshot snapshot)
    {
        account.ServiceName = snapshot.ServiceName;
        account.Username = snapshot.Username;
        account.Password = snapshot.Password;
        account.Note = snapshot.Note;
        account.WebsiteUrl = snapshot.WebsiteUrl;
        account.Folder = snapshot.Folder;
        account.Tags = (snapshot.Tags ?? new()).ToList();
        account.IsFavorite = snapshot.IsFavorite;
        account.CustomFields = (snapshot.CustomFields ?? new()).Where(field => field != null).Select(field => field.Clone()).ToList();
        account.Attachments = (snapshot.Attachments ?? new()).Where(attachment => attachment != null).Select(attachment => attachment.Clone()).ToList();
        account.TotpSecret = snapshot.TotpSecret;
        account.TotpIssuer = snapshot.TotpIssuer;
        account.TotpAlgorithm = snapshot.TotpAlgorithm;
        account.TotpDigits = snapshot.TotpDigits;
        account.TotpPeriodSeconds = snapshot.TotpPeriodSeconds;
        account.PasswordHistory = (snapshot.PasswordHistory ?? new()).Select(entry => entry.Clone()).ToList();
        account.PasswordChangedAt = snapshot.PasswordChangedAt;
        account.UpdatedAt = snapshot.UpdatedAt;
    }

    private static bool CustomFieldsEqual(
        IReadOnlyList<AccountCustomField>? left, IReadOnlyList<AccountCustomField>? right)
    {
        left ??= Array.Empty<AccountCustomField>();
        right ??= Array.Empty<AccountCustomField>();
        if (left.Count != right.Count) return false;
        for (int index = 0; index < left.Count; index++)
        {
            AccountCustomField? leftField = left[index];
            AccountCustomField? rightField = right[index];
            if (leftField == null || rightField == null ||
                !string.Equals(leftField.Name, rightField.Name, StringComparison.Ordinal) ||
                !string.Equals(leftField.Value, rightField.Value, StringComparison.Ordinal) ||
                leftField.IsSensitive != rightField.IsSensitive)
                return false;
        }
        return true;
    }
}
