using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;

namespace PersonalInfoManager.Services;

public static class LanguageService
{
    private sealed class ObservedValue
    {
        public string? Source { get; set; }
        public string? Rendered { get; set; }
        public bool IsUpdating { get; set; }
    }

    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, ObservedValue>> Observed = new();
    private static string _currentCode = RegistryService.GetLanguage();

    public static string CurrentCode => _currentCode;

    public static bool IsEnglish => _currentCode == "en";

    public static string GetDisplayName(string code) => code == "en" ? "English" : "Tiếng Việt";

    public static void SetLanguage(string code)
    {
        string normalized = code.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
        if (_currentCode == normalized) return;
        _currentCode = normalized;
        RegistryService.SetLanguage(normalized);
        if (Application.Current == null) return;
        foreach (Window window in Application.Current.Windows)
            ApplyToWindow(window);
    }

    public static string Translate(string value)
    {
        return Translate(value, _currentCode);
    }

    public static string TranslateLogContent(string content)
    {
        if (!IsEnglish || string.IsNullOrEmpty(content)) return content;

        string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        return string.Join(Environment.NewLine, normalized.Split('\n').Select(TranslateLogLine));
    }

    private static string TranslateLogLine(string line)
    {
        int timestampEnd = line.IndexOf("] [", 1, StringComparison.Ordinal);
        if (timestampEnd != 20) return Translate(line);

        int usernameEnd = line.IndexOf("] [", timestampEnd + 3, StringComparison.Ordinal);
        if (usernameEnd < 0) return Translate(line);

        int actionEnd = line.IndexOf("] ", usernameEnd + 3, StringComparison.Ordinal);
        if (actionEnd < 0) return Translate(line);

        int detailStart = actionEnd + 2;
        return line[..detailStart] + Translate(line[detailStart..]);
    }

    public static string Translate(string value, string languageCode)
    {
        if (!string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(value)) return value;
        if (EnglishCatalog.Entries.TryGetValue(value, out string? translated)) return translated;

        int start = 0;
        while (start < value.Length && char.IsWhiteSpace(value[start])) start++;
        int end = value.Length;
        while (end > start && char.IsWhiteSpace(value[end - 1])) end--;
        if (start > 0 || end < value.Length)
        {
            string content = value[start..end];
            if (EnglishCatalog.Entries.TryGetValue(content, out translated))
                return string.Concat(value.AsSpan(0, start), translated, value.AsSpan(end));
        }

        string result = ProtectQuotedValues(value, out List<(string Token, string Value)> quotedValues);
        foreach (var pair in EnglishCatalog.Fragments.OrderByDescending(pair => pair.Key.Length))
        {
            if (pair.Key.Length == 0) continue;
            if (pair.Key is " mục" or " phút" or " phút.")
            {
                result = ReplaceAfterNumber(result, pair.Key, pair.Value);
                continue;
            }
            if (char.IsWhiteSpace(pair.Key[0]))
            {
                if (result.Contains(pair.Key, StringComparison.Ordinal))
                    result = result.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
            }
            else if (result.StartsWith(pair.Key, StringComparison.Ordinal))
            {
                result = pair.Value + result[pair.Key.Length..];
            }
            else if (result.EndsWith(pair.Key, StringComparison.Ordinal))
            {
                result = result[..^pair.Key.Length] + pair.Value;
            }
            else if (char.IsPunctuation(pair.Key[0]) && result.Contains(pair.Key, StringComparison.Ordinal))
            {
                result = result.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
            }
        }
        foreach ((string token, string quotedValue) in quotedValues)
            result = result.Replace(token, quotedValue, StringComparison.Ordinal);
        return result;
    }

    private static string ProtectQuotedValues(string value, out List<(string Token, string Value)> quotedValues)
    {
        quotedValues = new List<(string Token, string Value)>();
        StringBuilder result = new(value.Length);
        int position = 0;
        while (position < value.Length)
        {
            if (value[position] != '\'')
            {
                result.Append(value[position++]);
                continue;
            }

            int end = position + 1;
            while (end < value.Length)
            {
                end = value.IndexOf('\'', end);
                if (end < 0) break;
                if (end + 1 == value.Length || !char.IsLetterOrDigit(value[end + 1])) break;
                end++;
            }

            if (end < 0)
            {
                result.Append(value.AsSpan(position));
                break;
            }

            result.Append('\'');
            string content = value[(position + 1)..end];
            bool hasLeadingOrTrailingWhitespace = content.Length > 0 &&
                (char.IsWhiteSpace(content[0]) || char.IsWhiteSpace(content[^1]));
            if (content.Length > 0 && !hasLeadingOrTrailingWhitespace && HasVietnameseCharacters(content))
            {
                string token = $"\uE000{quotedValues.Count}\uE001";
                quotedValues.Add((token, content));
                result.Append(token);
            }
            else
            {
                result.Append(content);
            }
            result.Append('\'');
            position = end + 1;
        }

        return result.ToString();
    }

    private static bool HasVietnameseCharacters(string value) => value.Any(character =>
        character is >= '\u00C0' and <= '\u024F' or >= '\u1E00' and <= '\u1EFF');

    private static string ReplaceAfterNumber(string value, string fragment, string replacement)
    {
        StringBuilder? result = null;
        int copiedThrough = 0;
        int searchFrom = 0;
        while (searchFrom < value.Length)
        {
            int index = value.IndexOf(fragment, searchFrom, StringComparison.Ordinal);
            if (index < 0) break;
            if (index > 0 && char.IsDigit(value[index - 1]))
            {
                result ??= new StringBuilder(value.Length + replacement.Length);
                result.Append(value, copiedThrough, index - copiedThrough);
                result.Append(replacement);
                copiedThrough = index + fragment.Length;
                searchFrom = copiedThrough;
            }
            else
            {
                searchFrom = index + 1;
            }
        }
        if (result == null) return value;
        result.Append(value, copiedThrough, value.Length - copiedThrough);
        return result.ToString();
    }

    public static void HandleWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window) ApplyToWindow(window);
    }

    public static void ApplyToWindow(Window window)
    {
        Visit(window, new HashSet<DependencyObject>());
    }

    private static void Visit(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element)) return;

        if (element is Window window)
            Observe(window, Window.TitleProperty, value => ((Window)value).Title, (value, text) => value.SetCurrentValue(Window.TitleProperty, text ?? ""));
        if (element is TextBlock textBlock)
        {
            if (textBlock.Inlines.Count == 0 && !IsUserDataTextBlock(textBlock))
                Observe(textBlock, TextBlock.TextProperty, value => ((TextBlock)value).Text, (value, text) => value.SetCurrentValue(TextBlock.TextProperty, text ?? ""));
            Inline[] inlines = textBlock.Inlines.ToArray();
            foreach (Inline inline in inlines)
                if (inline is Run run)
                    Observe(run, Run.TextProperty, value => ((Run)value).Text, (value, text) => value.SetCurrentValue(Run.TextProperty, text ?? ""));
        }
        if (element is HeaderedContentControl headeredContent)
            Observe(headeredContent, HeaderedContentControl.HeaderProperty, value => ((HeaderedContentControl)value).Header as string,
                (value, text) => value.SetCurrentValue(HeaderedContentControl.HeaderProperty, text));
        if (element is HeaderedItemsControl headeredItems)
            Observe(headeredItems, HeaderedItemsControl.HeaderProperty, value => ((HeaderedItemsControl)value).Header as string,
                (value, text) => value.SetCurrentValue(HeaderedItemsControl.HeaderProperty, text));
        if (element is ContentControl contentControl)
            Observe(contentControl, ContentControl.ContentProperty, value => ((ContentControl)value).Content as string,
                (value, text) => value.SetCurrentValue(ContentControl.ContentProperty, text));
        if (element is DataGrid grid)
            foreach (DataGridColumn column in grid.Columns)
                Observe(column, DataGridColumn.HeaderProperty, value => ((DataGridColumn)value).Header as string,
                    (value, text) => value.SetCurrentValue(DataGridColumn.HeaderProperty, text));
        if (element is FrameworkElement frameworkElement)
        {
            Observe(frameworkElement, FrameworkElement.ToolTipProperty,
                value => ((FrameworkElement)value).ToolTip as string,
                (value, text) => value.SetCurrentValue(FrameworkElement.ToolTipProperty, text));
            if (frameworkElement.ToolTip is DependencyObject toolTip) Visit(toolTip, visited);
            if (frameworkElement.ContextMenu is DependencyObject contextMenu) Visit(contextMenu, visited);
        }

        foreach (object child in LogicalTreeHelper.GetChildren(element))
            if (child is DependencyObject dependencyChild) Visit(dependencyChild, visited);
    }

    private static void Observe(
        DependencyObject target,
        DependencyProperty property,
        Func<DependencyObject, string?> getter,
        Action<DependencyObject, string?> setter)
    {
        if (BindingOperations.IsDataBound(target, property)) return;

        Dictionary<DependencyProperty, ObservedValue> values = Observed.GetValue(target, _ => new());
        if (!values.TryGetValue(property, out ObservedValue? observation))
        {
            string? current = getter(target);
            observation = new ObservedValue { Source = current, Rendered = current };
            values[property] = observation;

            DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, target.GetType());
            if (descriptor != null)
            {
                var weakTarget = new WeakReference<DependencyObject>(target);
                descriptor.AddValueChanged(target, (_, _) =>
                {
                    if (weakTarget.TryGetTarget(out DependencyObject? liveTarget))
                        OnValueChanged(liveTarget, getter, setter, observation);
                });
            }
        }
        else
        {
            string? current = getter(target);
            if (!observation.IsUpdating && !string.Equals(current, observation.Rendered, StringComparison.Ordinal))
                observation.Source = current;
        }

        Render(target, getter, setter, observation);
    }

    private static void OnValueChanged(
        DependencyObject target,
        Func<DependencyObject, string?> getter,
        Action<DependencyObject, string?> setter,
        ObservedValue observation)
    {
        if (observation.IsUpdating) return;
        string? current = getter(target);
        if (string.Equals(current, observation.Rendered, StringComparison.Ordinal)) return;
        observation.Source = current;
        Render(target, getter, setter, observation);
    }

    private static void Render(
        DependencyObject target,
        Func<DependencyObject, string?> getter,
        Action<DependencyObject, string?> setter,
        ObservedValue observation)
    {
        string? translated = observation.Source == null ? null : Translate(observation.Source);
        observation.Rendered = translated;
        if (translated == null || string.Equals(getter(target), translated, StringComparison.Ordinal)) return;
        observation.IsUpdating = true;
        try { setter(target, translated); }
        finally { observation.IsUpdating = false; }
    }

    private static bool IsUserDataTextBlock(TextBlock textBlock) => textBlock.Name is
        "lblAvatar" or "lblUserName" or "lblMenuEmail" or "lblProfileCreatedAt" or "lblAccount";
}
