using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PersonalInfoManager.Services;
namespace PersonalInfoManager;
public partial class BulkAccountActionsWindow : Window
{
    public bool ApplyFolder { get; private set; }
    public string Folder { get; private set; } = "";
    public bool ApplyTags { get; private set; }
    public List<string> Tags { get; private set; } = new();
    public bool MoveToTrash { get; private set; }
    public BulkAccountActionsWindow(int selectedCount)
    {
        InitializeComponent();
        lblSelectedCount.Text = string.Format(LanguageService.Translate("Đã chọn {0} tài khoản."), selectedCount);
        chkApplyFolder.Checked += (_, _) => txtFolder.IsEnabled = true;
        chkApplyFolder.Unchecked += (_, _) => txtFolder.IsEnabled = false;
        chkApplyTags.Checked += (_, _) => txtTags.IsEnabled = true;
        chkApplyTags.Unchecked += (_, _) => txtTags.IsEnabled = false;
    }
    private void Window_Loaded(object sender, RoutedEventArgs e) => LanguageService.ApplyToWindow(this);
    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        ApplyFolder = chkApplyFolder.IsChecked == true;
        Folder = txtFolder.Text.Trim();
        ApplyTags = chkApplyTags.IsChecked == true;
        Tags = txtTags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();
        MoveToTrash = chkMoveToTrash.IsChecked == true;
        if (!ApplyFolder && !ApplyTags && !MoveToTrash)
        {
            MessageBox.Show(this, LanguageService.Translate("Chọn ít nhất một thay đổi hoặc thao tác."),
                LanguageService.Translate("Thông tin"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }
}
