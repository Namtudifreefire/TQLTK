using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PersonalInfoManager.Services
{

    public static class ThemeService
    {
        private const string LightThemePath = "Themes/LightTheme.xaml";
        private const string DarkThemePath  = "Themes/DarkTheme.xaml";

        public static bool IsDarkMode { get; private set; } = false;

        public static void Initialize()
        {
            string saved = RegistryService.GetTheme();

            if (string.Equals(saved, "Dark", StringComparison.OrdinalIgnoreCase))
                ApplyDark(save: false);
            else
                ApplyLight(save: false);
        }

        public static void Toggle()
        {
            if (IsDarkMode) ApplyLight();
            else ApplyDark();
        }

        public static void ApplyLight(bool save = true)
        {
            SwapTheme(LightThemePath);
            IsDarkMode = false;
            if (save) { try { RegistryService.SetTheme("Light"); } catch { } }
            RefreshAllWindows();
        }

        public static void ApplyDark(bool save = true)
        {
            SwapTheme(DarkThemePath);
            IsDarkMode = true;
            if (save) { try { RegistryService.SetTheme("Dark"); } catch { } }
            RefreshAllWindows();
        }

        private static void SwapTheme(string newThemePath)
        {
            var app = Application.Current;
            if (app == null) return;

            var dicts = app.Resources.MergedDictionaries;
            if (dicts == null) return;

            var newDict = new ResourceDictionary
            {
                Source = new Uri(newThemePath, UriKind.Relative)
            };

            var oldTheme = dicts.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.EndsWith("LightTheme.xaml",
                    StringComparison.OrdinalIgnoreCase) ||
                 d.Source.OriginalString.EndsWith("DarkTheme.xaml",
                    StringComparison.OrdinalIgnoreCase)));

            if (oldTheme != null)
                dicts.Remove(oldTheme);

            dicts.Insert(0, newDict);
        }

        private static void RefreshAllWindows()
        {
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    RefreshWindow(w);
                }
            }
            catch { }
        }

        private static void RefreshWindow(Window w)
        {
            try
            {

                if (w is FrameworkElement fe)
                {

                    fe.InvalidateVisual();
                    fe.UpdateLayout();
                }

                foreach (var child in FindVisualChildren<FrameworkElement>(w))
                {
                    try
                    {

                        child.InvalidateVisual();

                        if (child is System.Windows.Controls.Control ctrl)
                        {
                            ctrl.InvalidateProperty(System.Windows.Controls.Control.BackgroundProperty);
                            ctrl.InvalidateProperty(System.Windows.Controls.Control.ForegroundProperty);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj)
            where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t) yield return t;

                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }
    }
}