using System.Windows;
using Microsoft.Win32;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class OpenSharedAccountWindow : Window
{
    public string PackagePath => txtPath.Text.Trim();
    public string Passphrase => txtPassphrase.Password;

    public OpenSharedAccountWindow() => InitializeComponent();
    private void Window_Loaded(object sender, RoutedEventArgs e) => LanguageService.ApplyToWindow(this);

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn gói chia sẻ",
            Filter = "Gói chia sẻ TQLTK (*.tqltkshare)|*.tqltkshare|Tất cả tệp (*.*)|*.*"
        };
        LocalizedFileDialog.Apply(dialog);
        if (dialog.ShowDialog(this) == true) txtPath.Text = dialog.FileName;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PackagePath) || string.IsNullOrWhiteSpace(Passphrase))
        {
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("Chọn tệp chia sẻ và nhập khóa."),
                LanguageService.Translate("Thiếu thông tin"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
        Close();
    }
}
