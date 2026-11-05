using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class DriveSharesWindow : Window
{
    private readonly List<DriveShareInfo> _shares;
    public DriveShareInfo? SelectedShare { get; private set; }

    public DriveSharesWindow(IEnumerable<DriveShareInfo> shares)
    {
        InitializeComponent();
        _shares = shares.ToList();
        dgShares.ItemsSource = _shares.Select(share => new ShareView(share,
            share.Expires?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Không đặt",
            share.Created?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Không rõ")).ToList();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => LanguageService.ApplyToWindow(this);
    private void BtnClose_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void BtnRevoke_Click(object sender, RoutedEventArgs e)
    {
        if (dgShares.SelectedItem is not ShareView selected) return;
        SelectedShare = _shares.FirstOrDefault(share => share.Id == selected.Id &&
            string.Equals(share.RecipientEmail, selected.RecipientEmail, StringComparison.OrdinalIgnoreCase));
        if (SelectedShare == null) return;
        DialogResult = true;
        Close();
    }

    private sealed record ShareView(string Id, string RecipientEmail, string ExpiresText, string CreatedText)
    {
        public ShareView(DriveShareInfo share, string expires, string created)
            : this(share.Id, share.RecipientEmail, expires, created) { }
    }
}
