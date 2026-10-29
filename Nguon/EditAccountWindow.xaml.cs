using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using PersonalInfoManager.Models;

namespace PersonalInfoManager
{
    public partial class EditAccountWindow : Window
    {
        private readonly SavedAccount? _existing;
        private bool _showPwd = false;

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
            var chars = new List<char>(length);

            chars.Add(Pick(upper));
            chars.Add(Pick(lower));
            chars.Add(Pick(digits));
            chars.Add(Pick(special));

            for (int i = 4; i < length; i++)
                chars.Add(Pick(all));

            for (int i = chars.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars.ToArray());
        }

        private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];

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