using System.Collections.Generic;
using System.Windows;

namespace PersonalInfoManager
{
    public partial class BackupRestorePreviewWindow : Window
    {
        public BackupRestorePreviewWindow(string title, string summary, IEnumerable<BackupPreviewItem> items)
        {
            InitializeComponent();
            Title = title;
            txtTitle.Text = title;
            txtSummary.Text = summary;
            dgItems.ItemsSource = items;
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
