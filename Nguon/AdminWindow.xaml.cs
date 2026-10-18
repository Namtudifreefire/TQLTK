using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class AdminWindow : Window
    {
        private readonly UserService _userService = new();

        public AdminWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => Refresh();
        }

        private void Refresh()
        {
            dgUsers.ItemsSource = null;
            dgUsers.ItemsSource = _userService.GetAll();
        }

        // ============ TẠO USER MỚI ============

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            var role = cbRole.SelectedIndex == 1 ? UserRole.Admin : UserRole.User;

            var result = _userService.CreateUserFull(
                txtNewUser.Text,
                txtNewPwd.Password,
                role,
                txtNewFullName.Text,
                txtNewEmail.Text,
                txtNewPhone.Text);

            MessageBox.Show(result.message,
                result.ok ? "Thành công" : "Lỗi",
                MessageBoxButton.OK,
                result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

            if (result.ok)
            {
                try
                {
                    ActivityLogger.Log(SessionService.CurrentUser!.Username,
                        "ADMIN_CREATE_USER",
                        $"Tạo '{txtNewUser.Text.Trim()}' - {txtNewFullName.Text}");
                }
                catch { }

                // Clear form
                txtNewUser.Clear();
                txtNewPwd.Clear();
                txtNewFullName.Clear();
                txtNewEmail.Clear();
                txtNewPhone.Clear();
                cbRole.SelectedIndex = 0;
                Refresh();
            }
        }

        // ============ SỬA THÔNG TIN USER ============

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is not User u)
            {
                MessageBox.Show("Chọn một tài khoản để sửa.");
                return;
            }

            // Mở dialog sửa
            var dlg = new EditUserWindow(u, _userService);
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    ActivityLogger.Log(SessionService.CurrentUser!.Username,
                        "ADMIN_EDIT_USER", $"Sửa '{u.Username}'");
                }
                catch { }
                Refresh();
            }
        }

        // ============ RESET MẬT KHẨU ============

        private void BtnResetPwd_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is not User u)
            {
                MessageBox.Show("Chọn một tài khoản.");
                return;
            }

            var input = Microsoft.VisualBasic.Interaction.InputBox(
                $"Nhập mật khẩu mới cho '{u.Username}' (>= 6 ký tự):",
                "Reset mật khẩu", "");

            if (string.IsNullOrWhiteSpace(input)) return;

            var result = _userService.ResetPassword(u.Id, input);
            MessageBox.Show(result.message,
                result.ok ? "OK" : "Lỗi",
                MessageBoxButton.OK,
                result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

            if (result.ok)
            {
                try
                {
                    ActivityLogger.Log(SessionService.CurrentUser!.Username,
                        "ADMIN_RESET_PWD", $"Reset mật khẩu '{u.Username}'");
                }
                catch { }
            }
        }

        // ============ KHÓA / MỞ ============

        private void BtnToggleActive_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is not User u)
            {
                MessageBox.Show("Chọn một tài khoản.");
                return;
            }

            if (u.Id == SessionService.CurrentUser!.Id)
            {
                MessageBox.Show("Không thể khóa chính mình.");
                return;
            }

            _userService.SetActive(u.Id, !u.IsActive);
            try
            {
                ActivityLogger.Log(SessionService.CurrentUser!.Username,
                    u.IsActive ? "ADMIN_LOCK_USER" : "ADMIN_UNLOCK_USER",
                    u.Username);
            }
            catch { }
            Refresh();
        }

        // ============ CẤP / THU ADMIN ============

        private void BtnToggleRole_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is not User u)
            {
                MessageBox.Show("Chọn một tài khoản.");
                return;
            }

            var oldRole = u.Role;
            var newRole = oldRole == UserRole.Admin ? UserRole.User : UserRole.Admin;
            _userService.SetRole(u.Id, newRole);

            try
            {
                ActivityLogger.Log(SessionService.CurrentUser!.Username,
                    "ADMIN_CHANGE_ROLE", $"{u.Username}: {oldRole} → {newRole}");
            }
            catch { }
            Refresh();
        }

        // ============ XÓA ============

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is not User u)
            {
                MessageBox.Show("Chọn một tài khoản.");
                return;
            }

            if (MessageBox.Show(
                    $"Xóa tài khoản '{u.Username}'?\nKhông thể hoàn tác!",
                    "Xác nhận", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var result = _userService.DeleteUser(u.Id, SessionService.CurrentUser!.Id);
            MessageBox.Show(result.message,
                result.ok ? "OK" : "Lỗi");

            if (result.ok)
            {
                try
                {
                    ActivityLogger.Log(SessionService.CurrentUser!.Username,
                        "ADMIN_DELETE_USER", u.Username);
                }
                catch { }
                Refresh();
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Refresh();

        private void BtnViewLog_Click(object sender, RoutedEventArgs e)
        {
            string log = ActivityLogger.ReadTodayLog();

            var dlg = new Window
            {
                Title = "Log hôm nay",
                Width = 900,
                Height = 550,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
            };

            var txt = new TextBox
            {
                Text = log,
                IsReadOnly = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                Foreground = Brushes.LightGreen,
                Padding = new Thickness(10)
            };

            dlg.Content = txt;
            dlg.ShowDialog();
        }
    }
}