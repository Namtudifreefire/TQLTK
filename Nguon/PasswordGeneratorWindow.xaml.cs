using System.Windows;
using System.Windows.Controls;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class PasswordGeneratorWindow : Window
{
    public string? GeneratedPassword { get; private set; }

    public PasswordGeneratorWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => txtLength.Focus();
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
            GeneratedPassword = PasswordGeneratorService.Generate(options);
            txtPreview.Text = GeneratedPassword;
        }
        catch (Exception ex)
        {
            GeneratedPassword = null;
            txtPreview.Clear();
            lblError.Text = ex.Message;
        }
    }

    private static int ParseNumber(string text, string label) =>
        int.TryParse(text, out int value) ? value : throw new FormatException($"{label} phải là số nguyên.");

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
