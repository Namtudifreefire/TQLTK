using System;
using System.Collections.Generic;
using System.Linq;
namespace PersonalInfoManager.Models
{
         public class SavedAccount
    {
        public Guid Id { get; set; } = Guid.NewGuid();
                 public string ServiceName { get; set; } = "";
                 public string Username { get; set; } = "";
                 public string Password { get; set; } = "";
                 public string Note { get; set; } = "";
        public string WebsiteUrl { get; set; } = "";
        public string Folder { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public bool IsFavorite { get; set; }
        public List<AccountCustomField> CustomFields { get; set; } = new();
        public string TotpSecret { get; set; } = "";
        public string TotpIssuer { get; set; } = "";
        public string TotpAlgorithm { get; set; } = "SHA1";
        public int TotpDigits { get; set; } = 6;
        public int TotpPeriodSeconds { get; set; } = 30;
        public List<PasswordHistoryEntry> PasswordHistory { get; set; } = new();
        public List<DeletedNoteEntry> DeletedNotes { get; set; } = new();
        public List<AccountAttachment> Attachments { get; set; } = new();
        public List<AccountRevisionEntry> EditHistory { get; set; } = new();
        public Guid? ImportBatchId { get; set; }
        public DateTime? ImportBatchAtUtc { get; set; }
        public bool ImportUndoable { get; set; }
        public bool CreatedInImportBatch { get; set; }
        public AccountImportUndoSnapshot? ImportUndoSnapshot { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public DateTime? PasswordChangedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public SavedAccount Clone()
        {
            var clone = (SavedAccount)MemberwiseClone();
            clone.PasswordHistory = (PasswordHistory ?? new List<PasswordHistoryEntry>())
                .Where(entry => entry != null)
                .Select(entry => entry.Clone()).ToList();
            clone.DeletedNotes = (DeletedNotes ?? new List<DeletedNoteEntry>())
                .Where(entry => entry != null)
                .Select(entry => entry.Clone()).ToList();
            clone.Tags = (Tags ?? new List<string>()).ToList();
            clone.CustomFields = (CustomFields ?? new List<AccountCustomField>())
                .Where(entry => entry != null)
                .Select(entry => entry.Clone()).ToList();
            clone.Attachments = (Attachments ?? new List<AccountAttachment>())
                .Where(entry => entry != null)
                .Select(entry => entry.Clone()).ToList();
            clone.EditHistory = (EditHistory ?? new List<AccountRevisionEntry>())
                .Where(entry => entry != null)
                .Select(entry => entry.Clone()).ToList();
            clone.ImportUndoSnapshot = ImportUndoSnapshot?.Clone();
            return clone;
        }
    }
    public class AccountCustomField
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public bool IsSensitive { get; set; }
        public AccountCustomField Clone()
        {
            var clone = (AccountCustomField)MemberwiseClone();
            clone.Name ??= "";
            clone.Value ??= "";
            return clone;
        }
    }
    public class DeletedNoteEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Content { get; set; } = "";
        public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
        public DeletedNoteEntry Clone() => (DeletedNoteEntry)MemberwiseClone();
    }
    public class PasswordHistoryEntry
    {
        public string Password { get; set; } = "";
        public DateTime ChangedAt { get; set; } = DateTime.Now;
        public PasswordHistoryEntry Clone() => (PasswordHistoryEntry)MemberwiseClone();
    }
    public sealed class AccountAttachment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "application/octet-stream";
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
        public AccountAttachment Clone() => new()
        {
            Id = Id,
            FileName = FileName,
            ContentType = ContentType,
            Content = (Content ?? Array.Empty<byte>()).ToArray(),
            AddedAtUtc = AddedAtUtc
        };
    }
    public sealed class AccountRevisionEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
        public string ChangedFields { get; set; } = "";
        public AccountRevisionSnapshot Snapshot { get; set; } = new();
        public Guid? ImportBatchId { get; set; }
        public AccountRevisionEntry Clone() => new()
        {
            Id = Id,
            ChangedAtUtc = ChangedAtUtc,
            ChangedFields = ChangedFields,
            Snapshot = Snapshot.Clone(),
            ImportBatchId = ImportBatchId
        };
    }
    public sealed class AccountRevisionSnapshot
    {
        public string ServiceName { get; set; } = "";
        public string Username { get; set; } = "";
        public string WebsiteUrl { get; set; } = "";
        public string Folder { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public bool IsFavorite { get; set; }
        public string Note { get; set; } = "";
        public List<AccountCustomField> CustomFields { get; set; } = new();
        public AccountRevisionSnapshot Clone() => new()
        {
            ServiceName = ServiceName,
            Username = Username,
            WebsiteUrl = WebsiteUrl,
            Folder = Folder,
            Tags = (Tags ?? new()).ToList(),
            IsFavorite = IsFavorite,
            Note = Note,
            CustomFields = (CustomFields ?? new()).Where(field => field != null).Select(field => field.Clone()).ToList()
        };
    }
    public sealed class AccountImportUndoSnapshot
    {
        public string ServiceName { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Note { get; set; } = "";
        public string WebsiteUrl { get; set; } = "";
        public string Folder { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public bool IsFavorite { get; set; }
        public List<AccountCustomField> CustomFields { get; set; } = new();
        public List<AccountAttachment> Attachments { get; set; } = new();
        public string TotpSecret { get; set; } = "";
        public string TotpIssuer { get; set; } = "";
        public string TotpAlgorithm { get; set; } = "SHA1";
        public int TotpDigits { get; set; } = 6;
        public int TotpPeriodSeconds { get; set; } = 30;
        public List<PasswordHistoryEntry> PasswordHistory { get; set; } = new();
        public DateTime? PasswordChangedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public AccountImportUndoSnapshot Clone() => new()
        {
            ServiceName = ServiceName,
            Username = Username,
            Password = Password,
            Note = Note,
            WebsiteUrl = WebsiteUrl,
            Folder = Folder,
            Tags = (Tags ?? new()).ToList(),
            IsFavorite = IsFavorite,
            CustomFields = (CustomFields ?? new()).Where(field => field != null).Select(field => field.Clone()).ToList(),
            Attachments = (Attachments ?? new()).Where(attachment => attachment != null).Select(attachment => attachment.Clone()).ToList(),
            TotpSecret = TotpSecret,
            TotpIssuer = TotpIssuer,
            TotpAlgorithm = TotpAlgorithm,
            TotpDigits = TotpDigits,
            TotpPeriodSeconds = TotpPeriodSeconds,
            PasswordHistory = (PasswordHistory ?? new()).Select(entry => entry.Clone()).ToList(),
            PasswordChangedAt = PasswordChangedAt,
            UpdatedAt = UpdatedAt
        };
    }
}
