using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PersonalInfoManager.Services;

public static class LanguageService
{
    private sealed class ObservedValue
    {
        public string? Source { get; set; }
        public string? Rendered { get; set; }
        public Func<string>? Renderer { get; set; }
        public bool IsUpdating { get; set; }
    }

    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, ObservedValue>> Observed = new();
    private static readonly ConditionalWeakTable<TextBlock, ObservedValue> LocalizedTextBlocks = new();
    private static readonly IReadOnlyDictionary<string, string> EnglishActivityActions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ADD_ACCOUNT"] = "Add account",
            ["ADMIN_CHANGE_ROLE"] = "Change user role",
            ["ADMIN_CREATE_USER"] = "Create user",
            ["ADMIN_DELETE_USER"] = "Delete user",
            ["ADMIN_DELETE_USER_DATA"] = "Delete user data",
            ["ADMIN_EDIT_USER"] = "Edit user",
            ["ADMIN_LOCK_USER"] = "Lock user",
            ["ADMIN_RESET_PWD"] = "Reset password",
            ["ADMIN_UNLOCK_USER"] = "Unlock user",
            ["APP_EXIT"] = "Application exit",
            ["AUTO_BACKUP"] = "Automatic backup",
            ["AUTO_BACKUP_FAILED"] = "Automatic backup failed",
            ["AUTO_LOCK"] = "Automatic lock",
            ["AUTO_THEME_SWITCH"] = "Automatic theme change",
            ["CHANGE_PASSWORD"] = "Change password",
            ["COPY_PASSWORD"] = "Copy password",
            ["COPY_PASSWORD_DENIED"] = "Password copy denied",
            ["CHECK_GOOGLE_DRIVE_ACCOUNT"] = "Check linked Google Drive account",
            ["CHECK_GOOGLE_DRIVE_ACCOUNT_DENIED"] = "Google Drive account check denied",
            ["CREATE_FILE"] = "Create file",
            ["DELETE_FILE"] = "Delete file",
            ["DISABLE_2FA"] = "Disable two-factor authentication",
            ["DISABLE_WINDOWS_HELLO"] = "Disable Windows Hello",
            ["DISCONNECT_GOOGLE_DRIVE"] = "Disconnect Google Drive on this device",
            ["DISCONNECT_GOOGLE_DRIVE_DENIED"] = "Google Drive disconnect denied",
            ["EDIT_ACCOUNT"] = "Edit account",
            ["EDIT_ACCOUNT_DENIED"] = "Account edit denied",
            ["EDIT_ACCOUNT_ATTACHMENTS"] = "Edit account attachments",
            ["ACCOUNT_ATTACHMENTS_DENIED"] = "Account attachment access denied",
            ["ACCOUNT_HISTORY_DENIED"] = "Account edit history access denied",
            ["RESTORE_ACCOUNT_REVISION"] = "Restore account revision",
            ["BULK_ACCOUNT_ACTION"] = "Bulk account action",
            ["BULK_ACCOUNT_ACTION_DENIED"] = "Bulk account action denied",
            ["UNDO_CSV_IMPORT"] = "Undo CSV import",
            ["UNDO_CSV_IMPORT_DENIED"] = "CSV import undo denied",
            ["UNDO_IMPORT"] = "Undo import",
            ["IMPORT_CSV_DENIED"] = "CSV import denied",
            ["IMPORT_CREDENTIALS"] = "Import credentials",
            ["CREATE_ACCOUNT_SHARE"] = "Create temporary account share",
            ["REVOKE_ACCOUNT_SHARE"] = "Revoke temporary account share",
            ["IMPORT_ACCOUNT_SHARE"] = "Import shared account",
            ["ACCOUNT_SHARE_DENIED"] = "Account sharing denied",
            ["ENABLE_2FA"] = "Enable two-factor authentication",
            ["ENABLE_WINDOWS_HELLO"] = "Enable Windows Hello",
            ["EXIT_APP"] = "Exit application",
            ["EXPORT_CSV"] = "Export CSV",
            ["EXPORT_CSV_DENIED"] = "CSV export denied",
            ["EXPORT_FILE"] = "Export file",
            ["FILE_BACKUP_RESTORE_DRILL"] = "Personal-file backup restore drill",
            ["EXPORT_VAULT"] = "Export vault",
            ["IMPORT_CSV"] = "Import CSV",
            ["IMPORT_FILE"] = "Import file",
            ["IMPORT_VAULT"] = "Import vault",
            ["LOGIN"] = "Sign-in",
            ["LOGIN_2FA_FAILED"] = "Two-factor sign-in failed",
            ["LOGIN_2FA_OK"] = "Two-factor sign-in succeeded",
            ["LOGIN_FAILED"] = "Sign-in failed",
            ["LOGIN_FORCED_CHANGE_CANCELLED"] = "Required password change cancelled",
            ["LOGIN_WINDOWS_HELLO"] = "Windows Hello sign-in",
            ["LOGOUT"] = "Sign-out",
            ["MOVE_ACCOUNT_TO_TRASH"] = "Move account to Trash",
            ["PASSWORD_HISTORY_DENIED"] = "Password history access denied",
            ["PERMANENT_DELETE_ACCOUNT"] = "Permanently delete account",
            ["PERMANENT_DELETE_NOTE"] = "Permanently delete note",
            ["PWNED_PASSWORD_CHECK"] = "Check for breached passwords",
            ["PWNED_PASSWORD_CHECK_DENIED"] = "Breached password check denied",
            ["REGENERATE_RECOVERY_CODES"] = "Regenerate recovery codes",
            ["REGISTER"] = "Register account",
            ["INITIAL_ADMIN_CREATED"] = "Create initial administrator",
            ["RESTORE_ACCOUNT_FROM_TRASH"] = "Restore account from Trash",
            ["RESTORE_FILES_GOOGLE_DRIVE"] = "Restore personal files from Google Drive",
            ["RESTORE_FILES_GOOGLE_DRIVE_DENIED"] = "Personal file restore denied",
            ["RESTORE_GOOGLE_DRIVE"] = "Restore vault from Google Drive",
            ["RESTORE_GOOGLE_DRIVE_DENIED"] = "Vault restore denied",
            ["RESTORE_NOTE_FROM_TRASH"] = "Restore note from Trash",
            ["RESTORE_PASSWORD_HISTORY"] = "Restore previous password",
            ["SET_BACKUP_PASSPHRASE"] = "Set backup passphrase",
            ["SET_BACKUP_PASSPHRASE_DENIED"] = "Backup passphrase change denied",
            ["SHOW_ACCOUNT_TOTP_DENIED"] = "Account TOTP access denied",
            ["SHOW_PASSWORD"] = "Show password",
            ["SHOW_PASSWORD_DENIED"] = "Password reveal denied",
            ["SYNC_GOOGLE_DRIVE_DENIED"] = "Google Drive sync denied",
            ["SYNC_FILES_GOOGLE_DRIVE"] = "Sync personal files to Google Drive",
            ["SYNC_GOOGLE_DRIVE"] = "Sync vault to Google Drive",
            ["TRASH_PERMANENT_DELETE_DENIED"] = "Permanent deletion from Trash denied",
            ["UNHANDLED_ERROR"] = "Unhandled error",
            ["UNOBSERVED_TASK_ERROR"] = "Background task error",
            ["UPDATE_PROFILE"] = "Update profile",
            ["EXPORT_VAULT_DENIED"] = "Vault export denied",
            ["IMPORT_VAULT_DENIED"] = "Vault import denied",
            ["VERIFY_BACKUP_DENIED"] = "Backup verification denied",
            ["VERIFY_FILE_BACKUP"] = "Verify file backup",
            ["VERIFY_VAULT_BACKUP"] = "Verify vault backup",
            ["VAULT_KEY_ROTATION_DENIED"] = "Vault key rotation denied",
            ["VAULT_KEY_ROTATION"] = "Vault key rotation",
            ["VAULT_BACKUP_RESTORE_DRILL"] = "Vault backup restore drill",
            ["CREATE_COMPLETE_BACKUP"] = "Create full backup",
            ["CREATE_COMPLETE_BACKUP_DENIED"] = "Full backup creation denied",
            ["RESTORE_COMPLETE_BACKUP"] = "Restore full backup",
            ["RESTORE_COMPLETE_BACKUP_DENIED"] = "Full backup restore denied",
            ["RESTORE_COMPLETE_BACKUP_FAILED"] = "Full backup restore failed",
            ["BACKUP_CENTER_DENIED"] = "Backup manager access denied",
            ["RECOVER_SELECTED_BACKUP_DENIED"] = "Selected backup recovery denied",
            ["RECOVER_SELECTED_BACKUP_COPIES"] = "Recover backup entries as copies",
            ["EDIT_DRAFT_SAVE_FAILED"] = "Encrypted edit draft save failed"
        };
    private static readonly Regex VaultKeyRotationLogDetail = new(
        @"^Đã mã hóa lại (?<files>\d+) tệp; giữ (?<backups>\d+) backup dùng cụm mật khẩu\.$",
        RegexOptions.CultureInvariant);
    private static readonly Regex CompleteRestoreLogDetail = new(
        @"^Đã (?:thêm|khôi phục thành bản sao) (?<accounts>\d+) tài khoản, (?<notes>\d+) ghi chú và (?<files>\d+) tệp\.$",
        RegexOptions.CultureInvariant);
    private static readonly Regex BulkAccountActionLogDetail = new(
        @"^Thay đổi (?<count>\d+) tài khoản$",
        RegexOptions.CultureInvariant);
    private static readonly Regex CsvExportLogDetail = new(
        @"^Xuất (?<count>\d+) tài khoản ra CSV$",
        RegexOptions.CultureInvariant);
    private static readonly Regex CredentialImportLogDetail = new(
        @"^Thêm (?<added>\d+), ghi đè (?<overwritten>\d+), bỏ qua (?<skipped>\d+) \((?<message>.*)\)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex AutoLockLogDetail = new(
        @"^Không hoạt động (?<minutes>\d+) phút$",
        RegexOptions.CultureInvariant);
    private static readonly Regex VaultExportLogDetail = new(
        @"^Đã xuất (?<count>\d+) tài khoản ra (?<file>.+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex VaultImportLogDetail = new(
        @"^Đã nhập (?<imported>\d+) tài khoản \(bỏ qua (?<skipped>\d+) trùng\)(?<owner> \(bản sao lưu của người dùng '.*'\))?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex PersonalFileRestoreLogDetail = new(
        @"^Khôi phục File cá nhân từ Drive: (?<file>.+) \((?<restored>\d+) file, bỏ qua (?<skipped>\d+)\)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex PersonalFileSyncLogDetail = new(
        @"^Tải lên (?<count>\d+) tệp cá nhân \((?<size>.*)\)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex BreachedPasswordCheckLogDetail = new(
        @"^Đã kiểm tra (?<checked>\d+) giá trị; ảnh hưởng (?<affected>\d+) mục$",
        RegexOptions.CultureInvariant);
    private static readonly Regex AdminDeleteUserDataLogDetail = new(
        @"^(?<username>.+): xóa (?<deleted>\d+) mục, lỗi (?<errors>\d+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex AutoThemeSwitchLogDetail = new(
        @"^Tự động đổi giao diện sang (?<theme>Sáng|Tối) lúc (?<time>\d{2}:\d{2})$",
        RegexOptions.CultureInvariant);
    private static string _currentCode = RegistryService.GetLanguage();

    public static string CurrentCode => _currentCode;

    public static bool IsEnglish => _currentCode == "en";

#if SECURITY_TEST
    internal static void SetLanguageForTesting(string code) => _currentCode = code == "en" ? "en" : "vi";
#endif

    public static string GetDisplayName(string code) => code == "en" ? "English" : "Tiếng Việt";

    public static void SetLanguage(string code)
    {
        string normalized = code.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
        if (_currentCode == normalized) return;
        _currentCode = normalized;
        RegistryService.SetLanguage(normalized);
        if (Application.Current == null) return;
        foreach (Window window in Application.Current.Windows)
            ApplyToWindow(window);
    }

    public static string Translate(string value)
    {
        return Translate(value, _currentCode);
    }

    public static string Format(string source, params object?[] arguments) =>
        LocalizationAuditService.FormatSource(source, _currentCode, arguments);

    public static string TranslateLogContent(string content)
    {
        return TranslateLogContent(content, _currentCode);
    }

    internal static string TranslateLogContent(string content, string languageCode)
    {
        if (!string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(content)) return content;

        string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        return string.Join(Environment.NewLine, normalized.Split('\n').Select(line => TranslateLogLine(line, languageCode)));
    }

    private static string TranslateLogLine(string line, string languageCode)
    {
        int timestampEnd = line.IndexOf("] [", StringComparison.Ordinal);
        if (timestampEnd != 20) return Translate(line, languageCode);

        int usernameEnd = line.IndexOf("] [", timestampEnd + 3, StringComparison.Ordinal);
        if (usernameEnd < 0) return Translate(line, languageCode);

        int actionEnd = line.IndexOf("] ", usernameEnd + 3, StringComparison.Ordinal);
        if (actionEnd < 0) return Translate(line, languageCode);

        int detailStart = actionEnd + 2;
        string action = line[(usernameEnd + 3)..actionEnd];
        string detail = line[detailStart..];
        string translatedAction = EnglishActivityActions.TryGetValue(action, out string? englishAction)
            ? englishAction
            : action;
        return line[..(usernameEnd + 3)] + translatedAction + line[actionEnd..detailStart] + TranslateActivityLogDetail(action, detail, languageCode);
    }

    private static string TranslateActivityLogDetail(string action, string detail, string languageCode)
    {
        Match match;
        switch (action)
        {
            case "CREATE_COMPLETE_BACKUP":
                const string packagePrefix = "Đã tạo gói chuyển máy: ";
                if (detail.StartsWith(packagePrefix, StringComparison.Ordinal))
                    return "Created migration package: " + detail[packagePrefix.Length..];
                break;
            case "RESTORE_COMPLETE_BACKUP":
            case "RECOVER_SELECTED_BACKUP_COPIES":
                match = CompleteRestoreLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "accounts", out int restoredAccountCount) &&
                    TryParseLogCount(match, "notes", out int restoredNoteCount) && TryParseLogCount(match, "files", out int restoredFileCount))
                    return $"Accounts restored: {restoredAccountCount}; private notes: {restoredNoteCount}; files: {restoredFileCount}.";
                break;
            case "VAULT_KEY_ROTATION":
                match = VaultKeyRotationLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "files", out int files) &&
                    TryParseLogCount(match, "backups", out int backups))
                {
                    return $"Re-encrypted {files} {Plural(files, "file", "files")}; retained {backups} passphrase-protected {Plural(backups, "backup", "backups")}.";
                }
                break;
            case "BULK_ACCOUNT_ACTION":
                match = BulkAccountActionLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "count", out int changed))
                    return $"Changed {changed} {Plural(changed, "account", "accounts")}";
                break;
            case "EXPORT_CSV":
                match = CsvExportLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "count", out int exported))
                    return $"Exported {exported} {Plural(exported, "account", "accounts")} to CSV";
                break;
            case "EXPORT_VAULT":
                match = VaultExportLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "count", out int exportedVault))
                    return $"Exported {exportedVault} {Plural(exportedVault, "account", "accounts")} to {match.Groups["file"].Value}";
                break;
            case "IMPORT_VAULT":
                match = VaultImportLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "imported", out int imported) &&
                    TryParseLogCount(match, "skipped", out int skippedVault))
                {
                    string owner = match.Groups["owner"].Value;
                    return $"Imported {imported} {Plural(imported, "account", "accounts")} (skipped {skippedVault} {Plural(skippedVault, "duplicate", "duplicates")})" +
                        (owner.Length == 0 ? "" : Translate(owner, languageCode));
                }
                break;
            case "IMPORT_CREDENTIALS":
                match = CredentialImportLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "added", out int added) &&
                    TryParseLogCount(match, "overwritten", out int overwritten) &&
                    TryParseLogCount(match, "skipped", out int skipped))
                {
                    string message = Translate(match.Groups["message"].Value, languageCode);
                    return $"Added {added}, overwrote {overwritten}, and skipped {skipped} ({message})";
                }
                break;
            case "AUTO_LOCK":
                match = AutoLockLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "minutes", out int minutes))
                    return $"After {minutes} {Plural(minutes, "minute", "minutes")} of inactivity";
                break;
            case "AUTO_THEME_SWITCH":
                match = AutoThemeSwitchLogDetail.Match(detail);
                if (match.Success)
                {
                    string theme = match.Groups["theme"].Value == "Sáng" ? "Light" : "Dark";
                    return $"Automatically switched theme to {theme} at {match.Groups["time"].Value}";
                }
                break;
            case "SYNC_FILES_GOOGLE_DRIVE":
                match = PersonalFileSyncLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "count", out int uploaded))
                {
                    string size = Translate(match.Groups["size"].Value, languageCode);
                    return $"Uploaded {uploaded} personal {Plural(uploaded, "file", "files")} ({size})";
                }
                break;
            case "RESTORE_FILES_GOOGLE_DRIVE":
                match = PersonalFileRestoreLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "restored", out int restored) &&
                    TryParseLogCount(match, "skipped", out int skippedFiles))
                    return $"Backup {match.Groups["file"].Value}: restored {restored} personal {Plural(restored, "file", "files")}; skipped {skippedFiles} {Plural(skippedFiles, "file", "files")}.";
                break;
            case "PWNED_PASSWORD_CHECK":
                match = BreachedPasswordCheckLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "checked", out int checkedPasswords) &&
                    TryParseLogCount(match, "affected", out int affectedAccounts))
                    return $"Checked {checkedPasswords} unique {Plural(checkedPasswords, "password", "passwords")}; {affectedAccounts} {Plural(affectedAccounts, "account", "accounts")} affected.";
                break;
            case "ADMIN_DELETE_USER_DATA":
                match = AdminDeleteUserDataLogDetail.Match(detail);
                if (match.Success && TryParseLogCount(match, "deleted", out int deleted) &&
                    TryParseLogCount(match, "errors", out int errors))
                    return $"{match.Groups["username"].Value}: deleted {deleted} {Plural(deleted, "entry", "entries")}, {errors} {Plural(errors, "error", "errors")}";
                break;
            case "AUTO_BACKUP":
                if (detail.StartsWith("Định kỳ: ", StringComparison.Ordinal))
                    return "Scheduled: " + detail["Định kỳ: ".Length..];
                if (detail.StartsWith("Khi thoát: ", StringComparison.Ordinal))
                    return "On exit: " + detail["Khi thoát: ".Length..];
                if (detail.StartsWith("Thủ công: ", StringComparison.Ordinal))
                    return "Manual: " + detail["Thủ công: ".Length..];
                break;
        }

        return Translate(detail, languageCode);
    }

    private static bool TryParseLogCount(Match match, string groupName, out int count) =>
        int.TryParse(match.Groups[groupName].Value, NumberStyles.None, CultureInfo.InvariantCulture, out count);

    private static string Plural(int count, string singular, string plural) => count == 1 ? singular : plural;

    public static string Translate(string value, string languageCode)
    {
        if (!string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(value)) return value;
        if (LocalizationAuditService.TryTranslateKnown(value, languageCode, out string translated)) return translated;

        int start = 0;
        while (start < value.Length && char.IsWhiteSpace(value[start])) start++;
        int end = value.Length;
        while (end > start && char.IsWhiteSpace(value[end - 1])) end--;
        if (start > 0 || end < value.Length)
        {
            string content = value[start..end];
            if (EnglishCatalog.Entries.TryGetValue(content, out string? innerTranslation))
                return string.Concat(value.AsSpan(0, start), innerTranslation, value.AsSpan(end));
        }

        string result = ProtectQuotedValues(value, out List<(string Token, string Value)> quotedValues);
        foreach (var pair in EnglishCatalog.Fragments.OrderByDescending(pair => pair.Key.Length))
        {
            if (pair.Key.Length == 0) continue;
            if (pair.Key is " mục" or " phút" or " phút.")
            {
                result = ReplaceAfterNumber(result, pair.Key, pair.Value);
                continue;
            }
            if (char.IsWhiteSpace(pair.Key[0]))
            {
                if (result.Contains(pair.Key, StringComparison.Ordinal))
                    result = result.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
            }
            else if (result.StartsWith(pair.Key, StringComparison.Ordinal))
            {
                result = pair.Value + result[pair.Key.Length..];
            }
            else if (result.EndsWith(pair.Key, StringComparison.Ordinal))
            {
                result = result[..^pair.Key.Length] + pair.Value;
            }
            else if (char.IsPunctuation(pair.Key[0]) && result.Contains(pair.Key, StringComparison.Ordinal))
            {
                result = result.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
            }
        }
        foreach ((string token, string quotedValue) in quotedValues)
            result = result.Replace(token, quotedValue, StringComparison.Ordinal);
        return result;
    }

    private static string ProtectQuotedValues(string value, out List<(string Token, string Value)> quotedValues)
    {
        quotedValues = new List<(string Token, string Value)>();
        StringBuilder result = new(value.Length);
        int position = 0;
        while (position < value.Length)
        {
            if (value[position] != '\'')
            {
                result.Append(value[position++]);
                continue;
            }

            int end = position + 1;
            while (end < value.Length)
            {
                end = value.IndexOf('\'', end);
                if (end < 0) break;
                if (end + 1 == value.Length || !char.IsLetterOrDigit(value[end + 1])) break;
                end++;
            }

            if (end < 0)
            {
                result.Append(value.AsSpan(position));
                break;
            }

            result.Append('\'');
            string content = value[(position + 1)..end];
            bool hasLeadingOrTrailingWhitespace = content.Length > 0 &&
                (char.IsWhiteSpace(content[0]) || char.IsWhiteSpace(content[^1]));
            if (content.Length > 0 && !hasLeadingOrTrailingWhitespace && HasVietnameseCharacters(content))
            {
                string token = $"\uE000{quotedValues.Count}\uE001";
                quotedValues.Add((token, content));
                result.Append(token);
            }
            else
            {
                result.Append(content);
            }
            result.Append('\'');
            position = end + 1;
        }

        return result.ToString();
    }

    private static bool HasVietnameseCharacters(string value) => value.Any(character =>
        character is >= '\u00C0' and <= '\u024F' or >= '\u1E00' and <= '\u1EFF');

    private static string ReplaceAfterNumber(string value, string fragment, string replacement)
    {
        StringBuilder? result = null;
        int copiedThrough = 0;
        int searchFrom = 0;
        while (searchFrom < value.Length)
        {
            int index = value.IndexOf(fragment, searchFrom, StringComparison.Ordinal);
            if (index < 0) break;
            if (index > 0 && char.IsDigit(value[index - 1]))
            {
                result ??= new StringBuilder(value.Length + replacement.Length);
                result.Append(value, copiedThrough, index - copiedThrough);
                result.Append(replacement);
                copiedThrough = index + fragment.Length;
                searchFrom = copiedThrough;
            }
            else
            {
                searchFrom = index + 1;
            }
        }
        if (result == null) return value;
        result.Append(value, copiedThrough, value.Length - copiedThrough);
        return result.ToString();
    }

    public static void HandleWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window) ApplyToWindow(window);
    }

    public static void ApplyToWindow(Window window)
    {
        Visit(window, new HashSet<DependencyObject>());
        RefreshLocalizedBindings(window, new HashSet<DependencyObject>());
    }

    private static void RefreshLocalizedBindings(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element)) return;

        if (element is TextBlock textBlock &&
            BindingOperations.GetBindingExpression(textBlock, TextBlock.TextProperty) is { } binding &&
            binding.ParentBinding.Converter is LocalizedTextConverter)
        {
            binding.UpdateTarget();
        }

        if (element is Visual or Visual3D)
        {
            int visualChildren = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < visualChildren; i++)
                RefreshLocalizedBindings(VisualTreeHelper.GetChild(element, i), visited);
        }

        foreach (object child in LogicalTreeHelper.GetChildren(element))
            if (child is DependencyObject dependencyChild)
                RefreshLocalizedBindings(dependencyChild, visited);
    }

    public static void SetLocalizedText(TextBlock textBlock, string sourceText)
    {
        ObservedValue observation = LocalizedTextBlocks.GetValue(textBlock, _ => new());
        observation.Source = sourceText;
        observation.Renderer = null;
        RenderLocalizedTextBlock(textBlock, observation);
    }

    public static void SetLocalizedKey(TextBlock textBlock, string localizationKey)
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        ArgumentException.ThrowIfNullOrWhiteSpace(localizationKey);
        ObservedValue observation = LocalizedTextBlocks.GetValue(textBlock, _ => new());
        observation.Source = localizationKey;
        observation.Renderer = () => LocalizationAuditService.Get(localizationKey);
        RenderLocalizedTextBlock(textBlock, observation);
    }

    public static void SetLocalizedFormattedText(
        TextBlock textBlock,
        string sourceTemplate,
        Func<object?[]> arguments)
    {
        ObservedValue observation = LocalizedTextBlocks.GetValue(textBlock, _ => new());
        observation.Source = sourceTemplate;
        observation.Renderer = () => Format(sourceTemplate, arguments());
        RenderLocalizedTextBlock(textBlock, observation);
    }

    private static void Visit(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element)) return;

        if (element is Window window)
            Observe(window, Window.TitleProperty, value => ((Window)value).Title, (value, text) => value.SetCurrentValue(Window.TitleProperty, text ?? ""));
        if (element is TextBlock textBlock)
        {
            if (LocalizedTextBlocks.TryGetValue(textBlock, out ObservedValue? localizedText))
            {
                RenderLocalizedTextBlock(textBlock, localizedText);
            }
            else
            {
                if (textBlock.Inlines.Count == 0 && !IsUserDataTextBlock(textBlock))
                    Observe(textBlock, TextBlock.TextProperty, value => ((TextBlock)value).Text, (value, text) => value.SetCurrentValue(TextBlock.TextProperty, text ?? ""));
                Inline[] inlines = textBlock.Inlines.ToArray();
                foreach (Inline inline in inlines)
                    if (inline is Run run)
                        Observe(run, Run.TextProperty, value => ((Run)value).Text, (value, text) => value.SetCurrentValue(Run.TextProperty, text ?? ""));
            }
        }
        if (element is HeaderedContentControl headeredContent)
            Observe(headeredContent, HeaderedContentControl.HeaderProperty, value => ((HeaderedContentControl)value).Header as string,
                (value, text) => value.SetCurrentValue(HeaderedContentControl.HeaderProperty, text));
        if (element is HeaderedItemsControl headeredItems)
            Observe(headeredItems, HeaderedItemsControl.HeaderProperty, value => ((HeaderedItemsControl)value).Header as string,
                (value, text) => value.SetCurrentValue(HeaderedItemsControl.HeaderProperty, text));
        if (element is ContentControl contentControl)
            Observe(contentControl, ContentControl.ContentProperty, value => ((ContentControl)value).Content as string,
                (value, text) => value.SetCurrentValue(ContentControl.ContentProperty, text));
        if (element is DataGrid grid)
            foreach (DataGridColumn column in grid.Columns)
                Observe(column, DataGridColumn.HeaderProperty, value => ((DataGridColumn)value).Header as string,
                    (value, text) => value.SetCurrentValue(DataGridColumn.HeaderProperty, text));
        if (element is FrameworkElement frameworkElement)
        {
            Observe(frameworkElement, FrameworkElement.ToolTipProperty,
                value => ((FrameworkElement)value).ToolTip as string,
                (value, text) => value.SetCurrentValue(FrameworkElement.ToolTipProperty, text));
            if (frameworkElement.ToolTip is DependencyObject toolTip) Visit(toolTip, visited);
            if (frameworkElement.ContextMenu is DependencyObject contextMenu) Visit(contextMenu, visited);
        }

        foreach (object child in LogicalTreeHelper.GetChildren(element))
            if (child is DependencyObject dependencyChild) Visit(dependencyChild, visited);
    }

    private static void RenderLocalizedTextBlock(TextBlock textBlock, ObservedValue observation)
    {
        string? translated = observation.Renderer?.Invoke() ??
            (observation.Source == null ? null : Translate(observation.Source));
        observation.Rendered = translated;
        if (translated == null || string.Equals(textBlock.Text, translated, StringComparison.Ordinal)) return;
        observation.IsUpdating = true;
        try { textBlock.SetCurrentValue(TextBlock.TextProperty, translated); }
        finally { observation.IsUpdating = false; }
    }

    private static void Observe(
        DependencyObject target,
        DependencyProperty property,
        Func<DependencyObject, string?> getter,
        Action<DependencyObject, string?> setter)
    {
        if (BindingOperations.IsDataBound(target, property)) return;

        Dictionary<DependencyProperty, ObservedValue> values = Observed.GetValue(target, _ => new());
        if (!values.TryGetValue(property, out ObservedValue? observation))
        {
            string? current = getter(target);
            observation = new ObservedValue { Source = current, Rendered = current };
            values[property] = observation;

            DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, target.GetType());
            if (descriptor != null)
            {
                var weakTarget = new WeakReference<DependencyObject>(target);
                descriptor.AddValueChanged(target, (_, _) =>
                {
                    if (weakTarget.TryGetTarget(out DependencyObject? liveTarget))
                        OnValueChanged(liveTarget, getter, setter, observation);
                });
            }
        }
        else
        {
            string? current = getter(target);
            if (!observation.IsUpdating && !string.Equals(current, observation.Rendered, StringComparison.Ordinal))
                observation.Source = current;
        }

        Render(target, getter, setter, observation);
    }

    private static void OnValueChanged(
        DependencyObject target,
        Func<DependencyObject, string?> getter,
        Action<DependencyObject, string?> setter,
        ObservedValue observation)
    {
        if (target is TextBlock textBlock && LocalizedTextBlocks.TryGetValue(textBlock, out _)) return;
        if (observation.IsUpdating) return;
        string? current = getter(target);
        if (string.Equals(current, observation.Rendered, StringComparison.Ordinal)) return;
        observation.Source = current;
        Render(target, getter, setter, observation);
    }

    private static void Render(
        DependencyObject target,
        Func<DependencyObject, string?> getter,
        Action<DependencyObject, string?> setter,
        ObservedValue observation)
    {
        string? translated = observation.Source == null ? null : Translate(observation.Source);
        observation.Rendered = translated;
        if (translated == null || string.Equals(getter(target), translated, StringComparison.Ordinal)) return;
        observation.IsUpdating = true;
        try { setter(target, translated); }
        finally { observation.IsUpdating = false; }
    }

    private static bool IsUserDataTextBlock(TextBlock textBlock) => textBlock.Name is
        "lblAvatar" or "lblUserName" or "lblMenuEmail" or "lblProfileCreatedAt" or "lblAccount";
}
