using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class RecoveryCodesWindow : Window
    {
        private readonly string _username;
        private readonly List<string> _codes;
        private bool _finished;

        public RecoveryCodesWindow(string username, List<string> codes)
        {
            InitializeComponent();

            _username = username;
            _codes = codes;
            txtCodes.Text = string.Join(Environment.NewLine, codes);
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!ClipboardGuard.CopySensitive(txtCodes.Text))
                MessageBox.Show(this,
                    "Không thể sao chép mã khôi phục vào clipboard an toàn. Hãy thử lại.",
                    "Không thể sao chép", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Chỉ khi bấm "hoàn tất" mới lưu bộ mã mới và vô hiệu hóa mã cũ
        private void BtnFinish_Click(object sender, RoutedEventArgs e)
        {
            var result = TwoFactorService.CommitRecoveryCodes(_username, _codes);
            if (!result.ok)
            {
                lblError.Text = result.message + "\nMã cũ vẫn còn hiệu lực. Hãy đóng cửa sổ và thử lại.";
                lblError.Visibility = Visibility.Visible;
                return;
            }

            _finished = true;
            DialogResult = true;
        }

        // Đóng khi chưa hoàn tất: mã mới bị hủy, mã cũ vẫn dùng được.
        // Nếu app đang tự khóa (đã đăng xuất) thì đóng luôn, không hỏi.
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_finished && SessionService.IsLoggedIn)
            {
                var r = MessageBox.Show(this,
                    "Bạn chưa bấm hoàn tất.\nNếu đóng bây giờ, các mã mới sẽ bị hủy và mã khôi phục cũ vẫn dùng như trước.\n\nĐóng cửa sổ?",
                    "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (r != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            base.OnClosing(e);
        }
    }
}
