using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class PasswordHistoryWindow : Window
    {
        public PasswordHistoryEntry? SelectedEntry { get; private set; }

        private sealed class HistoryRow : INotifyPropertyChanged
        {
            private bool _showPassword;

            public HistoryRow(PasswordHistoryEntry entry) => Entry = entry;

            public PasswordHistoryEntry Entry { get; }
            public DateTime ChangedAt => Entry.ChangedAt;
            public string DisplayPassword => string.IsNullOrEmpty(Entry.Password)
                ? "—"
                : _showPassword ? Entry.Password : "••••••••••••";

            public void SetPasswordVisible(bool visible)
            {
                if (_showPassword == visible) return;
                _showPassword = visible;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayPassword)));
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly List<HistoryRow> _rows;

        public PasswordHistoryWindow(SavedAccount account)
        {
            InitializeComponent();
            txtTitle.Text = $"Lịch sử mật khẩu — {account.ServiceName}";
            var entries = (account.PasswordHistory ?? new List<PasswordHistoryEntry>())
                .Where(entry => entry != null)
                .OrderByDescending(entry => entry.ChangedAt)
                .Select(entry => new HistoryRow(entry.Clone())).ToList();
            _rows = entries;
            dgHistory.ItemsSource = _rows;
            btnReveal.IsEnabled = entries.Count > 0;
            dgHistory.SelectionChanged += (_, _) =>
            {
                bool hasSelection = dgHistory.SelectedItem is HistoryRow;
                btnCopy.IsEnabled = hasSelection;
                btnRestore.IsEnabled = hasSelection;
            };
            if (entries.Count == 0)
            {
                dgHistory.IsEnabled = false;
                txtNote.Text = "Chưa có lịch sử. Mật khẩu trước được thêm khi bạn đổi mật khẩu của mục này.";
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (dgHistory.SelectedItem is not HistoryRow row) return;
            if (!ClipboardGuard.CopySensitive(row.Entry.Password))
            {
                PersonalInfoManager.Services.LocalizedMessageBox.Show("Không thể sao chép mật khẩu vào bộ nhớ tạm an toàn.", "Không thể sao chép",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            PersonalInfoManager.Services.LocalizedMessageBox.Show($"Đã sao chép. Bộ nhớ tạm sẽ tự xóa sau {ClipboardGuard.ClearAfterSeconds} giây.",
                "Đã sao chép", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (dgHistory.SelectedItem is not HistoryRow row) return;
            SelectedEntry = row.Entry.Clone();
            DialogResult = true;
        }

        private void BtnReveal_Click(object sender, RoutedEventArgs e)
        {
            bool reveal = btnReveal.Content?.ToString() != "Ẩn mật khẩu";
            foreach (HistoryRow row in _rows) row.SetPasswordVisible(reveal);
            btnReveal.Content = reveal ? "Ẩn mật khẩu" : "Hiện mật khẩu";
        }
    }
}
