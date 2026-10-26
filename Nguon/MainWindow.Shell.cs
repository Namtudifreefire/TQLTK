using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    /// <summary>
    /// Phần "khung" của MainWindow: sidebar chỉ có Tài khoản / Tài liệu, ô người dùng + menu ở chân sidebar,
    /// và hộp thoại Cài đặt (lớp phủ trong cửa sổ) chứa mọi mục còn lại.
    /// Các hàm xử lý nút trong từng trang cài đặt (Mnu..._Click, Btn..._Click) vẫn nằm ở MainWindow.xaml.cs.
    /// </summary>
    public partial class MainWindow
    {
        // ================================================================
        // ================== ĐIỀU HƯỚNG (sidebar chính + Cài đặt) ==================
        // ================================================================

        // Tag của RadioButton -> trang tương ứng. Có hai nhóm trang:
        //  - trang chính (accounts, files): hiện ở vùng nội dung bên phải sidebar
        //  - trang cài đặt (SettingsTags): hiện trong hộp thoại Cài đặt
        private Dictionary<string, FrameworkElement> _pages = new();

        private static readonly HashSet<string> SettingsTags = new()
        {
            "profile", "security", "appearance", "backup", "drive", "system", "admin"
        };

        private string? _currentSettingsTag;

        private void BuildPages()
        {
            _pages = new Dictionary<string, FrameworkElement>
            {
                ["accounts"] = pageAccounts,
                ["files"] = pageFiles,
                ["profile"] = pageProfile,
                ["security"] = pageSecurity,
                ["appearance"] = pageAppearance,
                ["backup"] = pageBackup,
                ["drive"] = pageDrive,
                ["system"] = pageSystem,
                ["admin"] = pageAdmin
            };
        }

        // Dùng chung cho cả sidebar chính lẫn danh sách trong Cài đặt (đều gắn Checked="Nav_Checked")
        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (_pages.Count == 0) return;   // sự kiện bắn trước khi cửa sổ nạp xong
            if (sender is RadioButton { Tag: string tag }) ShowPage(tag);
        }

        private void ShowPage(string tag)
        {
            // Trang Admin chỉ dành cho quản trị viên, kể cả khi bị gọi bằng cách khác
            if (tag == "admin" && !SessionService.IsAdmin) return;

            bool isSettings = SettingsTags.Contains(tag);

            foreach (var kv in _pages)
            {
                // Chỉ đổi trang trong cùng nhóm: chọn mục Cài đặt không làm mất trang chính phía sau
                if (SettingsTags.Contains(kv.Key) != isSettings) continue;
                kv.Value.Visibility = kv.Key == tag ? Visibility.Visible : Visibility.Collapsed;
            }

            if (isSettings) _currentSettingsTag = tag;

            if (tag == "files") RefreshFiles();   // danh sách file có thể đã đổi (ví dụ vừa khôi phục)
        }

        // ================================================================
        // ================== Ô NGƯỜI DÙNG + MENU ==================
        // ================================================================

        private DateTime _userMenuClosedAt = DateTime.MinValue;

        /// <summary>Cập nhật tên, vai trò và chữ cái đầu ở ô người dùng (gọi khi vào app và sau khi sửa hồ sơ).</summary>
        private void UpdateUserChip()
        {
            var u = SessionService.CurrentUser;
            if (u == null) return;

            string name = string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName.Trim();

            lblUserName.Text = name;
            lblUserRole.Text = u.Role == UserRole.Admin ? "Quản trị viên" : "Người dùng";
            lblAvatar.Text = name.Length > 0
                ? new StringInfo(name).SubstringByTextElements(0, 1).ToUpperInvariant()
                : "?";
        }

        private void BtnUser_Click(object sender, RoutedEventArgs e)
        {
            // Popup StaysOpen=False tự đóng ngay khi bấm xuống ô này lúc đang mở;
            // nếu không bỏ qua cú click đi kèm thì menu sẽ đóng rồi mở lại ngay.
            if ((DateTime.UtcNow - _userMenuClosedAt).TotalMilliseconds < 250) return;

            var u = SessionService.CurrentUser;
            lblMenuEmail.Text = u == null
                ? ""
                : (string.IsNullOrWhiteSpace(u.Email) ? u.Username : u.Email);

            popUserMenu.IsOpen = true;
        }

        private void PopUserMenu_Closed(object? sender, EventArgs e) => _userMenuClosedAt = DateTime.UtcNow;

        private void UserMenu_Click(object sender, RoutedEventArgs e)
        {
            popUserMenu.IsOpen = false;

            switch ((sender as FrameworkElement)?.Tag as string)
            {
                case "settings": OpenSettings(); break;
                case "logout": MnuLogout_Click(sender, e); break;
                case "exit": MnuExit_Click(sender, e); break;
            }
        }

        // ================================================================
        // ================== PHÍM TẮT ==================
        // ================================================================

        // Ctrl+Shift+, mở Cài đặt; Esc đóng menu / Cài đặt. (Ctrl+B ẩn/hiện sidebar vẫn ở hàm khởi tạo.)
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (popUserMenu.IsOpen) { popUserMenu.IsOpen = false; e.Handled = true; return; }
                if (_settingsOpen) { CloseSettings(); e.Handled = true; return; }
            }
            else if (e.Key == Key.OemComma &&
                     Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                OpenSettings();
                e.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        // ================================================================
        // ================== HỘP THOẠI CÀI ĐẶT ==================
        // ================================================================

        private bool _settingsOpen;
        private Dictionary<string, string>? _settingsIndex;   // tag trang -> toàn bộ chữ trong trang (đã bỏ dấu, chữ thường)

        /// <param name="tag">Trang cần mở: profile, security, appearance, backup, drive, system, admin.</param>
        private void OpenSettings(string tag = "profile")
        {
            if (_isLocked || !SessionService.IsLoggedIn || _pages.Count == 0) return;

            if (_settingsOpen)
            {
                SelectSettingsPage(tag);
                return;
            }

            if (tag == "admin" && !SessionService.IsAdmin) tag = "profile";

            _settingsOpen = true;
            _settingsIndex = null;           // dựng lại khi tìm kiếm (một số chữ đổi theo trạng thái, ví dụ "Bật/Tắt 2FA")

            LoadProfile();                   // bỏ các chỉnh sửa hồ sơ chưa lưu của lần mở trước
            txtSettingsSearch.Clear();
            lblSettingsSearchHint.Visibility = Visibility.Visible;
            ApplySettingsFilter();           // hiện đúng các mục (Quản trị chỉ hiện với Admin)

            SelectSettingsPage(tag);

            shellMain.IsEnabled = false;     // chặn Tab / bấm xuyên xuống giao diện phía sau
            settingsOverlay.Visibility = Visibility.Visible;
            settingsOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }

        private void CloseSettings()
        {
            if (!_settingsOpen) return;
            _settingsOpen = false;

            shellMain.IsEnabled = true;

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) =>
            {
                // Mở lại ngay trong lúc đang mờ dần thì giữ nguyên lớp phủ
                if (_settingsOpen) return;

                settingsOverlay.BeginAnimation(OpacityProperty, null);
                settingsOverlay.Visibility = Visibility.Collapsed;
            };
            settingsOverlay.BeginAnimation(OpacityProperty, fade);
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e) => CloseSettings();

        private void SettingsScrim_MouseDown(object sender, MouseButtonEventArgs e) => CloseSettings();

        private void SelectSettingsPage(string tag)
        {
            ShowPage(tag);
            var rb = FindSettingsNav(tag);
            if (rb != null) rb.IsChecked = true;
        }

        private RadioButton? FindSettingsNav(string tag) =>
            panelSettingsNav.Children.OfType<StackPanel>()
                .SelectMany(g => g.Children.OfType<RadioButton>())
                .FirstOrDefault(rb => rb.Tag as string == tag);

        // ================================================================
        // ================== TÌM KIẾM TRONG CÀI ĐẶT ==================
        // ================================================================

        private void TxtSettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            lblSettingsSearchHint.Visibility =
                txtSettingsSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            ApplySettingsFilter();
        }

        /// <summary>
        /// Lọc danh sách bên trái theo từ khóa: khớp với tên mục HOẶC bất kỳ chữ nào trong trang đó
        /// (tên hàng, mô tả, nhãn nút). Không phân biệt hoa thường và dấu: "mat khau" tìm ra "Mật khẩu".
        /// </summary>
        private void ApplySettingsFilter()
        {
            if (_pages.Count == 0) return;

            string q = Fold(txtSettingsSearch.Text.Trim());
            _settingsIndex ??= BuildSettingsIndex();

            string? firstMatch = null;
            int visibleCount = 0;

            foreach (var group in panelSettingsNav.Children.OfType<StackPanel>())
            {
                bool anyVisible = false;

                foreach (var rb in group.Children.OfType<RadioButton>())
                {
                    string tag = (string)rb.Tag;
                    bool allowed = tag != "admin" || SessionService.IsAdmin;

                    bool match = allowed && (q.Length == 0
                        || Fold((string)rb.Content).Contains(q)
                        || (_settingsIndex.TryGetValue(tag, out var text) && text.Contains(q)));

                    rb.Visibility = match ? Visibility.Visible : Visibility.Collapsed;

                    if (match)
                    {
                        anyVisible = true;
                        visibleCount++;
                        firstMatch ??= tag;
                    }
                }

                group.Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            lblSettingsNoResult.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Trang đang mở bị lọc mất: nhảy sang kết quả đầu tiên
            if (firstMatch != null &&
                (_currentSettingsTag == null || FindSettingsNav(_currentSettingsTag)?.Visibility != Visibility.Visible))
            {
                SelectSettingsPage(firstMatch);
            }
        }

        private Dictionary<string, string> BuildSettingsIndex()
        {
            var map = new Dictionary<string, string>();

            foreach (var kv in _pages)
            {
                if (!SettingsTags.Contains(kv.Key)) continue;

                var sb = new StringBuilder();
                CollectText(kv.Value, sb);
                map[kv.Key] = Fold(sb.ToString());
            }

            return map;
        }

        // Duyệt cây logic (có cả khi trang đang ẩn) và gom mọi chữ: TextBlock và nút có nội dung chuỗi
        private static void CollectText(DependencyObject node, StringBuilder sb)
        {
            if (node is TextBlock tb)
            {
                sb.Append(tb.Text).Append(' ');
                return;
            }

            if (node is ContentControl { Content: string s })
                sb.Append(s).Append(' ');

            foreach (object child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is DependencyObject d) CollectText(d, sb);
            }
        }

        /// <summary>Bỏ dấu tiếng Việt (kể cả đ/Đ) và đổi sang chữ thường để so khớp.</summary>
        private static string Fold(string s)
        {
            string decomposed = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);

            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString()
                .Replace('đ', 'd')
                .Replace('Đ', 'D')
                .ToLowerInvariant();
        }
    }
}
