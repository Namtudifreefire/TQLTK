using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PersonalInfoManager.Models;
namespace PersonalInfoManager;
public partial class CustomFieldsWindow : Window
{
    private readonly ObservableCollection<AccountCustomField> _fields = new();
    private AccountCustomField? _selected;
    private bool _loading;
    public List<AccountCustomField> Result { get; private set; } = new();
    public CustomFieldsWindow(IEnumerable<AccountCustomField>? fields)
    {
        InitializeComponent();
        foreach (var field in (fields ?? Enumerable.Empty<AccountCustomField>()).Where(field => field != null))
            _fields.Add(field.Clone());
        lstFields.ItemsSource = _fields;
        if (_fields.Count > 0) lstFields.SelectedIndex = 0;
        RefreshEditor();
    }
    private void Fields_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitEditor();
        _selected = lstFields.SelectedItem as AccountCustomField;
        RefreshEditor();
    }
    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        CommitEditor();
        var field = new AccountCustomField { Name = "Trường mới" };
        _fields.Add(field);
        lstFields.SelectedItem = field;
        txtName.Focus();
        txtName.SelectAll();
    }
    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        int index = _fields.IndexOf(_selected);
        _fields.Remove(_selected);
        lstFields.SelectedIndex = _fields.Count == 0 ? -1 : Math.Min(index, _fields.Count - 1);
    }
    private void TxtName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _selected == null) return;
        _selected.Name = txtName.Text;
        int index = lstFields.SelectedIndex;
        lstFields.Items.Refresh();
        lstFields.SelectedIndex = index;
    }
    private void TxtValue_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && _selected != null && !_selected.IsSensitive)
            _selected.Value = txtValue.Text;
    }
    private void PwdValue_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading && _selected != null && _selected.IsSensitive)
            _selected.Value = pwdValue.Password;
    }
    private void Sensitive_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _selected == null) return;
        CommitEditor();
        _selected.IsSensitive = chkSensitive.IsChecked == true;
        RefreshEditor();
    }
    private void ShowValue_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _selected == null || !_selected.IsSensitive) return;
        CommitEditor();
        RefreshValueEditor();
    }
    private void RefreshEditor()
    {
        _loading = true;
        bool enabled = _selected != null;
        txtName.IsEnabled = enabled;
        txtValue.IsEnabled = enabled;
        pwdValue.IsEnabled = enabled;
        chkSensitive.IsEnabled = enabled;
        chkShowValue.IsEnabled = enabled && _selected!.IsSensitive;
        chkShowValue.IsChecked = false;
        txtName.Text = _selected?.Name ?? "";
        chkSensitive.IsChecked = _selected?.IsSensitive == true;
        RefreshValueEditor();
        _loading = false;
    }
    private void RefreshValueEditor()
    {
        if (_selected == null)
        {
            txtValue.Text = "";
            pwdValue.Password = "";
            txtValue.Visibility = Visibility.Visible;
            pwdValue.Visibility = Visibility.Collapsed;
            return;
        }
        if (_selected.IsSensitive && chkShowValue.IsChecked != true)
        {
            txtValue.Visibility = Visibility.Collapsed;
            pwdValue.Visibility = Visibility.Visible;
            pwdValue.Password = _selected.Value;
        }
        else
        {
            pwdValue.Visibility = Visibility.Collapsed;
            txtValue.Visibility = Visibility.Visible;
            txtValue.Text = _selected.Value;
        }
    }
    private void CommitEditor()
    {
        if (_selected == null) return;
        _selected.Name = txtName.Text.Trim();
        if (_selected.IsSensitive && pwdValue.Visibility == Visibility.Visible)
            _selected.Value = pwdValue.Password;
        else
            _selected.Value = txtValue.Text;
    }
    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        CommitEditor();
        if (_fields.Any(field => string.IsNullOrWhiteSpace(field.Name)))
        {
            MessageBox.Show(this, "Mỗi trường tùy chỉnh cần có tên.", "Thiếu tên trường",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_fields.GroupBy(field => field.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            MessageBox.Show(this, "Tên trường tùy chỉnh không được trùng nhau.", "Tên bị trùng",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = _fields.Select(field => field.Clone()).ToList();
        DialogResult = true;
    }
}
