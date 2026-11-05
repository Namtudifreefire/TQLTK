using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public sealed class KeyboardShortcutsWindow : Window
{
    private sealed record EditorRow(KeyboardShortcutAction Action, CheckBox Enabled, ComboBox Gesture);
    private readonly List<EditorRow> _rows = new();
    private readonly TextBlock _error;

    public KeyboardShortcutsWindow()
    {
        Title = LanguageService.Translate("Phím tắt");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinHeight = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BrushResource("BackgroundBrush", Brushes.White);

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = LanguageService.Translate("Phím tắt"),
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = BrushResource("TextPrimaryBrush", Brushes.Black),
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = LanguageService.Translate("Chọn phím cho từng thao tác. Thao tác bị tắt sẽ không phản hồi với phím tắt."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = BrushResource("TextSecondaryBrush", Brushes.Gray),
            Margin = new Thickness(0, 0, 0, 14)
        });

        foreach (KeyboardShortcutBinding binding in KeyboardShortcutService.GetBindings())
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var enabled = new CheckBox
            {
                Content = LanguageService.Translate(ActionLabel(binding.Action)),
                IsChecked = binding.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = BrushResource("TextPrimaryBrush", Brushes.Black)
            };
            var gesture = new ComboBox
            {
                Width = 170,
                Height = 34,
                ItemsSource = KeyboardShortcutService.GetOptions(binding.Action),
                SelectedItem = binding.Gesture,
                IsEnabled = binding.Enabled
            };
            enabled.Checked += (_, _) => gesture.IsEnabled = true;
            enabled.Unchecked += (_, _) => gesture.IsEnabled = false;
            Grid.SetColumn(gesture, 1);
            row.Children.Add(enabled);
            row.Children.Add(gesture);
            root.Children.Add(row);
            _rows.Add(new EditorRow(binding.Action, enabled, gesture));
        }

        _error = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 8)
        };
        root.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = LanguageService.Translate("Hủy"), IsCancel = true, MinWidth = 95, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = LanguageService.Translate("Lưu"), IsDefault = true, MinWidth = 95, Padding = new Thickness(14, 8, 14, 8) };
        if (Application.Current.TryFindResource("OutlineButton") is Style outlineStyle) cancel.Style = outlineStyle;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);
        Content = root;
        save.Click += Save_Click;
        Loaded += (_, _) => LanguageService.ApplyToWindow(this);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            KeyboardShortcutService.Save(_rows.Select(row => new KeyboardShortcutBinding(
                row.Action, row.Gesture.SelectedItem?.ToString() ?? "", row.Enabled.IsChecked == true)));
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _error.Text = LanguageService.Translate(ex.Message);
        }
    }

    private static string ActionLabel(KeyboardShortcutAction action) => action switch
    {
        KeyboardShortcutAction.LockVault => "Khóa vault",
        KeyboardShortcutAction.SearchAccounts => "Tìm kiếm tài khoản",
        KeyboardShortcutAction.CopyUsername => "Sao chép tên đăng nhập đã chọn",
        KeyboardShortcutAction.ShowTotp => "Hiện mã TOTP của tài khoản đã chọn",
        _ => action.ToString()
    };

    private Brush BrushResource(string key, Brush fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? fallback;
}
