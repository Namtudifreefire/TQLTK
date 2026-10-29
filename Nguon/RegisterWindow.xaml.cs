using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class RegisterWindow : Window
    {
        private readonly UserService _userService = new();
        private readonly bool _isInitialAdmin;




        public string RegisteredUsername { get; private set; } = "";

        public RegisterWindow() : this(false) { }

        public RegisterWindow(bool isInitialAdmin)
        {
            InitializeComponent();
            _isInitialAdmin = isInitialAdmin;

            if (_isInitialAdmin)
            {
                Title = "Tạo quản trị viên đầu tiên - TQLTK";
                lblFormTitle.Text = "🛡 Tạo quản trị viên đầu tiên";
                lblFormDescription.Text = "Hãy chọn mật khẩu riêng, mạnh để bảo vệ tài khoản quản trị.";
                lblRegisterButton.Text = "✓ Tạo quản trị viên";
            }

            Loaded += (_, _) => txtUsername.Focus();
        }



        private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            string pwd = txtPassword.Password;
            if (string.IsNullOrEmpty(pwd))
            {
                lblStrength.Text = "";
                return;
            }

            int score = CalculateStrength(pwd);
            (string text, Brush color) = score switch
            {
                <= 2 => ("Yếu", Brushes.Red),
                3 => ("Trung bình", Brushes.Orange),
                4 => ("Mạnh", Brushes.Green),
                _ => ("Rất mạnh", Brushes.DarkGreen)
            };

            lblStrength.Text = $"Độ mạnh: {text}";
            lblStrength.Foreground = color;
        }

        private void TxtConfirm_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtConfirm.Password))
            {
                lblConfirmStatus.Text = "";
                return;
            }

            if (txtConfirm.Password == txtPassword.Password)
            {
                lblConfirmStatus.Text = "✓ Mật khẩu khớp";
                lblConfirmStatus.Foreground = Brushes.Green;
            }
            else
            {
                lblConfirmStatus.Text = "✗ Mật khẩu không khớp";
                lblConfirmStatus.Foreground = Brushes.Red;
            }
        }

        private static int CalculateStrength(string pwd)
        {
            int score = 0;
            if (pwd.Length >= 8) score++;
            if (pwd.Length >= 12) score++;
            if (pwd.Any(char.IsUpper) && pwd.Any(char.IsLower)) score++;
            if (pwd.Any(char.IsDigit)) score++;
            if (pwd.Any(c => !char.IsLetterOrDigit(c))) score++;
            return score;
        }



        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;
            string confirm = txtConfirm.Password;
            string fullName = txtFullName.Text.Trim();
            string email = txtEmail.Text.Trim();
            string phone = txtPhone.Text.Trim();



            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            {
                ShowError("Tên đăng nhập phải từ 3 ký tự trở lên.");
                txtUsername.Focus();
                return;
            }


            if (username.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
            {
                ShowError("Tên đăng nhập chỉ được chứa chữ, số và dấu gạch dưới.");
                txtUsername.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 15)
            {
                ShowError("Tài khoản mới chưa bật 2FA nên mật khẩu phải từ 15 ký tự trở lên.");
                txtPassword.Focus();
                return;
            }

            if (password != confirm)
            {
                ShowError("Mật khẩu xác nhận không khớp.");
                txtConfirm.Clear();
                txtConfirm.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                ShowError("Vui lòng nhập họ tên.");
                txtFullName.Focus();
                return;
            }


            var result = _isInitialAdmin
                ? _userService.CreateInitialAdmin(username, password, fullName, email, phone)
                : _userService.CreateUserFull(
                    username,
                    password,
                    UserRole.User,
                    fullName,
                    email,
                    phone);

            if (!result.ok)
            {
                ShowError(result.message);
                return;
            }


            try
            {
                ActivityLogger.Log(username, _isInitialAdmin ? "INITIAL_ADMIN_CREATED" : "REGISTER",
                    $"Họ tên: {fullName}, Email: {email}");
            }
            catch { }

            RegisteredUsername = username;

            MessageBox.Show(
                $"Đăng ký thành công!\n\n" +
                $"Tên đăng nhập: {username}\n" +
                $"Họ tên: {fullName}\n\n" +
                $"Vai trò: {(_isInitialAdmin ? "Quản trị viên" : "Người dùng")}\n\n" +
                $"Vui lòng đăng nhập để sử dụng.",
                "Thành công",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }



        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }



        private void ShowError(string message)
        {
            MessageBox.Show(message, "Lỗi",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
