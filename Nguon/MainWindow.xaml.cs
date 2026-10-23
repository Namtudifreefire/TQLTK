using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class MainWindow : Window
    {
        // ================== SERVICES ==================
        private readonly AccountVaultService _vaultService = new();
        private readonly LocalFileService _fileService = new();
        private readonly UserService _userService = new();

        // ================== STATE ==================
        private List<SavedAccount> _accounts = new();
        private bool _showPassword = false;

        // ================== TỰ KHÓA / TỰ ẨN MK / NHỚ XÁC THỰC ==================
        private const int AutoLockMinutes = 5;            // không thao tác bao lâu thì tự khóa
        private const int AutoHidePasswordSeconds = 30;   // mật khẩu đang hiện tự ẩn lại sau bao lâu
        private const int AuthRememberSeconds = 60;       // nhớ xác thực bao lâu (Hiện MK / Copy Password)

        private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly DispatcherTimer _hidePasswordTimer = new() { Interval = TimeSpan.FromSeconds(AutoHidePasswordSeconds) };
        private DateTime _lastActivity = DateTime.UtcNow;
        private DateTime _lastAuthTime = DateTime.MinValue;
        private bool _isLocked = false;

        // Menu "Tạo lại mã khôi phục 2FA" (tạo bằng code, không cần sửa XAML)
        private MenuItem? _mnu2FARegen;

        // ================== DEBOUNCE TIMER ==================
        private readonly DispatcherTimer _saveStateTimer;

        // ================== CONSTRUCTOR ==================
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

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            LocationChanged += MainWindow_LocationChanged;
            SizeChanged += MainWindow_SizeChanged;
        }

        // ================================================================
        // ================== KHỞI ĐỘNG ==================
        // ================================================================

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

            mnuAdmin.Visibility = SessionService.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

            var u = SessionService.CurrentUser!;
            string displayName = string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName;
            lblUser.Text = $"👤 {displayName} ({u.Role})";

            RefreshAccounts();
            RefreshFiles();
            UpdateStorageInfo();
            LoadProfile();

            try { mnuAutoTheme.IsChecked = AutoThemeService.IsEnabled; } catch { }

            UpdateThemeIndicator();
            Setup2FARegenMenu();
            Update2FAMenu();
            StartAutoLock();
        }

        // ================================================================
        // ================== NHỚ VỊ TRÍ + KÍCH THƯỚC ==================
        // ================================================================

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopAutoLock();
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

        // ================================================================
        // ================== TỰ KHÓA / TỰ ẨN MK / NHỚ XÁC THỰC ==================
        // ================================================================

        private void StartAutoLock()
        {
            _lastActivity = DateTime.UtcNow;
            InputManager.Current.PreProcessInput += OnPreProcessInput;
            _idleTimer.Start();
        }

        private void StopAutoLock()
        {
            try { InputManager.Current.PreProcessInput -= OnPreProcessInput; } catch { }
            _idleTimer.Stop();
            _hidePasswordTimer.Stop();
        }

        // Mọi thao tác chuột / bàn phím trong app đều làm mới thời điểm hoạt động
        private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
        {
            var input = e.StagingItem.Input;
            if (input is MouseEventArgs || input is KeyboardEventArgs)
                _lastActivity = DateTime.UtcNow;
        }

        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            if (_isLocked) return;
            if (DateTime.UtcNow - _lastActivity >= TimeSpan.FromMinutes(AutoLockMinutes))
                LockNow();
        }

        // Khóa = đăng xuất và quay về màn hình đăng nhập
        private void LockNow()
        {
            if (_isLocked) return;
            _isLocked = true;

            string username = SessionService.CurrentUser?.Username ?? "SYSTEM";
            StopAutoLock();
            try { ActivityLogger.Log(username, "AUTO_LOCK", $"Không hoạt động {AutoLockMinutes} phút"); } catch { }

            // Các cửa sổ con đang mở (Thêm/Sửa tài khoản, nhập mã 2FA...) cũng phải đóng để không lộ dữ liệu
            var others = Application.Current.Windows.Cast<Window>()
                .Where(w => w != this && w.IsVisible).ToList();

            SessionService.Logout();

            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();

            foreach (var w in others) { try { w.Close(); } catch { } }
            Close();
        }

        // ----- Tự ẩn mật khẩu -----
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
            _hidePasswordTimer.Start();   // đếm lại AutoHidePasswordSeconds giây
            RefreshAccountsKeepSelection();
        }

        private void HidePasswordNow()
        {
            _hidePasswordTimer.Stop();
            if (!_showPassword) return;
            _showPassword = false;
            RefreshAccountsKeepSelection();
        }

        // ----- Nhớ xác thực -----
        private bool IsAuthFresh() =>
            _lastAuthTime != DateTime.MinValue &&
            (DateTime.UtcNow - _lastAuthTime).TotalSeconds < AuthRememberSeconds;

        private void MarkAuthenticated() => _lastAuthTime = DateTime.UtcNow;

        // ================================================================
        // ================== TAB TÀI KHOẢN ==================
        // ================================================================

        private void RefreshAccounts()
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

            dgAccounts.ItemsSource = null;
            dgAccounts.ItemsSource = viewList;

            UpdateStorageInfo();
        }

        private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e) => RefreshAccounts();

        private void BtnRefreshAccounts_Click(object? sender, RoutedEventArgs e)
        {
            txtSearch.Clear();
            RefreshAccounts();
        }

        // ✅ Hiện MK: BẮT BUỘC xác thực Windows Hello. Ẩn lại thì không cần.
        private async void BtnToggleShow_Click(object? sender, RoutedEventArgs e)
        {
            // Đang hiện -> chỉ cần ẩn lại
            if (_showPassword)
            {
                HidePasswordNow();
                return;
            }

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false; // tránh bấm liên tục khi đang chờ xác thực

            try
            {
                string username = SessionService.CurrentUser!.Username;

                // Vừa xác thực trong AuthRememberSeconds giây qua -> không hỏi lại
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
                if (_isLocked) return; // app đã tự khóa trong lúc chờ xác thực
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

        private void BtnEditAccount_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) { MessageBox.Show("Chọn một tài khoản để sửa."); return; }

            var dlg = new EditAccountWindow(selected);
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

        // ✅ Copy Password:
        //    0. Vừa xác thực trong AuthRememberSeconds giây qua -> copy luôn.
        //    1. Windows Hello.
        //    2. Windows Hello thất bại / hủy / không hỗ trợ -> xác thực 2 bước (Authenticator).
        //    Không qua cả hai (hoặc chưa bật 2FA) -> KHÔNG copy.
        private async void BtnCopyPassword_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAccount();
            if (selected == null) return;

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false; // tránh bấm liên tục khi đang chờ xác thực

            try
            {
                string username = SessionService.CurrentUser!.Username;
                string method = "";
                bool ok = false;

                // ----- Đã xác thực gần đây -> không hỏi lại -----
                if (IsAuthFresh())
                {
                    ok = true;
                    method = $"nhớ xác thực ≤{AuthRememberSeconds}s";
                }

                // ----- Trường hợp 1: Windows Hello -----
                if (!ok)
                {
                    bool helloAvailable = false;
                    try { helloAvailable = await WindowsHelloService.IsAvailableAsync(); } catch { }

                    if (helloAvailable)
                    {
                        try { ok = await WindowsHelloService.VerifyAsync(this, "Xác nhận để copy mật khẩu"); } catch { ok = false; }
                        if (_isLocked) return; // app đã tự khóa trong lúc chờ
                        if (ok) { method = "Windows Hello"; MarkAuthenticated(); }
                    }
                }

                // ----- Trường hợp 2: Windows Hello thất bại -> xác thực 2 bước -----
                if (!ok)
                {
                    // Chưa bật 2FA thì không có cách dự phòng -> từ chối (không để lọt)
                    if (!TwoFactorService.IsEnabled(username))
                    {
                        try { ActivityLogger.Log(username, "COPY_PASSWORD_DENIED", "Windows Hello thất bại, chưa bật 2FA"); } catch { }
                        MessageBox.Show(
                            "Xác thực Windows Hello không thành công và bạn chưa bật xác thực 2 bước nên không thể copy mật khẩu.\n\n" +
                            "Vào Tiện ích → Bật xác thực 2 bước (Authenticator) để có cách xác thực dự phòng.",
                            "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    ok = TwoFactorPromptWindow.AskIfEnabled(this, username);
                    if (_isLocked) return; // app đã tự khóa và đóng cửa sổ nhập mã
                    if (ok) { method = "2FA"; MarkAuthenticated(); }
                }

                if (!ok)
                {
                    try { ActivityLogger.Log(username, "COPY_PASSWORD_DENIED", "Windows Hello và 2FA đều thất bại"); } catch { }
                    MessageBox.Show("Xác thực thất bại. Đã hủy copy mật khẩu.", "Từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Clipboard.SetText(selected.Password);
                try { ActivityLogger.Log(username, "COPY_PASSWORD", $"{selected.ServiceName} (xác thực: {method})"); } catch { }
                MessageBox.Show("Đã copy password!");
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

        // ================================================================
        // ================== TAB FILE ==================
        // ================================================================

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

        // ================================================================
        // ================== TAB PROFILE ==================
        // ================================================================

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
                var nu = SessionService.CurrentUser!;
                string dn = string.IsNullOrWhiteSpace(nu.FullName) ? nu.Username : nu.FullName;
                lblUser.Text = $"👤 {dn} ({nu.Role})";

                MessageBox.Show("Đã lưu thông tin cá nhân!");
            }
            else { MessageBox.Show(result.message, "Lỗi"); }
        }

        // ================================================================
        // ================== WINDOWS HELLO ==================
        // ================================================================

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

        // ================================================================
        // ================== XÁC THỰC 2 BƯỚC (AUTHENTICATOR) ==================
        // ================================================================

        private void Update2FAMenu()
        {
            try
            {
                string user = SessionService.CurrentUser!.Username;
                bool enabled = TwoFactorService.IsEnabled(user);

                mnu2FA.Header = enabled
                    ? "🔑 Tắt xác thực 2 bước (đang bật)"
                    : "🔑 Bật xác thực 2 bước (Authenticator)";

                // Chỉ hiện "Tạo lại mã khôi phục" khi 2FA đang bật
                if (_mnu2FARegen != null)
                    _mnu2FARegen.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        // Thêm mục menu ngay dưới "Bật/Tắt xác thực 2 bước"
        private void Setup2FARegenMenu()
        {
            try
            {
                if (_mnu2FARegen != null) return;
                if (mnu2FA.Parent is not ItemsControl parent) return;

                _mnu2FARegen = new MenuItem { Header = "🔁 Tạo lại mã khôi phục 2FA" };
                _mnu2FARegen.Click += Mnu2FARegen_Click;
                parent.Items.Insert(parent.Items.IndexOf(mnu2FA) + 1, _mnu2FARegen);
            }
            catch { }
        }

        // ✅ Tạo lại 8 mã khôi phục: phải nhập đúng mã 6 số Authenticator HIỆN TẠI.
        //    Mã cũ chỉ bị vô hiệu khi người dùng bấm "Tôi đã lưu, hoàn tất".
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
                    // ===== BẬT: quét QR -> nhập mã -> lưu mã khôi phục =====
                    var setup = new TwoFactorSetupWindow(username) { Owner = this };
                    setup.ShowDialog();

                    if (TwoFactorService.IsEnabled(username))
                    {
                        try { ActivityLogger.Log(username, "ENABLE_2FA", ""); } catch { }
                    }
                }
                else
                {
                    // ===== TẮT: bắt buộc nhập đúng mã hiện tại (hoặc mã khôi phục) =====
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

        // ================================================================
        // ================== CSV ==================
        // ================================================================

        // ✅ Xuất CSV: BẮT BUỘC xác thực Windows Hello trước (chỉ áp dụng cho chức năng này)
        private async void MnuExportCsv_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string username = SessionService.CurrentUser!.Username;

                // ===== BƯỚC 1: XÁC THỰC WINDOWS HELLO =====
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

                // ===== BƯỚC 2: XUẤT CSV =====
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

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("ServiceName,Username,Password,Note,UpdatedAt");
                foreach (var acc in accounts)
                {
                    sb.AppendLine($"{EscapeCsv(acc.ServiceName)},{EscapeCsv(acc.Username)},{EscapeCsv(acc.Password)},{EscapeCsv(acc.Note)},{acc.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);

                ActivityLogger.Log(username, "EXPORT_CSV", $"Xuất {accounts.Count} tài khoản ra CSV");
                MessageBox.Show($"Đã xuất {accounts.Count} tài khoản ra file CSV thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
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
                var existingAccounts = _vaultService.Load(username);

                var lines = File.ReadAllLines(dlg.FileName, System.Text.Encoding.UTF8);
                if (lines.Length <= 1) { MessageBox.Show("File CSV trống hoặc không đúng định dạng.", "Lỗi"); return; }

                int importedCount = 0;
                int skippedCount = 0;

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = ParseCsvLine(line);
                    if (parts.Count < 3) continue;

                    string serviceName = parts[0].Trim();
                    string usernameCsv = parts[1].Trim();
                    string password = parts[2].Trim();
                    string note = parts.Count > 3 ? parts[3].Trim() : "";

                    bool isDuplicate = existingAccounts.Any(a =>
                        string.Equals(a.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(a.Username, usernameCsv, StringComparison.OrdinalIgnoreCase));

                    if (isDuplicate) { skippedCount++; continue; }

                    existingAccounts.Add(new SavedAccount { ServiceName = serviceName, Username = usernameCsv, Password = password, Note = note, UpdatedAt = DateTime.Now });
                    importedCount++;
                }

                if (importedCount > 0)
                {
                    _vaultService.Save(username, existingAccounts);
                    RefreshAccounts();
                    UpdateStorageInfo();
                    ActivityLogger.Log(username, "IMPORT_CSV", $"Nhập {importedCount} tài khoản từ CSV");
                }

                string msg = $"Đã nhập thành công {importedCount} tài khoản.";
                if (skippedCount > 0) msg += $"\nBỏ qua {skippedCount} tài khoản trùng lặp.";
                MessageBox.Show(msg, "Kết quả nhập CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi nhập CSV: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n")) return $"\"{field.Replace("\"", "\"\"")}\"";
            return field;
        }

        private System.Collections.Generic.List<string> ParseCsvLine(string line)
        {
            var result = new System.Collections.Generic.List<string>();
            bool inQuotes = false;
            var currentField = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { currentField.Append('"'); i++; }
                    else { inQuotes = !inQuotes; }
                }
                else if (c == ',' && !inQuotes) { result.Add(currentField.ToString()); currentField.Clear(); }
                else { currentField.Append(c); }
            }
            result.Add(currentField.ToString());
            return result;
        }

        // ================================================================
        // ================== MENU ==================
        // ================================================================

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

        // ================================================================
        // ================== THEME ==================
        // ================================================================

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

        // ================================================================
        // ================== AUTO THEME ==================
        // ================================================================

        private void MnuAutoTheme_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                bool isEnabled = mnuAutoTheme.IsChecked;
                if (isEnabled)
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
                UpdateThemeIndicator();
            }
            catch (Exception ex) { MessageBox.Show($"Lỗi: {ex.Message}"); }
        }

        // ================================================================
        // ================== EXPORT / IMPORT VAULT ==================
        // ================================================================

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

        // ================================================================
        // ================== GOOGLE DRIVE SYNC ==================
        // ================================================================

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

        // Khôi phục = tải bản sao lưu (file .vault) từ Drive về file tạm rồi nhập bằng đúng hàm
        // "Nhập vault" có sẵn: tài khoản trùng bị bỏ qua, dữ liệu hiện tại không bị xóa.
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

        // ================================================================
        // ================== CỤM MẬT KHẨU SAO LƯU ==================
        // ================================================================

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

        // Mở cửa sổ đặt cụm, lưu lại trên máy (DPAPI). Trả về false nếu người dùng hủy.
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

        // Chuẩn bị cụm để nhập một file sao lưu.
        // Trả về false nếu người dùng hủy hoặc nhập sai 3 lần.
        // passphrase = null nghĩa là không cần nhập: file kiểu cũ, hoặc cụm đã lưu trên máy này mở được.
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

        // Máy chưa có cụm mật khẩu: sau khi nhập thành công bằng một cụm, gợi ý dùng luôn cụm đó cho máy này.
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

        // Bản sao lưu ghi tên người dùng khác: hỏi xác nhận (mặc định Không) trước khi nhập vào vault hiện tại.
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

        // Trước khi tách theo người dùng, máy chỉ có một cụm mật khẩu sao lưu dùng chung.
        // Hỏi người dùng hiện tại có nhận cụm đó không (chỉ người đầu tiên nhận được, sau đó file cũ bị xóa).
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
                    "Không đọc được cụm cũ trên máy này. Hãy đặt cụm mới (Tiện ích → Cụm mật khẩu sao lưu).",
                    "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ================================================================
        // ================== WINDOW POSITION ==================
        // ================================================================

        private void MnuCenterWindow_Click(object? sender, RoutedEventArgs e)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;

            RegistryService.ClearWindowPosition();
            SaveWindowState();

            MessageBox.Show("Đã đưa cửa sổ về giữa màn hình.");
        }

        // ================================================================
        // ================== RESET ==================
        // ================================================================

        private void MnuResetSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Xóa toàn bộ cài đặt (theme, kích thước, vị trí, user cuối)?\n\nKhởi động lại app để áp dụng toàn bộ.", "Xác nhận Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            RegistryService.Clear();
            ThemeService.ApplyLight();

            MessageBox.Show("Đã reset cài đặt.\n\nKhởi động lại app để áp dụng toàn bộ.");
        }

        // ================================================================
        // ================== STATUS BAR ==================
        // ================================================================

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