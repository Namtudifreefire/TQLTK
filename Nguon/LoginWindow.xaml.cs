using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class LoginWindow : Window
    {
        private readonly UserService _userService = new();

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += LoginWindow_Loaded;
        }

        private void LoginWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                string lastUser = RegistryService.GetLastUser();
                if (!string.IsNullOrWhiteSpace(lastUser))
                {
                    txtUsername.Text = lastUser;
                    txtPassword.Focus();
                }
                else txtUsername.Focus();
            }
            catch { txtUsername.Focus(); }
        }

        // ================== ĐĂNG NHẬP THƯỜNG ==================

        private void BtnLogin_Click(object? sender, RoutedEventArgs e) => DoLogin();

        private void TxtUsername_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { txtPassword.Focus(); e.Handled = true; }
        }

        private void TxtPassword_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { DoLogin(); e.Handled = true; }
        }

        private void DoLogin()
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

            lblStatus.Text = "";

            if (string.IsNullOrWhiteSpace(username))
            { ShowError("Vui lòng nhập tên đăng nhập."); txtUsername.Focus(); return; }

            if (string.IsNullOrWhiteSpace(password))
            { ShowError("Vui lòng nhập mật khẩu."); txtPassword.Focus(); return; }

            var result = _userService.Authenticate(username, password);

            if (!result.ok)
            {
                ShowError(result.message);
                txtPassword.Clear();
                txtPassword.Focus();
                try { ActivityLogger.Log(username, "LOGIN_FAILED", result.message); } catch { }
                return;
            }

            // ✅ Đăng nhập bằng MẬT KHẨU → có hỏi 2FA (nếu user đã bật)
            CompleteLogin(result.user!, requireTwoFactor: true);
        }

        // ================== WINDOWS HELLO ==================

        private async void BtnWindowsHello_Click(object? sender, RoutedEventArgs e)
        {
            lblStatus.Text = "";

            // 1. Kiểm tra máy có hỗ trợ Windows Hello không
            bool available = await WindowsHelloService.IsAvailableAsync();
            if (!available)
            {
                ShowError(
                    "Máy tính không hỗ trợ Windows Hello.\n\n" +
                    "Vui lòng thiết lập vân tay / khuôn mặt / PIN trong Windows Settings.");
                return;
            }

            // 2. Lấy SID
            string sid = WindowsHelloService.GetCurrentWindowsSid();
            if (string.IsNullOrWhiteSpace(sid))
            {
                ShowError("Không lấy được thông tin tài khoản Windows.");
                return;
            }

            // 3. Tìm user TQLTK khớp SID
            var user = _userService.FindByWindowsHelloSid(sid);
            if (user == null)
            {
                // ✅ Chưa bật → hướng dẫn
                MessageBox.Show(
                    "Tài khoản này chưa bật đăng nhập bằng Windows Hello.\n\n" +
                    "👉 Vui lòng đăng nhập bằng mật khẩu, sau đó vào:\n" +
                    "   Tab '👤 Thông tin cá nhân' → Bật 'Đăng nhập bằng Windows Hello'.",
                    "Chưa bật Windows Hello",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // 4. Gọi xác thực Windows Hello
            bool verified = await WindowsHelloService.VerifyAsync(
                this,
                $"Đăng nhập TQLTK với tài khoản '{user.Username}'");

            if (!verified)
            {
                ShowError("Xác thực Windows Hello thất bại.");
                return;
            }

            // 5. Đăng nhập thành công
            try
            {
                ActivityLogger.Log(user.Username, "LOGIN_WINDOWS_HELLO",
                    "Đăng nhập bằng Windows Hello");
            }
            catch { }

            // ✅ Đăng nhập bằng WINDOWS HELLO → BỎ QUA 2FA
            CompleteLogin(user, requireTwoFactor: false);
        }

        // ================== HOÀN TẤT ĐĂNG NHẬP ==================

        private void CompleteLogin(User user, bool requireTwoFactor)
        {
            // Chỉ hỏi mã Authenticator khi đăng nhập bằng mật khẩu và user đã bật 2FA.
            // Sai / hủy thì dừng đăng nhập, quay lại màn hình này.
            if (requireTwoFactor)
            {
                if (!TwoFactorPromptWindow.AskIfEnabled(this, user.Username))
                {
                    try { ActivityLogger.Log(user.Username, "LOGIN_2FA_FAILED", "Sai mã hoặc hủy xác thực 2 bước"); } catch { }
                    ShowError("Xác thực 2 bước thất bại hoặc đã bị hủy.");
                    txtPassword.Clear();
                    txtPassword.Focus();
                    return;
                }
            }

            SessionService.Login(user);

            try { RegistryService.SetLastUser(user.Username); } catch { }

            try
            {
                ActivityLogger.Log(user.Username, "LOGIN", $"Role={user.Role}");
            }
            catch { }

            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            Hide();
        }

        // ================== ĐĂNG KÝ ==================

        private void BtnRegister_Click(object? sender, RoutedEventArgs e)
        {
            var reg = new RegisterWindow { Owner = this };
            bool? result = reg.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(reg.RegisteredUsername))
            {
                txtUsername.Text = reg.RegisteredUsername;
                txtPassword.Clear();
                txtPassword.Focus();

                lblStatus.Foreground = new SolidColorBrush(
                    Color.FromRgb(34, 197, 94));
                lblStatus.Text = "✓ Đăng ký thành công! Vui lòng đăng nhập.";
            }
        }

        // ================== HELPER ==================

        private void ShowError(string message)
        {
            lblStatus.Foreground = new SolidColorBrush(
                Color.FromRgb(239, 68, 68));
            lblStatus.Text = message;
        }
    }
}