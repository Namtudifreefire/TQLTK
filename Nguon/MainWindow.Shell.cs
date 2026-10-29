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

    public partial class MainWindow
    {

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

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (_pages.Count == 0) return;
            if (sender is RadioButton { Tag: string tag }) ShowPage(tag);
        }

        private void ShowPage(string tag)
        {

            if (tag == "admin" && !SessionService.IsAdmin) return;

            bool isSettings = SettingsTags.Contains(tag);

            foreach (var kv in _pages)
            {

                if (SettingsTags.Contains(kv.Key) != isSettings) continue;
                kv.Value.Visibility = kv.Key == tag ? Visibility.Visible : Visibility.Collapsed;
            }

            if (isSettings) _currentSettingsTag = tag;

            if (tag == "files") RefreshFiles();
        }

        private DateTime _userMenuClosedAt = DateTime.MinValue;

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

        private bool _settingsOpen;
        private Dictionary<string, string>? _settingsIndex;

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
            _settingsIndex = null;

            LoadProfile();
            txtSettingsSearch.Clear();
            lblSettingsSearchHint.Visibility = Visibility.Visible;
            ApplySettingsFilter();

            SelectSettingsPage(tag);

            shellMain.IsEnabled = false;
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

        private void TxtSettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            lblSettingsSearchHint.Visibility =
                txtSettingsSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            ApplySettingsFilter();
        }

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
