using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public enum CsvImportAction
{
    Skip,
    ImportNew,
    Overwrite,
    ImportCopy
}

public sealed record CsvImportDecision(SavedAccount Account, CsvImportAction Action, Guid? TargetAccountId, bool IncludeAttachments);
public sealed record CsvImportActionOption(CsvImportAction Action, string Label);

public partial class CsvImportWindow : Window
{
    public ObservableCollection<CsvPreviewItem> Items { get; } = new();
    public Guid BatchId { get; } = Guid.NewGuid();
    private readonly bool _includeAttachments;
    public List<CsvImportDecision> Decisions => Items
        .Select(item => new CsvImportDecision(item.Account.Clone(), item.Action, item.TargetAccountId, _includeAttachments))
        .ToList();

    public CsvImportWindow(List<SavedAccount> imported, List<SavedAccount> existing, bool includeAttachments = false, string sourceFormat = "CSV")
    {
        InitializeComponent();
        _includeAttachments = includeAttachments;
        Title = LanguageService.Format("Xem trước nhập {0}", LanguageService.Translate(sourceFormat));
        var targets = new Dictionary<string, (Guid Id, string Description)>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in existing.Where(account => !account.IsDeleted))
            targets.TryAdd(BuildKey(account), (account.Id, $"{account.ServiceName} — {account.Username}"));

        foreach (var incoming in imported)
        {
            string key = BuildKey(incoming);
            bool duplicate = targets.TryGetValue(key, out var target);
            var item = new CsvPreviewItem(incoming.Clone(), duplicate, duplicate ? target.Id : null,
                duplicate ? target.Description : "—");
            item.PropertyChanged += (_, _) => UpdateCounts();
            Items.Add(item);
            if (!duplicate) targets.TryAdd(key, (incoming.Id, $"{incoming.ServiceName} — {incoming.Username}"));
        }

        dgPreview.ItemsSource = Items;
        lblInfo.Text = LanguageService.Translate("Xem lại từng mục trước khi nhập. Mục trùng mặc định được bỏ qua; có thể chọn ghi đè hoặc tạo bản sao cho từng mục.");
        lblTotal.Text = LanguageService.Format("Tổng: {0}", imported.Count);
        UpdateCounts();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => LanguageService.ApplyToWindow(this);

    private void UpdateCounts()
    {
        int newCount = Items.Count(item => item.Action is CsvImportAction.ImportNew or CsvImportAction.ImportCopy);
        int overwriteCount = Items.Count(item => item.Action == CsvImportAction.Overwrite);
        int duplicateCount = Items.Count(item => item.IsDuplicate);
        lblDuplicates.Text = LanguageService.Format("Bản trùng: {0}", duplicateCount);
        lblWillImport.Text = LanguageService.Format("Sẽ thêm: {0}", newCount);
        lblWillOverwrite.Text = LanguageService.Format("Sẽ ghi đè: {0}", overwriteCount);
    }

    private void BtnSelectNew_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items.Where(item => !item.IsDuplicate)) item.Action = CsvImportAction.ImportNew;
        dgPreview.Items.Refresh();
        UpdateCounts();
    }

    private void BtnSkipDuplicates_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items.Where(item => item.IsDuplicate)) item.Action = CsvImportAction.Skip;
        dgPreview.Items.Refresh();
        UpdateCounts();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (!Decisions.Any(decision => decision.Action != CsvImportAction.Skip))
        {
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate("Bạn chưa chọn tài khoản nào để nhập."),
                LanguageService.Translate("Thông báo"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private static string BuildKey(SavedAccount account) =>
        (account.ServiceName ?? "").Trim() + "\u001f" + (account.Username ?? "").Trim();
}

public sealed class CsvPreviewItem : INotifyPropertyChanged
{
    private CsvImportAction _action;
    public SavedAccount Account { get; }
    public bool IsDuplicate { get; }
    public Guid? TargetAccountId { get; }
    public string TargetDescription { get; }
    public string ServiceName => Account.ServiceName;
    public string Username => Account.Username;
    public string MaskedPassword => new string('•', Math.Min(8, Account.Password?.Length ?? 0));
    public string Note => Account.Note;
    public List<CsvImportActionOption> ActionOptions { get; }

    public CsvImportAction Action
    {
        get => _action;
        set
        {
            if (_action == value) return;
            _action = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CsvPreviewItem(SavedAccount account, bool isDuplicate, Guid? targetAccountId, string targetDescription)
    {
        Account = account;
        IsDuplicate = isDuplicate;
        TargetAccountId = targetAccountId;
        TargetDescription = targetDescription;
        ActionOptions = isDuplicate
            ? new() { Option(CsvImportAction.Skip), Option(CsvImportAction.Overwrite), Option(CsvImportAction.ImportCopy) }
            : new() { Option(CsvImportAction.ImportNew), Option(CsvImportAction.Skip) };
        _action = isDuplicate ? CsvImportAction.Skip : CsvImportAction.ImportNew;
    }

    private static CsvImportActionOption Option(CsvImportAction action) => new(action, LanguageService.Translate(action switch
    {
        CsvImportAction.Skip => "Bỏ qua",
        CsvImportAction.ImportNew => "Nhập mới",
        CsvImportAction.Overwrite => "Ghi đè",
        CsvImportAction.ImportCopy => "Tạo bản sao",
        _ => "Bỏ qua"
    }));

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
