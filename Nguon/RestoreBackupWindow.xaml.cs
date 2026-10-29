using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{

    public partial class RestoreBackupWindow : Window
    {
        private readonly CloudSyncService _cloud;
        private readonly BackupKind _kind;

        public BackupInfo? Selected { get; private set; }

        public RestoreBackupWindow(CloudSyncService cloud, BackupKind kind = BackupKind.Vault)
        {
            InitializeComponent();
            _cloud = cloud;
            _kind = kind;

            if (kind == BackupKind.Files)
            {
                Title = "Khôi phục File cá nhân từ Google Drive";
                txtTitle.Text = "📁 Khôi phục File cá nhân từ Google Drive";
                txtNote.Text = "Không có file nào bị ghi đè: file trùng nội dung được bỏ qua, " +
                               "file trùng tên nhưng khác nội dung được lưu thêm với tên mới.";
            }

            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            bool includeOthers = chkOthers.IsChecked == true;
            bool files = _kind == BackupKind.Files;

            chkOthers.IsEnabled = false;
            btnRestore.IsEnabled = false;
            dgBackups.ItemsSource = null;
            txtStatus.Text = "Đang kết nối Google Drive... (lần đầu có thể cần đăng nhập Google trong trình duyệt)";

            List<BackupInfo>? items;
            try
            {
                items = await _cloud.ListBackupsAsync(_kind, includeOthers);
            }
            finally
            {
                chkOthers.IsEnabled = true;
            }

            if (items == null)
            {
                txtStatus.Text = _cloud.LastError ?? "Không lấy được danh sách sao lưu.";
                return;
            }

            if (items.Count == 0)
            {
                if (files)
                {
                    txtStatus.Text = includeOthers
                        ? "Chưa có bản sao lưu File cá nhân nào trên Google Drive của tài khoản này."
                        : "Chưa có bản sao lưu File cá nhân nào của bạn trên Google Drive của tài khoản này. " +
                          "Nếu bạn dùng tên đăng nhập khác trên máy cũ, hãy tick ô bên dưới để xem thêm.";
                }
                else
                {
                    txtStatus.Text = includeOthers
                        ? "Chưa có bản sao lưu nào trên Google Drive của tài khoản này. " +
                          "Lưu ý: bản sao lưu tạo bằng phiên bản cũ hơn 3.6.1 sẽ không hiện ở đây."
                        : "Chưa có bản sao lưu nào của bạn trên Google Drive của tài khoản này. " +
                          "Nếu bạn dùng tên đăng nhập khác trên máy cũ, hãy tick ô bên dưới để xem thêm. " +
                          "Bản sao lưu tạo bằng phiên bản cũ hơn 3.6.1 sẽ không hiện ở đây.";
                }
                return;
            }

            dgBackups.ItemsSource = items;
            dgBackups.SelectedIndex = 0;
            txtStatus.Text = files
                ? $"Tìm thấy {items.Count} bản sao lưu File cá nhân, mới nhất ở trên cùng. Chọn một bản rồi bấm Khôi phục."
                : $"Tìm thấy {items.Count} bản sao lưu, mới nhất ở trên cùng. " +
                  "Cột \"Thuộc về\": \"Của bạn\" là bản tạo từ tài khoản này, \"Bản cũ\" là bản tạo trước khi tách theo người dùng. " +
                  "Chọn một bản rồi bấm Khôi phục.";
        }

        private async void ChkOthers_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            await LoadAsync();
        }

        private void DgBackups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnRestore.IsEnabled = dgBackups.SelectedItem is BackupInfo;
        }

        private void DgBackups_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {

            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(dgBackups, source) is DataGridRow)
            {
                Confirm();
            }
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e) => Confirm();

        private void Confirm()
        {
            if (dgBackups.SelectedItem is BackupInfo b)
            {
                Selected = b;
                DialogResult = true;
            }
        }
    }
}