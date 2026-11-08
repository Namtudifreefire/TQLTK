using System.Windows;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class MigrationGuideWindow : Window
{
    public MigrationGuideWindow()
    {
        InitializeComponent();
        LanguageService.SetLocalizedKey(txtGuide, "backup.migration.guide");
    }
}
