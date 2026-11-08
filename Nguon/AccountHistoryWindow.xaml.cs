using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class AccountHistoryWindow : Window
{
    private readonly List<AccountRevisionEntry> _entries;
    public AccountRevisionEntry? SelectedEntry { get; private set; }

    public AccountHistoryWindow(IEnumerable<AccountRevisionEntry> entries)
    {
        InitializeComponent();
        _entries = entries.Select(entry => entry.Clone()).OrderByDescending(entry => entry.ChangedAtUtc).ToList();
        dgHistory.ItemsSource = _entries.Select(entry => new HistoryView(entry)).ToList();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => LanguageService.ApplyToWindow(this);

    private void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) return;
        var entry = _entries.FirstOrDefault(item => item.Id == id);
        if (entry == null) return;

        string fields = string.Join(", ", entry.ChangedFields.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(FieldLabel).Select(LanguageService.Translate));
        var result = PersonalInfoManager.Services.LocalizedMessageBox.Show(this,
            LanguageService.Translate("Hoàn tác sẽ khôi phục các trường sau về trạng thái trước thay đổi này:") +
            "\n\n" + fields + "\n\n" + LanguageService.Translate("Bạn muốn tiếp tục?"),
            LanguageService.Translate("Xác nhận hoàn tác"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        SelectedEntry = entry;
        DialogResult = true;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string FieldLabel(string field) => field switch
    {
        "ServiceName" => "Dịch vụ",
        "Username" => "Tên đăng nhập",
        "WebsiteUrl" => "URL website",
        "Folder" => "Thư mục",
        "Tags" => "Nhãn",
        "IsFavorite" => "Mục yêu thích",
        "Note" => "Ghi chú",
        "CustomFields" => "Trường tùy chỉnh",
        _ => field
    };

    private sealed record HistoryView(Guid Id, string ChangedAtText, string ChangedFieldsText)
    {
        public HistoryView(AccountRevisionEntry entry) : this(entry.Id,
            LocalizationAuditService.FormatDateTime(entry.ChangedAtUtc, includeSeconds: true),
            string.Join(", ", entry.ChangedFields.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(FieldLabel).Select(LanguageService.Translate))) { }
    }
}
