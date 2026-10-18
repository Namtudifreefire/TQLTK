using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using PersonalInfoManager.Models;

namespace PersonalInfoManager
{
    public partial class EditAccountWindow : Window
    {
        private readonly SavedAccount? _existing;
        private bool _showPwd = false;

        /// <summary>
        /// Kết quả sau khi user bấm Lưu.
        /// </summary>
        public SavedAccount Result { get; private set; } = new();

        public EditAccountWindow(SavedAccount? existing)
        {
            InitializeComponent();
            _existing = existing;

            if (existing != null)
            {
                lblTitle.Text = "✏️ Sửa tài khoản";
                txtService.Text = existing.ServiceName;
                txtUsername.Text = existing.Username;
                txtPassword.Password = existing.Password;
                txtPasswordVisible.Text = existing.Password;
                txtNote.Text = existing.Note;
            }

            Loaded += (_, _) => txtService.Focus();
        }

        // ================== TOGGLE HIỆN MẬT KHẨU ==================

        private void BtnTogglePwd_Click(object sender, RoutedEventArgs e)
        {
            _showPwd = !_showPwd;
            if (_showPwd)
            {
                txtPasswordVisible.Text = txtPassword.Password;
                txtPassword.Visibility = Visibility.Collapsed;
                txtPasswordVisible.Visibility = Visibility.Visible;
                btnTogglePwd.Content = "🙈";
            }
            else
            {
                txtPassword.Password = txtPasswordVisible.Text;
                txtPassword.Visibility = Visibility.Visible;
                txtPasswordVisible.Visibility = Visibility.Collapsed;
                btnTogglePwd.Content = "👁️";
            }
        }

        // ================== SINH MẬT KHẨU MẠNH ==================

        private void BtnGeneratePwd_Click(object sender, RoutedEventArgs e)
        {
            string pwd = GenerateStrongPassword(16);

            if (_showPwd)
                txtPasswordVisible.Text = pwd;
            else
                txtPassword.Password = pwd;

            lblPwdStrength.Text = "Đã tạo mật khẩu mạnh 16 ký tự ✓";
            lblPwdStrength.Foreground = Brushes.Green;
        }

        private static string GenerateStrongPassword(int length)
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnpqrstuvwxyz";
            const string digits = "23456789";
            const string special = "!@#$%^&*_-+=";

            string all = upper + lower + digits + special;
            var rnd = new Random();
            var sb = new StringBuilder();

            // Đảm bảo có ít nhất 1 ký tự mỗi loại
            sb.Append(upper[rnd.Next(upper.Length)]);
            sb.Append(lower[rnd.Next(lower.Length)]);
            sb.Append(digits[rnd.Next(digits.Length)]);
            sb.Append(special[rnd.Next(special.Length)]);

            for (int i = 4; i < length; i++)
                sb.Append(all[rnd.Next(all.Length)]);

            // Trộn ngẫu nhiên
            return new string(sb.ToString().OrderBy(_ => rnd.Next()).ToArray());
        }

        // ================== LƯU ==================

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string service = txtService.Text.Trim();
            string username = txtUsername.Text.Trim();
            string password = _showPwd
                ? txtPasswordVisible.Text
                : txtPassword.Password;
            string note = txtNote.Text.Trim();

            if (string.IsNullOrWhiteSpace(service))
            {
                MessageBox.Show("Vui lòng nhập tên dịch vụ.");
                txtService.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Vui lòng nhập username.");
                txtUsername.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Vui lòng nhập password.");
                return;
            }

            if (_existing != null)
            {
                // Sửa
                Result = new SavedAccount
                {
                    Id = _existing.Id,
                    ServiceName = service,
                    Username = username,
                    Password = password,
                    Note = note,
                    CreatedAt = _existing.CreatedAt,
                    UpdatedAt = DateTime.Now
                };
            }
            else
            {
                // Thêm mới
                Result = new SavedAccount
                {
                    ServiceName = service,
                    Username = username,
                    Password = password,
                    Note = note
                };
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}