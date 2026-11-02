using System.Windows;
using System.Windows.Threading;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class TotpCodeWindow : Window
{
    private readonly SavedAccount _account;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public TotpCodeWindow(SavedAccount account)
    {
        InitializeComponent();
        _account = account;
        lblAccount.Text = account.ServiceName;
        barLifetime.Maximum = Math.Max(1, account.TotpPeriodSeconds);
        _timer.Tick += (_, _) => RefreshCode();
        Loaded += (_, _) => { RefreshCode(); _timer.Start(); };
    }

    private void RefreshCode()
    {
        try
        {
            var result = TotpService.GetCurrentCode(_account.TotpSecret, _account.TotpAlgorithm,
                _account.TotpDigits, _account.TotpPeriodSeconds);
            lblCode.Text = result.Code;
            lblCountdown.Text = $"Mã mới sau {result.SecondsRemaining} giây";
            barLifetime.Value = result.SecondsRemaining;
        }
        catch
        {
            lblCode.Text = "Không khả dụng";
            lblCountdown.Text = "Thông tin TOTP không hợp lệ.";
            barLifetime.Value = 0;
        }
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        string code = lblCode.Text;
        if (code.Length is not (6 or 8) || !code.All(char.IsDigit)) return;
        if (!ClipboardGuard.CopySensitive(code))
        {
            MessageBox.Show(this, "Không thể sao chép mã vào clipboard an toàn. Hãy thử lại.",
                "Không thể sao chép", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(this, $"Đã copy mã. Clipboard sẽ tự xóa sau {ClipboardGuard.ClearAfterSeconds} giây.",
            "Đã sao chép", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Window_Closed(object? sender, EventArgs e) => _timer.Stop();
}
