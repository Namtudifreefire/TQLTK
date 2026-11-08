using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using PersonalInfoManager.Services;
namespace PersonalInfoManager;
public partial class BackupCenterWindow : Window
{
    private readonly string _username;
    private readonly string _folder;
    private bool _isBusy;
    private bool _isClosed;
    public string? SelectedBackupPath { get; private set; }
    public BackupComparisonResult? SelectedComparison { get; private set; }
    public IReadOnlyList<BackupRecoverySelection> SelectedRecoveryItems { get; private set; } = Array.Empty<BackupRecoverySelection>();
    public BackupCenterWindow(string username)
    {
        InitializeComponent();
        _username = username;
        _folder = AutoBackupService.GetFolder(username);
        Closed += (_, _) => _isClosed = true;
        RefreshList();
    }
    private void Window_Loaded(object sender, RoutedEventArgs e) => LanguageService.ApplyToWindow(this);
    private void RefreshList()
    {
        var rows = CompleteBackupService.ListBackups(_folder)
            .Select(file => new BackupRow(file.FullName, file.Name,
                LocalizationAuditService.FormatDateTime(file.LastWriteTime), FileBackupService.FormatSize(file.Length),
                AutoBackupService.IsPinned(file.FullName) ? "Đã ghim" : "Thông thường") { CreatedAt = file.LastWriteTimeUtc })
            .ToList();
        dgBackups.ItemsSource = rows;
        txtStatus.Text = rows.Count == 0
            ? LanguageService.Translate("ui.backupcenterwindow.message001")
            : LanguageService.Translate("ui.backupcenterwindow.message002");
        UpdateSelection();
    }
    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshList();
    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            Process.Start(new ProcessStartInfo("explorer.exe", _folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LocalizedMessageBox.Show(this, "Không mở được thư mục sao lưu: " + ex.Message,
                "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void DgBackups_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateSelection();
    private void UpdateSelection()
    {
        var selected = dgBackups.SelectedItems.OfType<BackupRow>().ToList();
        btnRefresh.IsEnabled = !_isBusy;
        btnOpenFolder.IsEnabled = !_isBusy;
        dgBackups.IsEnabled = !_isBusy;
        btnRestore.IsEnabled = !_isBusy && selected.Count == 1;
        btnVerify.IsEnabled = !_isBusy && selected.Count == 1;
        btnPin.IsEnabled = !_isBusy && selected.Count == 1;
        btnCompare.IsEnabled = !_isBusy && selected.Count == 2;
        txtSelection.Text = selected.Count switch
        {
            0 => LanguageService.Translate("ui.backupcenterwindow.message003"),
            1 => LanguageService.Translate("ui.backupcenterwindow.message004"),
            2 => LanguageService.Translate("ui.backupcenterwindow.message005"),
            _ => LanguageService.Translate("ui.backupcenterwindow.message006")
        };
        if (selected.Count == 1)
            btnPin.Content = AutoBackupService.IsPinned(selected[0].Path)
                ? LanguageService.Translate("ui.backupcenterwindow.message007") : LanguageService.Translate("ui.backupcenterwindow.message008");
    }
    private void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        if (dgBackups.SelectedItem is not BackupRow row) return;
        SelectedBackupPath = row.Path;
        DialogResult = true;
    }
    private async void BtnVerify_Click(object sender, RoutedEventArgs e)
    {
        if (dgBackups.SelectedItem is not BackupRow row) return;
        string? passphrase = PromptPassphrase();
        if (passphrase == null) return;
        if (_isBusy) return;
        _isBusy = true;
        UpdateSelection();
        var result = await Task.Run(() => CompleteBackupService.Inspect(row.Path, passphrase));
        if (_isClosed) return;
        _isBusy = false;
        txtStatus.Text = LanguageService.Translate(result.message);
        if (result.ok) AutoBackupService.RecordBackupVerification(_username, row.Path, true);
        UpdateSelection();
    }
    private async void BtnPin_Click(object sender, RoutedEventArgs e)
    {
        if (dgBackups.SelectedItem is not BackupRow row) return;
        bool pin = !AutoBackupService.IsPinned(row.Path);
        string? passphrase = pin ? PromptPassphrase() : "";
        if (passphrase == null) return;
        if (_isBusy) return;
        _isBusy = true;
        UpdateSelection();
        var result = await Task.Run(() => AutoBackupService.SetPinned(row.Path, passphrase, pin));
        if (_isClosed) return;
        _isBusy = false;
        RefreshList();
        txtStatus.Text = LanguageService.Translate(result.message);
    }
    private async void BtnCompare_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var selected = dgBackups.SelectedItems.OfType<BackupRow>()
            .OrderBy(row => row.CreatedAt).ToArray();
        if (selected.Length != 2) return;
        string? firstPassphrase = PromptPassphrase();
        if (firstPassphrase == null) return;
        string? secondPassphrase = PromptPassphrase();
        if (secondPassphrase == null) return;
        _isBusy = true;
        UpdateSelection();
        var result = await Task.Run(() => CompleteBackupService.Compare(
            _username, selected[0].Path, firstPassphrase, selected[1].Path, secondPassphrase));
        if (_isClosed) return;
        _isBusy = false;
        UpdateSelection();
        if (!result.ok || result.result == null)
        {
            txtStatus.Text = LanguageService.Translate(result.message);
            return;
        }
        string summary = result.message + "\n" +
            LanguageService.Translate("ui.backupcenterwindow.message009") + " " + selected[0].Name + "   " +
            LanguageService.Translate("ui.backupcenterwindow.message010") + " " + selected[1].Name + "\n" +
            LanguageService.Translate("ui.backupcenterwindow.message011");
        var comparison = new BackupComparisonWindow(summary, result.result.Entries) { Owner = this };
        if (comparison.ShowDialog() == true && comparison.SelectedRecoveryItems.Count > 0)
        {
            SelectedComparison = result.result;
            SelectedRecoveryItems = comparison.SelectedRecoveryItems;
            DialogResult = true;
        }
    }
    private string? PromptPassphrase()
    {
        var dialog = new BackupPassphraseWindow(confirmMode: false) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Passphrase : null;
    }
    private sealed record BackupRow(string Path, string Name, string DateText, string SizeText, string PinText)
    {
        public DateTime CreatedAt { get; init; }
    }
}
