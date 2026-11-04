using Microsoft.Win32;
namespace PersonalInfoManager.Services;
public static class LocalizedFileDialog
{
    public static void Apply(FileDialog dialog)
    {
        if (!string.IsNullOrWhiteSpace(dialog.Title))
            dialog.Title = LanguageService.Translate(dialog.Title);
        if (!string.IsNullOrWhiteSpace(dialog.Filter))
            dialog.Filter = LanguageService.Translate(dialog.Filter);
    }
}
