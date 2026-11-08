using PersonalInfoManager.Services;
namespace PersonalInfoManager;
public enum BackupItemStatus { Added, Modified, Removed, Changed, NotComparable, AlwaysIncluded, Included, Excluded, KeepExisting, AddAccount, KeepNote, AddNote, SkipDuplicateFile, AddRenamedCopy, AddFile, SkipDuplicate, Restore, Import }
public enum BackupRecoverySource { None, Older, Newer, Unsupported }
public sealed record BackupPreviewItem(string Name, BackupItemStatus Status, string Details);
