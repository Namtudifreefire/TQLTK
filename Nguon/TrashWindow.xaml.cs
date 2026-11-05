using System.Windows;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public sealed class TrashItemRow
{
    public Guid AccountId { get; init; }
    public Guid? NoteId { get; init; }
    public bool IsAccount { get; init; }
    public string TypeLabel => IsAccount ? "Tài khoản" : "Ghi chú";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public DateTime DeletedAt { get; init; }
    public int DaysRemaining => Math.Max(0, AccountVaultService.TrashRetentionDays -
        (int)Math.Floor((DateTime.UtcNow - DeletedAt.ToUniversalTime()).TotalDays));
}

public partial class TrashWindow : Window
{
    private readonly Func<TrashItemRow, Task<bool>> _restore;
    private readonly Func<TrashItemRow, Task<bool>> _deleteForever;
    private readonly Func<IEnumerable<TrashItemRow>> _refreshItems;
    private List<TrashItemRow> _items = new();

    public TrashWindow(IEnumerable<TrashItemRow> items,
        Func<TrashItemRow, Task<bool>> restore,
        Func<TrashItemRow, Task<bool>> deleteForever,
        Func<IEnumerable<TrashItemRow>> refreshItems)
    {
        InitializeComponent();
        _items = items.ToList();
        _restore = restore;
        _deleteForever = deleteForever;
        _refreshItems = refreshItems;
        ReloadItems(items);
    }

    private void ReloadItems(IEnumerable<TrashItemRow>? items = null)
    {
        _items = (items ?? _refreshItems()).ToList();
        dgTrash.ItemsSource = null;
        dgTrash.ItemsSource = _items.OrderByDescending(item => item.DeletedAt).ToList();
        lblEmpty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        dgTrash.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        if (dgTrash.SelectedItem is not TrashItemRow item)
        {
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, "Chọn một mục để khôi phục.", "Thùng rác", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (await _restore(item))
        {
            ReloadItems();
        }
    }

    private async void BtnDeleteForever_Click(object sender, RoutedEventArgs e)
    {
        if (dgTrash.SelectedItem is not TrashItemRow item)
        {
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, "Chọn một mục để xóa hẳn.", "Thùng rác", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (PersonalInfoManager.Services.LocalizedMessageBox.Show(this, $"Xóa vĩnh viễn '{item.Title}'? Không thể hoàn tác.", "Xóa hẳn",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (await _deleteForever(item))
        {
            ReloadItems();
        }
    }
}
