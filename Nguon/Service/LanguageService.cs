using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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

    public static class LanguageSwitchProfiler
    {
        // Check environment variable once at startup
        private static readonly bool _isEnabled =
            Environment.GetEnvironmentVariable("TQLTK_PROFILE_LANG_SWITCH") == "1";

        public static bool Enabled => _isEnabled;

        private static readonly object _lock = new();
        // Per-switch timing data - each switch gets its own dictionaries
        private static Dictionary<string, long> _currentTimings = new();
        private static Dictionary<string, int> _currentCounts = new();
        private static int _currentSwitchId = 0;

        // Track active measurements for nested timing support
        private static readonly Dictionary<string, int> _activeMeasurements = new();

        // Track current language switch context
        private static string _currentDirection = "";
        private static bool _isInLanguageSwitch = false;

        public static void Reset()
        {
            lock (_lock)
            {
                _currentTimings.Clear();
                _currentCounts.Clear();
                _activeMeasurements.Clear();
                _currentDirection = "";
                // Do NOT reset _currentSwitchId - it must monotonically increase to prevent stale scope matching
                _isInLanguageSwitch = false;
            }
        }

        public static IDisposable Measure(string name)
        {
            if (!_isEnabled) return EmptyDisposable.Instance;
            var sw = Stopwatch.StartNew();
            int switchId;
            lock (_lock)
            {
                if (!_isInLanguageSwitch) return EmptyDisposable.Instance;
                switchId = _currentSwitchId;
                _activeMeasurements[name] = _activeMeasurements.GetValueOrDefault(name) + 1;
            }
            return new TimingScope(name, sw, switchId);
        }

        // Creates a switch scope that ensures proper begin/end pairing
        public static IDisposable BeginSwitch(string fromLang, string toLang)
        {
            if (!_isEnabled) return EmptyDisposable.Instance;
            lock (_lock)
            {
                _isInLanguageSwitch = true;
                _currentSwitchId++;
                _currentDirection = fromLang + " → " + toLang;
                _currentTimings.Clear();
                _currentCounts.Clear();
                _activeMeasurements.Clear();
            }
            return new SwitchScope(_currentSwitchId, _currentDirection);
        }

        sealed class TimingScope : IDisposable
        {
            readonly string _name;
            readonly Stopwatch _sw;
            readonly int _switchId;
            bool _disposed = false;
            public TimingScope(string name, Stopwatch sw, int switchId) { _name = name; _sw = sw; _switchId = switchId; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _sw.Stop();
                var elapsedTicks = _sw.ElapsedTicks;
                lock (_lock)
                {
                    // Only record if this is still the current switch
                    if (_isInLanguageSwitch && _currentSwitchId == _switchId)
                    {
                        _currentTimings[_name] = _currentTimings.GetValueOrDefault(_name) + elapsedTicks;
                        _currentCounts[_name] = _currentCounts.GetValueOrDefault(_name) + 1;
                        _activeMeasurements[_name] = _activeMeasurements.GetValueOrDefault(_name) - 1;
                        if (_activeMeasurements[_name] <= 0)
                        {
                            _activeMeasurements.Remove(_name);
                        }
                    }
                }
            }
        }

        sealed class SwitchScope : IDisposable
        {
            readonly int _switchId;
            readonly string _direction;
            bool _disposed = false;
            public SwitchScope(int switchId, string direction) { _switchId = switchId; _direction = direction; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                // Only log if this is still the current switch
                Dictionary<string, long> timingsCopy = null;
                Dictionary<string, int> countsCopy = null;
                string direction = null;
                bool completedSuccessfully = true;
                lock (_lock)
                {
                    if (_isInLanguageSwitch && _currentSwitchId == _switchId)
                    {
                        direction = _currentDirection;
                        _isInLanguageSwitch = false;
                        // Copy data for logging outside the lock
                        timingsCopy = new Dictionary<string, long>(_currentTimings);
                        countsCopy = new Dictionary<string, int>(_currentCounts);
                        _currentTimings.Clear();
                        _currentCounts.Clear();
                        _activeMeasurements.Clear();
                    }
                    else
                    {
                        completedSuccessfully = false;
                    }
                }
                if (direction != null)
                {
                    // Fire-and-forget async logging to avoid blocking UI thread
                    ThreadPool.QueueUserWorkItem(_ => LogLanguageSwitchToFile(direction, timingsCopy, countsCopy, _switchId, completedSuccessfully));
                }
            }
        }

        sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new();
            public void Dispose() { }
        }

        public static void LogResults()
        {
            lock (_lock)
            {
                if (!_currentTimings.Any()) return;
                var sb = new StringBuilder();
                sb.AppendLine("=== Language Switch Performance Profile ===");
                foreach (var kv in _currentTimings.OrderByDescending(kv => kv.Value))
                {
                    var count = _currentCounts[kv.Key];
                    // Convert ticks to milliseconds
                    var totalMs = (double)kv.Value * 1000.0 / Stopwatch.Frequency;
                    var avgMs = (double)kv.Value * 1000.0 / Stopwatch.Frequency / count;
                    sb.AppendLine(kv.Key + ": total=" + totalMs.ToString("F3") + "ms, count=" + count + ", avg=" + avgMs.ToString("F3") + "ms");
                }
                Debug.WriteLine(sb.ToString());
                System.Diagnostics.Trace.WriteLine(sb.ToString());
            }
        }

        public static void ResetResults()
        {
            lock (_lock)
            {
                _currentTimings.Clear();
                _currentCounts.Clear();
                _activeMeasurements.Clear();
            }
        }

        // Legacy methods kept for backward compatibility but now delegate to switch scope
        public static IDisposable BeginLanguageSwitch(string fromLang, string toLang)
        {
            return BeginSwitch(fromLang, toLang);
        }

        public static void EndLanguageSwitch()
        {
            // No-op: logging is handled by SwitchScope.Dispose() on thread pool
            // This method is kept for backward compatibility only
        }

        private static void LogLanguageSwitchToFile(string direction, Dictionary<string, long> timings, Dictionary<string, int> counts, int switchId, bool completedSuccessfully)
        {
            try
            {
                var logDir = GetLogDirectory();
                if (string.IsNullOrEmpty(logDir)) return;

                var fileName = "lang-switch-profile-" + DateTime.Now.ToString("yyyyMMdd") + ".log";
                var filePath = Path.Combine(logDir, fileName);

                var sb = new StringBuilder();
                sb.AppendLine("=== Language Switch Profile: " + direction + " ===");
                sb.AppendLine("Switch ID: " + switchId);
                sb.AppendLine("Status: " + (completedSuccessfully ? "COMPLETED" : "PARTIAL/ABORTED"));
                sb.AppendLine("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.AppendLine("OS: " + Environment.OSVersion);
                sb.AppendLine(".NET: " + Environment.Version);
                sb.AppendLine();

                if (timings.Any())
                {
                    sb.AppendLine("--- Timing Summary (ms) ---");
                    foreach (var kv in timings.OrderByDescending(kv => kv.Value))
                    {
                        var count = counts.GetValueOrDefault(kv.Key, 0);
                        // Convert ticks to milliseconds: ticks / (Stopwatch.Frequency / 1000)
                        var totalMs = (double)kv.Value * 1000.0 / Stopwatch.Frequency;
                        var avgMs = count > 0 ? totalMs / count : 0;
                        sb.AppendLine(kv.Key + ": total=" + totalMs.ToString("F3") + "ms, count=" + count + ", avg=" + avgMs.ToString("F3") + "ms");
                    }
                    sb.AppendLine();
                }

                // Calculate key metrics - convert ticks to ms
                double TicksToMs(long ticks) => (double)ticks * 1000.0 / Stopwatch.Frequency;

                var totalSync = TicksToMs(timings.GetValueOrDefault("SetLanguage.Total", 0L));
                var validation = TicksToMs(timings.GetValueOrDefault("SetLanguage.Validation", 0L));
                var registry = TicksToMs(timings.GetValueOrDefault("SetLanguage.Registry", 0L));
                var refreshTotal = TicksToMs(timings.GetValueOrDefault("RefreshResources.Total", 0L));
                var refreshAssign = TicksToMs(timings.GetValueOrDefault("RefreshResources.ResourceAssign", 0L));
                var languageChanged = TicksToMs(timings.GetValueOrDefault("SetLanguage.LanguageChanged", 0L));
                var applyToWindow = TicksToMs(timings.GetValueOrDefault("SetLanguage.ApplyToWindow", 0L));
                var visit = TicksToMs(timings.GetValueOrDefault("ApplyToWindow.Visit", 0L));
                var refreshBindings = TicksToMs(timings.GetValueOrDefault("ApplyToWindow.RefreshLocalizedBindings", 0L));
                var traversal = TicksToMs(timings.GetValueOrDefault("ApplyToWindow.RefreshLocalizedBindings.Traversal", 0L));
                var bindingUpdate = TicksToMs(timings.GetValueOrDefault("ApplyToWindow.RefreshLocalizedBindings.BindingUpdate", 0L));
                var settingsRefresh = TicksToMs(timings.GetValueOrDefault("Settings.LanguageChanged", 0L) +
                                    timings.GetValueOrDefault("Settings.RefreshAccounts", 0L) +
                                    timings.GetValueOrDefault("Settings.RefreshSecurityOverview", 0L) +
                                    timings.GetValueOrDefault("Settings.LoadBackupPolicyUi", 0L) +
                                    timings.GetValueOrDefault("Settings.RefreshVaultHealth", 0L));
                var cpFilter = TicksToMs(timings.GetValueOrDefault("MainWindow.LanguageChanged.FilterCommandPaletteResults", 0L));
                var showPage = TicksToMs(timings.GetValueOrDefault("ShowPage.Total", 0L));
                var syncSidebar = TicksToMs(timings.GetValueOrDefault("ShowPage.SyncSidebarSelection", 0L));

                sb.AppendLine("--- Key Metrics ---");
                sb.AppendLine("SetLanguage.Total: " + totalSync.ToString("F3") + "ms");
                sb.AppendLine("  - Validation: " + validation.ToString("F3") + "ms");
                sb.AppendLine("  - Registry: " + registry.ToString("F3") + "ms");
                sb.AppendLine("  - RefreshResources.Total: " + refreshTotal.ToString("F3") + "ms");
                sb.AppendLine("    - ResourceAssign: " + refreshAssign.ToString("F3") + "ms");
                sb.AppendLine("  - LanguageChanged: " + languageChanged.ToString("F3") + "ms");
                sb.AppendLine("  - ApplyToWindow: " + applyToWindow.ToString("F3") + "ms");
                sb.AppendLine("    - Visit: " + visit.ToString("F3") + "ms");
                sb.AppendLine("    - RefreshLocalizedBindings: " + refreshBindings.ToString("F3") + "ms");
                sb.AppendLine("      - Traversal: " + traversal.ToString("F3") + "ms");
                sb.AppendLine("      - BindingUpdate: " + bindingUpdate.ToString("F3") + "ms");
                sb.AppendLine("Settings.LanguageChanged: " + settingsRefresh + "ms");
                sb.AppendLine("MainWindow.LanguageChanged.FilterCommandPaletteResults: " + cpFilter + "ms");
                sb.AppendLine("ShowPage.Total: " + showPage + "ms");
                sb.AppendLine("  - SyncSidebarSelection: " + syncSidebar + "ms");
                sb.AppendLine();

                var targetMet = totalSync <= 500;
                sb.AppendLine("TARGET (<=500ms): " + (targetMet ? "MET" : "NOT MET") + " (" + totalSync + "ms)");
                sb.AppendLine();

                File.AppendAllText(filePath, sb.ToString());
                Debug.WriteLine(sb.ToString());
                System.Diagnostics.Trace.WriteLine(sb.ToString());
            }
            catch (Exception)
            {
                // Silent fail - profiling should never crash the app
            }
        }

        private static string GetLogDirectory()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logDir = Path.Combine(localAppData, "TQLTK", "Logs", "LanguageSwitchProfiler");
                Directory.CreateDirectory(logDir);
                return logDir;
            }
            catch
            {
                return null;
            }
        }
    }

    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, ObservedValue>> Observed = new();
    private static readonly ConditionalWeakTable<TextBlock, ObservedValue> LocalizedTextBlocks = new();
    public static event Action? LanguageChanged;

    public static string Fold(string s)
    {
        string decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString()
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .ToLowerInvariant();
    }

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
            ["RECOVER_SELECTED_BACKUP_COPIES_FAILED"] = "Recover backup entries as copies failed",
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

    // Dedicated localization ResourceDictionary key in MergedDictionaries
    private const string LocalizationDictionaryKey = "TQLTK.Localization";
    private static ResourceDictionary? _localizationDictionary;

    // Initialize the dedicated localization ResourceDictionary in MergedDictionaries
    // Fixed slot index for stable precedence (after theme/system dictionaries)
    private const int LocalizationDictionaryIndex = 2;

    public static void InitializeLocalizationDictionary()
    {
        if (Application.Current is not { } app) return;

        // Check if already initialized
        if (_localizationDictionary != null)
        {
            // Ensure it's at the correct index
            int currentIndex = app.Resources.MergedDictionaries.IndexOf(_localizationDictionary);
            if (currentIndex >= 0 && currentIndex != LocalizationDictionaryIndex)
            {
                app.Resources.MergedDictionaries.RemoveAt(currentIndex);
                app.Resources.MergedDictionaries.Insert(LocalizationDictionaryIndex, _localizationDictionary);
            }
            return;
        }

        // Create and add the dedicated localization dictionary (no Source URI)
        _localizationDictionary = new ResourceDictionary();

        // Insert at fixed index for stable precedence
        if (app.Resources.MergedDictionaries.Count > LocalizationDictionaryIndex)
        {
            app.Resources.MergedDictionaries.Insert(LocalizationDictionaryIndex, _localizationDictionary);
        }
        else
        {
            app.Resources.MergedDictionaries.Add(_localizationDictionary);
        }
    }

#if SECURITY_TEST
    internal static void SetLanguageForTesting(string code)
    {
        _currentCode = code == "en" ? "en" : "vi";
        RefreshResources();
    }
#endif
    public static string GetDisplayName(string code) => code == "en" ? "English" : "Tiếng Việt";
    public static void RefreshResources()
    {
        using (LanguageSwitchProfiler.Measure("RefreshResources.Total"))
        {
            if (Application.Current is not { } app) return;

            // Audit counters for Windows profiling
            int enumeratedCount = 0;

            // Build complete localization dictionary for batched swap
            var newLocalizationDict = new ResourceDictionary();

            foreach (var message in LocalizationAuditService.Catalog.Where(message => message.Id.StartsWith("xaml.", StringComparison.Ordinal) || message.Id.StartsWith("ui.", StringComparison.Ordinal)))
            {
                enumeratedCount++;
                string text = LocalizationAuditService.Get(message.Id, CurrentCode);

                // Build batched dictionary
                using (LanguageSwitchProfiler.Measure("RefreshResources.ResourceAssign"))
                {
                    newLocalizationDict[message.Id] = text;
                }
            }

            // Atomic swap via single index assignment (single invalidation)
            using (LanguageSwitchProfiler.Measure("RefreshResources.DictionarySwap"))
            {
                if (_localizationDictionary != null)
                {
                    int index = app.Resources.MergedDictionaries.IndexOf(_localizationDictionary);
                    if (index >= 0)
                    {
                        // Atomic swap: single invalidation
                        app.Resources.MergedDictionaries[index] = newLocalizationDict;
                        _localizationDictionary = newLocalizationDict;
                    }
                    else
                    {
                        // Dictionary was removed externally; re-add at fixed index
                        app.Resources.MergedDictionaries.Insert(LocalizationDictionaryIndex, newLocalizationDict);
                        _localizationDictionary = newLocalizationDict;
                    }
                }
                else
                {
                    // Fallback: first time or initialization issue - insert at fixed index
                    app.Resources.MergedDictionaries.Insert(LocalizationDictionaryIndex, newLocalizationDict);
                    _localizationDictionary = newLocalizationDict;
                }
            }

            // Debug output for Windows profiling analysis
            Debug.WriteLine($"RefreshResources Audit: enumerated={enumeratedCount}, resourceAssign={enumeratedCount}, dictSwap=1");
        }
    }
    public static void SetLanguage(string code)
    {
        // Use switch scope to ensure profiling for ALL language changes (including login/restore)
        using (LanguageSwitchProfiler.BeginSwitch(_currentCode, code.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi"))
        {
            string normalized;
            using (LanguageSwitchProfiler.Measure("SetLanguage.Validation"))
            {
                normalized = code.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
                if (_currentCode == normalized) { RefreshResources(); return; }
                var oldCode = _currentCode;
                _currentCode = normalized;
            }
            using (LanguageSwitchProfiler.Measure("SetLanguage.Total"))
            {
                using (LanguageSwitchProfiler.Measure("SetLanguage.Registry"))
                {
                    RegistryService.SetLanguage(normalized);
                }
                RefreshResources();
                using (LanguageSwitchProfiler.Measure("SetLanguage.LanguageChanged"))
                {
                    LanguageChanged?.Invoke();
                }
                if (Application.Current == null) return;
                using (LanguageSwitchProfiler.Measure("SetLanguage.ApplyToWindow"))
                {
                    foreach (Window window in Application.Current.Windows)
                        ApplyToWindow(window);
                }
            }
        }
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
        if (string.IsNullOrEmpty(content)) return content;
        string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        return string.Join(Environment.NewLine, normalized.Split('\n').Select(line => TranslateLogLine(line, languageCode)));
    }
    private static string TranslateLogLine(string line, string languageCode)
    {
        if (line.TrimStart().StartsWith('{'))
        {
            if (!ActivityLogCodec.TryParse(line, out var record) || record == null)
                return LocalizationAuditService.Get("log.unreadable", languageCode);
            string actionLabel = string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) && EnglishActivityActions.TryGetValue(record.EventId, out var label) ? label : record.EventId;
            string structuredDetail;
            try { structuredDetail = record.MessageId == null ? record.Detail : LocalizationAuditService.FormatForLanguage(record.MessageId, languageCode, ActivityLogCodec.GetArguments(record)); }
            catch (Exception ex) when (ex is KeyNotFoundException or FormatException or OverflowException)
            { structuredDetail = LocalizationAuditService.Get("log.unknown_message", languageCode); }
            return ActivityLogger.FormatLine(record.Timestamp.LocalDateTime, record.Username, actionLabel, structuredDetail);
        }
        if (!string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase)) return line;
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
        if (string.IsNullOrEmpty(value)) return value;
        if (LocalizationAuditService.TryTranslateKnown(value, languageCode, out string translated)) return translated;
        if (!string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase)) return value;
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
        using (LanguageSwitchProfiler.Measure("ApplyToWindow.Total"))
        {
            // RefreshResources is called once in SetLanguage; do not call again to avoid double-measurement
            using (LanguageSwitchProfiler.Measure("ApplyToWindow.Visit"))
            {
                Visit(window, new HashSet<DependencyObject>());
            }
            using (LanguageSwitchProfiler.Measure("ApplyToWindow.RefreshLocalizedBindings"))
            {
                RefreshLocalizedBindings(window, new HashSet<DependencyObject>());
            }
        }
    }
    private static void RefreshLocalizedBindings(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element)) return;

        // Measure binding update separately from traversal
        if (element is TextBlock textBlock &&
            BindingOperations.GetBindingExpression(textBlock, TextBlock.TextProperty) is { } binding &&
            binding.ParentBinding.Converter is LocalizedTextConverter)
        {
            using (LanguageSwitchProfiler.Measure("ApplyToWindow.RefreshLocalizedBindings.BindingUpdate"))
            {
                binding.UpdateTarget();
            }
        }

        // Measure ONLY the local traversal logic (iteration, not recursive calls)
        using (LanguageSwitchProfiler.Measure("ApplyToWindow.RefreshLocalizedBindings.Traversal"))
        {
            var visualChildren = new List<DependencyObject>();
            if (element is Visual or Visual3D)
            {
                int count = VisualTreeHelper.GetChildrenCount(element);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(element, i);
                    if (child is DependencyObject dep) visualChildren.Add(dep);
                }
            }
            var logicalChildren = new List<DependencyObject>();
            foreach (object child in LogicalTreeHelper.GetChildren(element))
                if (child is DependencyObject dep) logicalChildren.Add(dep);

            // Recursive calls happen OUTSIDE the traversal measurement
            foreach (var child in visualChildren)
                RefreshLocalizedBindings(child, visited);
            foreach (var child in logicalChildren)
                RefreshLocalizedBindings(child, visited);
        }
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
    public static void SetLocalizedFormattedKey(TextBlock textBlock, string key, Func<object?[]> arguments)
    {
        ObservedValue observation = LocalizedTextBlocks.GetValue(textBlock, _ => new());
        observation.Source = key;
        observation.Renderer = () => LocalizationAuditService.FormatForLanguage(key, CurrentCode, arguments());
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