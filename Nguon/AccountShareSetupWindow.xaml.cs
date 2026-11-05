using System;
using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class AccountShareSetupWindow : Window
{
    public string RecipientEmail { get; private set; } = "";
    public int ExpirationDays { get; private set; }

    public AccountShareSetupWindow() => InitializeComponent();
    private void Window_Loaded(object sender, RoutedEventArgs e) => LanguageService.ApplyToWindow(this);
    private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void BtnCreate_Click(object sender, RoutedEventArgs e)
    {
        string email = txtEmail.Text.Trim();
        try
        {
            var parsed = new MailAddress(email);
            if (!string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase)) throw new FormatException();
        }
        catch
        {
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("Nhập địa chỉ email người nhận hợp lệ."),
                LanguageService.Translate("Email không hợp lệ"), MessageBoxButton.OK, MessageBoxImage.Warning);
            txtEmail.Focus();
            return;
        }
        if (cmbDays.SelectedItem is not ComboBoxItem item || !int.TryParse(item.Tag?.ToString(), out int days) || days is not (1 or 7 or 30)) return;
        RecipientEmail = email;
        ExpirationDays = days;
        DialogResult = true;
        Close();
    }
}
