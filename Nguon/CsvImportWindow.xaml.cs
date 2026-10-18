using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using PersonalInfoManager.Models;

namespace PersonalInfoManager
{
    public partial class CsvImportWindow : Window
    {
        public ObservableCollection<CsvPreviewItem> Items { get; } = new();
        public List<SavedAccount> Result { get; private set; } = new();

        public CsvImportWindow(List<SavedAccount> imported, List<SavedAccount> existing)
        {
            InitializeComponent();

            // Đánh dấu trùng lặp
            var existingKeys = new HashSet<string>(
                existing.Select(a => $"{a.ServiceName.ToLower()}|{a.Username.ToLower()}"));

            int dupCount = 0;
            foreach (var acc in imported)
            {
                string key = $"{acc.ServiceName.ToLower()}|{acc.Username.ToLower()}";
                bool isDuplicate = existingKeys.Contains(key);
                if (isDuplicate) dupCount++;

                Items.Add(new CsvPreviewItem
                {
                    Account = acc,
                    IsSelected = !isDuplicate,   // mặc định bỏ chọn nếu trùng
                    Status = isDuplicate ? "⚠ Trùng lặp" : "✓ Mới"
                });
            }

            dgPreview.ItemsSource = Items;

            lblInfo.Text = "Chọn các tài khoản bạn muốn import. Các tài khoản trùng lặp sẽ được bỏ chọn sẵn.";
            lblTotal.Text = $"📊 Tổng: {imported.Count}";
            lblDuplicates.Text = $"⚠ Trùng: {dupCount}";
            UpdateWillImportCount();
        }

        private void UpdateWillImportCount()
        {
            int willImport = Items.Count(x => x.IsSelected);
            lblWillImport.Text = $"✅ Sẽ import: {willImport}";
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Items) item.IsSelected = true;
            dgPreview.Items.Refresh();
            UpdateWillImportCount();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Items) item.IsSelected = false;
            dgPreview.Items.Refresh();
            UpdateWillImportCount();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            Result = Items
                .Where(x => x.IsSelected)
                .Select(x => x.Account)
                .ToList();

            if (Result.Count == 0)
            {
                MessageBox.Show("Bạn chưa chọn tài khoản nào để import.",
                    "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }
    }

    /// <summary>
    /// Item hiển thị trong DataGrid preview.
    /// </summary>
    public class CsvPreviewItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public SavedAccount Account { get; set; } = new();

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string ServiceName => Account.ServiceName;
        public string Username => Account.Username;
        public string MaskedPassword => new string('•', Math.Min(8, Account.Password?.Length ?? 0));
        public string Note => Account.Note;
        public string Status { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}