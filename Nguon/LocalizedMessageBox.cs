using System.Windows;

namespace PersonalInfoManager.Services;

internal static class LocalizedMessageBox
{
    public static MessageBoxResult Show(
        string messageBoxText,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None,
        MessageBoxOptions options = MessageBoxOptions.None) =>
        System.Windows.MessageBox.Show(
            LanguageService.Translate(messageBoxText),
            LanguageService.Translate(caption), button, icon, defaultResult, options);

    public static MessageBoxResult Show(
        Window? owner,
        string messageBoxText,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None,
        MessageBoxOptions options = MessageBoxOptions.None)
    {
        string text = LanguageService.Translate(messageBoxText);
        string title = LanguageService.Translate(caption);
        return owner == null
            ? System.Windows.MessageBox.Show(text, title, button, icon, defaultResult, options)
            : System.Windows.MessageBox.Show(owner, text, title, button, icon, defaultResult, options);
    }
}
