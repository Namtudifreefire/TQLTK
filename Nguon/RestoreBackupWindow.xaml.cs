using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    /// <summary>
    /// Cửa sổ liệt kê các bản sao lưu trên Google Drive và cho người dùng chọn một bản.
    /// Mặc định chỉ hiện bản của người dùng đang đăng nhập và bản cũ chưa có nhãn;
    /// tick ô dưới danh sách để hiện thêm bản của người dùng khác.
    /// Cửa sổ này chỉ CHỌN, không tự khôi phục: sau ShowDialog() == true, đọc thuộc tính Selected,
    /// tải về bằng CloudSyncService.DownloadBackupAsync rồi nhập bằng VaultBackupService.ImportFromFile.
    /// </summary>
    public partial class RestoreBackupWindow : Window
    {
        private readonly CloudSyncService _cloud;

        /// <summary>Bản sao lưu người dùng đã chọn (null nếu hủy).</summary>
        public BackupInfo? Selected { get; private set; }

        public RestoreBackupWindow(CloudSyncService cloud)
        {
            InitializeComponent();
            _cloud = cloud;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            bool includeOthers = chkOthers.IsChecked == true;

            chkOthers.IsEnabled = false;
            btnRestore.IsEnabled = false;
            dgBackups.ItemsSource = null;
            txtStatus.Text = "Đang kết nối Google Drive... (lần đầu có thể cần đăng nhập Google trong trình duyệt)";

            List<BackupInfo>? items;
            try
            {
                items = await _cloud.ListBackupsAsync(includeOthers);
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
                txtStatus.Text = includeOthers
                    ? "Chưa có bản sao lưu nào trên Google Drive của tài khoản này. " +
                      "Lưu ý: bản sao lưu tạo bằng phiên bản cũ hơn 3.6.1 sẽ không hiện ở đây."
                    : "Chưa có bản sao lưu nào của bạn trên Google Drive của tài khoản này. " +
                      "Nếu bạn dùng tên đăng nhập khác trên máy cũ, hãy tick ô bên dưới để xem thêm. " +
                      "Bản sao lưu tạo bằng phiên bản cũ hơn 3.6.1 sẽ không hiện ở đây.";
                return;
            }

            dgBackups.ItemsSource = items;
            dgBackups.SelectedIndex = 0;
            txtStatus.Text = $"Tìm thấy {items.Count} bản sao lưu, mới nhất ở trên cùng. " +
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
            // Chỉ nhận nhấp đúp trên một dòng, không nhận khi nhấp đúp vào tiêu đề cột
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