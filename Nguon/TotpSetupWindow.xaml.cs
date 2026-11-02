using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class TotpSetupWindow : Window
{
    private readonly string _fallbackName;
    private List<TotpCredential> _entries = new();

    public TotpCredential? Result { get; private set; }

    public TotpSetupWindow(string fallbackName)
    {
        InitializeComponent();
        _fallbackName = string.IsNullOrWhiteSpace(fallbackName) ? "Tài khoản" : fallbackName;
    }

    private void BtnParse_Click(object sender, RoutedEventArgs e)
    {
        try { SetEntries(TotpService.ParseImportText(txtSource.Text, _fallbackName)); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void BtnReadImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn ảnh QR từ Authenticator",
            Filter = "Ảnh QR (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif|Tất cả file (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try { SetEntries(TotpService.DecodeQrImage(dialog.FileName)); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void BtnImportFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn export từ Authenticator",
            Filter = "Export Authenticator (*.2fas;*.json;*.txt)|*.2fas;*.json;*.txt|Tất cả file (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try { SetEntries(TotpService.ImportAuthenticatorFile(dialog.FileName, _fallbackName)); }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void SetEntries(IReadOnlyList<TotpCredential> entries)
    {
        _entries = entries.ToList();
        cmbEntries.ItemsSource = null;
        cmbEntries.ItemsSource = _entries;
        cmbEntries.SelectedIndex = _entries.Count > 0 ? 0 : -1;
        lblError.Text = "";
        if (_entries.Count > 1)
            lblDetails.Text = $"Đọc được {_entries.Count} mục từ QR chuyển tài khoản. Chọn mục dành cho website đang sửa.";
        else
            UpdateDetails();
    }

    private void Entries_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetails();

    private void UpdateDetails()
    {
        if (lblDetails == null) return;
        if (cmbEntries.SelectedItem is not TotpCredential entry)
        {
            lblDetails.Text = _entries.Count > 1
                ? $"Đọc được {_entries.Count} mục. Chọn một mục để xem thông tin."
                : "Chưa đọc dữ liệu.";
            return;
        }
        lblDetails.Text = $"Website/tài khoản: {entry.Name}\nNhà phát hành: " +
                          $"{(string.IsNullOrWhiteSpace(entry.Issuer) ? "không có" : entry.Issuer)}\n" +
                          $"TOTP: {entry.Digits} chữ số • {entry.Algorithm} • mỗi {entry.PeriodSeconds} giây";
    }

    private void SetError(string message)
    {
        _entries.Clear();
        cmbEntries.ItemsSource = null;
        UpdateDetails();
        lblError.Text = message;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (cmbEntries.SelectedItem is not TotpCredential entry)
        {
            lblError.Text = "Đọc secret hoặc QR và chọn một mục trước.";
            return;
        }
        Result = entry;
        txtSource.Clear();
        DialogResult = true;
    }
}
