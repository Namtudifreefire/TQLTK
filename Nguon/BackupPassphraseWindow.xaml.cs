using System;
using System.Text;
using System.Windows;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{






    public partial class BackupPassphraseWindow : Window
    {
        private readonly bool _confirmMode;

        public string Passphrase { get; private set; } = "";

        public BackupPassphraseWindow(bool confirmMode)
        {
            InitializeComponent();
            _confirmMode = confirmMode;

            if (confirmMode)
            {
                txtTitle.Text = "🔑 Đặt cụm mật khẩu sao lưu";
                txtDesc.Text = "Cụm mật khẩu này dùng để mã hóa các bản sao lưu, giúp bạn khôi phục được trên máy khác " +
                               "(ví dụ khi đổi máy hoặc cài lại Windows). Đồng bộ Drive sẽ không hỏi lại cụm này mỗi lần.";
                panelConfirm.Visibility = Visibility.Visible;
                txtHint.Visibility = Visibility.Visible;
                btnOk.Content = "Lưu";
            }
            else
            {
                txtTitle.Text = "🔑 Nhập cụm mật khẩu sao lưu";
                txtDesc.Text = "Bản sao lưu này được bảo vệ bằng cụm mật khẩu. Hãy nhập cụm bạn đã đặt khi sao lưu.";
                panelConfirm.Visibility = Visibility.Collapsed;
                txtHint.Visibility = Visibility.Collapsed;
                btnOk.Content = "Mở";
            }

            Loaded += (_, _) => pwdMain.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string p = pwdMain.Password;

            if (_confirmMode)
            {
                if (p.Length < BackupCrypto.MinPassphraseLength)
                {
                    ShowError($"Cụm mật khẩu cần ít nhất {BackupCrypto.MinPassphraseLength} ký tự.");
                    return;
                }


                string a = p.Normalize(NormalizationForm.FormC);
                string b = pwdConfirm.Password.Normalize(NormalizationForm.FormC);
                if (!string.Equals(a, b, StringComparison.Ordinal))
                {
                    ShowError("Hai lần nhập không giống nhau.");
                    return;
                }
            }
            else if (p.Length == 0)
            {
                ShowError("Hãy nhập cụm mật khẩu.");
                return;
            }

            Passphrase = p;
            DialogResult = true;
        }

        private void ShowError(string message)
        {
            txtError.Text = message;
            txtError.Visibility = Visibility.Visible;
        }
    }
}