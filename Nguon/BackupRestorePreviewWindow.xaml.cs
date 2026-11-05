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
            IEnumerable<BackupPreviewItem> items)
        {
            InitializeComponent();
            Title = title;
            txtTitle.Text = title;
            LanguageService.SetLocalizedFormattedText(txtSummary, summaryTemplate, summaryArguments);
            dgItems.ItemsSource = items;
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
