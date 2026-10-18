using System;
using System.Windows;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class TwoFactorPromptWindow : Window
    {
        private const int MaxAttempts = 5;

        private readonly Func<string, (bool ok, string message)> _verify;
        private int _attempts;

        /// <param name="title">Tiêu đề cửa sổ</param>
        /// <param name="description">Dòng hướng dẫn</param>
        /// <param name="verify">Hàm kiểm tra mã: trả về (ok, message)</param>
        public TwoFactorPromptWindow(string title, string description, Func<string, (bool ok, string message)> verify)
        {
            InitializeComponent();

            Title = title;
            lblTitle.Text = "🔑 " + title;
            lblDescription.Text = description;
            _verify = verify;

            Loaded += (_, _) => txtCode.Focus();
        }

        // ================================================================
        // ================== DÙNG CHO ĐĂNG NHẬP ==================
        // ================================================================

        /// <summary>
        /// Gọi sau khi mật khẩu (hoặc Windows Hello) đã đúng, TRƯỚC khi mở MainWindow.
        /// - Nếu user chưa bật 2FA: trả về true ngay.
        /// - Nếu đã bật: hiện cửa sổ nhập mã, trả về true khi nhập đúng.
        /// </summary>
        public static bool AskIfEnabled(Window? owner, string username)
        {
            if (!TwoFactorService.IsEnabled(username)) return true;

            var dlg = new TwoFactorPromptWindow(
                "Xác thực 2 bước",
                "Nhập mã 6 số từ ứng dụng Authenticator trên điện thoại.",
                code => TwoFactorService.Validate(username, code));

            if (owner != null && owner.IsVisible) dlg.Owner = owner;

            bool ok = dlg.ShowDialog() == true;

            try { ActivityLogger.Log(username, ok ? "LOGIN_2FA_OK" : "LOGIN_2FA_FAILED", ""); } catch { }
            return ok;
        }

        // ================================================================
        // ================== XÁC NHẬN ==================
        // ================================================================

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var result = _verify(txtCode.Text);

            if (result.ok)
            {
                if (!string.IsNullOrWhiteSpace(result.message))
                    MessageBox.Show(this, result.message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                return;
            }

            _attempts++;
            int left = MaxAttempts - _attempts;

            if (left <= 0)
            {
                MessageBox.Show(this, "Bạn đã nhập sai quá nhiều lần.", "Bị từ chối", MessageBoxButton.OK, MessageBoxImage.Warning);
                DialogResult = false;
                return;
            }

            lblError.Text = $"{result.message} (còn {left} lần thử)";
            lblError.Visibility = Visibility.Visible;
            txtCode.SelectAll();
            txtCode.Focus();
        }
    }
}
