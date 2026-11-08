using System.Collections.Generic;
using System.Windows;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class BackupRestorePreviewWindow : Window
    {
        public BackupRestorePreviewWindow(
            string title,
            string summaryTemplate,
            Func<object?[]> summaryArguments,
            IEnumerable<BackupPreviewItem> items,
            bool allowSettingsRestore = false)
        {
            InitializeComponent();
            Title = title;
            txtTitle.Text = title;
            LanguageService.SetLocalizedFormattedText(txtSummary, summaryTemplate, summaryArguments);
            dgItems.ItemsSource = items;
            chkRestoreSettings.Visibility = allowSettingsRestore ? Visibility.Visible : Visibility.Collapsed;
            Loaded += (_, _) => LanguageService.ApplyToWindow(this);
        }

        public bool RestoreSettingsRequested => chkRestoreSettings.IsChecked == true;

        private void BtnContinue_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
