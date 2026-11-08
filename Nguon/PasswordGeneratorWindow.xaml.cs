using System.Windows;
using System.Windows.Controls;
using System.Linq;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class PasswordGeneratorWindow : Window
{
    private readonly string _websiteUrl;
    private readonly string _websiteHost;
    public string? GeneratedPassword { get; private set; }

    public PasswordGeneratorWindow(string? websiteUrl = null)
    {
        InitializeComponent();
        _websiteUrl = websiteUrl ?? "";
        _websiteHost = WebsitePasswordPolicyService.NormalizeHost(_websiteUrl);
        if (_websiteHost.Length == 0)
        {
            borderWebsitePolicy.Visibility = Visibility.Collapsed;
        }
        else
        {
            WebsitePasswordPolicy policy = WebsitePasswordPolicyService.Get(_websiteUrl);
            lblWebsitePolicyHost.Text = LanguageService.Format("Quy tắc được lưu riêng cho {0}.", _websiteHost);
            txtPolicyMinLength.Text = policy.MinimumLength.ToString();
            txtPolicyMaxLength.Text = policy.MaximumLength.ToString();
            chkPolicyUpper.IsChecked = policy.RequireUppercase;
            chkPolicyLower.IsChecked = policy.RequireLowercase;
            chkPolicyDigits.IsChecked = policy.RequireDigits;
            chkPolicySymbols.IsChecked = policy.RequireSymbols;
            txtPolicyExcludedCharacters.Text = policy.ExcludedCharacters;
            chkUseWebsitePolicy.IsChecked = policy.Enabled;
            btnClearWebsitePolicy.IsEnabled = policy.Enabled;
        }
        UpdatePolicyEditors();
        Loaded += (_, _) =>
        {
            LanguageService.ApplyToWindow(this);
            txtLength.Focus();
        };
    }

    private void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (pnlRandom == null || pnlPassphrase == null || cmbMode == null) return;
        bool phrase = cmbMode.SelectedIndex == 1;
        pnlRandom.Visibility = phrase ? Visibility.Collapsed : Visibility.Visible;
        pnlPassphrase.Visibility = phrase ? Visibility.Visible : Visibility.Collapsed;
        txtPreview?.Clear();
        GeneratedPassword = null;
    }

    private void BtnGenerate_Click(object sender, RoutedEventArgs e)
    {
        lblError.Text = "";
        try
        {
            var separator = cmbSeparator.SelectedIndex switch
            {
                1 => " ",
                2 => ".",
                3 => "_",
                _ => "-"
            };
            var options = new PasswordGeneratorOptions(
                ParseNumber(txtLength.Text, "Độ dài"),
                chkUpper.IsChecked == true,
                chkLower.IsChecked == true,
                chkDigits.IsChecked == true,
                chkSymbols.IsChecked == true,
                chkExcludeAmbiguous.IsChecked == true,
                cmbMode.SelectedIndex == 1,
                ParseNumber(txtWordCount.Text, "Số từ"),
                separator);
            WebsitePasswordPolicy? policy = chkUseWebsitePolicy.IsChecked == true
                ? ReadWebsitePolicy(enabled: true)
                : null;
            GeneratedPassword = PasswordGeneratorService.Generate(options, policy);
            txtPreview.Text = GeneratedPassword;
        }
        catch (Exception ex)
        {
            GeneratedPassword = null;
            txtPreview.Clear();
            lblError.Text = LanguageService.Translate(ex.Message);
        }
    }

    private WebsitePasswordPolicy ReadWebsitePolicy(bool enabled) => new(
        enabled,
        ParseNumber(txtPolicyMinLength.Text, "Độ dài tối thiểu"),
        ParseNumber(txtPolicyMaxLength.Text, "Độ dài tối đa"),
        chkPolicyUpper.IsChecked == true,
        chkPolicyLower.IsChecked == true,
        chkPolicyDigits.IsChecked == true,
        chkPolicySymbols.IsChecked == true,
        txtPolicyExcludedCharacters.Text ?? "");

    private void WebsitePolicy_Changed(object sender, RoutedEventArgs e) => UpdatePolicyEditors();

    private void UpdatePolicyEditors()
    {
        if (gridWebsitePolicy == null) return;
        bool enabled = chkUseWebsitePolicy?.IsChecked == true;
        gridWebsitePolicy.IsEnabled = enabled;
        btnSaveWebsitePolicy.IsEnabled = _websiteHost.Length > 0;
        btnClearWebsitePolicy.IsEnabled = _websiteHost.Length > 0 && WebsitePasswordPolicyService.Get(_websiteUrl).Enabled;
        lblWebsitePolicyError.Text = "";
    }

    private void BtnSaveWebsitePolicy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WebsitePasswordPolicy policy = ReadWebsitePolicy(enabled: true);
            if (policy.MinimumLength is < 8 or > 64 || policy.MaximumLength is < 8 or > 64 ||
                policy.MinimumLength > policy.MaximumLength)
                throw new InvalidOperationException("Độ dài website phải trong khoảng 8–64 và độ dài tối thiểu không được lớn hơn tối đa.");
            int requiredGroups = (policy.RequireUppercase ? 1 : 0) + (policy.RequireLowercase ? 1 : 0) +
                                 (policy.RequireDigits ? 1 : 0) + (policy.RequireSymbols ? 1 : 0);
            if (requiredGroups > policy.MaximumLength)
                throw new InvalidOperationException("Độ dài tối đa không đủ cho các nhóm ký tự bắt buộc.");
            WebsitePasswordPolicyService.Save(_websiteUrl, policy);
            chkUseWebsitePolicy.IsChecked = true;
            btnClearWebsitePolicy.IsEnabled = true;
            LanguageService.SetLocalizedText(lblWebsitePolicyError, "Website password rules saved.");
        }
        catch (Exception ex)
        {
            LanguageService.SetLocalizedText(lblWebsitePolicyError, ex.Message);
        }
    }

    private void BtnClearWebsitePolicy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WebsitePasswordPolicyService.Save(_websiteUrl, WebsitePasswordPolicy.Default);
            txtPolicyMinLength.Text = "8";
            txtPolicyMaxLength.Text = "64";
            chkPolicyUpper.IsChecked = false;
            chkPolicyLower.IsChecked = false;
            chkPolicyDigits.IsChecked = false;
            chkPolicySymbols.IsChecked = false;
            txtPolicyExcludedCharacters.Clear();
            chkUseWebsitePolicy.IsChecked = false;
            btnClearWebsitePolicy.IsEnabled = false;
            LanguageService.SetLocalizedText(lblWebsitePolicyError, "Saved website password rules removed.");
        }
        catch (Exception ex)
        {
            LanguageService.SetLocalizedText(lblWebsitePolicyError, ex.Message);
        }
    }

    private static int ParseNumber(string text, string label) =>
        int.TryParse(text, out int value)
            ? value
            : throw new FormatException(LanguageService.Format("{0} phải là số nguyên.", LanguageService.Translate(label)));

    private void BtnUse_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(GeneratedPassword))
        {
            lblError.Text = "Hãy tạo mật khẩu trước.";
            return;
        }
        DialogResult = true;
    }
}
