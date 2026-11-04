using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PersonalInfoManager.Services;
namespace PersonalInfoManager
{
    public sealed class SecurityFindingReviewWindow : Window
    {
        private sealed record Choice(string Label, SecurityFindingReviewStatus Status, int SnoozeDays);
        private readonly ComboBox _status = new();
        private readonly TextBox _reason = new();
        private readonly List<Choice> _choices;
        public SecurityFindingReviewWindow(string description, SecurityFindingReviewState current)
        {
            Title = LanguageService.Translate("Trạng thái cảnh báo bảo mật");
            Width = 500;
            Height = 410;
            MinWidth = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            SetResourceReference(BackgroundProperty, "BackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
            _choices = new List<Choice>
            {
                new(LanguageService.Translate("Đang mở"), SecurityFindingReviewStatus.Open, 0),
                new(LanguageService.Translate("Đã xử lý"), SecurityFindingReviewStatus.Fixed, 0),
                new(LanguageService.Translate("Nhắc lại sau 1 ngày"), SecurityFindingReviewStatus.Snoozed, 1),
                new(LanguageService.Translate("Nhắc lại sau 7 ngày"), SecurityFindingReviewStatus.Snoozed, 7),
                new(LanguageService.Translate("Nhắc lại sau 30 ngày"), SecurityFindingReviewStatus.Snoozed, 30),
                new(LanguageService.Translate("Bỏ qua có lý do"), SecurityFindingReviewStatus.Ignored, 0)
            };
            var content = new StackPanel { Margin = new Thickness(22) };
            content.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 15
            });
            content.Children.Add(new TextBlock
            {
                Text = string.Format(LanguageService.Translate("Lần kiểm tra gần nhất: {0}"),
                    current.LastCheckedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = TryFindResource("TextSecondaryBrush") as Brush
            });
            content.Children.Add(new TextBlock
            {
                Text = LanguageService.Translate("Trạng thái"),
                Margin = new Thickness(0, 0, 0, 5)
            });
            _status.ItemsSource = _choices;
            _status.DisplayMemberPath = nameof(Choice.Label);
            _status.SelectedItem = current.Status switch
            {
                SecurityFindingReviewStatus.Fixed => _choices[1],
                SecurityFindingReviewStatus.Snoozed => _choices.First(choice => choice.SnoozeDays == 7),
                SecurityFindingReviewStatus.Ignored => _choices[^1],
                _ => _choices[0]
            };
            content.Children.Add(_status);
            content.Children.Add(new TextBlock
            {
                Text = LanguageService.Translate("Lý do (bắt buộc khi bỏ qua)"),
                Margin = new Thickness(0, 12, 0, 5)
            });
            _reason.Text = current.Reason;
            _reason.AcceptsReturn = true;
            _reason.TextWrapping = TextWrapping.Wrap;
            _reason.Height = 75;
            _reason.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            content.Children.Add(_reason);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var save = new Button
            {
                Content = LanguageService.Translate("Lưu trạng thái"),
                MinWidth = 120,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 7, 12, 7)
            };
            if (TryFindResource("RowButton") is Style saveStyle) save.Style = saveStyle;
            save.Click += (_, _) =>
            {
                if (_status.SelectedItem is not Choice choice) return;
                if (choice.Status == SecurityFindingReviewStatus.Ignored && string.IsNullOrWhiteSpace(_reason.Text))
                {
                    MessageBox.Show(this, LanguageService.Translate("Hãy ghi lý do khi bỏ qua cảnh báo."),
                        LanguageService.Translate("Thiếu lý do"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
            };
            var cancel = new Button
            {
                Content = LanguageService.Translate("Hủy"),
                MinWidth = 90,
                Padding = new Thickness(12, 7, 12, 7),
                IsCancel = true
            };
            if (TryFindResource("RowButton") is Style cancelStyle) cancel.Style = cancelStyle;
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            content.Children.Add(buttons);
            Content = content;
        }
        public SecurityFindingReviewStatus SelectedStatus => ((Choice)_status.SelectedItem).Status;
        public DateTime? RemindAtUtc
        {
            get
            {
                var choice = (Choice)_status.SelectedItem;
                return choice.Status == SecurityFindingReviewStatus.Snoozed
                    ? DateTime.UtcNow.AddDays(choice.SnoozeDays)
                    : null;
            }
        }
        public string Reason => _reason.Text.Trim();
    }
}
