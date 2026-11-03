using System;
using System.Collections.Generic;
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

        private sealed record SecurityFindingViewItem(Guid AccountId, string Description);

        private int AutoLockMinutes = 5;
        private bool _lockWhenMinimized;
        private const int AutoHidePasswordSeconds = 30;
        private const int AuthRememberSeconds = 60;

        private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly DispatcherTimer _hidePasswordTimer = new() { Interval = TimeSpan.FromSeconds(AutoHidePasswordSeconds) };

        private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private DateTime _lastActivity = DateTime.UtcNow;
        private DateTime _lastAuthTime = DateTime.MinValue;
        private bool _isLocked = false;
        private readonly CancellationTokenSource _sessionRevocationCts = new();

        private readonly DispatcherTimer _saveStateTimer;

        public MainWindow()
        {
            InitializeComponent();
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
            if (!SessionService.IsLoggedIn)
            {
                MessageBox.Show("Chưa đăng nhập. Vui lòng đăng nhập lại.");
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
                MessageBox.Show($"Không thể dọn thùng rác đã hết hạn. Dữ liệu hiện có vẫn được giữ nguyên.\n\n{ex.Message}",
                    "Không thể dọn thùng rác", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            BuildPages();
            LoadLockSettingsUi();

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

            navAccounts.IsChecked = true;
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
            _idleTimer.Start();
        }

        private void StopAutoLock()
        {
            try { InputManager.Current.PreProcessInput -= OnPreProcessInput; } catch { }
            try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
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
            if (e.Reason is SessionSwitchReason.SessionLock
                or SessionSwitchReason.ConsoleDisconnect
                or SessionSwitchReason.RemoteDisconnect)
            {
                string why = e.Reason == SessionSwitchReason.SessionLock
                    ? "Windows bị khóa (Win+L)"
                    : $"Phiên Windows ngắt kết nối ({e.Reason})";

                Dispatcher.BeginInvoke(new Action(() => LockNow(why)));
            }
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
                MessageBox.Show("Chọn thời gian tự khóa hợp lệ.", "Cài đặt bảo mật", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AutoLockMinutes = minutes;
            _lockWhenMinimized = chkLockWhenMinimized.IsChecked == true;
            RegistryService.SetAutoLockMinutes(AutoLockMinutes);
            RegistryService.SetLockWhenMinimized(_lockWhenMinimized);
            MessageBox.Show("Đã lưu cài đặt tự khóa.", "Cài đặt bảo mật", MessageBoxButton.OK, MessageBoxImage.Information);
        }

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

            foreach (var w in others) { try { w.Close(); } catch { } }
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
                MessageBox.Show(
                    $"Cần xác thực để {localizedActionText}, nhưng Windows Hello không thành công và bạn chưa bật xác thực 2 bước.\n\n" +
                    "Vào Bảo mật → Bật xác thực 2 bước (Authenticator) để có cách xác thực dự phòng.",
                    "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            bool ok2 = TwoFactorPromptWindow.AskIfEnabled(this, username);
            if (_isLocked || !EnsureActiveSession()) return false;
            if (ok2) { MarkAuthenticated(); return true; }

            try { ActivityLogger.Log(username, deniedLogCode, "Windows Hello và 2FA đều thất bại"); } catch { }
            MessageBox.Show("Xác thực thất bại.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void RefreshAccounts(bool updateStatus = true)
        {
            if (!EnsureActiveSession()) return;

            string user = SessionService.CurrentUser!.Username;
            string keyword = txtSearch.Text;

            try { _accounts = _vaultService.Search(user, keyword); }
            catch (UnauthorizedAccessException)
            {
                LockNow("Tài khoản đã bị khóa.");
                return;
            }

            var viewList = _accounts.Select(a => new
            {
                a.Id,
                a.ServiceName,
                a.Username,
                PasswordMasked = _showPassword ? a.Password : "••••••••",
                a.Note,
                a.WebsiteUrl,
                WebsiteDisplay = WebsiteDomainService.TryGetHost(a.WebsiteUrl, out string websiteHost)
                    ? websiteHost : "",
                a.Folder,
                TagsText = string.Join(", ", a.Tags ?? new List<string>()),
                FavoriteText = a.IsFavorite ? "★" : "",
                a.CreatedAt,
                a.UpdatedAt,
                PasswordChangedText = a.PasswordChangedAt?.ToString("dd/MM/yyyy") ?? "Chưa rõ",
                TotpState = string.IsNullOrWhiteSpace(a.TotpSecret) ? "—" : "Có",
                _original = a
            }).ToList();

            dgAccounts.ItemsSource = chkFavoritesOnly.IsChecked == true
                ? viewList.Where(item => item._original.IsFavorite).ToList()
                : viewList;

            if (updateStatus) UpdateStorageInfo();
        }

        private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void AccountFilters_Changed(object? sender, RoutedEventArgs e)
        {
            if (IsLoaded) RefreshAccounts();
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
                    MessageBox.Show(
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
                    MessageBox.Show("Xác thực thất bại. Mật khẩu vẫn được ẩn.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                MarkAuthenticated();
                ShowPasswordNow();
                try { ActivityLogger.Log(username, "SHOW_PASSWORD", ""); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (selected == null) { MessageBox.Show("Chọn một tài khoản để sửa."); return; }

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
                    MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
                }
                _vaultService.Save(user, list);

                try { ActivityLogger.Log(user, "EDIT_ACCOUNT", dlg.Result.ServiceName); } catch { }
                RefreshAccounts();
            }
        }

        private async void BtnPasswordHistory_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) { MessageBox.Show("Chọn một tài khoản để xem lịch sử mật khẩu."); return; }
            if (!await RequireAuthAsync("xem lịch sử mật khẩu", "PASSWORD_HISTORY_DENIED")) return;

            string username = SessionService.CurrentUser!.Username;
            var latest = _vaultService.Load(username).FirstOrDefault(account => account.Id == selected.Id);
            if (latest == null) { MessageBox.Show("Tài khoản không còn trong kho mật khẩu.", "Không tìm thấy", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!EnsureActiveSession()) return;

            var dialog = new PasswordHistoryWindow(latest) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedEntry == null) return;
            if (!EnsureActiveSession()) return;

            if (MessageBox.Show(
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

            try
            {
                _vaultService.Save(username, accounts);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể khôi phục mật khẩu từ lịch sử. Kho mật khẩu chưa được cập nhật.\n\n{ex.Message}",
                    "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try { ActivityLogger.Log(username, "RESTORE_PASSWORD_HISTORY", latest.ServiceName); } catch { }
            RefreshAccounts();
        }

        private void BtnDeleteAccount_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) { MessageBox.Show("Chọn một tài khoản để xóa."); return; }

            if (MessageBox.Show($"Đưa '{selected.ServiceName}' vào thùng rác 30 ngày?", "Xóa tài khoản", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            string user = SessionService.CurrentUser!.Username;
            var list = _vaultService.Load(user);
            var target = list.FirstOrDefault(x => x.Id == selected.Id);
            if (target == null) return;
            target.IsDeleted = true;
            target.DeletedAt = DateTime.UtcNow;
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
                MessageBox.Show("Tài khoản này chưa có mã TOTP.", "Mã TOTP", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show($"Không thể cập nhật thùng rác. Dữ liệu hiện có vẫn được giữ nguyên.\n\n{ex.Message}",
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
                _vaultService.Save(username, accounts);
                try { ActivityLogger.Log(username, "RESTORE_ACCOUNT_FROM_TRASH", account.ServiceName); } catch { }
                return true;
            }

            if (account.IsDeleted)
            {
                MessageBox.Show("Khôi phục tài khoản trước khi khôi phục ghi chú của tài khoản đó.",
                    "Chưa thể khôi phục", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            var note = account.DeletedNotes.FirstOrDefault(entry => entry.Id == item.NoteId);
            if (note == null) return false;
            if (!string.IsNullOrWhiteSpace(account.Note))
            {
                if (MessageBox.Show($"Ghi chú hiện tại của '{account.ServiceName}' sẽ được đưa vào thùng rác và thay bằng ghi chú được khôi phục. Tiếp tục?",
                        "Khôi phục ghi chú", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return false;
                account.DeletedNotes.Add(new DeletedNoteEntry { Content = account.Note, DeletedAt = DateTime.UtcNow });
            }
            account.Note = note.Content;
            account.DeletedNotes.RemoveAll(entry => entry.Id == note.Id);
            account.UpdatedAt = DateTime.Now;
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
            try { Clipboard.SetText(selected.Username); MessageBox.Show("Đã sao chép tên đăng nhập!"); } catch { }
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
                        MessageBox.Show(
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
                    MessageBox.Show("Xác thực thất bại. Đã hủy sao chép mật khẩu.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!EnsureActiveSession()) return;

                if (!ClipboardGuard.CopySensitive(selected.Password))
                {
                    MessageBox.Show(
                        "Không thể sao chép mật khẩu vào bộ nhớ tạm an toàn. Hãy thử lại.",
                        "Không thể sao chép", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try { ActivityLogger.Log(username, "COPY_PASSWORD", $"{selected.ServiceName} (xác thực: {method})"); } catch { }
                MessageBox.Show($"Đã sao chép mật khẩu! Bộ nhớ tạm sẽ tự xóa sau {ClipboardGuard.ClearAfterSeconds} giây.");
            }
            catch (Exception ex)
            {
                if (!_isLocked)
                    MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(
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
                    MessageBox.Show(
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
                catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
            }
            RefreshFiles();
            UpdateStorageInfo();
        }

        private void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            if (dgFiles.SelectedItem is not FileInfo fi) { MessageBox.Show("Chọn tệp cần xuất."); return; }

            var dlg = new SaveFileDialog { Title = "Xuất tệp", Filter = "Tất cả tệp (*.*)|*.*", FileName = fi.Name };
            LocalizedFileDialog.Apply(dlg);
            if (dlg.ShowDialog() != true) return;

            try
            {
                _fileService.ExportFile(fi.FullName, dlg.FileName);
                try { ActivityLogger.Log(SessionService.CurrentUser!.Username, "EXPORT_FILE", fi.Name); } catch { }
                MessageBox.Show("Xuất thành công!");
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
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
            if (MessageBox.Show($"Xóa {fi.Name}?", "Xác nhận", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

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
            lblProfileCreatedAt.Text = u.CreatedAt.ToString("dd/MM/yyyy HH:mm:ss");

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

                MessageBox.Show("Đã lưu thông tin cá nhân!");
            }
            else { MessageBox.Show(result.message, "Lỗi"); }
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
                    if (!available) { MessageBox.Show("Máy tính không hỗ trợ Windows Hello.", "Không khả dụng", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                    string sid = WindowsHelloService.GetCurrentWindowsSid();
                    if (string.IsNullOrWhiteSpace(sid)) { MessageBox.Show("Không lấy được SID tài khoản Windows.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error); return; }

                    var confirm = MessageBox.Show($"Bật đăng nhập bằng Windows Hello cho '{u.Username}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;

                    bool verified = await WindowsHelloService.VerifyAsync(this, $"Xác nhận bật Windows Hello");
                    if (!verified) { MessageBox.Show("Xác thực thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                    var result = _userService.SetWindowsHello(u.Id, true, sid);
                    if (result.ok)
                    {
                        var updated = _userService.FindById(u.Id);
                        if (updated != null) SessionService.Login(updated);

                        ActivityLogger.Log(u.Username, "ENABLE_WINDOWS_HELLO", "");
                        MessageBox.Show("Đã bật thành công!", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadProfile();
                    }
                }
                else
                {
                    var confirm = MessageBox.Show($"Tắt đăng nhập bằng Windows Hello cho '{u.Username}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;

                    var result = _userService.SetWindowsHello(u.Id, false, "");
                    if (result.ok)
                    {
                        var updated = _userService.FindById(u.Id);
                        if (updated != null) SessionService.Login(updated);

                        ActivityLogger.Log(u.Username, "DISABLE_WINDOWS_HELLO", "");
                        MessageBox.Show("Đã tắt thành công!", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadProfile();
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
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
                    MessageBox.Show("Bạn chưa bật xác thực 2 bước nên chưa có mã khôi phục.",
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
                    MessageBox.Show("Đã tạo mã khôi phục mới. Các mã cũ không còn dùng được.",
                        "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        MessageBox.Show("Đã tắt xác thực 2 bước.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                Update2FAMenu();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show(
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
                    MessageBox.Show("Xác thực thất bại. Đã hủy xuất CSV.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    MessageBox.Show("Không có tài khoản nào để xuất.", "Thông báo");
                    return;
                }

                var res = CsvService.ExportToFile(accounts, dlg.FileName);
                if (!res.ok)
                {
                    MessageBox.Show(res.message, "Lỗi xuất CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ActivityLogger.Log(username, "EXPORT_CSV", $"Xuất {accounts.Count} tài khoản ra CSV");
                MessageBox.Show(res.message, "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xuất CSV: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MnuImportCsv_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog { Title = "Nhập CSV", Filter = "Tệp CSV (*.csv)|*.csv|Tất cả tệp (*.*)|*.*" };
                LocalizedFileDialog.Apply(dlg);
                if (dlg.ShowDialog() != true) return;

                string username = SessionService.CurrentUser!.Username;

                var parsed = CsvService.ImportFromFile(dlg.FileName);
                if (!parsed.ok)
                {
                    MessageBox.Show(parsed.message, "Lỗi nhập CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var preview = new CsvImportWindow(parsed.accounts,
                    _vaultService.Load(username).Where(account => !account.IsDeleted).ToList()) { Owner = this };
                bool confirmed = preview.ShowDialog() == true;
                if (_isLocked || !confirmed) return;

                var list = _vaultService.Load(username);
                list.AddRange(preview.Result);
                _vaultService.Save(username, list);

                RefreshAccounts();
                UpdateStorageInfo();
                ActivityLogger.Log(username, "IMPORT_CSV", $"Nhập {preview.Result.Count} tài khoản từ CSV ({parsed.message})");

                MessageBox.Show($"Đã nhập {preview.Result.Count} tài khoản.", "Kết quả nhập CSV",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi nhập CSV: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void MnuAdmin_Click(object? sender, RoutedEventArgs e) => new AdminWindow().ShowDialog();
        private void MnuChangePwd_Click(object? sender, RoutedEventArgs e) => new ChangePasswordWindow().ShowDialog();
        private void MnuSysInfo_Click(object? sender, RoutedEventArgs e) => MessageBox.Show(SystemInfoService.GetSystemReport(), LanguageService.Translate("Thông tin hệ thống"));

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
                MessageBox.Show($"Đã bật tự động đổi giao diện theo giờ.\n\n• 06:00 - 18:00: Sáng\n• 18:00 - 06:00: Tối\n\nHiện tại ({DateTime.Now:HH:mm}): {currentTheme}", "Giao diện tự động");
                }
                else
                {
                    AutoThemeService.Stop();
                    RegistryService.SetAutoThemeEnabled(false);
                MessageBox.Show("Đã tắt tự động đổi giao diện theo giờ.", "Giao diện tự động");
                }

                UpdateAutoThemeCard();
                UpdateThemeIndicator();
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
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
                MessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
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
                    MessageBox.Show(prepared.message, "Không thể xem trước", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var preview = prepared.preview;
                string summaryTemplate = "Nguồn: {0}\nNgười tạo: {1}\nSẽ nhập {2} tài khoản; bỏ qua {3} mục trùng.\nMật khẩu không hiển thị trong phần xem trước.";
                var previewWindow = new BackupRestorePreviewWindow(
                    "Xem trước nhập vault", summaryTemplate,
                    () => new object?[]
                    {
                        Path.GetFileName(dlg.FileName),
                        string.IsNullOrWhiteSpace(preview.Owner) ? LanguageService.Translate("Không rõ") : preview.Owner,
                        preview.ImportedCount,
                        preview.SkippedCount
                    }, preview.Items) { Owner = this };
                if (previewWindow.ShowDialog() != true || !EnsureActiveSession()) return;

                var result = VaultBackupService.CommitImport(username, preview, ConfirmForeignOwner);
                MessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

                if (result.ok) await OfferSavePassphraseAsync(passphrase);
                if (_isLocked) return;

                if (result.ok && result.importedCount > 0)
                {
                    RefreshAccounts();
                    UpdateStorageInfo();
                }
            }
            catch (Exception ex) { if (!_isLocked) MessageBox.Show($"Lỗi: {ex.Message}"); }
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
                    try { ActivityLogger.Log(username, "VERIFY_FILE_BACKUP", Path.GetFileName(dlg.FileName)); } catch { }
                    MessageBox.Show(inspection.message,
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
                    try { ActivityLogger.Log(username, "VERIFY_VAULT_BACKUP", Path.GetFileName(dlg.FileName)); } catch { }
                    MessageBox.Show(inspection.message,
                        inspection.ok ? "Bản sao lưu hợp lệ" : "Không thể xác minh",
                        MessageBoxButton.OK,
                        inspection.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                if (!_isLocked)
                    MessageBox.Show("Đã hủy kiểm tra vì phiên đăng nhập không còn hoạt động.", "Đã hủy",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (!_isLocked)
                    MessageBox.Show($"Không thể kiểm tra bản sao lưu: {ex.Message}", "Lỗi",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
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

                var answer = MessageBox.Show(
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
                    MessageBox.Show($"Không thể ngắt kết nối Google Drive: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
                    var ask = MessageBox.Show(
                        "Bản sao lưu lên Google Drive chưa có cụm mật khẩu khôi phục, nên chỉ mở được trên máy này. " +
                        "Nếu mất máy hoặc cài lại Windows, bạn sẽ không khôi phục được.\n\nĐặt cụm mật khẩu ngay bây giờ?",
                        "Nên đặt cụm mật khẩu sao lưu", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

                    if (ask == MessageBoxResult.Cancel) return;
                    if (ask == MessageBoxResult.Yes && !await SetBackupPassphraseAsync()) return;
                }

                tempFile = Path.Combine(Path.GetTempPath(), $"vault_backup_{DateTime.Now:yyyyMMdd_HHmmss}.vault");

                var exportResult = VaultBackupService.ExportToFile(username, tempFile);
                if (!exportResult.ok) { MessageBox.Show(exportResult.message, "Lỗi xuất file", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (!EnsureActiveSession()) return;

                var syncService = new CloudSyncService(username);

                Mouse.OverrideCursor = Cursors.Wait;
                bool success = await syncService.UploadVaultAsync(tempFile, ct: _sessionRevocationCts.Token);
                Mouse.OverrideCursor = null;

                if (_isLocked || !EnsureActiveSession()) return;

                if (success)
                {
                    MessageBox.Show("Đã đồng bộ kho mật khẩu lên Google Drive thành công!\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    ActivityLogger.Log(username, "SYNC_GOOGLE_DRIVE", "Đã tải kho mật khẩu lên thành công");
                }
                else
                {
                    MessageBox.Show("Đồng bộ thất bại.\n\n" + (syncService.LastError ?? "Vui lòng kiểm tra kết nối mạng, quyền truy cập Google Drive và cửa sổ trình duyệt (nếu có)."), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked)
                    MessageBox.Show($"Lỗi đồng bộ: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show(syncService.LastError ?? "Tải bản sao lưu từ Google Drive thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TryGetImportPassphrase(tempFile, out string? passphrase)) return;

                Mouse.OverrideCursor = Cursors.Wait;
                var prepared = await Task.Run(() => VaultBackupService.PrepareImport(username, tempFile, passphrase));
                Mouse.OverrideCursor = null;
                if (_isLocked || !EnsureActiveSession()) return;
                if (!prepared.ok || prepared.preview == null)
                {
                    MessageBox.Show(prepared.message, "Không thể xem trước", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        string.IsNullOrWhiteSpace(preview.Owner) ? LanguageService.Translate("Không rõ") : preview.Owner,
                        preview.ImportedCount,
                        preview.SkippedCount
                    }, preview.Items) { Owner = this };
                if (previewWindow.ShowDialog() != true || !EnsureActiveSession()) return;

                var result = VaultBackupService.CommitImport(username, preview, ConfirmForeignOwner);
                MessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

                if (result.ok)
                {
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
                if (!_isLocked) MessageBox.Show($"Lỗi khôi phục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show(
                        "Máy này đã có cụm mật khẩu sao lưu.\n\nĐổi sang cụm mới? Các bản sao lưu đã tạo trước đó " +
                        "vẫn cần cụm cũ để mở, nên hãy giữ lại cụm cũ.",
                        "Đổi cụm mật khẩu sao lưu", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                await SetBackupPassphraseAsync();
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
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

            MessageBox.Show(
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

                MessageBox.Show("Cụm mật khẩu không đúng.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }

        private async Task OfferSavePassphraseAsync(string? passphrase)
        {
            if (_isLocked || SessionService.CurrentUser is not { } currentUser) return;
            string username = currentUser.Username;
            if (passphrase == null || BackupCrypto.HasStoredPassphrase(username)) return;

            if (MessageBox.Show(
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

            return MessageBox.Show(
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

            var ask = MessageBox.Show(
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
                MessageBox.Show(
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
                    MessageBox.Show("Kho File cá nhân đang trống, không có gì để sao lưu.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (bytes > FileBackupService.MaxTotalBytes)
                {
                    MessageBox.Show(
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
                    var ask = MessageBox.Show(
                        "Bản sao lưu lên Google Drive cần cụm mật khẩu sao lưu để có thể mở trên máy khác.\n\n" +
                        "Đặt cụm mật khẩu ngay bây giờ?",
                        "Cần cụm mật khẩu sao lưu", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (ask != MessageBoxResult.Yes || !await SetBackupPassphraseAsync()) return;

                    passphrase = BackupCrypto.GetStoredPassphrase(username);
                    if (passphrase == null) return;
                }

                if (MessageBox.Show(
                        $"Sao lưu {count} File cá nhân ({FileBackupService.FormatSize(bytes)}) lên Google Drive?\n\n" +
                        "Các file được nén và mã hóa bằng cụm mật khẩu sao lưu trước khi tải lên.",
                        "Xác nhận sao lưu", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

                Mouse.OverrideCursor = Cursors.Wait;
                string pass = passphrase;
                var pack = await Task.Run(() => FileBackupService.CreateArchive(username, tempFile, pass));

                if (!pack.ok)
                {
                    Mouse.OverrideCursor = null;
                    MessageBox.Show(pack.message, "Không thể sao lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!EnsureActiveSession()) return;

                var syncService = new CloudSyncService(username);
                bool success = await syncService.UploadFilesBackupAsync(tempFile, ct: _sessionRevocationCts.Token);
                Mouse.OverrideCursor = null;

                if (_isLocked || !EnsureActiveSession()) return;

                if (success)
                {
                    MessageBox.Show(
                        $"{pack.message}\n\nĐã đồng bộ lên Google Drive thành công!\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.",
                        "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    ActivityLogger.Log(username, "SYNC_FILES_GOOGLE_DRIVE", $"Tải lên {pack.count} tệp cá nhân ({FileBackupService.FormatSize(pack.bytes)})");
                }
                else
                {
                    MessageBox.Show(
                        "Đồng bộ thất bại.\n\n" + (syncService.LastError ?? "Vui lòng kiểm tra kết nối mạng và quyền truy cập Google Drive."),
                        "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                if (!_isLocked)
                    MessageBox.Show($"Lỗi đồng bộ File cá nhân: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show(syncService.LastError ?? "Tải bản sao lưu từ Google Drive thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    MessageBox.Show(prepared.message, "Không thể xem trước", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                            string.IsNullOrWhiteSpace(preview.Owner) ? LanguageService.Translate("Không rõ") : preview.Owner,
                            preview.AddedCount,
                            preview.SkippedCount,
                            FileBackupService.FormatSize(preview.TotalBytes),
                            preview.InvalidCount
                        }
                        : new object?[]
                        {
                            pick.CreatedText,
                            pick.SizeText,
                            string.IsNullOrWhiteSpace(preview.Owner) ? LanguageService.Translate("Không rõ") : preview.Owner,
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

                MessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

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
                    MessageBox.Show($"Lỗi khôi phục File cá nhân: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(this, previousError, "Cụm mật khẩu sao lưu", MessageBoxButton.OK, MessageBoxImage.Warning);

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

            MessageBox.Show("Đã đưa cửa sổ về giữa màn hình.");
        }

        private void MnuResetSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Xóa toàn bộ cài đặt (giao diện, kích thước, vị trí, tài khoản gần nhất)?\n\nKhởi động lại ứng dụng để áp dụng toàn bộ.", "Xác nhận đặt lại", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            RegistryService.Clear();
            ThemeService.ApplyLight();

            MessageBox.Show("Đã đặt lại cài đặt.\n\nKhởi động lại ứng dụng để áp dụng toàn bộ.");
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
            if (MessageBox.Show(
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
                _pwnedResultsAreCurrent = true;
                LanguageService.SetLocalizedText(lblPwnedPasswordAudit, "Chưa có mật khẩu trong kho mật khẩu để kiểm tra.");
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

                if (result.AffectedAccounts == 0)
                {
                    LanguageService.SetLocalizedFormattedText(
                        lblPwnedPasswordAudit,
                        "Kiểm tra xong: không tìm thấy mật khẩu nào trong dữ liệu Pwned Passwords ({0} mật khẩu duy nhất).",
                        () => new object?[] { result.CheckedUniquePasswords });
                }
                else
                {
                    var affectedAccounts = accounts
                        .Where(account => result.AffectedAccountIds.Contains(account.Id))
                        .ToList();
                    string affectedNames = string.Join(Environment.NewLine,
                        affectedAccounts.Take(15).Select(account => $"• {account.ServiceName} ({account.Username})"));
                    if (affectedAccounts.Count > 15)
                        affectedNames += $"{Environment.NewLine}… và {affectedAccounts.Count - 15} mục khác.";

                    LanguageService.SetLocalizedFormattedText(
                        lblPwnedPasswordAudit,
                        "Cần đổi {0} mật khẩu trong kho mật khẩu ({1} giá trị duy nhất xuất hiện trong dữ liệu rò rỉ).{2}{3}",
                        () => new object?[] { result.AffectedAccounts, result.BreachedUniquePasswords, Environment.NewLine, affectedNames });
                }
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

                _pwnedResultsAreCurrent = _pwnedCheckCompleted &&
                    accounts.Count == _pwnedCheckedAccountVersions.Count &&
                    accounts.All(account => _pwnedCheckedAccountVersions.TryGetValue(account.Id, out DateTime checkedVersion) &&
                        checkedVersion == account.UpdatedAt);

                if (!_pwnedCheckCompleted)
                {
                    LanguageService.SetLocalizedText(lblPwnedPasswordAudit,
                        "Chưa kiểm tra mật khẩu rò rỉ trong phiên này; dùng nút kiểm tra để đối chiếu tùy chọn.");
                }
                else if (!_pwnedResultsAreCurrent)
                {
                    LanguageService.SetLocalizedText(lblPwnedPasswordAudit,
                        "Vault đã thay đổi sau lần kiểm tra rò rỉ; hãy chạy lại để cập nhật kết quả.");
                }

                var findings = PasswordSecurityAuditService.Analyze(
                    accounts, _pwnedBreachedAccountIds, _pwnedResultsAreCurrent, DateTime.Now);
                var accountById = accounts.ToDictionary(account => account.Id);
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
                        return new SecurityFindingViewItem(account.Id, description);
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

                DateTime? lastBackup = AutoBackupService.GetLastBackupUtc(username);
                if (lastBackup == null)
                {
                    LanguageService.SetLocalizedText(lblSecurityBackupAudit, "Sao lưu kho mật khẩu: chưa ghi nhận bản sao lưu nào.");
                }
                else
                {
                    string backupDate = lastBackup.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                    LanguageService.SetLocalizedFormattedText(
                        lblSecurityBackupAudit,
                        AutoBackupService.NeedsBackup(username)
                            ? "Sao lưu kho mật khẩu gần nhất: {0} • dữ liệu đã thay đổi sau lần sao lưu."
                            : "Sao lưu kho mật khẩu gần nhất: {0} • chưa phát hiện thay đổi mới.",
                        () => new object?[] { backupDate });
                }

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
                MessageBox.Show(this, ex.Message, "Không thể mở tài khoản",
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
    }
}
