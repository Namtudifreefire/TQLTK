using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PersonalInfoManager;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;
internal static class UiLayoutRegression
{
    internal static void Run(string username)
    {
        string? folderError = AutoBackupService.TrySetFolder(username, Path.Combine(ApplicationDataPath.Root, "ui-backups"));
        if (folderError != null) throw new IOException(folderError);
        if (Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        string originalLanguage = LanguageService.CurrentCode;
        try
        {
            foreach (string language in new[] { "vi", "en" })
            {
                LanguageService.SetLanguageForTesting(language);
                foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    var entry = new BackupComparisonEntry
                    {
                        Name = "Synthetic example account", Status = "Đã sửa", Details = "Synthetic account details",
                        Kind = BackupRecoveryItemKind.Account, OlderKey = "synthetic", NewerKey = "synthetic",
                        AvailableSources = new[] { LanguageService.Translate("Không khôi phục"), LanguageService.Translate("Bản sao lưu cũ"), LanguageService.Translate("Bản sao lưu mới") }
                    };
                    entry.SelectedSource = entry.AvailableSources[0];
                    Func<Window>[] createWindows =
                    [
                        () => new LoginWindow(),
                        () => new MigrationGuideWindow(),
                        () => new BackupCenterWindow(username),
                        () => new BackupComparisonWindow(LanguageService.Translate("Chọn mục cần lấy từ một trong hai bản. Ứng dụng sẽ thêm thành bản sao để giữ nguyên dữ liệu hiện tại."), new[] { entry }),
                        () => new BackupRestorePreviewWindow("Xem trước khôi phục", "{0}", () => new object?[] { "Synthetic backup preview" },
                            new[] { new BackupPreviewItem("Synthetic account", "Sẽ thêm", "synthetic@example.invalid") }, allowSettingsRestore: true),
                        () => new BackupPassphraseWindow(confirmMode: true),
                        () => new EditAccountWindow(new SavedAccount { ServiceName = "Synthetic service", Username = "synthetic@example.invalid" })
                    ];
                    foreach (Func<Window> createWindow in createWindows)
                    {
                        Window window = createWindow();
                        try
                        {
                            LanguageService.ApplyToWindow(window);
                            if (window is LoginWindow)
                            {
                                var footer = (TextBlock)window.FindName("txtCopyright");
                                foreach (string selectedLanguage in new[] { language, language == "en" ? "vi" : "en", language })
                                {
                                    LanguageService.SetLanguageForTesting(selectedLanguage);
                                    LanguageService.ApplyToWindow(window);
                                    string expected = LocalizationAuditService.FormatForLanguage("app.footer", selectedLanguage, AppVersion.Current);
                                    if (footer.Text != expected) throw new InvalidDataException("The login footer did not update when switching languages.");
                                }
                            }
                            FrameworkElement content = (FrameworkElement)window.Content;
                            double width = Math.Min(double.IsFinite(window.Width) ? window.Width : 900, 1920 / scale * 0.96) - 18;
                            double height = Math.Min(double.IsFinite(window.Height) ? window.Height : 700, 1080 / scale * 0.94) - 42;
                            content.Measure(new Size(width, height));
                            content.Arrange(new Rect(0, 0, width, height));
                            content.UpdateLayout();
                            foreach (Button button in Descendants(content).OfType<Button>().Where(button => button.IsVisible || button.Visibility == Visibility.Visible))
                            {
                                if (InsideScrollableArea(button, content)) continue;
                                Rect bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
                                if (bounds.Width <= 0 || bounds.Height < 28 || bounds.Left < -1 || bounds.Top < -1 ||
                                    bounds.Right > content.RenderSize.Width + 1 || bounds.Bottom > content.RenderSize.Height + 1)
                                    throw new InvalidDataException($"An action button is clipped in {window.GetType().Name} ({language}, {scale:P0}): {button.Content}; {bounds} / {content.RenderSize}.");
                            }
                            foreach (DataGridCell cell in Descendants(content).OfType<DataGridCell>())
                                foreach (ComboBox combo in Descendants(cell).OfType<ComboBox>())
                                {
                                    Rect bounds = combo.TransformToAncestor(cell).TransformBounds(new Rect(combo.RenderSize));
                                    if (bounds.Top < -1 || bounds.Bottom > cell.RenderSize.Height + 1)
                                        throw new InvalidDataException($"The restore-source selector is clipped inside its table row ({language}, {scale:P0}): {bounds} / {cell.RenderSize}.");
                                }
                            if (scale == 2 && language == "en" && window is BackupComparisonWindow or EditAccountWindow)
                                SavePreview(content, window.Background, window.GetType().Name, language, scale);
                        }
                        finally
                        {
                            if (window is EditAccountWindow editor) editor.CloseForSessionEnd(); else window.Close();
                        }
                    }
                }
            }
            VerifyDraftPreservedOnSessionEnd();
        }
        finally { LanguageService.SetLanguageForTesting(originalLanguage); }
    }
    private static void VerifyDraftPreservedOnSessionEnd()
    {
        var saved = new SavedAccount { ServiceName = "Synthetic draft close test", Username = "synthetic@example.invalid" };
        var editor = new EditAccountWindow(saved);
        ((CheckBox)editor.FindName("chkEncryptedDraft")).IsChecked = true;
        ((TextBox)editor.FindName("txtService")).Text = "Unsaved synthetic change";
        editor.CloseForSessionEnd();
        string scope = SessionService.CurrentUser?.Username ?? Environment.UserName;
        if (!EditAccountDraftService.TryLoad(scope, saved.Id, out EditAccountDraft? draft, out string? error) ||
            draft?.ServiceName != "Unsaved synthetic change")
            throw new InvalidDataException("The editor did not preserve its opted-in draft when the session ended: " + error);
        EditAccountDraftService.Delete(scope, saved.Id);
    }
    private static bool InsideScrollableArea(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(element); current != null && current != root; current = VisualTreeHelper.GetParent(current))
            if (current is ScrollViewer) return true;
        return false;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void SavePreview(FrameworkElement content, Brush background, string name, string language, double scale)
    {
        string folder = Path.Combine(Directory.GetCurrentDirectory(), "obj", "ui-layout-previews");
        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.RenderSize.Width * scale),
            (int)Math.Ceiling(content.RenderSize.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            var bounds = new Rect(content.RenderSize);
            drawing.DrawRectangle(background, null, bounds);
            drawing.DrawRectangle(new VisualBrush(content), null, bounds);
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(folder, name + "-" + language + "-200.png"));
        encoder.Save(output);
    }
}
