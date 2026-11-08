using System;
using System.Windows;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class ShareCreatedWindow : Window
{
    private readonly string _passphrase;

    public ShareCreatedWindow(DriveShareInfo share, string passphrase)
    {
        InitializeComponent();
        _passphrase = passphrase;
        txtLink.Text = share.Link;
        txtKey.Text = passphrase;
        string expiration = share.Expires.HasValue
            ? LocalizationAuditService.FormatDateTime(share.Expires.Value)
            : LanguageService.Translate("Không rõ");
        lblInfo.Text = LanguageService.Format("Người nhận: {0}\nCó hiệu lực đến: {1} (giờ địa phương).",
            share.RecipientEmail, expiration);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => LanguageService.ApplyToWindow(this);
    private void BtnCopyLink_Click(object sender, RoutedEventArgs e) => Copy(txtLink.Text, "Đã sao chép liên kết.");
    private void BtnCopyKey_Click(object sender, RoutedEventArgs e) => Copy(_passphrase, "Đã sao chép khóa. Clipboard sẽ tự xóa sau 30 giây.");
    private void BtnClose_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }

    private void Copy(string text, string message)
    {
        if (!ClipboardGuard.CopySensitive(text))
        {
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("Không thể sao chép an toàn."),
                LanguageService.Translate("Lỗi clipboard"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(message), LanguageService.Translate("Đã sao chép"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
