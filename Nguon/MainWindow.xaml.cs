using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;
namespace PersonalInfoManager
{
    public partial class MainWindow : Window
    {
        private readonly AccountVaultService _vaultService = new();
        private readonly LocalFileService _fileService = new();
        private readonly UserService _userService = new();
        private List<SavedAccount> _accounts = new();
        private bool _showPassword = false;
        private bool _pwnedCheckCompleted;
        private bool _pwnedResultsAreCurrent;
        private readonly HashSet<Guid> _pwnedBreachedAccountIds = new();
        private readonly Dictionary<Guid, DateTime> _pwnedCheckedAccountVersions = new();
        private string? _pwnedStateUser;
        private DateTime? _pwnedLastCheckedUtc;
        private int _pwnedCheckedUniquePasswords;
        private int _pwnedBreachedUniquePasswords;
        private bool _pwnedStateLoadFailed;
        private bool _pwnedStateSaveFailed;
        private bool _accountFilterOptionsUpdating;
        private sealed record SecurityFindingViewItem(
            Guid AccountId,
            PasswordSecurityFindingKind Kind,
            string Description,
            string ReviewStatus);
        private int AutoLockMinutes = 5;
        private bool _lockWhenMinimized;
        private const int AutoHidePasswordSeconds = 30;
        private const int AuthRememberSeconds = 60;
        private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly DispatcherTimer _hidePasswordTimer = new() { Interval = TimeSpan.FromSeconds(AutoHidePasswordSeconds) };
        private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly DispatcherTimer _periodicBackupTimer = new() { Interval = TimeSpan.FromMinutes(5) };
        private bool _periodicBackupRunning;
        private DateTime _lastActivity = DateTime.UtcNow;
        private DateTime _lastAuthTime = DateTime.MinValue;
        private bool _isLocked = false;
        private readonly CancellationTokenSource _sessionRevocationCts = new();
        private readonly DispatcherTimer _saveStateTimer;
        public MainWindow()
        {
            InitializeComponent();
            cmbPwnedReminder.SelectedValue = RegistryService.GetPwnedPasswordReminderDays().ToString();
            cmbLanguageSettings.SelectedValuePath = "Tag";
            cmbLanguageSettings.SelectedValue = LanguageService.CurrentCode;
            cmbLanguageSettings.SelectionChanged += (_, _) =>
            {
                if (cmbLanguageSettings.SelectedValue is string language)
                {
                    LanguageService.SetLanguage(language);
                    if (SessionService.IsLoggedIn)
                    {
                        RefreshAccounts(updateStatus: false);
                        RefreshSecurityOverview();
                        LoadBackupPolicyUi();
                    }
                }
            };
            AutoLockMinutes = RegistryService.GetAutoLockMinutes();
            _lockWhenMinimized = RegistryService.GetLockWhenMinimized();
            var (w, h) = RegistryService.GetWindowSize(1100, 700);
            Width = w;
            Height = h;
            var (left, top) = RegistryService.GetWindowPosition();
            if (!double.IsNaN(left) && !double.IsNaN(top))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            _saveStateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _saveStateTimer.Tick += (_, _) =>
            {
                _saveStateTimer.Stop();
                SaveWindowState();
            };
            _idleTimer.Tick += IdleTimer_Tick;
            _hidePasswordTimer.Tick += (_, _) => HidePasswordNow();
            _searchTimer.Tick += (_, _) =>
            {
                _searchTimer.Stop();
                if (SessionService.IsLoggedIn) RefreshAccounts(updateStatus: false);
            };
            _periodicBackupTimer.Tick += PeriodicBackupTimer_Tick;
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    ToggleSidebar();
                    e.Handled = true;
                }
            };
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            LocationChanged += MainWindow_LocationChanged;
            SizeChanged += MainWindow_SizeChanged;
            StateChanged += MainWindow_StateChanged;
        }
        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            FitWindowToCurrentWorkArea();
            if (!SessionService.IsLoggedIn)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show("Chưa đăng nhập. Vui lòng đăng nhập lại.");
                var login = new LoginWindow();
                Application.Current.MainWindow = login;
                login.Show();
                Close();
                return;
            }
            if (!EnsureActiveSession()) return;
            try { _vaultService.PurgeExpiredTrash(SessionService.CurrentUser!.Username); }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show($"Không thể dọn thùng rác đã hết hạn. Dữ liệu hiện có vẫn được giữ nguyên.\n\n{ex.Message}",
                    "Không thể dọn thùng rác", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            BuildPages();
            LoadLockSettingsUi();
            LoadBackupPolicyUi();
            navAdmin.Visibility = SessionService.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
            UpdateUserChip();
            RefreshAccounts();
            if (_isLocked) return;
            await RefreshFilesAsync();
            if (_isLocked) return;
            UpdateStorageInfo();
            LoadProfile();
            UpdateAutoThemeCard();
            UpdateThemeIndicator();
            Update2FAMenu();
            StartAutoLock();
            _periodicBackupTimer.Start();
            navAccounts.IsChecked = true;
        }
        private void FitWindowToCurrentWorkArea()
        {
            WindowLayoutService.FitToWorkArea(this);
        }
        private const double SidebarWidth = 250;
        private bool _sidebarVisible = true;
        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e) => ToggleSidebar();
        private void ToggleSidebar()
        {
            _sidebarVisible = !_sidebarVisible;
            bool show = _sidebarVisible;
            double target = show ? SidebarWidth : 0;
            if (show) panelSidebar.Visibility = Visibility.Visible;
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            anim.Completed += (_, _) =>
            {
                if (_sidebarVisible != show) return;
                panelSidebar.BeginAnimation(WidthProperty, null);
                panelSidebar.Width = target;
                if (!show) panelSidebar.Visibility = Visibility.Collapsed;
            };
            panelSidebar.BeginAnimation(WidthProperty, anim);
        }
        private void GoProfile_Click(object sender, RoutedEventArgs e) => navProfile.IsChecked = true;
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopAutoLock();
            _periodicBackupTimer.Stop();
            foreach (EditAccountWindow editor in OwnedWindows.OfType<EditAccountWindow>().ToArray())
                editor.CloseForSessionEnd();
            try { _sessionRevocationCts.Cancel(); } catch { }
            try { ClipboardGuard.ClearNow(); } catch { }
            try { AccountVaultService.ClearCache(); } catch { }
            SaveWindowState();
        }
        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (IsLoaded && WindowState == WindowState.Normal) ResetSaveTimer();
        }
        private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (IsLoaded && WindowState == WindowState.Normal) ResetSaveTimer();
        }
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && _lockWhenMinimized && IsLoaded && SessionService.IsLoggedIn)
                LockNow("Cửa sổ được thu nhỏ theo tùy chọn bảo mật.");
        }
        private void ResetSaveTimer()
        {
            _saveStateTimer.Stop();
            _saveStateTimer.Start();
        }
        private void SaveWindowState()
        {
            try
            {
                if (WindowState == WindowState.Normal)
                {
                    RegistryService.SetWindowPosition(Left, Top);
                    RegistryService.SetWindowSize(Width, Height);
                }
                else if (WindowState == WindowState.Maximized)
                {
                    RegistryService.SetWindowPosition(RestoreBounds.Left, RestoreBounds.Top);
                    RegistryService.SetWindowSize(RestoreBounds.Width, RestoreBounds.Height);
                }
            }
            catch { }
        }
        private void StartAutoLock()
        {
            _lastActivity = DateTime.UtcNow;
            InputManager.Current.PreProcessInput += OnPreProcessInput;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _idleTimer.Start();
        }
        private void StopAutoLock()
        {
            try { InputManager.Current.PreProcessInput -= OnPreProcessInput; } catch { }
            try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
            try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; } catch { }
            _idleTimer.Stop();
            _hidePasswordTimer.Stop();
            _searchTimer.Stop();
        }
        private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
        {
            var input = e.StagingItem.Input;
            if (input is MouseEventArgs || input is KeyboardEventArgs)
                _lastActivity = DateTime.UtcNow;
        }
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (ShouldLockForSessionSwitch(e.Reason))
            {
                string why = e.Reason == SessionSwitchReason.SessionLock
                    ? "Windows bị khóa (Win+L)"
                    : $"Phiên Windows ngắt kết nối ({e.Reason})";
                Dispatcher.BeginInvoke(new Action(() => LockNow(why)));
            }
        }
        internal static bool ShouldLockForSessionSwitch(SessionSwitchReason reason) =>
            reason is SessionSwitchReason.SessionLock or
                SessionSwitchReason.ConsoleDisconnect or
                SessionSwitchReason.RemoteDisconnect;
        internal static bool ShouldLockForPowerMode(PowerModes mode) =>
            mode is PowerModes.Suspend or PowerModes.Resume;
        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (!ShouldLockForPowerMode(e.Mode)) return;
            string reason = e.Mode == PowerModes.Suspend
                ? "Windows chuyển sang chế độ ngủ."
                : "Windows vừa tiếp tục sau chế độ ngủ.";
            try
            {
                if (Dispatcher.CheckAccess()) LockNow(reason);
                else Dispatcher.Invoke(new Action(() => LockNow(reason)), DispatcherPriority.Send,
                    TimeSpan.FromSeconds(5));
            }
            catch { }
        }
        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            if (_isLocked) return;
            if (!EnsureActiveSession()) return;
            if (DateTime.UtcNow - _lastActivity >= TimeSpan.FromMinutes(AutoLockMinutes))
                LockNow();
        }
        private void LoadLockSettingsUi()
        {
            AutoLockMinutes = RegistryService.GetAutoLockMinutes();
            _lockWhenMinimized = RegistryService.GetLockWhenMinimized();
            chkFipsCompatiblePasswordHash.IsChecked = RegistryService.GetFipsCompatiblePasswordHash();
            foreach (var item in cmbAutoLockMinutes.Items.OfType<ComboBoxItem>())
            {
                if (int.TryParse(item.Tag?.ToString(), out int value) && value == AutoLockMinutes)
                {
                    cmbAutoLockMinutes.SelectedItem = item;
                    break;
                }
            }
            chkLockWhenMinimized.IsChecked = _lockWhenMinimized;
        }
        private void BtnSaveLockSettings_Click(object sender, RoutedEventArgs e)
        {
            if (cmbAutoLockMinutes.SelectedItem is not ComboBoxItem item ||
                !int.TryParse(item.Tag?.ToString(), out int minutes) || minutes is not (1 or 5 or 10 or 15))
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate("ui.mainwindow.message001"),
                    LanguageService.Translate("ui.mainwindow.message002"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AutoLockMinutes = minutes;
            _lockWhenMinimized = chkLockWhenMinimized.IsChecked == true;
            RegistryService.SetAutoLockMinutes(AutoLockMinutes);
            RegistryService.SetLockWhenMinimized(_lockWhenMinimized);
            PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate("ui.mainwindow.message003"),
                LanguageService.Translate("ui.mainwindow.message002"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void BtnSavePasswordHashSettings_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = chkFipsCompatiblePasswordHash.IsChecked == true;
            if (enabled != RegistryService.GetFipsCompatiblePasswordHash() &&
                PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate(
                    enabled
                        ? "Mật khẩu mới và hash được nâng cấp sau đăng nhập sẽ dùng PBKDF2-HMAC-SHA256 600.000 vòng. Hash Argon2id hiện tại vẫn được xác minh bình thường."
                        : "Mật khẩu mới và hash được nâng cấp sau đăng nhập sẽ dùng Argon2id. Hash PBKDF2 hiện tại vẫn được xác minh bình thường."),
                    LanguageService.Translate("ui.mainwindow.message004"), MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            {
                chkFipsCompatiblePasswordHash.IsChecked = RegistryService.GetFipsCompatiblePasswordHash();
                return;
            }
            RegistryService.SetFipsCompatiblePasswordHash(enabled);
            PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate("ui.mainwindow.message005"),
                LanguageService.Translate("ui.mainwindow.message002"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private async void BtnRotateVaultKey_Click(object sender, RoutedEventArgs e)
        {
            if (!await RequireAuthAsync("xoay khóa vault", "VAULT_KEY_ROTATION_DENIED")) return;
            User? authenticatedUser = SessionService.CurrentUser;
            User? currentUser = authenticatedUser == null ? null : _userService.FindById(authenticatedUser.Id);
            if (!CanRotateVaultKey(currentUser))
            {
                try { ActivityLogger.Log(authenticatedUser?.Username ?? "SYSTEM", "VAULT_KEY_ROTATION_DENIED", "Chỉ quản trị viên đang hoạt động được xoay khóa dùng chung."); } catch { }
                PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    LanguageService.Translate("ui.mainwindow.message006"),
                    LanguageService.Translate("ui.mainwindow.message007"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string username = currentUser!.Username;
            if (!BackupCrypto.HasStoredPassphrase(username))
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    LanguageService.Translate("ui.mainwindow.message008"),
                    LanguageService.Translate("ui.mainwindow.message009"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var dialog = new SaveFileDialog
            {
                Title = "Lưu bản phục hồi trước khi xoay khóa",
                Filter = "Bản sao lưu vault (*.vault)|*.vault",
                FileName = $"vault-before-key-rotation-{DateTime.Now:yyyyMMdd-HHmmss}.vault",
                AddExtension = true,
                DefaultExt = ".vault"
            };
            LocalizedFileDialog.Apply(dialog);
            if (dialog.ShowDialog(this) != true || !EnsureActiveSession()) return;
            if (PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    LanguageService.Translate("ui.mainwindow.message010"),
                    LanguageService.Translate("ui.mainwindow.message011"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            bool wasEnabled = IsEnabled;
            IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var operation = await Task.Run(() =>
                {
                    using var storageLock = AesCrypto.AcquireStorageLock();
                    var export = VaultBackupService.ExportToFile(username, dialog.FileName);
                    if (!export.ok)
                        throw new InvalidDataException("Không tạo được bản phục hồi: " + export.message);
                    var verification = VaultBackupService.InspectBackup(username, dialog.FileName);
                    if (!verification.ok)
                        throw new InvalidDataException("Bản phục hồi không qua xác minh: " + verification.message);
                    AutoBackupService.RecordBackup(username, dialog.FileName);
                    var result = VaultKeyRotationService.Rotate(username);
                    return (result, verification.accountCount);
                });
                if (_isLocked || !EnsureActiveSession()) return;
                AccountVaultService.ClearCache();
                try { ActivityLogger.Log(username, "VAULT_KEY_ROTATION", $"Đã mã hóa lại {operation.result.ReencryptedFiles} tệp; giữ {operation.result.PreservedLegacyBackups} backup dùng cụm mật khẩu."); } catch { }
                RefreshAccounts();
                RefreshSecurityOverview();
                string details = LanguageService.Format("ui.mainwindow.message012",
                    operation.result.ReencryptedFiles, operation.accountCount, dialog.FileName);
                if (operation.result.PreservedLegacyBackups > 0)
                    details += "\n" + LanguageService.Format("ui.mainwindow.message013",
                        operation.result.PreservedLegacyBackups);
                PersonalInfoManager.Services.LocalizedMessageBox.Show(details, LanguageService.Translate("ui.mainwindow.message014"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    LanguageService.Format("ui.mainwindow.message015",
                        LanguageService.Translate(ex.Message)),
                    LanguageService.Translate("ui.mainwindow.message016"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked) IsEnabled = wasEnabled;
            }
        }
        internal static bool CanRotateVaultKey(User? user) =>
            user is { IsActive: true, Role: UserRole.Admin };
        private bool EnsureActiveSession()
        {
            if (_isLocked) return false;
            var current = SessionService.CurrentUser;
            if (current != null)
            {
                try
                {
                    if (_userService.FindById(current.Id) is { IsActive: true })
                        return true;
                }
                catch { }
            }
            LockNow("Tài khoản đã bị khóa hoặc không thể xác minh trạng thái.");
            return false;
        }
        private void LockNow(string? reason = null)
        {
            if (_isLocked) return;
            _isLocked = true;
            string username = SessionService.CurrentUser?.Username ?? "SYSTEM";
            StopAutoLock();
            try { _sessionRevocationCts.Cancel(); } catch { }
            _showPassword = false;
            try { _accounts.Clear(); } catch { }
            try { dgAccounts.ItemsSource = null; } catch { }
            try { ClipboardGuard.ClearNow(); } catch { }
            try { AccountVaultService.ClearCache(); } catch { }
            try { ActivityLogger.Log(username, "AUTO_LOCK", reason ?? $"Không hoạt động {AutoLockMinutes} phút"); } catch { }
            var others = Application.Current.Windows.Cast<Window>()
                .Where(w => w != this && w.IsVisible).ToList();
            SessionService.Logout();
            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            foreach (var w in others)
            {
                try
                {
                    if (w is EditAccountWindow editor) editor.CloseForSessionEnd();
                    else w.Close();
                }
                catch { }
            }
            Close();
        }
        private void RefreshAccountsKeepSelection()
        {
            var selectedId = GetSelectedAccount()?.Id;
            RefreshAccounts();
            if (selectedId == null) return;
            foreach (var item in dgAccounts.Items)
            {
                var orig = item.GetType().GetProperty("_original")?.GetValue(item) as SavedAccount;
                if (orig != null && Equals(orig.Id, selectedId))
                {
                    dgAccounts.SelectedItem = item;
                    break;
                }
            }
        }
        private void ShowPasswordNow()
        {
            _showPassword = true;
            _hidePasswordTimer.Stop();
            _hidePasswordTimer.Start();
            RefreshAccountsKeepSelection();
        }
        private void HidePasswordNow()
        {
            _hidePasswordTimer.Stop();
            if (!_showPassword) return;
            _showPassword = false;
            RefreshAccountsKeepSelection();
        }
        private bool IsAuthFresh() =>
            _lastAuthTime != DateTime.MinValue &&
            (DateTime.UtcNow - _lastAuthTime).TotalSeconds < AuthRememberSeconds;
        private void MarkAuthenticated() => _lastAuthTime = DateTime.UtcNow;
        private async Task<bool> RequireAuthAsync(string actionText, string deniedLogCode)
        {
            if (!EnsureActiveSession()) return false;
            string username = SessionService.CurrentUser!.Username;
            if (IsAuthFresh()) return true;
            string localizedActionText = LanguageService.Translate(actionText);
            bool helloAvailable = false;
            try { helloAvailable = await WindowsHelloService.IsAvailableAsync(); } catch { }
            if (_isLocked || !EnsureActiveSession()) return false;
            if (helloAvailable)
            {
                bool ok = false;
                try { ok = await WindowsHelloService.VerifyAsync(this, $"Xác nhận để {localizedActionText}"); } catch { }
                if (_isLocked || !EnsureActiveSession()) return false;
                if (ok) { MarkAuthenticated(); return true; }
            }
            if (!TwoFactorService.IsEnabled(username))
            {
                try { ActivityLogger.Log(username, deniedLogCode, "Windows Hello thất bại, chưa bật 2FA"); } catch { }
                PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    $"Cần xác thực để {localizedActionText}, nhưng Windows Hello không thành công và bạn chưa bật xác thực 2 bước.\n\n" +
                    "Vào Bảo mật → Bật xác thực 2 bước (Authenticator) để có cách xác thực dự phòng.",
                    "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            bool ok2 = TwoFactorPromptWindow.AskIfEnabled(this, username);
            if (_isLocked || !EnsureActiveSession()) return false;
            if (ok2) { MarkAuthenticated(); return true; }
            try { ActivityLogger.Log(username, deniedLogCode, "Windows Hello và 2FA đều thất bại"); } catch { }
            PersonalInfoManager.Services.LocalizedMessageBox.Show("Xác thực thất bại.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        private void RefreshAccounts(bool updateStatus = true)
        {
            if (!EnsureActiveSession()) return;
            string user = SessionService.CurrentUser!.Username;
            EnsurePwnedAuditLoaded(user);
            try { _accounts = _vaultService.Search(user, ""); }
            catch (UnauthorizedAccessException)
            {
                LockNow("Tài khoản đã bị khóa.");
                return;
            }
            UpdateAccountFilterOptions(_accounts);
            _pwnedResultsAreCurrent = IsPwnedCheckCurrent(_accounts);
            var warningIds = PasswordSecurityAuditService.Analyze(
                    _accounts, _pwnedBreachedAccountIds, _pwnedResultsAreCurrent, DateTime.Now)
                .Select(finding => finding.AccountId)
                .ToHashSet();
            var filteredAccounts = AccountSearchService.Filter(_accounts, new AccountSearchFilters(
                txtSearch.Text,
                GetFilterValue(cmbDomainFilter),
                GetFilterValue(cmbFolderFilter),
                GetFilterValue(cmbTagFilter),
                chkFavoritesOnly.IsChecked == true,
                chkMissingTotpOnly.IsChecked == true,
                chkSecurityWarningOnly.IsChecked == true), warningIds);
            var viewList = filteredAccounts.Select(a =>
            {
                string tagsText = string.Join(", ", a.Tags ?? new List<string>());
                string folderAndTagsText = string.Join(" · ", new[] { a.Folder, tagsText }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                return new
                {
                    a.Id,
                    a.ServiceName,
                    a.Username,
                    PasswordMasked = _showPassword ? a.Password : "••••••••",
                    a.Note,
                    a.WebsiteUrl,
                    WebsiteDisplay = WebsiteDomainService.TryGetHost(a.WebsiteUrl, out string websiteHost)
                        ? websiteHost : "",
                    FolderAndTagsText = string.IsNullOrWhiteSpace(folderAndTagsText) ? "—" : folderAndTagsText,
                    FavoriteText = a.IsFavorite ? "★" : "",
                    a.CreatedAt,
                    a.UpdatedAt,
                    PasswordChangedText = a.PasswordChangedAt.HasValue
                        ? LocalizationAuditService.FormatDate(a.PasswordChangedAt.Value)
                        : "Chưa rõ",
                    TotpState = string.IsNullOrWhiteSpace(a.TotpSecret) ? "—" : "Có",
                    _original = a
                };
            }).ToList();
            dgAccounts.ItemsSource = viewList;
            if (updateStatus) UpdateStorageInfo();
        }
        private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        }
        private void AccountFilters_Changed(object? sender, RoutedEventArgs e)
        {
            if (!_accountFilterOptionsUpdating && IsLoaded) RefreshAccounts(updateStatus: false);
        }
        private bool IsPwnedVaultVersionCurrent(IReadOnlyCollection<SavedAccount> accounts) =>
            _pwnedCheckCompleted &&
            accounts.Count == _pwnedCheckedAccountVersions.Count &&
            accounts.All(account => _pwnedCheckedAccountVersions.TryGetValue(account.Id, out DateTime checkedVersion) &&
                checkedVersion.ToUniversalTime() == account.UpdatedAt.ToUniversalTime());
        private bool IsPwnedCheckCurrent(IReadOnlyCollection<SavedAccount> accounts) =>
            IsPwnedVaultVersionCurrent(accounts) &&
            !PwnedPasswordAuditStore.IsReminderDue(
                _pwnedLastCheckedUtc.HasValue
                    ? new PwnedPasswordAuditState(_pwnedLastCheckedUtc.Value, _pwnedCheckedUniquePasswords,
                        _pwnedBreachedUniquePasswords, _pwnedBreachedAccountIds.ToList(),
                        _pwnedCheckedAccountVersions.ToDictionary(pair => pair.Key, pair => pair.Value.ToUniversalTime()))
                    : null,
                RegistryService.GetPwnedPasswordReminderDays(), DateTime.UtcNow);
        private void EnsurePwnedAuditLoaded(string username)
        {
            if (string.Equals(_pwnedStateUser, username, StringComparison.OrdinalIgnoreCase)) return;
            _pwnedStateUser = username;
            _pwnedLastCheckedUtc = null;
            _pwnedCheckedUniquePasswords = 0;
            _pwnedBreachedUniquePasswords = 0;
            _pwnedBreachedAccountIds.Clear();
            _pwnedCheckedAccountVersions.Clear();
            _pwnedCheckCompleted = false;
            _pwnedResultsAreCurrent = false;
            _pwnedStateLoadFailed = false;
            _pwnedStateSaveFailed = false;
            try
            {
                PwnedPasswordAuditState? state = PwnedPasswordAuditStore.Load(username);
                if (state == null) return;
                _pwnedLastCheckedUtc = state.CheckedAtUtc;
                _pwnedCheckedUniquePasswords = state.CheckedUniquePasswords;
                _pwnedBreachedUniquePasswords = state.BreachedUniquePasswords;
                foreach (Guid id in state.BreachedAccountIds) _pwnedBreachedAccountIds.Add(id);
                foreach (var pair in state.AccountVersions) _pwnedCheckedAccountVersions[pair.Key] = pair.Value;
                _pwnedCheckCompleted = true;
            }
            catch
            {
                _pwnedStateLoadFailed = true;
            }
        }
        private void UpdatePwnedAuditStatus(IReadOnlyCollection<SavedAccount> accounts)
        {
            if (_pwnedStateLoadFailed && !_pwnedCheckCompleted)
            {
                LanguageService.SetLocalizedText(lblPwnedPasswordAudit,
                    "Không thể đọc kết quả kiểm tra rò rỉ đã lưu trong tài khoản Windows này. Hãy chạy lại kiểm tra nếu muốn.");
                return;
            }
            if (!_pwnedCheckCompleted || !_pwnedLastCheckedUtc.HasValue)
            {
                LanguageService.SetLocalizedText(lblPwnedPasswordAudit,
                    "Chưa chạy kiểm tra rò rỉ. Kiểm tra là tùy chọn và không bao giờ tự gửi yêu cầu.");
                return;
            }
            string status = !IsPwnedVaultVersionCurrent(accounts)
                ? "Vault đã thay đổi sau lần kiểm tra này; kết quả đã cũ."
                : PwnedPasswordAuditStore.IsReminderDue(
                    new PwnedPasswordAuditState(_pwnedLastCheckedUtc.Value, _pwnedCheckedUniquePasswords,
                        _pwnedBreachedUniquePasswords, _pwnedBreachedAccountIds.ToList(),
                        _pwnedCheckedAccountVersions.ToDictionary(pair => pair.Key, pair => pair.Value.ToUniversalTime())),
                    RegistryService.GetPwnedPasswordReminderDays(), DateTime.UtcNow)
                    ? "Đã đến hạn nhắc kiểm tra lại theo lựa chọn của bạn."
                    : "Kết quả khớp với vault hiện tại.";
            if (_pwnedStateSaveFailed)
                status += " Không thể lưu kết quả; dữ liệu này chỉ dùng được đến khi đóng ứng dụng.";
            LanguageService.SetLocalizedFormattedText(
                lblPwnedPasswordAudit,
                "Lần kiểm tra HIBP gần nhất: {0} • đã kiểm tra {1} mật khẩu duy nhất • {2} giá trị bị lộ • {3}",
                () => new object?[]
                {
                    LocalizationAuditService.FormatDateTime(_pwnedLastCheckedUtc.Value),
                    _pwnedCheckedUniquePasswords,
                    _pwnedBreachedUniquePasswords,
                    LanguageService.Translate(status)
                });
        }
        private void PwnedReminder_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || !int.TryParse(cmbPwnedReminder.SelectedValue?.ToString(), out int days)) return;
            RegistryService.SetPwnedPasswordReminderDays(days);
            if (SessionService.IsLoggedIn) RefreshSecurityOverview();
        }
        private static string GetFilterValue(ComboBox combo) =>
            (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        private void UpdateAccountFilterOptions(IReadOnlyCollection<SavedAccount> accounts)
        {
            _accountFilterOptionsUpdating = true;
            try
            {
                UpdateFilterCombo(cmbDomainFilter, "Tất cả tên miền", accounts
                    .Select(account => AccountSearchService.GetHost(account.WebsiteUrl))
                    .Where(host => host.Length > 0));
                UpdateFilterCombo(cmbFolderFilter, "Tất cả thư mục", accounts
                    .Select(account => (account.Folder ?? "").Trim())
                    .Where(folder => folder.Length > 0));
                UpdateFilterCombo(cmbTagFilter, "Tất cả nhãn", accounts
                    .SelectMany(account => account.Tags ?? new List<string>())
                    .Select(tag => (tag ?? "").Trim())
                    .Where(tag => tag.Length > 0));
            }
            finally { _accountFilterOptionsUpdating = false; }
        }
        private static void UpdateFilterCombo(ComboBox combo, string allLabel, IEnumerable<string> values)
        {
            string selected = GetFilterValue(combo);
            var items = values.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                .Take(500)
                .ToList();
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem
            {
                Content = LanguageService.Translate(allLabel),
                Tag = ""
            });
            foreach (string value in items)
                combo.Items.Add(new ComboBoxItem { Content = value, Tag = value });
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selected, StringComparison.OrdinalIgnoreCase))
                ?? combo.Items[0];
        }
        private void BtnRefreshAccounts_Click(object? sender, RoutedEventArgs e)
        {
            txtSearch.Clear();
            _searchTimer.Stop();
            RefreshAccounts();
        }
        private async void BtnToggleShow_Click(object? sender, RoutedEventArgs e)
        {
            if (!EnsureActiveSession()) return;
            if (_showPassword)
            {
                HidePasswordNow();
                return;
            }
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                string username = SessionService.CurrentUser!.Username;
                if (IsAuthFresh())
                {
                    ShowPasswordNow();
                    try { ActivityLogger.Log(username, "SHOW_PASSWORD", "Nhớ xác thực"); } catch { }
                    return;
                }
                bool available = await WindowsHelloService.IsAvailableAsync();
                if (_isLocked || !EnsureActiveSession()) return;
                if (!available)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        "Hiện mật khẩu cần xác thực bằng Windows Hello nhưng máy này chưa hỗ trợ hoặc chưa thiết lập.\n\n" +
                        "Hãy cài đặt vân tay / khuôn mặt / PIN trong Cài đặt Windows rồi thử lại.",
                        "Không thể xác thực", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                bool verified = await WindowsHelloService.VerifyAsync(this, "Xác nhận để hiện mật khẩu");
                if (_isLocked || !EnsureActiveSession()) return;
                if (!verified)
                {
                    try { ActivityLogger.Log(username, "SHOW_PASSWORD_DENIED", "Xác thực Windows Hello thất bại"); } catch { }
                    PersonalInfoManager.Services.LocalizedMessageBox.Show("Xác thực thất bại. Mật khẩu vẫn được ẩn.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                MarkAuthenticated();
                ShowPasswordNow();
                try { ActivityLogger.Log(username, "SHOW_PASSWORD", ""); } catch { }
            }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }
        private void BtnAddAccount_Click(object? sender, RoutedEventArgs e)
        {
            var dlg = new EditAccountWindow(null);
            if (dlg.ShowDialog() == true)
            {
                string user = SessionService.CurrentUser!.Username;
                var list = _vaultService.Load(user);
                list.Add(dlg.Result);
                _vaultService.Save(user, list);
                try { ActivityLogger.Log(user, "ADD_ACCOUNT", $"Dịch vụ: {dlg.Result.ServiceName}"); } catch { }
                RefreshAccounts();
            }
        }
        private async void BtnEditAccount_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Chọn một tài khoản để sửa."); return; }
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            bool authorized = false;
            try
            {
                authorized = await RequireAuthAsync("sửa tài khoản (cửa sổ Sửa hiển thị được mật khẩu)", "EDIT_ACCOUNT_DENIED");
            }
            catch (Exception ex)
            {
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
            if (!authorized || _isLocked) return;
            var dlg = new EditAccountWindow(selected) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                string user = SessionService.CurrentUser!.Username;
                var list = _vaultService.Load(user);
                var existing = list.FirstOrDefault(x => x.Id == selected.Id);
                if (existing != null)
                {
                    AccountRevisionSnapshot previous = AccountRevisionService.Capture(existing);
                    bool passwordChanged = !string.Equals(existing.Password, dlg.Result.Password, StringComparison.Ordinal);
                    existing.ServiceName = dlg.Result.ServiceName;
                    existing.Username = dlg.Result.Username;
                    existing.Password = dlg.Result.Password;
                    existing.Note = dlg.Result.Note;
                    existing.WebsiteUrl = dlg.Result.WebsiteUrl;
                    existing.Folder = dlg.Result.Folder;
                    existing.Tags = dlg.Result.Tags;
                    existing.IsFavorite = dlg.Result.IsFavorite;
                    existing.CustomFields = dlg.Result.CustomFields;
                    existing.PasswordHistory = dlg.Result.PasswordHistory;
                    existing.DeletedNotes = dlg.Result.DeletedNotes;
                    existing.TotpSecret = dlg.Result.TotpSecret;
                    existing.TotpIssuer = dlg.Result.TotpIssuer;
                    existing.TotpAlgorithm = dlg.Result.TotpAlgorithm;
                    existing.TotpDigits = dlg.Result.TotpDigits;
                    existing.TotpPeriodSeconds = dlg.Result.TotpPeriodSeconds;
                    if (passwordChanged) existing.PasswordChangedAt = DateTime.Now;
                    existing.UpdatedAt = DateTime.Now;
                    AccountRevisionService.RecordPreviousIfChanged(existing, previous, DateTime.UtcNow);
                    MarkImportUndoUnavailable(existing);
                }
                _vaultService.Save(user, list);
                try { ActivityLogger.Log(user, "EDIT_ACCOUNT", dlg.Result.ServiceName); } catch { }
                RefreshAccounts();
                RefreshSecurityOverview();
            }
        }
        private void BtnMoreAccountActions_Click(object? sender, RoutedEventArgs e)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PasswordHistory"] = "Lịch sử mật khẩu",
                ["Totp"] = "Mã TOTP",
                ["Website"] = "Mở website",
                ["Attachments"] = "Tệp đính kèm",
                ["EditHistory"] = "Lịch sử sửa",
                ["Share"] = "Chia sẻ tạm thời",
                ["Bulk"] = "Thao tác hàng loạt",
                ["Export"] = "Xuất mục đã chọn",
                ["UndoImport"] = "Hoàn tác nhập",
                ["Delete"] = "Xóa"
            };
            foreach (Button item in accountActionPopupItems.Children.OfType<Button>())
                if (item.Tag is string key && labels.TryGetValue(key, out string? label))
                    item.Content = LanguageService.Translate(label);
            popMoreAccountActions.PlacementTarget = btnMoreAccountActions;
            popMoreAccountActions.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            popMoreAccountActions.VerticalOffset = -6;
            popMoreAccountActions.IsOpen = !popMoreAccountActions.IsOpen;
        }
        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (popMoreAccountActions.IsOpen && !btnMoreAccountActions.IsMouseOver)
                popMoreAccountActions.IsOpen = false;
        }
        private void AccountActionPopup_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            popMoreAccountActions.IsOpen = false;
            btnMoreAccountActions.Focus();
            e.Handled = true;
        }
        private void AccountActionPopupItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button item || item.Tag is not string action) return;
            popMoreAccountActions.IsOpen = false;
            switch (action)
            {
                case "PasswordHistory": BtnPasswordHistory_Click(sender, e); break;
                case "Totp": BtnShowTotp_Click(sender, e); break;
                case "Website": BtnOpenWebsite_Click(sender, e); break;
                case "Attachments": BtnManageAttachments_Click(sender, e); break;
                case "EditHistory": BtnAccountHistory_Click(sender, e); break;
                case "Share": BtnCreateShare_Click(sender, e); break;
                case "Bulk": BtnBulkActions_Click(sender, e); break;
                case "Export": BtnExportSelected_Click(sender, e); break;
                case "UndoImport": BtnUndoCsvImport_Click(sender, e); break;
                case "Delete": BtnDeleteAccount_Click(sender, e); break;
            }
        }
        private async void BtnPasswordHistory_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Chọn một tài khoản để xem lịch sử mật khẩu."); return; }
            if (!await RequireAuthAsync("xem lịch sử mật khẩu", "PASSWORD_HISTORY_DENIED")) return;
            string username = SessionService.CurrentUser!.Username;
            var latest = _vaultService.Load(username).FirstOrDefault(account => account.Id == selected.Id);
            if (latest == null) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Tài khoản không còn trong kho mật khẩu.", "Không tìm thấy", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!EnsureActiveSession()) return;
            var dialog = new PasswordHistoryWindow(latest) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedEntry == null) return;
            if (!EnsureActiveSession()) return;
            if (PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    $"Đặt lại mật khẩu của '{latest.ServiceName}' về mật khẩu đã dùng ngày {dialog.SelectedEntry.ChangedAt:dd/MM/yyyy HH:mm}?",
                    "Dùng lại mật khẩu cũ", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            if (!EnsureActiveSession()) return;
            var accounts = _vaultService.Load(username);
            var target = accounts.FirstOrDefault(account => account.Id == latest.Id);
            if (target == null) return;
            string restoredPassword = dialog.SelectedEntry.Password;
            if (string.Equals(restoredPassword, target.Password, StringComparison.Ordinal)) return;
            var history = (target.PasswordHistory ?? new List<PasswordHistoryEntry>())
                .Where(entry => entry != null)
                .Select(entry => entry.Clone()).ToList();
            history.RemoveAll(entry => string.Equals(entry.Password, restoredPassword, StringComparison.Ordinal) ||
                                       string.Equals(entry.Password, target.Password, StringComparison.Ordinal));
            history.Insert(0, new PasswordHistoryEntry { Password = target.Password, ChangedAt = DateTime.Now });
            target.PasswordHistory = history.Take(5).ToList();
            target.Password = restoredPassword;
            target.UpdatedAt = DateTime.Now;
            target.PasswordChangedAt = DateTime.Now;
            MarkImportUndoUnavailable(target);
            try
            {
                _vaultService.Save(username, accounts);
            }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show($"Không thể khôi phục mật khẩu từ lịch sử. Kho mật khẩu chưa được cập nhật.\n\n{ex.Message}",
                    "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            try { ActivityLogger.Log(username, "RESTORE_PASSWORD_HISTORY", latest.ServiceName); } catch { }
            RefreshAccounts();
        }
        private void BtnDeleteAccount_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Chọn một tài khoản để xóa."); return; }
            if (PersonalInfoManager.Services.LocalizedMessageBox.Show($"Đưa '{selected.ServiceName}' vào thùng rác 30 ngày?", "Xóa tài khoản", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            string user = SessionService.CurrentUser!.Username;
            var list = _vaultService.Load(user);
            var target = list.FirstOrDefault(x => x.Id == selected.Id);
            if (target == null) return;
            target.IsDeleted = true;
            target.DeletedAt = DateTime.UtcNow;
            MarkImportUndoUnavailable(target);
            _vaultService.Save(user, list);
            try { ActivityLogger.Log(user, "MOVE_ACCOUNT_TO_TRASH", selected.ServiceName); } catch { }
            RefreshAccounts();
            UpdateStorageInfo();
        }
        private async void BtnShowTotp_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null || string.IsNullOrWhiteSpace(selected.TotpSecret))
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show("Tài khoản này chưa có mã TOTP.", "Mã TOTP", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!await RequireAuthAsync("xem mã TOTP", "SHOW_ACCOUNT_TOTP_DENIED")) return;
            string username = SessionService.CurrentUser!.Username;
            var latest = _vaultService.Load(username).FirstOrDefault(item => item.Id == selected.Id && !item.IsDeleted);
            if (latest == null || string.IsNullOrWhiteSpace(latest.TotpSecret)) return;
            new TotpCodeWindow(latest) { Owner = this }.ShowDialog();
        }
        private void BtnOpenTrash_Click(object? sender, RoutedEventArgs e)
        {
            if (!EnsureActiveSession()) return;
            string username = SessionService.CurrentUser!.Username;
            try { _vaultService.PurgeExpiredTrash(username); }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show($"Không thể cập nhật thùng rác. Dữ liệu hiện có vẫn được giữ nguyên.\n\n{ex.Message}",
                    "Không thể mở thùng rác", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var items = BuildTrashRows(_vaultService.Load(username));
            var window = new TrashWindow(items,
                item => Task.FromResult(RestoreTrashItem(username, item)),
                item => DeleteTrashItemForeverAsync(username, item),
                () => BuildTrashRows(_vaultService.Load(username))) { Owner = this };
            window.ShowDialog();
            if (!EnsureActiveSession()) return;
            RefreshAccounts();
            UpdateStorageInfo();
        }
        private static List<TrashItemRow> BuildTrashRows(IEnumerable<SavedAccount> accounts)
        {
            var rows = new List<TrashItemRow>();
            foreach (var account in accounts)
            {
                if (account.IsDeleted && account.DeletedAt.HasValue)
                    rows.Add(new TrashItemRow
                    {
                        AccountId = account.Id,
                        IsAccount = true,
                        Title = account.ServiceName,
                        Detail = account.Username,
                        DeletedAt = account.DeletedAt.Value
                    });
                foreach (var note in account.DeletedNotes ?? new List<DeletedNoteEntry>())
                    rows.Add(new TrashItemRow
                    {
                        AccountId = account.Id,
                        NoteId = note.Id,
                        IsAccount = false,
                        Title = account.ServiceName,
                        Detail = "Ghi chú đã xóa",
                        DeletedAt = note.DeletedAt
                    });
            }
            return rows;
        }
        private bool RestoreTrashItem(string username, TrashItemRow item)
        {
            if (!EnsureActiveSession()) return false;
            var accounts = _vaultService.Load(username);
            var account = accounts.FirstOrDefault(entry => entry.Id == item.AccountId);
            if (account == null) return false;
            if (item.IsAccount)
            {
                account.IsDeleted = false;
                account.DeletedAt = null;
                MarkImportUndoUnavailable(account);
                _vaultService.Save(username, accounts);
                try { ActivityLogger.Log(username, "RESTORE_ACCOUNT_FROM_TRASH", account.ServiceName); } catch { }
                return true;
            }
            if (account.IsDeleted)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show("Khôi phục tài khoản trước khi khôi phục ghi chú của tài khoản đó.",
                    "Chưa thể khôi phục", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            var note = account.DeletedNotes.FirstOrDefault(entry => entry.Id == item.NoteId);
            if (note == null) return false;
            if (!string.IsNullOrWhiteSpace(account.Note))
            {
                if (PersonalInfoManager.Services.LocalizedMessageBox.Show($"Ghi chú hiện tại của '{account.ServiceName}' sẽ được đưa vào thùng rác và thay bằng ghi chú được khôi phục. Tiếp tục?",
                        "Khôi phục ghi chú", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return false;
                account.DeletedNotes.Add(new DeletedNoteEntry { Content = account.Note, DeletedAt = DateTime.UtcNow });
            }
            account.Note = note.Content;
            account.DeletedNotes.RemoveAll(entry => entry.Id == note.Id);
            account.UpdatedAt = DateTime.Now;
            MarkImportUndoUnavailable(account);
            _vaultService.Save(username, accounts);
            try { ActivityLogger.Log(username, "RESTORE_NOTE_FROM_TRASH", account.ServiceName); } catch { }
            return true;
        }
        private async Task<bool> DeleteTrashItemForeverAsync(string username, TrashItemRow item)
        {
            if (!await RequireAuthAsync("xóa hẳn mục khỏi thùng rác", "TRASH_PERMANENT_DELETE_DENIED")) return false;
            if (!EnsureActiveSession()) return false;
            var accounts = _vaultService.Load(username);
            var account = accounts.FirstOrDefault(entry => entry.Id == item.AccountId);
            if (account == null) return false;
            MarkImportUndoUnavailable(account);
            if (item.IsAccount) accounts.Remove(account);
            else account.DeletedNotes.RemoveAll(note => note.Id == item.NoteId);
            _vaultService.Save(username, accounts);
            try { ActivityLogger.Log(username, item.IsAccount ? "PERMANENT_DELETE_ACCOUNT" : "PERMANENT_DELETE_NOTE", account.ServiceName); } catch { }
            return true;
        }
        private void BtnCopyUsername_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) return;
            try { Clipboard.SetText(selected.Username); PersonalInfoManager.Services.LocalizedMessageBox.Show("Đã sao chép tên đăng nhập!"); } catch { }
        }
        private async void BtnCopyPassword_Click(object? sender, RoutedEventArgs e)
        {
            if (!EnsureActiveSession()) return;
            var selected = GetSelectedAccount();
            if (selected == null) return;
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                string username = SessionService.CurrentUser!.Username;
                string method = "";
                bool ok = false;
                if (IsAuthFresh())
                {
                    ok = true;
                    method = $"nhớ xác thực ≤{AuthRememberSeconds}s";
                }
                if (!ok)
                {
                    bool helloAvailable = false;
                    try { helloAvailable = await WindowsHelloService.IsAvailableAsync(); } catch { }
                    if (_isLocked || !EnsureActiveSession()) return;
                    if (helloAvailable)
                    {
                        try { ok = await WindowsHelloService.VerifyAsync(this, "Xác nhận để sao chép mật khẩu"); } catch { ok = false; }
                        if (_isLocked || !EnsureActiveSession()) return;
                        if (ok) { method = "Windows Hello"; MarkAuthenticated(); }
                    }
                }
                if (!ok)
                {
                    if (!TwoFactorService.IsEnabled(username))
                    {
                        try { ActivityLogger.Log(username, "COPY_PASSWORD_DENIED", "Windows Hello thất bại, chưa bật 2FA"); } catch { }
                        PersonalInfoManager.Services.LocalizedMessageBox.Show(
                            "Xác thực Windows Hello không thành công và bạn chưa bật xác thực 2 bước nên không thể sao chép mật khẩu.\n\n" +
                            "Vào Bảo mật → Bật xác thực 2 bước (Authenticator) để có cách xác thực dự phòng.",
                            "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    ok = TwoFactorPromptWindow.AskIfEnabled(this, username);
                    if (_isLocked || !EnsureActiveSession()) return;
                    if (ok) { method = "2FA"; MarkAuthenticated(); }
                }
                if (!ok)
                {
                    try { ActivityLogger.Log(username, "COPY_PASSWORD_DENIED", "Windows Hello và 2FA đều thất bại"); } catch { }
                    PersonalInfoManager.Services.LocalizedMessageBox.Show("Xác thực thất bại. Đã hủy sao chép mật khẩu.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!EnsureActiveSession()) return;
                if (!ClipboardGuard.CopySensitive(selected.Password))
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        "Không thể sao chép mật khẩu vào bộ nhớ tạm an toàn. Hãy thử lại.",
                        "Không thể sao chép", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                try { ActivityLogger.Log(username, "COPY_PASSWORD", $"{selected.ServiceName} (xác thực: {method})"); } catch { }
                PersonalInfoManager.Services.LocalizedMessageBox.Show($"Đã sao chép mật khẩu! Bộ nhớ tạm sẽ tự xóa sau {ClipboardGuard.ClearAfterSeconds} giây.");
            }
            catch (Exception ex)
            {
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }
        private void DgAccounts_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }
        private SavedAccount? GetSelectedAccount()
        {
            if (dgAccounts.SelectedItem == null) return null;
            var item = dgAccounts.SelectedItem;
            var prop = item.GetType().GetProperty("_original");
            return prop?.GetValue(item) as SavedAccount;
        }
        private List<SavedAccount> GetSelectedAccounts() => dgAccounts.SelectedItems
            .Cast<object>()
            .Select(item => item.GetType().GetProperty("_original")?.GetValue(item) as SavedAccount)
            .Where(account => account != null)
            .Cast<SavedAccount>()
            .DistinctBy(account => account.Id)
            .ToList();
        private static void MarkImportUndoUnavailable(SavedAccount account)
        {
            if (account.ImportBatchId == null) return;
            account.ImportUndoable = false;
            account.ImportUndoSnapshot = null;
        }
        private void BtnOpenWebsite_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Chọn một tài khoản trước."); return; }
            if (!WebsiteDomainService.TryNormalizeUrl(selected.WebsiteUrl, out Uri? uri, out string host) || uri == null)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message017"),
                    LanguageService.Translate("ui.mainwindow.message018"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var risks = WebsiteDomainService.GetRiskWarnings(selected.WebsiteUrl);
            if (risks.Count > 0 && PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                    LanguageService.Format("ui.mainwindow.message019", host,
                        string.Join("\n", risks.Select(LanguageService.Translate))),
                    LanguageService.Translate("ui.mainwindow.message020"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Format("ui.mainwindow.message021", ex.Message),
                    LanguageService.Translate("ui.mainwindow.message022"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private async void BtnManageAttachments_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Chọn một tài khoản trước."); return; }
            if (!await RequireAuthAsync("quản lý tệp đính kèm", "ACCOUNT_ATTACHMENTS_DENIED")) return;
            string username = SessionService.CurrentUser!.Username;
            var accounts = _vaultService.Load(username);
            var account = accounts.FirstOrDefault(item => item.Id == selected.Id && !item.IsDeleted);
            if (account == null || !EnsureActiveSession()) return;
            var dialog = new AccountAttachmentsWindow(account.Attachments) { Owner = this };
            if (dialog.ShowDialog() != true || !EnsureActiveSession()) return;
            account.Attachments = dialog.Result.Select(item => item.Clone()).ToList();
            account.UpdatedAt = DateTime.Now;
            MarkImportUndoUnavailable(account);
            _vaultService.Save(username, accounts);
            try { ActivityLogger.Log(username, "EDIT_ACCOUNT_ATTACHMENTS", account.ServiceName); } catch { }
            RefreshAccounts();
        }
        private async void BtnAccountHistory_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Chọn một tài khoản trước."); return; }
            if (!await RequireAuthAsync("xem và hoàn tác lịch sử chỉnh sửa", "ACCOUNT_HISTORY_DENIED")) return;
            string username = SessionService.CurrentUser!.Username;
            var accounts = _vaultService.Load(username);
            var account = accounts.FirstOrDefault(item => item.Id == selected.Id && !item.IsDeleted);
            if (account == null || !EnsureActiveSession()) return;
            var dialog = new AccountHistoryWindow(account.EditHistory) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedEntry == null || !EnsureActiveSession()) return;
            account = _vaultService.Load(username).FirstOrDefault(item => item.Id == selected.Id && !item.IsDeleted);
            if (account == null) return;
            var revision = account.EditHistory.FirstOrDefault(item => item.Id == dialog.SelectedEntry.Id);
            if (revision == null) return;
            AccountRevisionSnapshot current = AccountRevisionService.Capture(account);
            AccountRevisionService.Apply(account, revision.Snapshot);
            AccountRevisionService.RecordPreviousIfChanged(account, current, DateTime.UtcNow);
            account.EditHistory.RemoveAll(item => item.Id == revision.Id);
            account.UpdatedAt = DateTime.Now;
            MarkImportUndoUnavailable(account);
            var latest = _vaultService.Load(username);
            int index = latest.FindIndex(item => item.Id == account.Id);
            if (index < 0) return;
            latest[index] = account;
            _vaultService.Save(username, latest);
            try { ActivityLogger.Log(username, "RESTORE_ACCOUNT_REVISION", account.ServiceName); } catch { }
            RefreshAccounts();
        }
        private async void BtnBulkActions_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccounts();
            if (selected.Count == 0) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Chọn một hoặc nhiều tài khoản trước."); return; }
            if (!await RequireAuthAsync("thay đổi nhiều tài khoản cùng lúc", "BULK_ACCOUNT_ACTION_DENIED")) return;
            var dialog = new BulkAccountActionsWindow(selected.Count) { Owner = this };
            if (dialog.ShowDialog() != true || !EnsureActiveSession()) return;
            if (dialog.MoveToTrash && PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                    LanguageService.Format("ui.mainwindow.message023", selected.Count),
                    LanguageService.Translate("ui.mainwindow.message024"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            string username = SessionService.CurrentUser!.Username;
            var accounts = _vaultService.Load(username);
            DateTime now = DateTime.Now;
            int changed = 0;
            foreach (var viewAccount in selected)
            {
                var account = accounts.FirstOrDefault(item => item.Id == viewAccount.Id && !item.IsDeleted);
                if (account == null) continue;
                AccountRevisionSnapshot previous = AccountRevisionService.Capture(account);
                if (dialog.ApplyFolder) account.Folder = dialog.Folder;
                if (dialog.ApplyTags) account.Tags = dialog.Tags.ToList();
                var revision = AccountRevisionService.RecordPreviousIfChanged(account, previous, now.ToUniversalTime());
                if (dialog.MoveToTrash)
                {
                    account.IsDeleted = true;
                    account.DeletedAt = DateTime.UtcNow;
                }
                if (revision != null || dialog.MoveToTrash)
                {
                    account.UpdatedAt = now;
                    MarkImportUndoUnavailable(account);
                    changed++;
                }
            }
            if (changed == 0) return;
            _vaultService.Save(username, accounts);
            try { ActivityLogger.Log(username, "BULK_ACCOUNT_ACTION", $"Thay đổi {changed} tài khoản"); } catch { }
            RefreshAccounts();
        }
        private async void BtnExportSelected_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccounts();
            if (selected.Count == 0) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Chọn một hoặc nhiều tài khoản trước."); return; }
            if (!await RequireAuthAsync("xuất các tài khoản đã chọn", "EXPORT_CSV_DENIED")) return;
            var dialog = new SaveFileDialog
            {
                Title = "Xuất tài khoản đã chọn",
                Filter = "Tệp CSV (*.csv)|*.csv|Tất cả tệp (*.*)|*.*",
                FileName = $"accounts_selected_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            LocalizedFileDialog.Apply(dialog);
            if (dialog.ShowDialog() != true || !EnsureActiveSession()) return;
            var current = _vaultService.Load(SessionService.CurrentUser!.Username)
                .Where(account => !account.IsDeleted && selected.Any(item => item.Id == account.Id)).ToList();
            var result = CsvService.ExportToFile(current, dialog.FileName);
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(result.message),
                LanguageService.Translate(result.ok ? "Thành công" : "Lỗi xuất CSV"), MessageBoxButton.OK,
                result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        private async void BtnUndoCsvImport_Click(object? sender, RoutedEventArgs e)
        {
            if (!await RequireAuthAsync("hoàn tác lần nhập gần nhất", "UNDO_IMPORT_DENIED")) return;
            string username = SessionService.CurrentUser!.Username;
            CsvImportBatchRecord? batch;
            try { batch = CsvImportBatchStore.Load(username); }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message025") + ex.Message,
                    LanguageService.Translate("ui.mainwindow.message022"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (batch == null)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message026"),
                    LanguageService.Translate("ui.mainwindow.message027"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var accounts = _vaultService.Load(username);
            var affected = accounts.Where(account => batch.AccountIds.Contains(account.Id)).ToList();
            if (affected.Count != batch.AccountIds.Distinct().Count() || affected.Any(account =>
                    account.ImportBatchId != batch.Id || !account.ImportUndoable ||
                    (!account.CreatedInImportBatch && account.ImportUndoSnapshot == null)))
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message028"),
                    LanguageService.Translate("ui.mainwindow.message029"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                    LanguageService.Format("ui.mainwindow.message030", affected.Count),
                    LanguageService.Translate("ui.mainwindow.message031"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes || !EnsureActiveSession())
                return;
            foreach (var account in affected)
            {
                if (account.CreatedInImportBatch)
                {
                    accounts.Remove(account);
                    continue;
                }
                AccountRevisionService.RestoreImportUndo(account, account.ImportUndoSnapshot!);
                account.EditHistory.RemoveAll(entry => entry.ImportBatchId == batch.Id);
                account.ImportBatchId = null;
                account.ImportBatchAtUtc = null;
                account.ImportUndoable = false;
                account.CreatedInImportBatch = false;
                account.ImportUndoSnapshot = null;
            }
            _vaultService.Save(username, accounts);
            CsvImportBatchStore.Clear(username);
            try { ActivityLogger.Log(username, "UNDO_IMPORT", batch.Id.ToString("N")); } catch { }
            RefreshAccounts();
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message032"),
                LanguageService.Translate("ui.mainwindow.message033"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void RefreshFiles()
        {
            if (!EnsureActiveSession()) return;
            string user = SessionService.CurrentUser!.Username;
            try
            {
                dgFiles.ItemsSource = _fileService.ListFiles(user);
            }
            catch (Exception ex)
            {
                dgFiles.ItemsSource = null;
                PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    $"Không thể đọc hoặc mã hóa kho File cá nhân. File chưa chuyển đổi được vẫn được giữ nguyên.\n\n{ex.Message}",
                    "Kho File cá nhân", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private async Task RefreshFilesAsync()
        {
            if (!EnsureActiveSession()) return;
            string user = SessionService.CurrentUser!.Username;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var files = await Task.Run(() => _fileService.ListFiles(user, _sessionRevocationCts.Token));
                if (_isLocked || !EnsureActiveSession()) return;
                dgFiles.ItemsSource = files;
            }
            catch (OperationCanceledException)
            {
                if (!_isLocked) dgFiles.ItemsSource = null;
            }
            catch (Exception ex)
            {
                dgFiles.ItemsSource = null;
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        $"Không thể đọc hoặc mã hóa kho File cá nhân. File chưa chuyển đổi được vẫn được giữ nguyên.\n\n{ex.Message}",
                        "Kho File cá nhân", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        private void BtnImport_Click(object? sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Chọn tệp cần nhập", Filter = "Tất cả tệp (*.*)|*.*", Multiselect = true };
            LocalizedFileDialog.Apply(dlg);
            if (dlg.ShowDialog() != true) return;
            string user = SessionService.CurrentUser!.Username;
            foreach (var path in dlg.FileNames)
            {
                try
                {
                    _fileService.ImportFile(user, path);
                    try { ActivityLogger.Log(user, "IMPORT_FILE", Path.GetFileName(path)); } catch { }
                }
                catch (Exception ex) { PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}"); }
            }
            RefreshFiles();
            UpdateStorageInfo();
        }
        private void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            if (dgFiles.SelectedItem is not FileInfo fi) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Chọn tệp cần xuất."); return; }
            var dlg = new SaveFileDialog { Title = "Xuất tệp", Filter = "Tất cả tệp (*.*)|*.*", FileName = fi.Name };
            LocalizedFileDialog.Apply(dlg);
            if (dlg.ShowDialog() != true) return;
            try
            {
                _fileService.ExportFile(fi.FullName, dlg.FileName);
                try { ActivityLogger.Log(SessionService.CurrentUser!.Username, "EXPORT_FILE", fi.Name); } catch { }
                PersonalInfoManager.Services.LocalizedMessageBox.Show("Xuất thành công!");
            }
            catch (Exception ex) { PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}"); }
        }
        private void BtnCreateText_Click(object? sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "Tạo tệp văn bản", Filter = "Tệp văn bản (*.txt)|*.txt", FileName = "new_file.txt" };
            LocalizedFileDialog.Apply(dlg);
            if (dlg.ShowDialog() != true) return;
            string name = Path.GetFileName(dlg.FileName);
            _fileService.SaveTextFile(SessionService.CurrentUser!.Username, name, "Nội dung mẫu\n");
            try { ActivityLogger.Log(SessionService.CurrentUser!.Username, "CREATE_FILE", name); } catch { }
            RefreshFiles();
            UpdateStorageInfo();
        }
        private void BtnDeleteFile_Click(object? sender, RoutedEventArgs e)
        {
            if (dgFiles.SelectedItem is not FileInfo fi) return;
            if (PersonalInfoManager.Services.LocalizedMessageBox.Show($"Xóa {fi.Name}?", "Xác nhận", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _fileService.DeleteFile(fi.FullName);
            try { ActivityLogger.Log(SessionService.CurrentUser!.Username, "DELETE_FILE", fi.Name); } catch { }
            RefreshFiles();
            UpdateStorageInfo();
        }
        private void BtnRefreshFiles_Click(object? sender, RoutedEventArgs e) => RefreshFiles();
        private void LoadProfile()
        {
            var u = SessionService.CurrentUser!;
            txtProfileUsername.Text = u.Username;
            txtProfileFullName.Text = u.FullName;
            txtProfileEmail.Text = u.Email;
            txtProfilePhone.Text = u.Phone;
            lblProfileRole.Text = u.Role == UserRole.Admin ? "👑 Quản trị viên" : "👤 Người dùng";
            lblProfileCreatedAt.Text = LocalizationAuditService.FormatDateTime(u.CreatedAt, includeSeconds: true);
            try
            {
                if (u.WindowsHelloEnabled)
                {
                    LanguageService.SetLocalizedText(lblWindowsHelloBtnText, "Tắt Windows Hello");
                    btnToggleWindowsHello.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                    LanguageService.SetLocalizedText(lblWindowsHelloStatus, "✓ Đang bật đăng nhập bằng Windows Hello.");
                }
                else
                {
                    LanguageService.SetLocalizedText(lblWindowsHelloBtnText, "Bật Windows Hello");
                    btnToggleWindowsHello.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
                    LanguageService.SetLocalizedText(lblWindowsHelloStatus, "Chưa bật đăng nhập bằng Windows Hello.");
                }
            }
            catch { }
        }
        private void BtnSaveProfile_Click(object? sender, RoutedEventArgs e)
        {
            var u = SessionService.CurrentUser!;
            var result = _userService.UpdateProfile(u.Id, txtProfileFullName.Text, txtProfileEmail.Text, txtProfilePhone.Text);
            if (result.ok)
            {
                var updated = _userService.FindById(u.Id);
                if (updated != null) SessionService.Login(updated);
                try { ActivityLogger.Log(u.Username, "UPDATE_PROFILE", ""); } catch { }
                LoadProfile();
                UpdateUserChip();
                PersonalInfoManager.Services.LocalizedMessageBox.Show("Đã lưu thông tin cá nhân!");
            }
            else { PersonalInfoManager.Services.LocalizedMessageBox.Show(result.message, "Lỗi"); }
        }
        private async void BtnToggleWindowsHello_Click(object sender, RoutedEventArgs e)
        {
            var u = SessionService.CurrentUser!;
            btnToggleWindowsHello.IsEnabled = false;
            try
            {
                if (!u.WindowsHelloEnabled)
                {
                    bool available = await WindowsHelloService.IsAvailableAsync();
                    if (!available) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Máy tính không hỗ trợ Windows Hello.", "Không khả dụng", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                    string sid = WindowsHelloService.GetCurrentWindowsSid();
                    if (string.IsNullOrWhiteSpace(sid)) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Không lấy được SID tài khoản Windows.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error); return; }
                    var confirm = PersonalInfoManager.Services.LocalizedMessageBox.Show($"Bật đăng nhập bằng Windows Hello cho '{u.Username}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;
                    bool verified = await WindowsHelloService.VerifyAsync(this, $"Xác nhận bật Windows Hello");
                    if (!verified) { PersonalInfoManager.Services.LocalizedMessageBox.Show("Xác thực thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                    var result = _userService.SetWindowsHello(u.Id, true, sid);
                    if (result.ok)
                    {
                        var updated = _userService.FindById(u.Id);
                        if (updated != null) SessionService.Login(updated);
                        ActivityLogger.Log(u.Username, "ENABLE_WINDOWS_HELLO", "");
                        PersonalInfoManager.Services.LocalizedMessageBox.Show("Đã bật thành công!", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadProfile();
                    }
                }
                else
                {
                    var confirm = PersonalInfoManager.Services.LocalizedMessageBox.Show($"Tắt đăng nhập bằng Windows Hello cho '{u.Username}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;
                    var result = _userService.SetWindowsHello(u.Id, false, "");
                    if (result.ok)
                    {
                        var updated = _userService.FindById(u.Id);
                        if (updated != null) SessionService.Login(updated);
                        ActivityLogger.Log(u.Username, "DISABLE_WINDOWS_HELLO", "");
                        PersonalInfoManager.Services.LocalizedMessageBox.Show("Đã tắt thành công!", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadProfile();
                    }
                }
            }
            catch (Exception ex) { PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}"); }
            finally { btnToggleWindowsHello.IsEnabled = true; }
        }
        private static void SetCardState(TextBlock label, bool on)
        {
            LanguageService.SetLocalizedText(label, on ? "● Đang bật" : "○ Đang tắt");
            if (on)
            {
                label.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(34, 197, 94));
            }
            else
            {
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            }
        }
        private void UpdateAutoThemeCard()
        {
            try { SetCardState(lblAutoThemeState, AutoThemeService.IsEnabled); } catch { }
        }
        private void Update2FAMenu()
        {
            try
            {
                string user = SessionService.CurrentUser!.Username;
                bool enabled = TwoFactorService.IsEnabled(user);
                LanguageService.SetLocalizedText(lbl2FATitle, enabled ? "Tắt xác thực 2 bước" : "Bật xác thực 2 bước");
                SetCardState(lbl2FAState, enabled);
                card2FARegen.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }
        private void Mnu2FARegen_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string username = SessionService.CurrentUser!.Username;
                if (!TwoFactorService.IsEnabled(username))
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show("Bạn chưa bật xác thực 2 bước nên chưa có mã khôi phục.",
                        "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    Update2FAMenu();
                    return;
                }
                int remaining = TwoFactorService.GetRecoveryCodeCount(username);
                string remainText = remaining >= 0 ? $"Hiện còn {remaining} mã khôi phục chưa dùng.\n" : "";
                List<string>? newCodes = null;
                var prompt = new TwoFactorPromptWindow(
                    "Tạo lại mã khôi phục",
                    remainText + "Nhập mã 6 số hiện tại từ Authenticator để tạo 8 mã khôi phục mới (không dùng mã khôi phục cũ).",
                    code =>
                    {
                        var r = TwoFactorService.PrepareRecoveryCodes(username, code);
                        if (r.ok) newCodes = r.recoveryCodes;
                        return (r.ok, r.message);
                    })
                { Owner = this };
                if (prompt.ShowDialog() != true || newCodes == null) return;
                var codesWindow = new RecoveryCodesWindow(username, newCodes) { Owner = this };
                if (codesWindow.ShowDialog() == true)
                {
                    try { ActivityLogger.Log(username, "REGENERATE_RECOVERY_CODES", ""); } catch { }
                    PersonalInfoManager.Services.LocalizedMessageBox.Show("Đã tạo mã khôi phục mới. Các mã cũ không còn dùng được.",
                        "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void Mnu2FA_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string username = SessionService.CurrentUser!.Username;
                if (!TwoFactorService.IsEnabled(username))
                {
                    var setup = new TwoFactorSetupWindow(username) { Owner = this };
                    setup.ShowDialog();
                    if (TwoFactorService.IsEnabled(username))
                    {
                        try { ActivityLogger.Log(username, "ENABLE_2FA", ""); } catch { }
                    }
                }
                else
                {
                    var dlg = new TwoFactorPromptWindow(
                        "Tắt xác thực 2 bước",
                        "Nhập mã 6 số từ Authenticator (hoặc mã khôi phục) để xác nhận tắt.",
                        code => TwoFactorService.Disable(username, code))
                    { Owner = this };
                    if (dlg.ShowDialog() == true)
                    {
                        try { ActivityLogger.Log(username, "DISABLE_2FA", ""); } catch { }
                        PersonalInfoManager.Services.LocalizedMessageBox.Show("Đã tắt xác thực 2 bước.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                Update2FAMenu();
            }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async void MnuExportCsv_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string username = SessionService.CurrentUser!.Username;
                bool available = await WindowsHelloService.IsAvailableAsync();
                if (!available)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        "Xuất CSV cần xác thực bằng Windows Hello nhưng máy này chưa hỗ trợ hoặc chưa thiết lập.\n\n" +
                        "Hãy cài đặt vân tay / khuôn mặt / PIN trong Cài đặt Windows rồi thử lại.",
                        "Không thể xác thực", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                bool verified = await WindowsHelloService.VerifyAsync(this,
                    "Xác nhận để xuất CSV. File sẽ chứa mật khẩu, khóa TOTP, trường tùy chỉnh và URL đầy đủ dạng chữ thường.");
                if (!verified)
                {
                    try { ActivityLogger.Log(username, "EXPORT_CSV_DENIED", "Xác thực Windows Hello thất bại"); } catch { }
                    PersonalInfoManager.Services.LocalizedMessageBox.Show("Xác thực thất bại. Đã hủy xuất CSV.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var dlg = new SaveFileDialog
                {
                    Title = "Xuất CSV",
                    Filter = "Tệp CSV (*.csv)|*.csv|Tất cả tệp (*.*)|*.*",
                    FileName = $"accounts_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };
                LocalizedFileDialog.Apply(dlg);
                if (dlg.ShowDialog() != true) return;
                var accounts = _vaultService.Load(username).Where(account => !account.IsDeleted).ToList();
                if (accounts.Count == 0)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show("Không có tài khoản nào để xuất.", "Thông báo");
                    return;
                }
                var res = CsvService.ExportToFile(accounts, dlg.FileName);
                if (!res.ok)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(res.message, "Lỗi xuất CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                ActivityLogger.Log(username, "EXPORT_CSV", $"Xuất {accounts.Count} tài khoản ra CSV");
                PersonalInfoManager.Services.LocalizedMessageBox.Show(res.message, "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi xuất CSV: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async void MnuImportCsv_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!await RequireAuthAsync("nhập dữ liệu tài khoản", "IMPORT_CREDENTIALS_DENIED")) return;
                var dlg = new OpenFileDialog
                {
                    Title = "Nhập dữ liệu tài khoản",
                    Filter = "Tệp tài khoản (*.csv;*.json;*.xml;*.kdbx)|*.csv;*.json;*.xml;*.kdbx|CSV (*.csv)|*.csv|JSON (*.json)|*.json|KeePass XML (*.xml)|*.xml|Tất cả tệp (*.*)|*.*"
                };
                LocalizedFileDialog.Apply(dlg);
                if (dlg.ShowDialog() != true) return;
                string username = SessionService.CurrentUser!.Username;
                CredentialImportResult parsed = CredentialImportService.ImportFromFile(dlg.FileName);
                if (!parsed.Ok)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(parsed.Message, LanguageService.Translate("ui.mainwindow.message034"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var preview = new CsvImportWindow(parsed.Accounts,
                    _vaultService.Load(username).Where(account => !account.IsDeleted).ToList(), sourceFormat: parsed.SourceFormat) { Owner = this };
                bool confirmed = preview.ShowDialog() == true;
                if (_isLocked || !confirmed || !EnsureActiveSession()) return;
                var list = _vaultService.Load(username);
                var original = list.Select(account => account.Clone()).ToList();
                CsvImportBatchRecord? previousBatch = CsvImportBatchStore.Load(username);
                if (previousBatch != null)
                {
                    foreach (var prior in list.Where(account => account.ImportBatchId == previousBatch.Id))
                    {
                        prior.ImportUndoable = false;
                        prior.ImportUndoSnapshot = null;
                    }
                }
                Guid batchId = preview.BatchId;
                DateTime now = DateTime.UtcNow;
                var sourceToTarget = new Dictionary<Guid, Guid>();
                var affectedIds = new HashSet<Guid>();
                int added = 0;
                int overwritten = 0;
                int skipped = 0;
                foreach (var decision in preview.Decisions)
                {
                    if (!EnsureActiveSession()) return;
                    if (decision.Action == CsvImportAction.Skip) { skipped++; continue; }
                    if (decision.Action is CsvImportAction.ImportNew or CsvImportAction.ImportCopy)
                    {
                        var copy = decision.Account.Clone();
                        copy.Id = Guid.NewGuid();
                        copy.CreatedAt = now.ToLocalTime();
                        copy.UpdatedAt = now.ToLocalTime();
                        copy.IsDeleted = false;
                        copy.DeletedAt = null;
                        copy.ImportBatchId = batchId;
                        copy.ImportBatchAtUtc = now;
                        copy.ImportUndoable = true;
                        copy.CreatedInImportBatch = true;
                        copy.ImportUndoSnapshot = null;
                        list.Add(copy);
                        sourceToTarget[decision.Account.Id] = copy.Id;
                        affectedIds.Add(copy.Id);
                        added++;
                        continue;
                    }
                    Guid? requestedTarget = decision.TargetAccountId;
                    if (requestedTarget.HasValue && sourceToTarget.TryGetValue(requestedTarget.Value, out Guid mapped))
                        requestedTarget = mapped;
                    var target = requestedTarget.HasValue
                        ? list.FirstOrDefault(account => account.Id == requestedTarget.Value && !account.IsDeleted)
                        : null;
                    if (target == null)
                    {
                        skipped++;
                        continue;
                    }
                    bool isNewInThisBatch = target.CreatedInImportBatch && target.ImportBatchId == batchId;
                    if (!isNewInThisBatch && (target.ImportBatchId != batchId || !target.ImportUndoable))
                        target.ImportUndoSnapshot = AccountRevisionService.CaptureForImportUndo(target);
                    AccountRevisionSnapshot previousMetadata = AccountRevisionService.Capture(target);
                    bool passwordChanged = !string.Equals(target.Password, decision.Account.Password, StringComparison.Ordinal);
                    string priorPassword = target.Password;
                    target.ServiceName = decision.Account.ServiceName;
                    target.Username = decision.Account.Username;
                    target.Password = decision.Account.Password;
                    target.Note = decision.Account.Note;
                    target.WebsiteUrl = decision.Account.WebsiteUrl;
                    target.Folder = decision.Account.Folder;
                    target.Tags = decision.Account.Tags.ToList();
                    target.IsFavorite = decision.Account.IsFavorite;
                    target.CustomFields = decision.Account.CustomFields.Select(field => field.Clone()).ToList();
                    if (decision.IncludeAttachments)
                        target.Attachments = decision.Account.Attachments.Select(attachment => attachment.Clone()).ToList();
                    target.TotpSecret = decision.Account.TotpSecret;
                    target.TotpIssuer = decision.Account.TotpIssuer;
                    target.TotpAlgorithm = decision.Account.TotpAlgorithm;
                    target.TotpDigits = decision.Account.TotpDigits;
                    target.TotpPeriodSeconds = decision.Account.TotpPeriodSeconds;
                    if (passwordChanged)
                    {
                        target.PasswordHistory ??= new List<PasswordHistoryEntry>();
                        if (!string.IsNullOrEmpty(priorPassword))
                            target.PasswordHistory.Insert(0, new PasswordHistoryEntry { Password = priorPassword, ChangedAt = now.ToLocalTime() });
                        target.PasswordHistory = target.PasswordHistory.Take(5).ToList();
                        target.PasswordChangedAt = now.ToLocalTime();
                    }
                    target.UpdatedAt = now.ToLocalTime();
                    AccountRevisionService.RecordPreviousIfChanged(target, previousMetadata, now, batchId);
                    target.ImportBatchId = batchId;
                    target.ImportBatchAtUtc = now;
                    target.ImportUndoable = true;
                    if (!isNewInThisBatch) target.CreatedInImportBatch = false;
                    sourceToTarget[decision.Account.Id] = target.Id;
                    affectedIds.Add(target.Id);
                    overwritten++;
                }
                if (affectedIds.Count == 0)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message035"),
                        LanguageService.Translate("ui.mainwindow.message036"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _vaultService.Save(username, list);
                try
                {
                    CsvImportBatchStore.Save(username, new CsvImportBatchRecord
                    {
                        Id = batchId,
                        CreatedAtUtc = now,
                        AccountIds = affectedIds.ToList()
                    });
                }
                catch
                {
                    _vaultService.Save(username, original);
                    if (previousBatch != null) CsvImportBatchStore.Save(username, previousBatch);
                    else CsvImportBatchStore.Clear(username);
                    throw;
                }
                RefreshAccounts();
                RefreshSecurityOverview();
                UpdateStorageInfo();
                ActivityLogger.Log(username, "IMPORT_CREDENTIALS", $"Thêm {added}, ghi đè {overwritten}, bỏ qua {skipped} ({parsed.Message})");
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Format("ui.mainwindow.message037", added, overwritten, skipped),
                    LanguageService.Translate("ui.mainwindow.message036"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message038") + ex.Message, LanguageService.Translate("ui.mainwindow.message022"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private void BtnKeyboardShortcuts_Click(object? sender, RoutedEventArgs e) =>
            new KeyboardShortcutsWindow { Owner = this }.ShowDialog();
        private void MnuAdmin_Click(object? sender, RoutedEventArgs e) => new AdminWindow().ShowDialog();
        private void MnuChangePwd_Click(object? sender, RoutedEventArgs e) => new ChangePasswordWindow().ShowDialog();
        private void MnuSysInfo_Click(object? sender, RoutedEventArgs e) => PersonalInfoManager.Services.LocalizedMessageBox.Show(SystemInfoService.GetSystemReport(), LanguageService.Translate("ui.mainwindow.message039"));
        private void MnuLogout_Click(object? sender, RoutedEventArgs e)
        {
            var u = SessionService.CurrentUser;
            try { ActivityLogger.Log(u?.Username ?? "SYSTEM", "LOGOUT"); } catch { }
            SessionService.Logout();
            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            Close();
        }
        private void MnuExit_Click(object? sender, RoutedEventArgs e)
        {
            var u = SessionService.CurrentUser;
            try { ActivityLogger.Log(u?.Username ?? "SYSTEM", "EXIT_APP"); } catch { }
            Close();
        }
        private void MnuToggleTheme_Click(object? sender, RoutedEventArgs e)
        {
            ThemeService.Toggle();
            UpdateThemeIndicator();
        }
        private void UpdateThemeIndicator()
        {
            try
            {
                string theme = ThemeService.IsDarkMode ? "🌙 Tối" : "☀️ Sáng";
                string auto = AutoThemeService.IsEnabled ? " • Auto" : "";
                lblTheme.Text = $"{theme}{auto}";
            }
            catch { }
        }
        private void MnuAutoTheme_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                bool enable = !AutoThemeService.IsEnabled;
                if (enable)
                {
                    AutoThemeService.Start();
                    RegistryService.SetAutoThemeEnabled(true);
                    string currentTheme = AutoThemeService.GetCurrentThemeByTime();
                    string message = LanguageService.Format(
                        "ui.mainwindow.message040",
                        DateTime.Now.ToString("HH:mm"), LanguageService.Translate(currentTheme));
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(message, "Giao diện tự động");
                }
                else
                {
                    AutoThemeService.Stop();
                    RegistryService.SetAutoThemeEnabled(false);
                PersonalInfoManager.Services.LocalizedMessageBox.Show("Đã tắt tự động đổi giao diện theo giờ.", "Giao diện tự động");
                }
                UpdateAutoThemeCard();
                UpdateThemeIndicator();
            }
            catch (Exception ex) { PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}"); }
        }
        private void LoadBackupPolicyUi()
        {
            if (SessionService.CurrentUser is not { } currentUser) return;
            string username = currentUser.Username;
            chkPeriodicBackup.IsChecked = AutoBackupService.IsPeriodicEnabled(username);
            SelectComboTag(cmbPeriodicBackupInterval, AutoBackupService.GetPeriodicIntervalHours(username));
            SelectComboTag(cmbBackupRetentionCount, AutoBackupService.GetRetentionCount(username));
            SelectComboTag(cmbBackupRetentionDays, AutoBackupService.GetRetentionDays(username));
            SelectComboTag(cmbBackupStorageLimit, AutoBackupService.GetStorageLimitMb(username));
            SelectComboTag(cmbBackupDailyRetention, AutoBackupService.GetDailyRetention(username));
            SelectComboTag(cmbBackupWeeklyRetention, AutoBackupService.GetWeeklyRetention(username));
            SelectComboTag(cmbBackupMonthlyRetention, AutoBackupService.GetMonthlyRetention(username));
            lblPeriodicBackupStatus.Text = LanguageService.Translate(AutoBackupService.GetPeriodicStatusLine(username));
        }
        private static void SelectComboTag(ComboBox combo, int value)
        {
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), value.ToString(), StringComparison.Ordinal));
        }
        private int ReadComboTag(ComboBox combo, int fallback)
        {
            return combo.SelectedItem is ComboBoxItem item &&
                   int.TryParse(item.Tag?.ToString(), out int value) ? value : fallback;
        }
        private void BtnSaveBackupPolicy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SessionService.CurrentUser is not { } currentUser) return;
                string username = currentUser.Username;
                AutoBackupService.SetPeriodicEnabled(username, chkPeriodicBackup.IsChecked == true);
                AutoBackupService.SetPeriodicIntervalHours(username, ReadComboTag(cmbPeriodicBackupInterval, 24));
                AutoBackupService.SetRetentionCount(username, ReadComboTag(cmbBackupRetentionCount, 7));
                AutoBackupService.SetRetentionDays(username, ReadComboTag(cmbBackupRetentionDays, 90));
                AutoBackupService.SetStorageLimitMb(username, ReadComboTag(cmbBackupStorageLimit, 2048));
                AutoBackupService.SetCalendarRetention(username,
                    ReadComboTag(cmbBackupDailyRetention, 0), ReadComboTag(cmbBackupWeeklyRetention, 0),
                    ReadComboTag(cmbBackupMonthlyRetention, 0));
                AutoBackupService.RetentionResult result = AutoBackupService.PruneCompleteBackups(username);
                lblPeriodicBackupStatus.Text = LanguageService.Translate(AutoBackupService.GetPeriodicStatusLine(username));
                if (!string.IsNullOrWhiteSpace(result.Message))
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(result.Message),
                        LanguageService.Translate("ui.mainwindow.message041"), MessageBoxButton.OK, MessageBoxImage.Warning);
                else
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message042"),
                        LanguageService.Translate("ui.mainwindow.message043"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                    LanguageService.Format("ui.mainwindow.message044", ex.Message),
                    LanguageService.Translate("ui.mainwindow.message022"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private async void PeriodicBackupTimer_Tick(object? sender, EventArgs e)
        {
            if (_periodicBackupRunning || _isLocked || !SessionService.IsLoggedIn || !EnsureActiveSession()) return;
            string username = SessionService.CurrentUser!.Username;
            if (!AutoBackupService.IsPeriodicEnabled(username)) return;
            _periodicBackupRunning = true;
            try
            {
                var result = await Task.Run(() => AutoBackupService.RunPeriodicIfDue(username, _sessionRevocationCts.Token),
                    _sessionRevocationCts.Token);
                if (_isLocked || !EnsureActiveSession()) return;
                lblPeriodicBackupStatus.Text = LanguageService.Translate(AutoBackupService.GetPeriodicStatusLine(username));
                if (result.Attempted && !result.Succeeded)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(result.Message),
                        LanguageService.Translate("ui.mainwindow.message045"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AutoBackupService.RecordCompleteBackupFailure(username, ex.Message);
                lblPeriodicBackupStatus.Text = LanguageService.Translate(AutoBackupService.GetPeriodicStatusLine(username));
            }
            finally
            {
                _periodicBackupRunning = false;
            }
        }
        private async void MnuCreateCompleteBackup_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!await RequireAuthAsync("tạo gói sao lưu đầy đủ", "CREATE_COMPLETE_BACKUP_DENIED")) return;
                string username = SessionService.CurrentUser!.Username;
                string? passphrase = BackupCrypto.GetStoredPassphrase(username);
                if (passphrase == null)
                {
                    MessageBoxResult choose = PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                        LanguageService.Translate("ui.mainwindow.message046"),
                        LanguageService.Translate("ui.mainwindow.message047"), MessageBoxButton.OKCancel, MessageBoxImage.Information);
                    if (choose != MessageBoxResult.OK || !await SetBackupPassphraseAsync()) return;
                    passphrase = BackupCrypto.GetStoredPassphrase(username);
                }
                if (string.IsNullOrEmpty(passphrase) || !EnsureActiveSession()) return;
                string folder = AutoBackupService.GetFolder(username);
                Directory.CreateDirectory(folder);
                var dialog = new SaveFileDialog
                {
                    Title = "Lưu gói sao lưu đầy đủ",
                    Filter = "Gói sao lưu TQLTK (*.tqltk)|*.tqltk",
                    InitialDirectory = folder,
                    FileName = $"TQLTK-Full-{DateTime.Now:yyyyMMdd-HHmmss}.tqltk",
                    AddExtension = true,
                    DefaultExt = ".tqltk",
                    OverwritePrompt = true
                };
                LocalizedFileDialog.Apply(dialog);
                if (dialog.ShowDialog() != true || !EnsureActiveSession()) return;
                Mouse.OverrideCursor = Cursors.Wait;
                bool includeNotes = chkCompleteBackupNotes.IsChecked == true;
                bool includeFiles = chkCompleteBackupFiles.IsChecked == true;
                bool includeSettings = chkCompleteBackupSettings.IsChecked == true;
                var created = await Task.Run(() => CompleteBackupService.Create(username, dialog.FileName, passphrase,
                    includeNotes, includeFiles, includeSettings));
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (!created.ok || created.info == null)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(created.message),
                        LanguageService.Translate("ui.mainwindow.message048"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (includeNotes && includeFiles && includeSettings)
                    AutoBackupService.RecordCompleteBackup(username, dialog.FileName, created.info);
                AutoBackupService.RetentionResult retention = AutoBackupService.PruneCompleteBackups(username);
                lblPeriodicBackupStatus.Text = LanguageService.Translate(AutoBackupService.GetPeriodicStatusLine(username));
                string summary = LocalizationAuditService.Format("backup.created.contents_summary",
                    Path.GetFileName(dialog.FileName), created.info.AccountCount,
                    created.info.NoteCount, LanguageService.Translate(created.info.IncludesNotes ? "Được sao lưu" : "Đã bỏ chọn"),
                    created.info.PersonalFileCount, LanguageService.Translate(created.info.IncludesPersonalFiles ? "Được sao lưu" : "Đã bỏ chọn"),
                    LanguageService.Translate(created.info.IncludesSettings ? "Được sao lưu" : "Đã bỏ chọn"));
                summary += "\n\n" + LocalizationAuditService.Format("backup.migration.next_steps", Path.GetFileName(dialog.FileName));
                if (!string.IsNullOrWhiteSpace(retention.Message)) summary += "\n\n" + LanguageService.Translate(retention.Message);
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, summary,
                    LanguageService.Translate("ui.mainwindow.message049"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (!_isLocked) PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                    LanguageService.Format("ui.mainwindow.message050", ex.Message),
                    LanguageService.Translate("ui.mainwindow.message022"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { Mouse.OverrideCursor = null; }
        }
        private void BtnMigrationGuide_Click(object sender, RoutedEventArgs e)
        {
            new MigrationGuideWindow { Owner = this }.ShowDialog();
        }
        private async void MnuRestoreCompleteBackup_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!await RequireAuthAsync("khôi phục gói sao lưu đầy đủ", "RESTORE_COMPLETE_BACKUP_DENIED")) return;
                var dialog = new OpenFileDialog
                {
                    Title = "Chọn gói sao lưu đầy đủ",
                    Filter = "Gói sao lưu TQLTK (*.tqltk)|*.tqltk|Tất cả tệp (*.*)|*.*",
                    CheckFileExists = true
                };
                LocalizedFileDialog.Apply(dialog);
                if (dialog.ShowDialog() == true && EnsureActiveSession())
                    await RestoreCompleteBackupAsync(dialog.FileName);
            }
            catch (Exception ex)
            {
                if (!_isLocked) PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                    LanguageService.Format("ui.mainwindow.message051", ex.Message),
                    LanguageService.Translate("ui.mainwindow.message022"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private async void MnuBackupCenter_Click(object? sender, RoutedEventArgs e)
        {
            if (!await RequireAuthAsync("duyệt bản sao lưu", "BACKUP_CENTER_DENIED")) return;
            if (!EnsureActiveSession()) return;
            string username = SessionService.CurrentUser!.Username;
            var center = new BackupCenterWindow(username) { Owner = this };
            if (center.ShowDialog() != true || !EnsureActiveSession()) return;
            if (center.SelectedComparison != null && center.SelectedRecoveryItems.Count > 0)
            {
                await RecoverSelectedBackupsAsync(center.SelectedComparison, center.SelectedRecoveryItems);
                return;
            }
            if (!string.IsNullOrWhiteSpace(center.SelectedBackupPath))
                await RestoreCompleteBackupAsync(center.SelectedBackupPath);
        }
        private async Task RecoverSelectedBackupsAsync(BackupComparisonResult comparison,
            IReadOnlyList<BackupRecoverySelection> selections)
        {
            string username = SessionService.CurrentUser!.Username;
            string? storedPassphrase = BackupCrypto.GetStoredPassphrase(username);
            string olderPassphrase = storedPassphrase ?? "";
            string newerPassphrase = storedPassphrase ?? "";
            (bool ok, string message, CompleteBackupInfo? info) olderCheck = (false, "", null);
            (bool ok, string message, CompleteBackupInfo? info) newerCheck = (false, "", null);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0 || !olderCheck.ok)
                {
                    var prompt = new BackupPassphraseWindow(confirmMode: false) { Owner = this };
                    if (prompt.ShowDialog() != true) return;
                    olderPassphrase = prompt.Passphrase;
                }
                if (!EnsureActiveSession()) return;
                olderCheck = await Task.Run(() => CompleteBackupService.Inspect(comparison.OlderPath, olderPassphrase));
                if (!olderCheck.ok)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(olderCheck.message),
                        LanguageService.Translate("ui.mainwindow.message052"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }
                if (attempt > 0 || !newerCheck.ok)
                {
                    var prompt = new BackupPassphraseWindow(confirmMode: false) { Owner = this };
                    if (prompt.ShowDialog() != true) return;
                    newerPassphrase = prompt.Passphrase;
                }
                if (!EnsureActiveSession()) return;
                newerCheck = await Task.Run(() => CompleteBackupService.Inspect(comparison.NewerPath, newerPassphrase));
                if (!newerCheck.ok)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(newerCheck.message),
                        LanguageService.Translate("ui.mainwindow.message053"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }
                break;
            }
            if (!olderCheck.ok || !newerCheck.ok || !EnsureActiveSession()) return;
            var foreignOwners = selections
                .Select(selection => selection.FromOlder ? olderCheck.info!.ExportedBy : newerCheck.info!.ExportedBy)
                .Where(owner => !string.Equals(owner, username, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (string owner in foreignOwners)
                if (!ConfirmForeignOwner(owner)) return;
            if (!await RequireAuthAsync("khôi phục mục từ bản sao lưu", "RECOVER_SELECTED_BACKUP_DENIED") || !EnsureActiveSession()) return;
            Mouse.OverrideCursor = Cursors.Wait;
            var recovered = await Task.Run(() => CompleteBackupService.RecoverSelectedAsCopies(
                username, comparison, olderPassphrase, newerPassphrase, selections, foreignOwners.Length > 0));
            Mouse.OverrideCursor = null;
            if (_isLocked || !EnsureActiveSession()) return;
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(recovered.message),
                LanguageService.Translate(recovered.ok ? "Khôi phục hoàn tất" : "Khôi phục chưa hoàn tất"),
                MessageBoxButton.OK, recovered.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (recovered.ok)
            {
                RefreshAccounts();
                await RefreshFilesAsync();
                UpdateStorageInfo();
            }
        }
        private async Task RestoreCompleteBackupAsync(string path)
        {
            string username = SessionService.CurrentUser!.Username;
            string? passphrase = BackupCrypto.GetStoredPassphrase(username);
            (bool ok, string message, CompleteBackupPreview? preview) prepared = (false, "", null);
            if (!string.IsNullOrWhiteSpace(passphrase))
                prepared = await Task.Run(() => CompleteBackupService.PrepareRestore(username, path, passphrase));
            if (!prepared.ok || prepared.preview == null)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var prompt = new BackupPassphraseWindow(confirmMode: false) { Owner = this };
                    if (prompt.ShowDialog() != true) return;
                    passphrase = prompt.Passphrase;
                    if (!EnsureActiveSession()) return;
                    Mouse.OverrideCursor = Cursors.Wait;
                    prepared = await Task.Run(() => CompleteBackupService.PrepareRestore(username, path, passphrase));
                    Mouse.OverrideCursor = null;
                    if (prepared.ok && prepared.preview != null) break;
                    if (attempt < 2)
                        PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(prepared.message),
                            LanguageService.Translate("ui.mainwindow.message054"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            if (!prepared.ok || prepared.preview == null || passphrase == null || !EnsureActiveSession())
            {
                if (!string.IsNullOrWhiteSpace(prepared.message))
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(prepared.message),
                        LanguageService.Translate("ui.mainwindow.message055"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bool foreignOwner = !string.Equals(prepared.preview.Info.ExportedBy, username, StringComparison.OrdinalIgnoreCase);
            if (foreignOwner && !ConfirmForeignOwner(prepared.preview.Info.ExportedBy)) return;
            var previewWindow = new BackupRestorePreviewWindow(
                "Xem trước khôi phục gói đầy đủ",
                "Nguồn: {0}\nNgười tạo: {1}\nTài khoản thêm: {2}; ghi chú thêm: {3}; tệp thêm: {4}.\nKhông ghi đè dữ liệu trùng. Cài đặt an toàn chỉ được áp dụng nếu chọn bên dưới.",
                () => new object?[] { Path.GetFileName(path), prepared.preview.Info.ExportedBy,
                    prepared.preview.AccountsToAdd, prepared.preview.NotesToAdd, prepared.preview.FilesToAdd },
                prepared.preview.Items, allowSettingsRestore: prepared.preview.Info.IncludesSettings) { Owner = this };
            if (previewWindow.ShowDialog() != true || !EnsureActiveSession()) return;
            if (!await RequireAuthAsync("xác nhận khôi phục gói sao lưu", "RESTORE_COMPLETE_BACKUP_DENIED") || !EnsureActiveSession()) return;
            bool restoreSettings = previewWindow.RestoreSettingsRequested;
            Mouse.OverrideCursor = Cursors.Wait;
            var restored = await Task.Run(() => CompleteBackupService.Restore(
                username, prepared.preview, passphrase, restoreSettings, foreignOwner));
            Mouse.OverrideCursor = null;
            if (_isLocked || !EnsureActiveSession()) return;
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(restored.message),
                LanguageService.Translate(restored.ok ? "Khôi phục hoàn tất" : "Khôi phục chưa hoàn tất"),
                MessageBoxButton.OK, restored.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (restored.ok)
            {
                RefreshAccounts();
                await RefreshFilesAsync();
                UpdateStorageInfo();
                if (restoreSettings)
                {
                    AutoLockMinutes = RegistryService.GetAutoLockMinutes();
                    _lockWhenMinimized = RegistryService.GetLockWhenMinimized();
                    LoadLockSettingsUi();
                    UpdateAutoThemeCard();
                    UpdateThemeIndicator();
                    cmbLanguageSettings.SelectedValue = LanguageService.CurrentCode;
                }
            }
        }
        private async void MnuExportVault_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog { Title = "Xuất kho mật khẩu", Filter = "Bản sao lưu kho mật khẩu (*.vault)|*.vault|Tất cả tệp (*.*)|*.*", FileName = $"vault_backup_{DateTime.Now:yyyyMMdd_HHmmss}.vault" };
                LocalizedFileDialog.Apply(dlg);
                if (dlg.ShowDialog() != true) return;
                if (!await RequireAuthAsync("xuất kho mật khẩu", "EXPORT_VAULT_DENIED")) return;
                string username = SessionService.CurrentUser!.Username;
                var result = VaultBackupService.ExportToFile(username, dlg.FileName);
                if (result.ok) AutoBackupService.RecordBackup(username, dlg.FileName);
                PersonalInfoManager.Services.LocalizedMessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex) { PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}"); }
        }
        private async void MnuImportVault_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!await RequireAuthAsync("nhập kho mật khẩu", "IMPORT_VAULT_DENIED")) return;
                var dlg = new OpenFileDialog { Title = "Nhập kho mật khẩu", Filter = "Bản sao lưu kho mật khẩu (*.vault)|*.vault|Tất cả tệp (*.*)|*.*" };
                LocalizedFileDialog.Apply(dlg);
                if (dlg.ShowDialog() != true) return;
                string username = SessionService.CurrentUser!.Username;
                if (!TryGetImportPassphrase(dlg.FileName, out string? passphrase)) return;
                Mouse.OverrideCursor = Cursors.Wait;
                var prepared = await Task.Run(() => VaultBackupService.PrepareImport(username, dlg.FileName, passphrase));
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (!prepared.ok || prepared.preview == null)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(prepared.message, "Không thể xem trước", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var preview = prepared.preview;
                string summaryTemplate = "Nguồn: {0}\nNgười tạo: {1}\nSẽ nhập {2} tài khoản; bỏ qua {3} mục trùng.\nMật khẩu không hiển thị trong phần xem trước.";
                var previewWindow = new BackupRestorePreviewWindow(
                    "Xem trước nhập vault", summaryTemplate,
                    () => new object?[]
                    {
                        Path.GetFileName(dlg.FileName),
                        string.IsNullOrWhiteSpace(preview.Owner) ? LanguageService.Translate("ui.mainwindow.message056") : preview.Owner,
                        preview.ImportedCount,
                        preview.SkippedCount
                    }, preview.Items) { Owner = this };
                if (previewWindow.ShowDialog() != true || !EnsureActiveSession()) return;
                var result = VaultBackupService.CommitImport(username, preview, ConfirmForeignOwner);
                PersonalInfoManager.Services.LocalizedMessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                if (result.ok) await OfferSavePassphraseAsync(passphrase);
                if (_isLocked) return;
                if (result.ok && result.importedCount > 0)
                {
                    RefreshAccounts();
                    UpdateStorageInfo();
                }
            }
            catch (Exception ex) { if (!_isLocked) PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}"); }
            finally { Mouse.OverrideCursor = null; }
        }
        private async void MnuVerifyBackup_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!await RequireAuthAsync("kiểm tra bản sao lưu", "VERIFY_BACKUP_DENIED")) return;
                var dlg = new OpenFileDialog
                {
                    Title = "Chọn bản sao lưu cần kiểm tra",
                    Filter = "Bản sao lưu TQLTK (*.vault;*.tqf)|*.vault;*.tqf|Tất cả tệp (*.*)|*.*"
                };
                LocalizedFileDialog.Apply(dlg);
                if (dlg.ShowDialog() != true || !EnsureActiveSession()) return;
                string username = SessionService.CurrentUser!.Username;
                if (FileBackupService.IsBackupFile(dlg.FileName))
                {
                    string? typedPassphrase = null;
                    Mouse.OverrideCursor = Cursors.Wait;
                    var inspection = await Task.Run(() => FileBackupService.InspectBackup(
                        username, dlg.FileName, null,
                        error =>
                        {
                            typedPassphrase = AskOnUi(() => PromptRestorePassphrase(error));
                            return typedPassphrase;
                        },
                        _sessionRevocationCts.Token));
                    if (_isLocked || !EnsureActiveSession()) return;
                    AutoBackupService.RecordBackupVerification(username, dlg.FileName, inspection.ok);
                    try { ActivityLogger.Log(username, "VERIFY_FILE_BACKUP", Path.GetFileName(dlg.FileName)); } catch { }
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(inspection.message,
                        inspection.ok ? "Bản sao lưu hợp lệ" : "Không thể xác minh",
                        MessageBoxButton.OK,
                        inspection.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                else
                {
                    if (!TryGetImportPassphrase(dlg.FileName, out string? passphrase)) return;
                    if (!EnsureActiveSession()) return;
                    Mouse.OverrideCursor = Cursors.Wait;
                    var inspection = await Task.Run(() => VaultBackupService.InspectBackup(
                        username, dlg.FileName, passphrase));
                    if (_isLocked || !EnsureActiveSession()) return;
                    AutoBackupService.RecordBackupVerification(username, dlg.FileName, inspection.ok);
                    try { ActivityLogger.Log(username, "VERIFY_VAULT_BACKUP", Path.GetFileName(dlg.FileName)); } catch { }
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(inspection.message,
                        inspection.ok ? "Bản sao lưu hợp lệ" : "Không thể xác minh",
                        MessageBoxButton.OK,
                        inspection.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show("Đã hủy kiểm tra vì phiên đăng nhập không còn hoạt động.", "Đã hủy",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show($"Không thể kiểm tra bản sao lưu: {ex.Message}", "Lỗi",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        private async void MnuDrillRestore_Click(object? sender, RoutedEventArgs e)
        {
            if (!await RequireAuthAsync("diễn tập khôi phục bản sao lưu", "BACKUP_RESTORE_DRILL_DENIED")) return;
            var dialog = new OpenFileDialog
            {
                Title = "Chọn bản sao lưu để diễn tập khôi phục",
                Filter = "Bản sao lưu TQLTK (*.vault;*.tqf)|*.vault;*.tqf|Tất cả tệp (*.*)|*.*"
            };
            LocalizedFileDialog.Apply(dialog);
            if (dialog.ShowDialog() != true || !EnsureActiveSession()) return;
            string username = SessionService.CurrentUser!.Username;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                if (FileBackupService.IsBackupFile(dialog.FileName))
                {
                    string? enteredPassphrase = null;
                    var result = await Task.Run(() => FileBackupService.DrillRestore(
                        username, dialog.FileName, null,
                        error =>
                        {
                            enteredPassphrase = AskOnUi(() => PromptRestorePassphrase(error));
                            return enteredPassphrase;
                        }, _sessionRevocationCts.Token));
                    if (_isLocked || !EnsureActiveSession()) return;
                    AutoBackupService.RecordRestoreDrill(username, dialog.FileName, result.ok, result.message);
                    try { ActivityLogger.Log(username, "FILE_BACKUP_RESTORE_DRILL", $"{Path.GetFileName(dialog.FileName)}: {result.ok}"); } catch { }
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate(result.message), LanguageService.Translate(result.ok ? "Diễn tập hoàn tất" : "Diễn tập thất bại"),
                        MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                else
                {
                    if (!TryGetImportPassphrase(dialog.FileName, out string? passphrase)) return;
                    if (!EnsureActiveSession()) return;
                    var result = await Task.Run(() => VaultBackupService.DrillRestore(
                        username, dialog.FileName, passphrase));
                    if (_isLocked || !EnsureActiveSession()) return;
                    AutoBackupService.RecordRestoreDrill(username, dialog.FileName, result.ok, result.message);
                    try { ActivityLogger.Log(username, "VAULT_BACKUP_RESTORE_DRILL", $"{Path.GetFileName(dialog.FileName)}: {result.ok}"); } catch { }
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate(result.message), LanguageService.Translate(result.ok ? "Diễn tập hoàn tất" : "Diễn tập thất bại"),
                        MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                RefreshSecurityOverview();
            }
            catch (OperationCanceledException)
            {
                if (!_isLocked) PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate("ui.mainwindow.message057"), LanguageService.Translate("ui.mainwindow.message058"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (!_isLocked) PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate($"Không thể diễn tập khôi phục: {ex.Message}"), LanguageService.Translate("ui.mainwindow.message022"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { Mouse.OverrideCursor = null; }
        }
        private void RefreshDriveTokenStatus()
        {
            try
            {
                string username = SessionService.CurrentUser?.Username ?? "";
                bool hasToken = !string.IsNullOrWhiteSpace(username) &&
                    new CloudSyncService(username).HasLocallyStoredDriveToken;
                LanguageService.SetLocalizedText(
                    lblGoogleDriveAccountStatus,
                    hasToken
                        ? "Đã tìm thấy thông tin đăng nhập Drive trên máy này. Nhấn Kiểm tra tài khoản để xem tài khoản Google."
                        : "Chưa lưu tài khoản Google Drive trên máy này.");
            }
            catch
            {
                LanguageService.SetLocalizedText(lblGoogleDriveAccountStatus,
                    "Không thể đọc trạng thái token Google Drive trên máy này.");
            }
        }
        private async void BtnCheckGoogleDriveAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!await RequireAuthAsync("kiểm tra tài khoản Google Drive đã liên kết", "CHECK_GOOGLE_DRIVE_ACCOUNT_DENIED")) return;
                string username = SessionService.CurrentUser!.Username;
                var syncService = new CloudSyncService(username);
                LanguageService.SetLocalizedText(lblGoogleDriveAccountStatus, "Đang kiểm tra tài khoản Google Drive...");
                Mouse.OverrideCursor = Cursors.Wait;
                var result = await syncService.GetLinkedAccountAsync(_sessionRevocationCts.Token);
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (result.hasLocalToken && result.account != null)
                {
                    LanguageService.SetLocalizedFormattedText(
                        lblGoogleDriveAccountStatus,
                        "Tài khoản Google Drive đã liên kết: {0}.",
                        () => new object?[] { result.account });
                }
                else
                {
                    LanguageService.SetLocalizedText(lblGoogleDriveAccountStatus, result.message);
                }
            }
            catch (OperationCanceledException)
            {
                LanguageService.SetLocalizedText(lblGoogleDriveAccountStatus, "Đã hủy kiểm tra tài khoản Google Drive.");
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked)
                    LanguageService.SetLocalizedFormattedText(
                        lblGoogleDriveAccountStatus,
                        "Không thể kiểm tra tài khoản Google Drive: {0}",
                        () => new object?[] { ex.Message });
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        private async void BtnDisconnectGoogleDrive_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!await RequireAuthAsync("ngắt kết nối Google Drive trên máy này", "DISCONNECT_GOOGLE_DRIVE_DENIED")) return;
                if (!EnsureActiveSession()) return;
                var answer = PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    "Xóa token Google Drive đã mã hóa đang lưu trên máy này? Bản sao lưu trên Drive không bị xóa. " +
                    "Quyền ứng dụng trong tài khoản Google vẫn còn và cần thu hồi riêng nếu bạn muốn.",
                    "Ngắt kết nối Google Drive", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes || !EnsureActiveSession()) return;
                new CloudSyncService(SessionService.CurrentUser!.Username).DisconnectDriveLocally();
                try { ActivityLogger.Log(SessionService.CurrentUser!.Username, "DISCONNECT_GOOGLE_DRIVE", ""); } catch { }
                LanguageService.SetLocalizedText(lblGoogleDriveAccountStatus,
                    "Đã ngắt kết nối Drive trên máy này. Bản sao lưu trên Drive không bị xóa; quyền Google vẫn có thể được thu hồi riêng.");
            }
            catch (Exception ex)
            {
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show($"Không thể ngắt kết nối Google Drive: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private async void BtnCreateShare_Click(object? sender, RoutedEventArgs e)
        {
            string? tempFile = null;
            try
            {
                if (GetSelectedAccounts().Count != 1)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message059"),
                        LanguageService.Translate("ui.mainwindow.message027"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var selected = GetSelectedAccount();
                if (selected == null) { PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message060"), LanguageService.Translate("ui.mainwindow.message027"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
                if (!await RequireAuthAsync("tạo liên kết chia sẻ tạm thời cho tài khoản", "ACCOUNT_SHARE_DENIED")) return;
                string username = SessionService.CurrentUser!.Username;
                var account = _vaultService.Load(username).FirstOrDefault(item => item.Id == selected.Id && !item.IsDeleted);
                if (account == null || !EnsureActiveSession()) return;
                var setup = new AccountShareSetupWindow { Owner = this };
                if (setup.ShowDialog() != true || !EnsureActiveSession()) return;
                if (PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                        LanguageService.Format("ui.mainwindow.message061",
                            account.ServiceName, setup.RecipientEmail, setup.ExpirationDays),
                        LanguageService.Translate("ui.mainwindow.message062"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                DateTimeOffset expires = DateTimeOffset.UtcNow.AddDays(setup.ExpirationDays);
                string passphrase = AccountShareService.GeneratePassphrase();
                tempFile = Path.Combine(Path.GetTempPath(), $"tqltk_share_{Guid.NewGuid():N}.tqltkshare");
                AccountShareService.CreateEncryptedPackage(account, expires, passphrase, tempFile);
                if (!EnsureActiveSession()) return;
                var cloud = new CloudSyncService(username);
                Mouse.OverrideCursor = Cursors.Wait;
                var share = await cloud.UploadExpiringShareAsync(tempFile, setup.RecipientEmail, expires, _sessionRevocationCts.Token);
                Mouse.OverrideCursor = null;
                if (share == null)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message063") + LanguageService.Translate(cloud.LastError ?? ""),
                        LanguageService.Translate("ui.mainwindow.message064"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (_isLocked || !EnsureActiveSession())
                {
                    await cloud.RevokeExpiringShareAsync(share.Id, share.RecipientEmail, CancellationToken.None);
                    return;
                }
                try { ActivityLogger.Log(username, "CREATE_ACCOUNT_SHARE", account.ServiceName); } catch { }
                new ShareCreatedWindow(share, passphrase) { Owner = this }.ShowDialog();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message065") + LanguageService.Translate(ex.Message),
                        LanguageService.Translate("ui.mainwindow.message064"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                try { if (tempFile != null && File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }
        private async void BtnManageDriveShares_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!await RequireAuthAsync("xem hoặc thu hồi liên kết chia sẻ Drive", "ACCOUNT_SHARE_DENIED")) return;
                string username = SessionService.CurrentUser!.Username;
                var cloud = new CloudSyncService(username);
                while (EnsureActiveSession())
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    var shares = await cloud.ListExpiringSharesAsync(_sessionRevocationCts.Token);
                    Mouse.OverrideCursor = null;
                    if (_isLocked || !EnsureActiveSession()) return;
                    if (shares == null)
                    {
                        PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message066") + LanguageService.Translate(cloud.LastError ?? ""),
                            LanguageService.Translate("ui.mainwindow.message064"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    var dialog = new DriveSharesWindow(shares) { Owner = this };
                    if (dialog.ShowDialog() != true || dialog.SelectedShare == null || !EnsureActiveSession()) return;
                    if (PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                            LanguageService.Format("ui.mainwindow.message067", dialog.SelectedShare.RecipientEmail),
                            LanguageService.Translate("ui.mainwindow.message068"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        continue;
                    Mouse.OverrideCursor = Cursors.Wait;
                    bool revoked = await cloud.RevokeExpiringShareAsync(dialog.SelectedShare.Id, dialog.SelectedShare.RecipientEmail, _sessionRevocationCts.Token);
                    Mouse.OverrideCursor = null;
                    if (_isLocked || !EnsureActiveSession()) return;
                    if (!revoked)
                    {
                        PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message069") + LanguageService.Translate(cloud.LastError ?? ""),
                            LanguageService.Translate("ui.mainwindow.message064"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    try { ActivityLogger.Log(username, "REVOKE_ACCOUNT_SHARE", dialog.SelectedShare.RecipientEmail); } catch { }
                    if (!string.IsNullOrWhiteSpace(cloud.LastError))
                        PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(cloud.LastError),
                            LanguageService.Translate("ui.mainwindow.message070"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message071") + LanguageService.Translate(ex.Message),
                        LanguageService.Translate("ui.mainwindow.message064"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private async void MnuImportShare_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!await RequireAuthAsync("mở gói tài khoản được chia sẻ", "ACCOUNT_SHARE_DENIED")) return;
                var dialog = new OpenSharedAccountWindow { Owner = this };
                if (dialog.ShowDialog() != true || !EnsureActiveSession()) return;
                Mouse.OverrideCursor = Cursors.Wait;
                AccountSharePackage package;
                try
                {
                    package = await Task.Run(() => AccountShareService.OpenEncryptedPackage(dialog.PackagePath, dialog.Passphrase));
                }
                finally { Mouse.OverrideCursor = null; }
                if (!EnsureActiveSession()) return;
                string username = SessionService.CurrentUser!.Username;
                var preview = new CsvImportWindow(new List<SavedAccount> { package.Account },
                    _vaultService.Load(username).Where(account => !account.IsDeleted).ToList(), includeAttachments: true) { Owner = this };
                if (preview.ShowDialog() != true || !EnsureActiveSession()) return;
                if (package.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message072"),
                        LanguageService.Translate("ui.mainwindow.message073"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                ApplySingleShareImport(username, preview);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message074") + LanguageService.Translate(ex.Message),
                        LanguageService.Translate("ui.mainwindow.message064"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private void ApplySingleShareImport(string username, CsvImportWindow preview)
        {
            var decision = preview.Decisions.SingleOrDefault();
            if (decision == null || decision.Action == CsvImportAction.Skip) return;
            var accounts = _vaultService.Load(username);
            var original = accounts.Select(account => account.Clone()).ToList();
            var oldBatch = CsvImportBatchStore.Load(username);
            if (oldBatch != null)
            {
                foreach (var account in accounts.Where(item => item.ImportBatchId == oldBatch.Id))
                {
                    account.ImportUndoable = false;
                    account.ImportUndoSnapshot = null;
                }
            }
            Guid batchId = preview.BatchId;
            DateTime now = DateTime.UtcNow;
            var affected = new List<Guid>();
            if (decision.Action is CsvImportAction.ImportNew or CsvImportAction.ImportCopy)
            {
                var added = decision.Account.Clone();
                added.Id = Guid.NewGuid();
                added.CreatedAt = added.UpdatedAt = now.ToLocalTime();
                added.ImportBatchId = batchId;
                added.ImportBatchAtUtc = now;
                added.ImportUndoable = true;
                added.CreatedInImportBatch = true;
                added.ImportUndoSnapshot = null;
                accounts.Add(added);
                affected.Add(added.Id);
            }
            else if (decision.Action == CsvImportAction.Overwrite && decision.TargetAccountId is Guid targetId)
            {
                var target = accounts.FirstOrDefault(item => item.Id == targetId && !item.IsDeleted);
                if (target == null)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message075"),
                        LanguageService.Translate("ui.mainwindow.message076"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                target.ImportUndoSnapshot = AccountRevisionService.CaptureForImportUndo(target);
                var previous = AccountRevisionService.Capture(target);
                bool passwordChanged = target.Password != decision.Account.Password;
                if (passwordChanged && !string.IsNullOrEmpty(target.Password))
                {
                    target.PasswordHistory.Insert(0, new PasswordHistoryEntry { Password = target.Password, ChangedAt = now.ToLocalTime() });
                    target.PasswordHistory = target.PasswordHistory.Take(5).ToList();
                    target.PasswordChangedAt = now.ToLocalTime();
                }
                target.ServiceName = decision.Account.ServiceName;
                target.Username = decision.Account.Username;
                target.Password = decision.Account.Password;
                target.Note = decision.Account.Note;
                target.WebsiteUrl = decision.Account.WebsiteUrl;
                target.Folder = decision.Account.Folder;
                target.Tags = decision.Account.Tags.ToList();
                target.IsFavorite = decision.Account.IsFavorite;
                target.CustomFields = decision.Account.CustomFields.Select(field => field.Clone()).ToList();
                target.Attachments = decision.Account.Attachments.Select(item => item.Clone()).ToList();
                target.TotpSecret = decision.Account.TotpSecret;
                target.TotpIssuer = decision.Account.TotpIssuer;
                target.TotpAlgorithm = decision.Account.TotpAlgorithm;
                target.TotpDigits = decision.Account.TotpDigits;
                target.TotpPeriodSeconds = decision.Account.TotpPeriodSeconds;
                AccountRevisionService.RecordPreviousIfChanged(target, previous, now, batchId);
                target.ImportBatchId = batchId;
                target.ImportBatchAtUtc = now;
                target.ImportUndoable = true;
                target.CreatedInImportBatch = false;
                target.UpdatedAt = now.ToLocalTime();
                affected.Add(target.Id);
            }
            if (affected.Count == 0) return;
            _vaultService.Save(username, accounts);
            try
            {
                CsvImportBatchStore.Save(username, new CsvImportBatchRecord { Id = batchId, CreatedAtUtc = now, AccountIds = affected });
            }
            catch
            {
                _vaultService.Save(username, original);
                if (oldBatch != null) CsvImportBatchStore.Save(username, oldBatch);
                else CsvImportBatchStore.Clear(username);
                throw;
            }
            try { ActivityLogger.Log(username, "IMPORT_ACCOUNT_SHARE", decision.Account.ServiceName); } catch { }
            RefreshAccounts();
            UpdateStorageInfo();
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message077"),
                LanguageService.Translate("ui.mainwindow.message078"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private async void MnuSyncGoogleDrive_Click(object? sender, RoutedEventArgs e)
        {
            string? tempFile = null;
            try
            {
                if (!await RequireAuthAsync("đồng bộ kho mật khẩu lên Google Drive", "SYNC_GOOGLE_DRIVE_DENIED")) return;
                string username = SessionService.CurrentUser!.Username;
                await OfferAdoptLegacyPassphraseAsync(username);
                if (_isLocked) return;
                if (!BackupCrypto.HasStoredPassphrase(username))
                {
                    var ask = PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        "Bản sao lưu lên Google Drive chưa có cụm mật khẩu khôi phục, nên chỉ mở được trên máy này. " +
                        "Nếu mất máy hoặc cài lại Windows, bạn sẽ không khôi phục được.\n\nĐặt cụm mật khẩu ngay bây giờ?",
                        "Nên đặt cụm mật khẩu sao lưu", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    if (ask == MessageBoxResult.Cancel) return;
                    if (ask == MessageBoxResult.Yes && !await SetBackupPassphraseAsync()) return;
                }
                var syncService = new CloudSyncService(username);
                var currentVault = _vaultService.Load(username);
                string localFingerprint = VaultSyncCheckpointStore.ComputeFingerprint(currentVault);
                var checkpoint = VaultSyncCheckpointStore.Load(username);
                Mouse.OverrideCursor = Cursors.Wait;
                var remoteBackups = await syncService.ListBackupsAsync(
                    BackupKind.Vault, includeOthers: false, _sessionRevocationCts.Token);
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (remoteBackups == null)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message079") +
                        (syncService.LastError ?? ""), LanguageService.Translate("ui.mainwindow.message080"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var latestRemote = remoteBackups.Where(item => item.Owner == BackupOwner.Mine)
                    .OrderByDescending(item => item.Created).FirstOrDefault();
                int keep = 5;
                if (checkpoint == null && latestRemote != null)
                {
                    var firstSyncChoice = PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                        LanguageService.Translate("ui.mainwindow.message081"),
                        LanguageService.Translate("ui.mainwindow.message082"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    if (firstSyncChoice != MessageBoxResult.Yes) return;
                    keep = 0;
                }
                else if (checkpoint != null)
                {
                    bool remoteChanged = latestRemote == null || latestRemote.Id != checkpoint.RemoteBackupId;
                    bool localChanged = localFingerprint != checkpoint.LocalFingerprint;
                    if (remoteChanged && !localChanged)
                    {
                        PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                            LanguageService.Translate("ui.mainwindow.message083"),
                            LanguageService.Translate("ui.mainwindow.message084"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (remoteChanged && localChanged)
                    {
                        var conflictChoice = PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                            LanguageService.Translate("ui.mainwindow.message085"),
                            LanguageService.Translate("ui.mainwindow.message086"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                        if (conflictChoice != MessageBoxResult.Yes) return;
                        keep = 0;
                    }
                    else if (!localChanged)
                    {
                        PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message087"),
                            LanguageService.Translate("ui.mainwindow.message088"), MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }
                tempFile = Path.Combine(Path.GetTempPath(), $"vault_backup_{DateTime.Now:yyyyMMdd_HHmmss}.vault");
                var exportResult = VaultBackupService.ExportToFile(username, tempFile, out string exportedFingerprint);
                if (!exportResult.ok) { PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate(exportResult.message), LanguageService.Translate("ui.mainwindow.message089"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (!EnsureActiveSession()) return;
                Mouse.OverrideCursor = Cursors.Wait;
                var remoteImmediatelyBeforeUpload = await syncService.ListBackupsAsync(
                    BackupKind.Vault, includeOthers: false, _sessionRevocationCts.Token);
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                string? newestRemoteId = remoteImmediatelyBeforeUpload?
                    .Where(item => item.Owner == BackupOwner.Mine)
                    .OrderByDescending(item => item.Created)
                    .FirstOrDefault()?.Id;
                if (remoteImmediatelyBeforeUpload == null || newestRemoteId != latestRemote?.Id)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
                        LanguageService.Translate("ui.mainwindow.message090"),
                        LanguageService.Translate("ui.mainwindow.message086"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Mouse.OverrideCursor = Cursors.Wait;
                bool success = await syncService.UploadVaultAsync(tempFile, keep, _sessionRevocationCts.Token);
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (success)
                {
                    if (!string.IsNullOrWhiteSpace(syncService.LastUploadedId))
                        VaultSyncCheckpointStore.Save(username, new VaultSyncCheckpoint
                        {
                            RemoteBackupId = syncService.LastUploadedId,
                            LocalFingerprint = exportedFingerprint,
                            LastSyncAtUtc = DateTime.UtcNow
                        });
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate("ui.mainwindow.message091"), LanguageService.Translate("ui.mainwindow.message043"), MessageBoxButton.OK, MessageBoxImage.Information);
                    ActivityLogger.Log(username, "SYNC_GOOGLE_DRIVE", "Đã tải kho mật khẩu lên thành công");
                }
                else
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate("ui.mainwindow.message092") + LanguageService.Translate(syncService.LastError ?? "Vui lòng kiểm tra kết nối mạng, quyền truy cập Google Drive và cửa sổ trình duyệt (nếu có)."), LanguageService.Translate("ui.mainwindow.message022"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Format("ui.mainwindow.message093", LanguageService.Translate(ex.Message)), LanguageService.Translate("ui.mainwindow.message022"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { if (tempFile != null && File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }
        private async void MnuRestoreGoogleDrive_Click(object? sender, RoutedEventArgs e)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"vault_restore_{DateTime.Now:yyyyMMdd_HHmmss}.vault");
            try
            {
                if (!await RequireAuthAsync("khôi phục kho mật khẩu từ Google Drive", "RESTORE_GOOGLE_DRIVE_DENIED")) return;
                string username = SessionService.CurrentUser!.Username;
                var syncService = new CloudSyncService(username);
                var dlg = new RestoreBackupWindow(syncService) { Owner = this };
                if (dlg.ShowDialog() != true || dlg.Selected is not { } pick) return;
                if (!EnsureActiveSession()) return;
                Mouse.OverrideCursor = Cursors.Wait;
                bool downloaded = await syncService.DownloadBackupAsync(pick.Id, tempFile, _sessionRevocationCts.Token);
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (!downloaded)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate(syncService.LastError ?? "Tải bản sao lưu từ Google Drive thất bại."), LanguageService.Translate("ui.mainwindow.message022"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!TryGetImportPassphrase(tempFile, out string? passphrase)) return;
                Mouse.OverrideCursor = Cursors.Wait;
                var prepared = await Task.Run(() => VaultBackupService.PrepareImport(username, tempFile, passphrase));
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (!prepared.ok || prepared.preview == null)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate(prepared.message), LanguageService.Translate("ui.mainwindow.message055"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var preview = prepared.preview;
                const string summaryTemplate = "Bản sao lưu Google Drive: {0} ({1})\nNgười tạo: {2}\nSẽ nhập {3} tài khoản; bỏ qua {4} mục trùng. Mật khẩu không hiển thị.";
                var previewWindow = new BackupRestorePreviewWindow(
                    "Xem trước khôi phục kho mật khẩu", summaryTemplate,
                    () => new object?[]
                    {
                        pick.CreatedText,
                        pick.SizeText,
                        string.IsNullOrWhiteSpace(preview.Owner) ? LanguageService.Translate("ui.mainwindow.message056") : preview.Owner,
                        preview.ImportedCount,
                        preview.SkippedCount
                    }, preview.Items) { Owner = this };
                if (previewWindow.ShowDialog() != true || !EnsureActiveSession()) return;
                var remoteNow = await syncService.ListBackupsAsync(BackupKind.Vault, includeOthers: false,
                    ct: _sessionRevocationCts.Token);
                if (_isLocked || !EnsureActiveSession()) return;
                bool selectedBackupStillExists = remoteNow?.Any(item =>
                    item.Owner == BackupOwner.Mine && item.Id == pick.Id) == true;
                if (!selectedBackupStillExists)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("ui.mainwindow.message094"),
                        LanguageService.Translate("ui.mainwindow.message086"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var result = VaultBackupService.CommitImport(username, preview, ConfirmForeignOwner);
                PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Translate(result.message), LanguageService.Translate(result.ok ? "Thành công" : "Lỗi"), MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                if (result.ok)
                {
                    VaultSyncCheckpointStore.Save(username, new VaultSyncCheckpoint
                    {
                        RemoteBackupId = pick.Id,
                        LocalFingerprint = VaultSyncCheckpointStore.ComputeFingerprint(preview.Accounts),
                        LastSyncAtUtc = DateTime.UtcNow
                    });
                    await OfferSavePassphraseAsync(passphrase);
                    if (_isLocked) return;
                    ActivityLogger.Log(username, "RESTORE_GOOGLE_DRIVE", $"Nhập kho mật khẩu từ Drive: {pick.Name}");
                    if (result.importedCount > 0)
                    {
                        RefreshAccounts();
                        UpdateStorageInfo();
                    }
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked) PersonalInfoManager.Services.LocalizedMessageBox.Show(LanguageService.Format("ui.mainwindow.message095", LanguageService.Translate(ex.Message)), LanguageService.Translate("ui.mainwindow.message022"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }
        private async void MnuBackupPassphrase_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string username = SessionService.CurrentUser!.Username;
                await OfferAdoptLegacyPassphraseAsync(username);
                if (_isLocked) return;
                if (BackupCrypto.HasStoredPassphrase(username) &&
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        "Máy này đã có cụm mật khẩu sao lưu.\n\nĐổi sang cụm mới? Các bản sao lưu đã tạo trước đó " +
                        "vẫn cần cụm cũ để mở, nên hãy giữ lại cụm cũ.",
                        "Đổi cụm mật khẩu sao lưu", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                await SetBackupPassphraseAsync();
            }
            catch (Exception ex) { PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi: {ex.Message}"); }
        }
        private async Task<bool> SetBackupPassphraseAsync()
        {
            if (!await RequireAuthAsync("đặt hoặc đổi cụm mật khẩu sao lưu", "SET_BACKUP_PASSPHRASE_DENIED")) return false;
            var dlg = new BackupPassphraseWindow(confirmMode: true) { Owner = this };
            if (dlg.ShowDialog() != true) return false;
            if (!EnsureActiveSession()) return false;
            string username = SessionService.CurrentUser!.Username;
            BackupCrypto.SaveStoredPassphrase(username, dlg.Passphrase);
            try { ActivityLogger.Log(SessionService.CurrentUser!.Username, "SET_BACKUP_PASSPHRASE", ""); } catch { }
            PersonalInfoManager.Services.LocalizedMessageBox.Show(
                "Đã đặt cụm mật khẩu sao lưu.\n\nTừ giờ các bản sao lưu và tệp xuất kho mật khẩu mới sẽ mở được trên máy khác " +
                "bằng cụm này. Hãy ghi lại cụm ở nơi an toàn.",
                "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        private bool TryGetImportPassphrase(string file, out string? passphrase)
        {
            passphrase = null;
            string username = SessionService.CurrentUser!.Username;
            if (!VaultBackupService.IsPassphraseProtected(file)) return true;
            if (VaultBackupService.PassphraseWorks(username, file)) return true;
            for (int i = 0; i < 3; i++)
            {
                var dlg = new BackupPassphraseWindow(confirmMode: false) { Owner = this };
                if (dlg.ShowDialog() != true) return false;
                if (VaultBackupService.PassphraseWorks(username, file, dlg.Passphrase))
                {
                    passphrase = dlg.Passphrase;
                    return true;
                }
                PersonalInfoManager.Services.LocalizedMessageBox.Show("Cụm mật khẩu không đúng.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }
        private async Task OfferSavePassphraseAsync(string? passphrase)
        {
            if (_isLocked || SessionService.CurrentUser is not { } currentUser) return;
            string username = currentUser.Username;
            if (passphrase == null || BackupCrypto.HasStoredPassphrase(username)) return;
            if (PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    "Dùng cụm mật khẩu vừa nhập làm cụm sao lưu cho máy này?\n\n" +
                    "Từ giờ các bản sao lưu mới từ máy này sẽ mở được bằng cùng cụm đó.",
                    "Lưu cụm mật khẩu", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                if (!await RequireAuthAsync("lưu cụm mật khẩu sao lưu", "SET_BACKUP_PASSPHRASE_DENIED")) return;
                if (!EnsureActiveSession()) return;
                BackupCrypto.SaveStoredPassphrase(username, passphrase);
            }
        }
        private bool ConfirmForeignOwner(string owner)
        {
            string current = SessionService.CurrentUser!.Username;
            return PersonalInfoManager.Services.LocalizedMessageBox.Show(
                $"Bản sao lưu này được tạo bởi người dùng '{owner}', không phải '{current}'.\n\n" +
                    $"Nhập vào sẽ thêm các tài khoản của '{owner}' vào kho mật khẩu của '{current}'.\n" +
                "Chỉ tiếp tục nếu đây là dữ liệu của chính bạn (ví dụ bạn dùng tên đăng nhập khác trên máy mới).\n\n" +
                "Tiếp tục?",
                "Bản sao lưu của người dùng khác",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }
        private async Task OfferAdoptLegacyPassphraseAsync(string username)
        {
            if (BackupCrypto.HasStoredPassphrase(username) || !BackupCrypto.HasLegacyPassphrase()) return;
            var ask = PersonalInfoManager.Services.LocalizedMessageBox.Show(
                "Máy này có một cụm mật khẩu sao lưu được đặt từ phiên bản trước, khi app chưa tách theo người dùng.\n\n" +
                $"Đây có phải cụm của '{username}'?\n\n" +
                "Có: dùng cụm đó cho tài khoản này (người dùng khác sẽ không nhận được nữa).\n" +
                "Không: giữ nguyên để đúng người đã đặt cụm đó nhận sau.",
                "Cụm mật khẩu sao lưu cũ", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (ask != MessageBoxResult.Yes) return;
            if (!await RequireAuthAsync("nhận cụm mật khẩu sao lưu cũ", "SET_BACKUP_PASSPHRASE_DENIED")) return;
            if (!EnsureActiveSession()) return;
            if (!BackupCrypto.TryAdoptLegacyPassphrase(username))
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    "Không đọc được cụm cũ trên máy này. Hãy đặt cụm mới (Google Drive → Cụm mật khẩu sao lưu).",
                    "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private async void MnuSyncFilesGoogleDrive_Click(object? sender, RoutedEventArgs e)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"files_backup_{DateTime.Now:yyyyMMdd_HHmmss}.tqf");
            try
            {
                string username = SessionService.CurrentUser!.Username;
                var (count, bytes) = FileBackupService.Summarize(username);
                if (count == 0)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show("Kho File cá nhân đang trống, không có gì để sao lưu.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (bytes > FileBackupService.MaxTotalBytes)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        $"Kho File cá nhân hiện là {FileBackupService.FormatSize(bytes)}, vượt giới hạn sao lưu {FileBackupService.FormatSize(FileBackupService.MaxTotalBytes)}.\n\n" +
                        "Hãy xóa bớt hoặc xuất bớt file ra ngoài rồi thử lại.",
                        "Quá dung lượng", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await OfferAdoptLegacyPassphraseAsync(username);
                if (_isLocked) return;
                string? passphrase = BackupCrypto.GetStoredPassphrase(username);
                if (passphrase == null)
                {
                    var ask = PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        "Bản sao lưu lên Google Drive cần cụm mật khẩu sao lưu để có thể mở trên máy khác.\n\n" +
                        "Đặt cụm mật khẩu ngay bây giờ?",
                        "Cần cụm mật khẩu sao lưu", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (ask != MessageBoxResult.Yes || !await SetBackupPassphraseAsync()) return;
                    passphrase = BackupCrypto.GetStoredPassphrase(username);
                    if (passphrase == null) return;
                }
                if (PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        $"Sao lưu {count} File cá nhân ({FileBackupService.FormatSize(bytes)}) lên Google Drive?\n\n" +
                        "Các file được nén và mã hóa bằng cụm mật khẩu sao lưu trước khi tải lên.",
                        "Xác nhận sao lưu", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                Mouse.OverrideCursor = Cursors.Wait;
                string pass = passphrase;
                var pack = await Task.Run(() => FileBackupService.CreateArchive(username, tempFile, pass));
                if (!pack.ok)
                {
                    Mouse.OverrideCursor = null;
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(pack.message, "Không thể sao lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!EnsureActiveSession()) return;
                var syncService = new CloudSyncService(username);
                bool success = await syncService.UploadFilesBackupAsync(tempFile, ct: _sessionRevocationCts.Token);
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (success)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        $"{pack.message}\n\nĐã đồng bộ lên Google Drive thành công!\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.",
                        "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    ActivityLogger.Log(username, "SYNC_FILES_GOOGLE_DRIVE", $"Tải lên {pack.count} tệp cá nhân ({FileBackupService.FormatSize(pack.bytes)})");
                }
                else
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(
                        "Đồng bộ thất bại.\n\n" + (syncService.LastError ?? "Vui lòng kiểm tra kết nối mạng và quyền truy cập Google Drive."),
                        "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi đồng bộ File cá nhân: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }
        private async void MnuRestoreFilesGoogleDrive_Click(object? sender, RoutedEventArgs e)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"files_restore_{DateTime.Now:yyyyMMdd_HHmmss}.tqf");
            try
            {
                if (!await RequireAuthAsync("khôi phục tệp cá nhân từ Google Drive", "RESTORE_FILES_GOOGLE_DRIVE_DENIED")) return;
                string username = SessionService.CurrentUser!.Username;
                var syncService = new CloudSyncService(username);
                var dlg = new RestoreBackupWindow(syncService, BackupKind.Files) { Owner = this };
                if (dlg.ShowDialog() != true || dlg.Selected is not { } pick) return;
                if (!EnsureActiveSession()) return;
                Mouse.OverrideCursor = Cursors.Wait;
                bool downloaded = await syncService.DownloadBackupAsync(pick.Id, tempFile, _sessionRevocationCts.Token);
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (!downloaded)
                {
                    Mouse.OverrideCursor = null;
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(syncService.LastError ?? "Tải bản sao lưu từ Google Drive thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                string? typed = null;
                var prepared = await Task.Run(() => FileBackupService.PreviewRestore(
                    username, tempFile, null,
                    err => { typed = AskOnUi(() => PromptRestorePassphrase(err)); return typed; },
                    _sessionRevocationCts.Token));
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (!prepared.ok || prepared.preview == null)
                {
                    PersonalInfoManager.Services.LocalizedMessageBox.Show(prepared.message, "Không thể xem trước", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var preview = prepared.preview;
                bool hasInvalidEntries = preview.InvalidCount > 0;
                string summaryTemplate = hasInvalidEntries
                    ? "Bản sao lưu Google Drive: {0} ({1})\nNgười tạo: {2}\nSẽ khôi phục {3} file; bỏ qua {4} file trùng. Dung lượng: {5}. Bỏ qua {6} mục không hợp lệ."
                    : "Bản sao lưu Google Drive: {0} ({1})\nNgười tạo: {2}\nSẽ khôi phục {3} file; bỏ qua {4} file trùng. Dung lượng: {5}.";
                var previewWindow = new BackupRestorePreviewWindow(
                    "Xem trước khôi phục tệp cá nhân", summaryTemplate,
                    () => hasInvalidEntries
                        ? new object?[]
                        {
                            pick.CreatedText,
                            pick.SizeText,
                            string.IsNullOrWhiteSpace(preview.Owner) ? LanguageService.Translate("ui.mainwindow.message056") : preview.Owner,
                            preview.AddedCount,
                            preview.SkippedCount,
                            FileBackupService.FormatSize(preview.TotalBytes),
                            preview.InvalidCount
                        }
                        : new object?[]
                        {
                            pick.CreatedText,
                            pick.SizeText,
                            string.IsNullOrWhiteSpace(preview.Owner) ? LanguageService.Translate("ui.mainwindow.message056") : preview.Owner,
                            preview.AddedCount,
                            preview.SkippedCount,
                            FileBackupService.FormatSize(preview.TotalBytes)
                        }, preview.Items) { Owner = this };
                if (previewWindow.ShowDialog() != true || !EnsureActiveSession()) return;
                if (preview.Owner.Length > 0 && !string.Equals(preview.Owner, username, StringComparison.OrdinalIgnoreCase) &&
                    !ConfirmForeignOwner(preview.Owner)) return;
                var result = await Task.Run(() => FileBackupService.RestoreFromFile(
                    username, tempFile, typed,
                    err => typed ??= AskOnUi(() => PromptRestorePassphrase(err)),
                    owner => true,
                    _sessionRevocationCts.Token,
                    preview.Fingerprint,
                    preview.LocalFingerprint));
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                PersonalInfoManager.Services.LocalizedMessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                RefreshFiles();
                UpdateStorageInfo();
                if (result.ok)
                {
                    await OfferSavePassphraseAsync(typed);
                    if (_isLocked) return;
                    ActivityLogger.Log(username, "RESTORE_FILES_GOOGLE_DRIVE",
                        $"Khôi phục File cá nhân từ Drive: {pick.Name} ({result.added} file, bỏ qua {result.skipped})");
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked)
                    PersonalInfoManager.Services.LocalizedMessageBox.Show($"Lỗi khôi phục File cá nhân: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }
        private T AskOnUi<T>(Func<T> ask) => Dispatcher.Invoke(() =>
        {
            Mouse.OverrideCursor = null;
            try { return ask(); }
            finally { Mouse.OverrideCursor = Cursors.Wait; }
        });
        private string? PromptRestorePassphrase(string? previousError)
        {
            if (previousError != null)
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, previousError, "Cụm mật khẩu sao lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
            var dlg = new BackupPassphraseWindow(confirmMode: false) { Owner = this };
            return dlg.ShowDialog() == true ? dlg.Passphrase : null;
        }
        private void MnuCenterWindow_Click(object? sender, RoutedEventArgs e)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
            RegistryService.ClearWindowPosition();
            SaveWindowState();
            PersonalInfoManager.Services.LocalizedMessageBox.Show("Đã đưa cửa sổ về giữa màn hình.");
        }
        private void MnuResetSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (PersonalInfoManager.Services.LocalizedMessageBox.Show("Xóa toàn bộ cài đặt (giao diện, kích thước, vị trí, tài khoản gần nhất)?\n\nKhởi động lại ứng dụng để áp dụng toàn bộ.", "Xác nhận đặt lại", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            RegistryService.Clear();
            ThemeService.ApplyLight();
            PersonalInfoManager.Services.LocalizedMessageBox.Show("Đã đặt lại cài đặt.\n\nKhởi động lại ứng dụng để áp dụng toàn bộ.");
        }
        private void UpdateStorageInfo()
        {
            try
            {
                string user = SessionService.CurrentUser!.Username;
                long bytes = _fileService.GetUsedBytes(user);
                int accountCount = _vaultService.Count(user);
                lblStorage.Text = $"🔐 {accountCount} tài khoản | 💾 {bytes / 1024.0:F1} KB";
            }
            catch { lblStorage.Text = ""; }
        }
        private void BtnRefreshSecurityOverview_Click(object? sender, RoutedEventArgs e) => RefreshSecurityOverview();
        private async void BtnCheckPwnedPasswords_Click(object? sender, RoutedEventArgs e)
        {
            if (PersonalInfoManager.Services.LocalizedMessageBox.Show(
                    "Ứng dụng sẽ kiểm tra các mật khẩu hiện tại trong vault qua Have I Been Pwned. " +
                    "Ứng dụng chỉ gửi 5 ký tự đầu của SHA-1 hash qua HTTPS; mật khẩu đầy đủ, hash đầy đủ, tên dịch vụ và username không được gửi. " +
                    "Dịch vụ vẫn có thể thấy địa chỉ IP và tiền tố hash. Bạn chỉ tiếp tục nếu đồng ý gửi yêu cầu này.",
                    "Cho phép kiểm tra mật khẩu rò rỉ", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;
            if (!await RequireAuthAsync("kiểm tra mật khẩu rò rỉ", "PWNED_PASSWORD_CHECK_DENIED")) return;
            string username = SessionService.CurrentUser!.Username;
            var accounts = _vaultService.Load(username).Where(account => !account.IsDeleted).ToList();
            if (accounts.Count == 0)
            {
                _pwnedBreachedAccountIds.Clear();
                _pwnedCheckedAccountVersions.Clear();
                _pwnedCheckCompleted = true;
                _pwnedLastCheckedUtc = DateTime.UtcNow;
                _pwnedCheckedUniquePasswords = 0;
                _pwnedBreachedUniquePasswords = 0;
                _pwnedStateUser = username;
                try
                {
                    PwnedPasswordAuditStore.Save(username,
                        PwnedPasswordAuditStore.CreateState(accounts, Array.Empty<Guid>(), 0, 0, _pwnedLastCheckedUtc.Value));
                    _pwnedStateSaveFailed = false;
                }
                catch { _pwnedStateSaveFailed = true; }
                _pwnedResultsAreCurrent = true;
                RefreshSecurityOverview();
                return;
            }
            var button = sender as Button;
            if (button != null) button.IsEnabled = false;
            LanguageService.SetLocalizedText(lblPwnedPasswordAudit, "Đang kiểm tra các mật khẩu duy nhất…");
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var result = await PwnedPasswordService.CheckAsync(
                    accounts.Select(account => (account.Id, account.Password)), _sessionRevocationCts.Token);
                if (_isLocked || !EnsureActiveSession()) return;
                _pwnedBreachedAccountIds.Clear();
                foreach (Guid accountId in result.AffectedAccountIds)
                    _pwnedBreachedAccountIds.Add(accountId);
                _pwnedCheckedAccountVersions.Clear();
                foreach (var account in accounts)
                    _pwnedCheckedAccountVersions[account.Id] = account.UpdatedAt;
                _pwnedCheckCompleted = true;
                _pwnedLastCheckedUtc = DateTime.UtcNow;
                _pwnedCheckedUniquePasswords = result.CheckedUniquePasswords;
                _pwnedBreachedUniquePasswords = result.BreachedUniquePasswords;
                _pwnedStateUser = username;
                try
                {
                    PwnedPasswordAuditStore.Save(username,
                        PwnedPasswordAuditStore.CreateState(accounts, _pwnedBreachedAccountIds,
                            result.CheckedUniquePasswords, result.BreachedUniquePasswords, _pwnedLastCheckedUtc.Value));
                    _pwnedStateSaveFailed = false;
                    _pwnedStateLoadFailed = false;
                }
                catch { _pwnedStateSaveFailed = true; }
                try { ActivityLogger.Log(username, "PWNED_PASSWORD_CHECK", $"Đã kiểm tra {result.CheckedUniquePasswords} giá trị; ảnh hưởng {result.AffectedAccounts} mục"); } catch { }
                RefreshSecurityOverview();
            }
            catch (OperationCanceledException)
            {
                if (!_isLocked && EnsureActiveSession())
                    LanguageService.SetLocalizedText(lblPwnedPasswordAudit, "Đã hủy kiểm tra; không có kết luận về mật khẩu.");
            }
            catch (Exception ex)
            {
                if (!_isLocked && EnsureActiveSession())
                    LanguageService.SetLocalizedFormattedText(
                        lblPwnedPasswordAudit,
                        "Không thể hoàn tất kiểm tra; chưa thể kết luận an toàn. {0}",
                        () => new object?[] { ex.Message });
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked && button != null) button.IsEnabled = true;
            }
        }
        private void RefreshSecurityOverview()
        {
            if (!EnsureActiveSession()) return;
            try
            {
                string username = SessionService.CurrentUser!.Username;
                var accounts = _vaultService.Load(username).Where(account => !account.IsDeleted).ToList();
                EnsurePwnedAuditLoaded(username);
                int weakCount = accounts.Count(a => !string.IsNullOrEmpty(a.Password) &&
                    PasswordStrengthService.CalculateScore(a.Password) <= 2);
                int reusedEntries = accounts
                    .Where(a => !string.IsNullOrEmpty(a.Password))
                    .GroupBy(a => a.Password, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Sum(group => group.Count());
                int unknownPasswordDate = accounts.Count(account => account.PasswordChangedAt == null);
                int oldestKnownDays = accounts.Where(account => account.PasswordChangedAt.HasValue)
                    .Select(account => Math.Max(0, (DateTime.Now - account.PasswordChangedAt!.Value).Days))
                    .DefaultIfEmpty(0).Max();
                LanguageService.SetLocalizedFormattedText(
                    lblSecurityPasswordAudit,
                    "Kho mật khẩu: {0} tài khoản • {1} mật khẩu yếu • {2} mục dùng mật khẩu trùng • mật khẩu cũ nhất đã biết: {3} ngày • {4} tài khoản chưa rõ ngày đổi MK",
                    () => new object?[] { accounts.Count, weakCount, reusedEntries, oldestKnownDays, unknownPasswordDate });
                _pwnedResultsAreCurrent = IsPwnedCheckCurrent(accounts);
                UpdatePwnedAuditStatus(accounts);
                var findings = PasswordSecurityAuditService.Analyze(
                    accounts, _pwnedBreachedAccountIds, _pwnedResultsAreCurrent, DateTime.Now);
                var accountById = accounts.ToDictionary(account => account.Id);
                var reviewStates = SecurityFindingReviewService.SyncFindings(username, findings, accountById);
                var findingItems = findings
                    .Where(finding => accountById.ContainsKey(finding.AccountId))
                    .Take(50)
                    .Select(finding =>
                    {
                        SavedAccount account = accountById[finding.AccountId];
                        string issue = finding.Kind switch
                        {
                            PasswordSecurityFindingKind.Breached => "Mật khẩu xuất hiện trong dữ liệu rò rỉ",
                            PasswordSecurityFindingKind.Reused => "Mật khẩu được dùng lại",
                            PasswordSecurityFindingKind.Weak => "Mật khẩu yếu",
                            PasswordSecurityFindingKind.OlderThanYear => "Mật khẩu chưa đổi hơn 365 ngày",
                            _ => "Chưa rõ ngày đổi mật khẩu"
                        };
                        string description = $"{LanguageService.Translate(issue)} — {account.ServiceName} ({account.Username})";
                        SecurityFindingReviewState review = reviewStates[SecurityFindingReviewService.Key(account.Id, finding.Kind)];
                        string reviewText = review.Status switch
                        {
                            SecurityFindingReviewStatus.Fixed => LanguageService.Format(
                                "ui.mainwindow.message096",
                                LocalizationAuditService.FormatDateTime(review.LastCheckedUtc)),
                            SecurityFindingReviewStatus.Snoozed => LanguageService.Format(
                                "ui.mainwindow.message097",
                                review.RemindAtUtc.HasValue ? LocalizationAuditService.FormatDateTime(review.RemindAtUtc.Value) : "",
                                LocalizationAuditService.FormatDateTime(review.LastCheckedUtc)),
                            SecurityFindingReviewStatus.Ignored => LanguageService.Format(
                                "ui.mainwindow.message098",
                                review.Reason,
                                LocalizationAuditService.FormatDateTime(review.LastCheckedUtc)),
                            _ => LanguageService.Format(
                                "ui.mainwindow.message099",
                                LocalizationAuditService.FormatDateTime(review.LastCheckedUtc))
                        };
                        return new SecurityFindingViewItem(account.Id, finding.Kind, description, reviewText);
                    })
                    .ToList();
                listSecurityFindings.ItemsSource = findingItems;
                string findingsMessage = findings.Count == 0
                    ? (_pwnedResultsAreCurrent
                        ? "Chưa phát hiện vấn đề trong các kiểm tra hiện có."
                        : "Chưa có vấn đề cục bộ; kiểm tra mật khẩu rò rỉ chưa chạy hoặc kết quả đã cũ.")
                    : findings.Count > findingItems.Count
                        ? "Tìm thấy {0} cảnh báo; đang hiển thị {1} mục ưu tiên cao nhất."
                        : "Tìm thấy {0} cảnh báo mật khẩu cần xem xét.";
                if (findings.Count == 0)
                    LanguageService.SetLocalizedText(lblSecurityFindingsSummary, findingsMessage);
                else
                    LanguageService.SetLocalizedFormattedText(
                        lblSecurityFindingsSummary,
                        findingsMessage,
                        () => findings.Count > findingItems.Count
                            ? new object?[] { findings.Count, findingItems.Count }
                            : new object?[] { findings.Count });
                LanguageService.SetLocalizedText(lblSecurity2FAAudit, TwoFactorService.IsEnabled(username)
                    ? "2FA: đã bật cho đăng nhập bằng mật khẩu."
                    : "2FA: đang tắt; nên bật để tăng bảo vệ khi đăng nhập bằng mật khẩu.");
                int recoveryCodeCount = TwoFactorService.GetRecoveryCodeCount(username);
                bool hasBackupPassphrase = BackupCrypto.HasStoredPassphrase(username);
                LanguageService.SetLocalizedFormattedText(
                    lblSecurityRecoveryAudit,
                    "Mã khôi phục 2FA còn: {0} • Cụm mật khẩu sao lưu: {1}. Khóa tệp cá nhân gắn với Windows hiện tại; để đổi máy hoặc cài lại Windows, hãy tạo backup có cụm mật khẩu và giữ mã khôi phục riêng.",
                    () => new object?[]
                    {
                        recoveryCodeCount,
                        hasBackupPassphrase
                            ? (LanguageService.IsEnglish ? "set" : "đã đặt")
                            : (LanguageService.IsEnglish ? "not set" : "chưa đặt")
                    });
                LanguageService.SetLocalizedText(lblSecurityBackupAudit, AutoBackupService.GetHealthLine(username));
                var fileStatus = _fileService.GetEncryptionStatus(username);
                if (fileStatus.invalid > 0)
                    LanguageService.SetLocalizedFormattedText(lblSecurityFileAudit,
                        "Kho tệp: {0} tệp mã hóa không đọc được; giữ nguyên để xử lý riêng.",
                        () => new object?[] { fileStatus.invalid });
                else if (fileStatus.legacy > 0)
                    LanguageService.SetLocalizedFormattedText(lblSecurityFileAudit,
                        "Kho tệp: {0}/{1} tệp đã mã hóa; tệp cũ sẽ được chuyển đổi khi mở kho.",
                        () => new object?[] { fileStatus.encrypted, fileStatus.total });
                else
                    LanguageService.SetLocalizedFormattedText(lblSecurityFileAudit,
                        "Kho tệp: {0} tệp được mã hóa AES-GCM, khóa gắn với tài khoản Windows hiện tại.",
                        () => new object?[] { fileStatus.encrypted });
            }
            catch (UnauthorizedAccessException)
            {
                LockNow("Tài khoản đã bị khóa.");
            }
            catch (Exception ex)
            {
                LanguageService.SetLocalizedText(lblSecurityPasswordAudit, "Không thể đọc đầy đủ trạng thái kho mật khẩu.");
                LanguageService.SetLocalizedText(lblSecurity2FAAudit, "Không thể đọc trạng thái 2FA.");
                LanguageService.SetLocalizedText(lblSecurityBackupAudit, "Không thể đọc trạng thái sao lưu.");
                LanguageService.SetLocalizedText(lblSecurityRecoveryAudit, "Không thể đọc trạng thái khôi phục.");
                LanguageService.SetLocalizedFormattedText(
                    lblSecurityFileAudit,
                    "Không thể đọc trạng thái mã hóa tệp: {0}",
                    () => new object?[] { ex.Message });
            }
        }
        private void BtnOpenSecurityFinding_Click(object? sender, RoutedEventArgs e)
        {
            if (!EnsureActiveSession() || sender is not Button { Tag: Guid accountId }) return;
            string username = SessionService.CurrentUser!.Username;
            SavedAccount? account;
            try
            {
                account = _vaultService.Load(username)
                    .FirstOrDefault(item => item.Id == accountId && !item.IsDeleted);
            }
            catch (UnauthorizedAccessException)
            {
                LockNow("Tài khoản đã bị khóa.");
                return;
            }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, ex.Message, "Không thể mở tài khoản",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (account == null)
            {
                RefreshSecurityOverview();
                return;
            }
            CloseSettings();
            txtSearch.Clear();
            _searchTimer.Stop();
            if (chkFavoritesOnly.IsChecked == true)
                chkFavoritesOnly.IsChecked = false;
            ShowPage("accounts");
            navAccounts.IsChecked = true;
            RefreshAccounts();
            foreach (var item in dgAccounts.Items)
            {
                var original = item.GetType().GetProperty("_original")?.GetValue(item) as SavedAccount;
                if (original?.Id != accountId) continue;
                dgAccounts.SelectedItem = item;
                dgAccounts.ScrollIntoView(item);
                dgAccounts.Focus();
                return;
            }
        }
        private void BtnReviewSecurityFinding_Click(object? sender, RoutedEventArgs e)
        {
            if (!EnsureActiveSession() || sender is not Button { Tag: SecurityFindingViewItem item }) return;
            string username = SessionService.CurrentUser!.Username;
            SavedAccount? account;
            try
            {
                account = _vaultService.Load(username)
                    .FirstOrDefault(current => current.Id == item.AccountId && !current.IsDeleted);
            }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, ex.Message, LanguageService.Translate("ui.mainwindow.message100"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (account == null)
            {
                RefreshSecurityOverview();
                return;
            }
            var finding = new PasswordSecurityFinding(account.Id, item.Kind);
            var states = SecurityFindingReviewService.SyncFindings(
                username, new[] { finding }, new Dictionary<Guid, SavedAccount> { [account.Id] = account });
            var current = states[SecurityFindingReviewService.Key(account.Id, item.Kind)];
            var dialog = new SecurityFindingReviewWindow(item.Description, current) { Owner = this };
            if (dialog.ShowDialog() != true || !EnsureActiveSession()) return;
            try
            {
                SecurityFindingReviewService.Set(username, account, item.Kind,
                    dialog.SelectedStatus, dialog.RemindAtUtc, dialog.Reason);
                RefreshSecurityOverview();
            }
            catch (Exception ex)
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show(this, ex.Message, LanguageService.Translate("ui.mainwindow.message101"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
