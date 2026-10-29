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


        private const int AutoLockMinutes = 5;
        private const int AutoHidePasswordSeconds = 30;
        private const int AuthRememberSeconds = 60;

        private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly DispatcherTimer _hidePasswordTimer = new() { Interval = TimeSpan.FromSeconds(AutoHidePasswordSeconds) };


        private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private DateTime _lastActivity = DateTime.UtcNow;
        private DateTime _lastAuthTime = DateTime.MinValue;
        private bool _isLocked = false;


        private readonly DispatcherTimer _saveStateTimer;


        public MainWindow()
        {
            InitializeComponent();

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
        }





        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
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

            BuildPages();


            navAdmin.Visibility = SessionService.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

            UpdateUserChip();

            RefreshAccounts();
            RefreshFiles();
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
            if (DateTime.UtcNow - _lastActivity >= TimeSpan.FromMinutes(AutoLockMinutes))
                LockNow();
        }


        private void LockNow(string? reason = null)
        {
            if (_isLocked) return;
            _isLocked = true;

            string username = SessionService.CurrentUser?.Username ?? "SYSTEM";
            StopAutoLock();
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
            string username = SessionService.CurrentUser!.Username;

            if (IsAuthFresh()) return true;

            bool helloAvailable = false;
            try { helloAvailable = await WindowsHelloService.IsAvailableAsync(); } catch { }

            if (helloAvailable)
            {
                bool ok = false;
                try { ok = await WindowsHelloService.VerifyAsync(this, $"Xác nhận để {actionText}"); } catch { }
                if (_isLocked) return false;
                if (ok) { MarkAuthenticated(); return true; }
            }

            if (!TwoFactorService.IsEnabled(username))
            {
                try { ActivityLogger.Log(username, deniedLogCode, "Windows Hello thất bại, chưa bật 2FA"); } catch { }
                MessageBox.Show(
                    $"Cần xác thực để {actionText}, nhưng Windows Hello không thành công và bạn chưa bật xác thực 2 bước.\n\n" +
                    "Vào Bảo mật → Bật xác thực 2 bước (Authenticator) để có cách xác thực dự phòng.",
                    "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            bool ok2 = TwoFactorPromptWindow.AskIfEnabled(this, username);
            if (_isLocked) return false;
            if (ok2) { MarkAuthenticated(); return true; }

            try { ActivityLogger.Log(username, deniedLogCode, "Windows Hello và 2FA đều thất bại"); } catch { }
            MessageBox.Show("Xác thực thất bại.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }







        private void RefreshAccounts(bool updateStatus = true)
        {
            string user = SessionService.CurrentUser!.Username;
            string keyword = txtSearch.Text;

            _accounts = _vaultService.Search(user, keyword);

            var viewList = _accounts.Select(a => new
            {
                a.Id,
                a.ServiceName,
                a.Username,
                PasswordMasked = _showPassword ? a.Password : "••••••••",
                a.Note,
                a.CreatedAt,
                a.UpdatedAt,
                _original = a
            }).ToList();


            dgAccounts.ItemsSource = viewList;

            if (updateStatus) UpdateStorageInfo();
        }


        private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void BtnRefreshAccounts_Click(object? sender, RoutedEventArgs e)
        {
            txtSearch.Clear();
            _searchTimer.Stop();
            RefreshAccounts();
        }


        private async void BtnToggleShow_Click(object? sender, RoutedEventArgs e)
        {

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
                if (!available)
                {
                    MessageBox.Show(
                        "Hiện mật khẩu cần xác thực bằng Windows Hello nhưng máy này chưa hỗ trợ hoặc chưa thiết lập.\n\n" +
                        "Hãy cài đặt vân tay / khuôn mặt / PIN trong Cài đặt Windows rồi thử lại.",
                        "Không thể xác thực", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool verified = await WindowsHelloService.VerifyAsync(this, "Xác nhận để hiện mật khẩu");
                if (_isLocked) return;
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
                    existing.ServiceName = dlg.Result.ServiceName;
                    existing.Username = dlg.Result.Username;
                    existing.Password = dlg.Result.Password;
                    existing.Note = dlg.Result.Note;
                    existing.UpdatedAt = DateTime.Now;
                }
                _vaultService.Save(user, list);

                try { ActivityLogger.Log(user, "EDIT_ACCOUNT", dlg.Result.ServiceName); } catch { }
                RefreshAccounts();
            }
        }

        private void BtnDeleteAccount_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) { MessageBox.Show("Chọn một tài khoản để xóa."); return; }

            if (MessageBox.Show($"Xóa '{selected.ServiceName}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            string user = SessionService.CurrentUser!.Username;
            var list = _vaultService.Load(user);
            list.RemoveAll(x => x.Id == selected.Id);
            _vaultService.Save(user, list);

            try { ActivityLogger.Log(user, "DELETE_ACCOUNT", selected.ServiceName); } catch { }
            RefreshAccounts();
        }

        private void BtnCopyUsername_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) return;
            try { Clipboard.SetText(selected.Username); MessageBox.Show("Đã copy username!"); } catch { }
        }

        private async void BtnCopyPassword_Click(object? sender, RoutedEventArgs e)
        {
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

                    if (helloAvailable)
                    {
                        try { ok = await WindowsHelloService.VerifyAsync(this, "Xác nhận để copy mật khẩu"); } catch { ok = false; }
                        if (_isLocked) return;
                        if (ok) { method = "Windows Hello"; MarkAuthenticated(); }
                    }
                }


                if (!ok)
                {

                    if (!TwoFactorService.IsEnabled(username))
                    {
                        try { ActivityLogger.Log(username, "COPY_PASSWORD_DENIED", "Windows Hello thất bại, chưa bật 2FA"); } catch { }
                        MessageBox.Show(
                            "Xác thực Windows Hello không thành công và bạn chưa bật xác thực 2 bước nên không thể copy mật khẩu.\n\n" +
                            "Vào Bảo mật → Bật xác thực 2 bước (Authenticator) để có cách xác thực dự phòng.",
                            "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    ok = TwoFactorPromptWindow.AskIfEnabled(this, username);
                    if (_isLocked) return;
                    if (ok) { method = "2FA"; MarkAuthenticated(); }
                }

                if (!ok)
                {
                    try { ActivityLogger.Log(username, "COPY_PASSWORD_DENIED", "Windows Hello và 2FA đều thất bại"); } catch { }
                    MessageBox.Show("Xác thực thất bại. Đã hủy copy mật khẩu.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!ClipboardGuard.CopySensitive(selected.Password))
                {
                    MessageBox.Show(
                        "Không thể sao chép mật khẩu vào clipboard an toàn. Hãy thử lại.",
                        "Không thể sao chép", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try { ActivityLogger.Log(username, "COPY_PASSWORD", $"{selected.ServiceName} (xác thực: {method})"); } catch { }
                MessageBox.Show($"Đã copy password! Clipboard sẽ tự xóa sau {ClipboardGuard.ClearAfterSeconds} giây.");
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
            string user = SessionService.CurrentUser!.Username;
            dgFiles.ItemsSource = _fileService.ListFiles(user);
        }

        private void BtnImport_Click(object? sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Multiselect = true };
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
            if (dgFiles.SelectedItem is not FileInfo fi) { MessageBox.Show("Chọn file cần export."); return; }

            var dlg = new SaveFileDialog { FileName = fi.Name };
            if (dlg.ShowDialog() != true) return;

            try
            {
                _fileService.ExportFile(fi.FullName, dlg.FileName);
                try { ActivityLogger.Log(SessionService.CurrentUser!.Username, "EXPORT_FILE", fi.Name); } catch { }
                MessageBox.Show("Export thành công!");
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
        }

        private void BtnCreateText_Click(object? sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "Text files|*.txt", FileName = "new_file.txt" };
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
            lblProfileRole.Text = u.Role == UserRole.Admin ? "👑 Quản trị viên (Admin)" : "👤 Người dùng (User)";
            lblProfileCreatedAt.Text = u.CreatedAt.ToString("dd/MM/yyyy HH:mm:ss");

            try
            {
                if (u.WindowsHelloEnabled)
                {
                    lblWindowsHelloBtnText.Text = "Tắt Windows Hello";
                    btnToggleWindowsHello.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                    lblWindowsHelloStatus.Text = "✓ Đang bật đăng nhập bằng Windows Hello.";
                }
                else
                {
                    lblWindowsHelloBtnText.Text = "Bật Windows Hello";
                    btnToggleWindowsHello.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
                    lblWindowsHelloStatus.Text = "Chưa bật đăng nhập bằng Windows Hello.";
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
            label.Text = on ? "● Đang bật" : "○ Đang tắt";

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

                lbl2FATitle.Text = enabled ? "Tắt xác thực 2 bước" : "Bật xác thực 2 bước";
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

                bool verified = await WindowsHelloService.VerifyAsync(this, "Xác nhận để xuất CSV (chứa mật khẩu dạng chữ thường)");
                if (!verified)
                {
                    try { ActivityLogger.Log(username, "EXPORT_CSV_DENIED", "Xác thực Windows Hello thất bại"); } catch { }
                    MessageBox.Show("Xác thực thất bại. Đã hủy xuất CSV.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }


                var dlg = new SaveFileDialog
                {
                    Title = "Xuất CSV",
                    Filter = "CSV files (*.csv)|*.csv|Tất cả file (*.*)|*.*",
                    FileName = $"accounts_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };
                if (dlg.ShowDialog() != true) return;

                var accounts = _vaultService.Load(username);
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
                var dlg = new OpenFileDialog { Title = "Nhập CSV", Filter = "CSV files (*.csv)|*.csv|Tất cả file (*.*)|*.*" };
                if (dlg.ShowDialog() != true) return;

                string username = SessionService.CurrentUser!.Username;

                var parsed = CsvService.ImportFromFile(dlg.FileName);
                if (!parsed.ok)
                {
                    MessageBox.Show(parsed.message, "Lỗi nhập CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var preview = new CsvImportWindow(parsed.accounts, _vaultService.Load(username)) { Owner = this };
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
        private void MnuSysInfo_Click(object? sender, RoutedEventArgs e) => MessageBox.Show(SystemInfoService.GetSystemReport(), "Thông tin hệ thống");

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
                    MessageBox.Show($"Đã bật tự động đổi theme theo giờ.\n\n• 06:00 - 18:00: Sáng\n• 18:00 - 06:00: Tối\n\nHiện tại ({DateTime.Now:HH:mm}): {currentTheme}", "Auto Theme");
                }
                else
                {
                    AutoThemeService.Stop();
                    RegistryService.SetAutoThemeEnabled(false);
                    MessageBox.Show("Đã tắt tự động đổi theme theo giờ.", "Auto Theme");
                }

                UpdateAutoThemeCard();
                UpdateThemeIndicator();
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
        }





        private void MnuExportVault_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog { Title = "Xuất vault", Filter = "Vault backup (*.vault)|*.vault|Tất cả file (*.*)|*.*", FileName = $"vault_backup_{DateTime.Now:yyyyMMdd_HHmmss}.vault" };
                if (dlg.ShowDialog() != true) return;

                string username = SessionService.CurrentUser!.Username;
                var result = VaultBackupService.ExportToFile(username, dlg.FileName);
                MessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
        }

        private void MnuImportVault_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog { Title = "Nhập vault", Filter = "Vault backup (*.vault)|*.vault|Tất cả file (*.*)|*.*" };
                if (dlg.ShowDialog() != true) return;

                string username = SessionService.CurrentUser!.Username;
                if (MessageBox.Show($"Nhập vault từ file:\n{Path.GetFileName(dlg.FileName)}?\n\nCác tài khoản trùng sẽ được bỏ qua.", "Xác nhận nhập", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

                if (!TryGetImportPassphrase(dlg.FileName, out string? passphrase)) return;

                var result = VaultBackupService.ImportFromFile(username, dlg.FileName, passphrase, ConfirmForeignOwner);
                MessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

                if (result.ok) OfferSavePassphrase(passphrase);

                if (result.ok && result.importedCount > 0)
                {
                    RefreshAccounts();
                    UpdateStorageInfo();
                }
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
        }





        private async void MnuSyncGoogleDrive_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string username = SessionService.CurrentUser!.Username;
                OfferAdoptLegacyPassphrase(username);
                if (!BackupCrypto.HasStoredPassphrase(username))
                {
                    var ask = MessageBox.Show(
                        "Bản sao lưu lên Google Drive chưa có cụm mật khẩu khôi phục, nên chỉ mở được trên máy này. " +
                        "Nếu mất máy hoặc cài lại Windows, bạn sẽ không khôi phục được.\n\nĐặt cụm mật khẩu ngay bây giờ?",
                        "Nên đặt cụm mật khẩu sao lưu", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

                    if (ask == MessageBoxResult.Cancel) return;
                    if (ask == MessageBoxResult.Yes && !SetBackupPassphrase()) return;
                }

                string tempFile = Path.Combine(Path.GetTempPath(), $"vault_backup_{DateTime.Now:yyyyMMdd_HHmmss}.vault");

                var exportResult = VaultBackupService.ExportToFile(username, tempFile);
                if (!exportResult.ok) { MessageBox.Show(exportResult.message, "Lỗi xuất file", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                var syncService = new CloudSyncService(username);

                Mouse.OverrideCursor = Cursors.Wait;
                bool success = await syncService.UploadVaultAsync(tempFile);
                Mouse.OverrideCursor = null;

                try { File.Delete(tempFile); } catch { }

                if (success)
                {
                    MessageBox.Show("Đã đồng bộ vault lên Google Drive thành công!\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    ActivityLogger.Log(username, "SYNC_GOOGLE_DRIVE", "Upload vault thành công");
                }
                else
                {
                    MessageBox.Show("Đồng bộ thất bại.\n\n" + (syncService.LastError ?? "Vui lòng kiểm tra kết nối mạng, quyền truy cập Google Drive và cửa sổ trình duyệt (nếu có)."), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show($"Lỗi đồng bộ: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private async void MnuRestoreGoogleDrive_Click(object? sender, RoutedEventArgs e)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"vault_restore_{DateTime.Now:yyyyMMdd_HHmmss}.vault");

            try
            {
                string username = SessionService.CurrentUser!.Username;
                var syncService = new CloudSyncService(username);

                var dlg = new RestoreBackupWindow(syncService) { Owner = this };
                if (dlg.ShowDialog() != true || dlg.Selected is not { } pick) return;

                if (MessageBox.Show(
                        $"Nhập dữ liệu từ bản sao lưu trên Google Drive:\n{pick.CreatedText} ({pick.SizeText})?\n\n" +
                        "Các tài khoản trùng sẽ được bỏ qua, dữ liệu hiện tại không bị xóa.",
                        "Xác nhận khôi phục", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

                Mouse.OverrideCursor = Cursors.Wait;
                bool downloaded = await syncService.DownloadBackupAsync(pick.Id, tempFile);
                Mouse.OverrideCursor = null;

                if (!downloaded)
                {
                    MessageBox.Show(syncService.LastError ?? "Tải bản sao lưu từ Google Drive thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TryGetImportPassphrase(tempFile, out string? passphrase)) return;

                var result = VaultBackupService.ImportFromFile(username, tempFile, passphrase, ConfirmForeignOwner);
                MessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

                if (result.ok)
                {
                    OfferSavePassphrase(passphrase);
                    ActivityLogger.Log(username, "RESTORE_GOOGLE_DRIVE", $"Nhập vault từ Drive: {pick.Name}");

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
                MessageBox.Show($"Lỗi khôi phục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }





        private void MnuBackupPassphrase_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string username = SessionService.CurrentUser!.Username;
                OfferAdoptLegacyPassphrase(username);

                if (BackupCrypto.HasStoredPassphrase(username) &&
                    MessageBox.Show(
                        "Máy này đã có cụm mật khẩu sao lưu.\n\nĐổi sang cụm mới? Các bản sao lưu đã tạo trước đó " +
                        "vẫn cần cụm cũ để mở, nên hãy giữ lại cụm cũ.",
                        "Đổi cụm mật khẩu sao lưu", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                SetBackupPassphrase();
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
        }


        private bool SetBackupPassphrase()
        {
            var dlg = new BackupPassphraseWindow(confirmMode: true) { Owner = this };
            if (dlg.ShowDialog() != true) return false;

            string username = SessionService.CurrentUser!.Username;
            BackupCrypto.SaveStoredPassphrase(username, dlg.Passphrase);
            try { ActivityLogger.Log(SessionService.CurrentUser!.Username, "SET_BACKUP_PASSPHRASE", ""); } catch { }

            MessageBox.Show(
                "Đã đặt cụm mật khẩu sao lưu.\n\nTừ giờ các bản sao lưu và file xuất vault mới sẽ mở được trên máy khác " +
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


        private void OfferSavePassphrase(string? passphrase)
        {
            string username = SessionService.CurrentUser!.Username;
            if (passphrase == null || BackupCrypto.HasStoredPassphrase(username)) return;

            if (MessageBox.Show(
                    "Dùng cụm mật khẩu vừa nhập làm cụm sao lưu cho máy này?\n\n" +
                    "Từ giờ các bản sao lưu mới từ máy này sẽ mở được bằng cùng cụm đó.",
                    "Lưu cụm mật khẩu", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                BackupCrypto.SaveStoredPassphrase(username, passphrase);
            }
        }


        private bool ConfirmForeignOwner(string owner)
        {
            string current = SessionService.CurrentUser!.Username;

            return MessageBox.Show(
                $"Bản sao lưu này được tạo bởi người dùng '{owner}', không phải '{current}'.\n\n" +
                $"Nhập vào sẽ thêm các tài khoản của '{owner}' vào vault của '{current}'.\n" +
                "Chỉ tiếp tục nếu đây là dữ liệu của chính bạn (ví dụ bạn dùng tên đăng nhập khác trên máy mới).\n\n" +
                "Tiếp tục?",
                "Bản sao lưu của người dùng khác",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }



        private void OfferAdoptLegacyPassphrase(string username)
        {
            if (BackupCrypto.HasStoredPassphrase(username) || !BackupCrypto.HasLegacyPassphrase()) return;

            var ask = MessageBox.Show(
                "Máy này có một cụm mật khẩu sao lưu được đặt từ phiên bản trước, khi app chưa tách theo người dùng.\n\n" +
                $"Đây có phải cụm của '{username}'?\n\n" +
                "Có: dùng cụm đó cho tài khoản này (người dùng khác sẽ không nhận được nữa).\n" +
                "Không: giữ nguyên để đúng người đã đặt cụm đó nhận sau.",
                "Cụm mật khẩu sao lưu cũ", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

            if (ask == MessageBoxResult.Yes && !BackupCrypto.TryAdoptLegacyPassphrase(username))
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

                OfferAdoptLegacyPassphrase(username);
                string? passphrase = BackupCrypto.GetStoredPassphrase(username);
                if (passphrase == null)
                {
                    var ask = MessageBox.Show(
                        "File cá nhân được lưu không mã hóa trên máy này, nên bản sao lưu lên Google Drive cần được mã hóa bằng cụm mật khẩu sao lưu.\n\n" +
                        "Đặt cụm mật khẩu ngay bây giờ?",
                        "Cần cụm mật khẩu sao lưu", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (ask != MessageBoxResult.Yes || !SetBackupPassphrase()) return;

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

                var syncService = new CloudSyncService(username);
                bool success = await syncService.UploadFilesBackupAsync(tempFile);
                Mouse.OverrideCursor = null;

                if (_isLocked) return;

                if (success)
                {
                    MessageBox.Show(
                        $"{pack.message}\n\nĐã đồng bộ lên Google Drive thành công!\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.",
                        "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    ActivityLogger.Log(username, "SYNC_FILES_GOOGLE_DRIVE", $"Upload {pack.count} File cá nhân ({FileBackupService.FormatSize(pack.bytes)})");
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
                string username = SessionService.CurrentUser!.Username;
                var syncService = new CloudSyncService(username);

                var dlg = new RestoreBackupWindow(syncService, BackupKind.Files) { Owner = this };
                if (dlg.ShowDialog() != true || dlg.Selected is not { } pick) return;

                if (MessageBox.Show(
                        $"Khôi phục File cá nhân từ bản sao lưu trên Google Drive:\n{pick.CreatedText} ({pick.SizeText})?\n\n" +
                        "Không có file nào bị ghi đè: file trùng nội dung được bỏ qua, file trùng tên nhưng khác nội dung được lưu thêm với tên mới.",
                        "Xác nhận khôi phục", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

                Mouse.OverrideCursor = Cursors.Wait;
                bool downloaded = await syncService.DownloadBackupAsync(pick.Id, tempFile);

                if (!downloaded)
                {
                    Mouse.OverrideCursor = null;
                    MessageBox.Show(syncService.LastError ?? "Tải bản sao lưu từ Google Drive thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }


                string? typed = null;
                var result = await Task.Run(() => FileBackupService.RestoreFromFile(
                    username, tempFile, null,
                    err => { typed = AskOnUi(() => PromptRestorePassphrase(err)); return typed; },
                    owner => AskOnUi(() => ConfirmForeignOwner(owner))));
                Mouse.OverrideCursor = null;

                if (_isLocked) return;

                MessageBox.Show(result.message, result.ok ? "Thành công" : "Lỗi", MessageBoxButton.OK, result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);


                RefreshFiles();
                UpdateStorageInfo();

                if (result.ok)
                {
                    OfferSavePassphrase(typed);
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
            if (MessageBox.Show("Xóa toàn bộ cài đặt (theme, kích thước, vị trí, user cuối)?\n\nKhởi động lại app để áp dụng toàn bộ.", "Xác nhận Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            RegistryService.Clear();
            ThemeService.ApplyLight();

            MessageBox.Show("Đã reset cài đặt.\n\nKhởi động lại app để áp dụng toàn bộ.");
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
    }
}
