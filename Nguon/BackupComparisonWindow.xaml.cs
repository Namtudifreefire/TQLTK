using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class BackupComparisonWindow : Window
{
    private readonly IReadOnlyList<BackupComparisonEntry> _entries;

    public IReadOnlyList<BackupRecoverySelection> SelectedRecoveryItems { get; private set; } = Array.Empty<BackupRecoverySelection>();

    public BackupComparisonWindow(string summary, IEnumerable<BackupComparisonEntry> changes)
    {
        InitializeComponent();
        _entries = changes.ToList();
        txtSummary.Text = summary;
        dgChanges.ItemsSource = _entries;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => LanguageService.ApplyToWindow(this);

    private void BtnRecover_Click(object sender, RoutedEventArgs e)
    {
        dgChanges.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        dgChanges.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        string older = LanguageService.Translate("Bản sao lưu cũ");
        string newer = LanguageService.Translate("Bản sao lưu mới");
        var selected = _entries
            .Where(entry => entry.Kind != BackupRecoveryItemKind.Settings)
            .Select(entry => entry.SelectedSource == older && entry.OlderKey != null
                ? new BackupRecoverySelection(entry.Kind, entry.OlderKey, true)
                : entry.SelectedSource == newer && entry.NewerKey != null
                    ? new BackupRecoverySelection(entry.Kind, entry.NewerKey, false)
                    : null)
            .Where(item => item != null)
            .Cast<BackupRecoverySelection>()
            .Distinct()
            .ToList();
        if (selected.Count == 0)
        {
            LocalizedMessageBox.Show(this, "Chọn bản cũ hoặc bản mới cho ít nhất một tài khoản, ghi chú hoặc tệp.",
                "Chưa chọn mục khôi phục", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (LocalizedMessageBox.Show(this,
                LanguageService.Format("Sẽ thêm {0} mục thành bản sao riêng. Dữ liệu hiện tại không bị ghi đè. Tiếp tục?", selected.Count),
                "Xác nhận khôi phục", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SelectedRecoveryItems = selected;
        DialogResult = true;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
