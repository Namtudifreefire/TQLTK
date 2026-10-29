using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PersonalInfoManager.Services;
using QRCoder;

namespace PersonalInfoManager
{
    public partial class TwoFactorSetupWindow : Window
    {
        private readonly string _username;
        private readonly string _secret;

        private bool _enabled;
        private bool _finished;

        public TwoFactorSetupWindow(string username)
        {
            InitializeComponent();

            _username = username;
            _secret = TwoFactorService.GenerateSecret();

            txtSecret.Text = TwoFactorService.FormatSecret(_secret);
            imgQr.Source = BuildQr(TwoFactorService.BuildOtpAuthUri(username, _secret));

            Loaded += (_, _) => txtCode.Focus();
        }





        private static BitmapImage BuildQr(string content)
        {
            var generator = new QRCodeGenerator();
            var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
            byte[] png = new PngByteQRCode(data).GetGraphic(8);

            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(png))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            return bmp;
        }





        private void BtnCopySecret_Click(object sender, RoutedEventArgs e)
        {
            if (!ClipboardGuard.CopySensitive(_secret))
                MessageBox.Show(this,
                    "Không thể sao chép khóa 2FA vào clipboard an toàn. Hãy thử lại.",
                    "Không thể sao chép", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            var result = TwoFactorService.Enable(_username, _secret, txtCode.Text);

            if (!result.ok)
            {
                lblError.Text = result.message;
                lblError.Visibility = Visibility.Visible;
                txtCode.SelectAll();
                txtCode.Focus();
                return;
            }

            _enabled = true;
            txtRecovery.Text = string.Join(Environment.NewLine, result.recoveryCodes);

            pnlStep1.Visibility = Visibility.Collapsed;
            pnlStep2.Visibility = Visibility.Visible;
        }





        private void BtnCopyRecovery_Click(object sender, RoutedEventArgs e)
        {
            if (!ClipboardGuard.CopySensitive(txtRecovery.Text))
                MessageBox.Show(this,
                    "Không thể sao chép mã khôi phục vào clipboard an toàn. Hãy thử lại.",
                    "Không thể sao chép", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void BtnFinish_Click(object sender, RoutedEventArgs e)
        {
            _finished = true;
            DialogResult = true;
        }


        protected override void OnClosing(CancelEventArgs e)
        {
            if (_enabled && !_finished)
            {
                var r = MessageBox.Show(this,
                    "Bạn đã lưu mã khôi phục chưa?\nMã này chỉ hiển thị một lần và sẽ không xem lại được.",
                    "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning);

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
