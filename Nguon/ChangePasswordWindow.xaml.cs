using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class ChangePasswordWindow : Window
    {
        private readonly UserService _userService = new();

        public ChangePasswordWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => txtOldPwd.Focus();
        }

        // ================== ĐÁNH GIÁ ĐỘ MẠNH ==================

        private void TxtNewPwd_PasswordChanged(object sender, RoutedEventArgs e)
        {
            string pwd = txtNewPwd.Password;

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

        private void TxtConfirmPwd_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtConfirmPwd.Password))
            {
                lblConfirm.Text = "";
                return;
            }

            if (txtConfirmPwd.Password == txtNewPwd.Password)
            {
                lblConfirm.Text = "✓ Mật khẩu khớp";
                lblConfirm.Foreground = Brushes.Green;
            }
            else
            {
                lblConfirm.Text = "✗ Mật khẩu không khớp";
                lblConfirm.Foreground = Brushes.Red;
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

        // ================== XÁC NHẬN ĐỔI ==================

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string oldPwd = txtOldPwd.Password;
            string newPwd = txtNewPwd.Password;
            string confirm = txtConfirmPwd.Password;

            lblStatus.Text = "";

            // Validate
            if (string.IsNullOrWhiteSpace(oldPwd) ||
                string.IsNullOrWhiteSpace(newPwd))
            {
                lblStatus.Text = "Vui lòng nhập đầy đủ thông tin.";
                return;
            }

            if (newPwd != confirm)
            {
                lblStatus.Text = "Mật khẩu xác nhận không khớp.";
                return;
            }

            if (newPwd == oldPwd)
            {
                lblStatus.Text = "Mật khẩu mới phải khác mật khẩu cũ.";
                return;
            }

            if (newPwd.Length < 6)
            {
                lblStatus.Text = "Mật khẩu mới phải từ 6 ký tự trở lên.";
                return;
            }

            // Gọi service
            string username = SessionService.CurrentUser!.Username;
            var result = _userService.ChangePassword(username, oldPwd, newPwd);

            if (result.ok)
            {
                try
                {
                    ActivityLogger.Log(username, "CHANGE_PASSWORD",
                        "Đổi mật khẩu thành công");
                }
                catch { }

                MessageBox.Show(result.message, "Thành công",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            else
            {
                lblStatus.Text = result.message;

                if (result.message.Contains("cũ không đúng"))
                {
                    txtOldPwd.Clear();
                    txtOldPwd.Focus();
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}