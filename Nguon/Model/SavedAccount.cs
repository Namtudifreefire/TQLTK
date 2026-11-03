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
}
