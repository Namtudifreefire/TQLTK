using System;
using System.Linq;
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
                $"Nhập mật khẩu mới cho '{u.Username}' (>= 6 ký tự):\n" +
                "(Người dùng sẽ phải đổi lại mật khẩu này khi đăng nhập lần tới.)",
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

            // Tính trạng thái mới TRƯỚC khi đổi (bản cũ ghi log ngược: khóa lại ghi thành mở)
            bool newActive = !u.IsActive;
            _userService.SetActive(u.Id, newActive);

            try
            {
                ActivityLogger.Log(SessionService.CurrentUser!.Username,
                    newActive ? "ADMIN_UNLOCK_USER" : "ADMIN_LOCK_USER",
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

            // Dữ liệu của user này vẫn nằm trên máy sau khi xóa dòng tài khoản:
            // hỏi luôn có xóa không, kẻo user mới tạo trùng tên sẽ thừa hưởng dữ liệu cũ.
            var leftovers = UserDataCleaner.Describe(u.Username);
            bool wipeData = false;

            if (leftovers.Count > 0)
            {
                var ask = MessageBox.Show(
                    $"Dữ liệu còn lại của '{u.Username}' trên máy này:\n\n" +
                    string.Join("\n", leftovers) + "\n\n" +
                    "Xóa vĩnh viễn luôn các dữ liệu này?\n\n" +
                    "• Có: xóa hết, không thể khôi phục.\n" +
                    $"• Không: giữ lại. Người dùng mới tạo trùng tên '{u.Username}' sẽ thấy lại dữ liệu này.\n" +
                    "• Hủy: không xóa gì cả.\n\n" +
                    "Bản sao lưu trên Google Drive (nếu có) không bị ảnh hưởng.",
                    "Xóa dữ liệu của người dùng",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (ask == MessageBoxResult.Cancel) return;
                wipeData = ask == MessageBoxResult.Yes;
            }

            var result = _userService.DeleteUser(u.Id, SessionService.CurrentUser!.Id);

            if (!result.ok)
            {
                MessageBox.Show(result.message, "Lỗi");
                return;
            }

            string message = result.message;

            if (wipeData)
            {
                var (removed, errors) = UserDataCleaner.Delete(u.Username);

                if (removed.Count > 0)
                    message += "\n\nĐã xóa dữ liệu:\n" + string.Join("\n", removed.Select(r => "• " + r));

                if (errors.Count > 0)
                    message += "\n\nKhông xóa được:\n" + string.Join("\n", errors.Select(r => "• " + r));

                try
                {
                    ActivityLogger.Log(SessionService.CurrentUser!.Username,
                        "ADMIN_DELETE_USER_DATA",
                        $"{u.Username}: xóa {removed.Count} mục, lỗi {errors.Count}");
                }
                catch { }
            }

            try
            {
                ActivityLogger.Log(SessionService.CurrentUser!.Username,
                    "ADMIN_DELETE_USER",
                    wipeData ? $"{u.Username} (kèm dữ liệu)" : $"{u.Username} (giữ dữ liệu)");
            }
            catch { }

            MessageBox.Show(message, "OK");
            Refresh();
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