using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;

namespace PersonalInfoManager.Services;

public enum KeyboardShortcutAction
{
    LockVault,
    SearchAccounts,
    CopyUsername,
    ShowTotp
}

public sealed record KeyboardShortcutBinding(KeyboardShortcutAction Action, string Gesture, bool Enabled);

public static class KeyboardShortcutService
{
    private const string RegistryValue = "KeyboardShortcutsJson";
    private static readonly object Sync = new();
    private static IReadOnlyList<KeyboardShortcutBinding>? _cached;

    private static readonly IReadOnlyDictionary<KeyboardShortcutAction, string[]> Options =
        new Dictionary<KeyboardShortcutAction, string[]>
        {
            [KeyboardShortcutAction.LockVault] = new[] { "Ctrl+L", "Ctrl+Shift+L" },
            [KeyboardShortcutAction.SearchAccounts] = new[] { "Ctrl+F", "Ctrl+Shift+F" },
            [KeyboardShortcutAction.CopyUsername] = new[] { "Ctrl+Shift+U", "Ctrl+Alt+U" },
            [KeyboardShortcutAction.ShowTotp] = new[] { "Ctrl+Shift+T", "Ctrl+Alt+T" }
        };

    private static readonly IReadOnlyDictionary<string, KeyGesture> Gestures = new Dictionary<string, KeyGesture>(StringComparer.Ordinal)
    {
        ["Ctrl+L"] = new(Key.L, ModifierKeys.Control),
        ["Ctrl+Shift+L"] = new(Key.L, ModifierKeys.Control | ModifierKeys.Shift),
        ["Ctrl+F"] = new(Key.F, ModifierKeys.Control),
        ["Ctrl+Shift+F"] = new(Key.F, ModifierKeys.Control | ModifierKeys.Shift),
        ["Ctrl+Shift+U"] = new(Key.U, ModifierKeys.Control | ModifierKeys.Shift),
        ["Ctrl+Alt+U"] = new(Key.U, ModifierKeys.Control | ModifierKeys.Alt),
        ["Ctrl+Shift+T"] = new(Key.T, ModifierKeys.Control | ModifierKeys.Shift),
        ["Ctrl+Alt+T"] = new(Key.T, ModifierKeys.Control | ModifierKeys.Alt)
    };

    public static IReadOnlyList<KeyboardShortcutBinding> GetBindings()
    {
        lock (Sync)
        {
            if (_cached != null) return _cached.ToList();
            List<KeyboardShortcutBinding> defaults = CreateDefaults();
            try
            {
                string? json = ReadStoredJson();
                List<KeyboardShortcutBinding>? loaded = string.IsNullOrWhiteSpace(json) || json.Length > 16 * 1024
                    ? null
                    : JsonSerializer.Deserialize<List<KeyboardShortcutBinding>>(json);
                _cached = IsValid(loaded, out _) ? Normalize(loaded!) : defaults;
            }
            catch { _cached = defaults; }
            return _cached.ToList();
        }
    }

    public static IReadOnlyList<string> GetOptions(KeyboardShortcutAction action) => Options[action].ToArray();

    public static void Save(IEnumerable<KeyboardShortcutBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        List<KeyboardShortcutBinding> requested = bindings.ToList();
        if (!IsValid(requested, out string error)) throw new ArgumentException(error, nameof(bindings));
        List<KeyboardShortcutBinding> normalized = Normalize(requested);

        string json = JsonSerializer.Serialize(normalized);
        WriteStoredJson(json);
        lock (Sync) _cached = normalized;
    }

    public static bool TryMatch(Key key, ModifierKeys modifiers, out KeyboardShortcutAction action)
    {
        foreach (KeyboardShortcutBinding binding in GetBindings())
        {
            if (!binding.Enabled || !Gestures.TryGetValue(binding.Gesture, out KeyGesture? gesture)) continue;
            if (gesture.Key == key && gesture.Modifiers == modifiers)
            {
                action = binding.Action;
                return true;
            }
        }
        action = default;
        return false;
    }

    private static List<KeyboardShortcutBinding> CreateDefaults() => new()
    {
        new(KeyboardShortcutAction.LockVault, "Ctrl+L", true),
        new(KeyboardShortcutAction.SearchAccounts, "Ctrl+F", true),
        new(KeyboardShortcutAction.CopyUsername, "Ctrl+Shift+U", true),
        new(KeyboardShortcutAction.ShowTotp, "Ctrl+Shift+T", true)
    };

    private static List<KeyboardShortcutBinding> Normalize(List<KeyboardShortcutBinding> bindings)
    {
        var byAction = bindings.Where(binding => Enum.IsDefined(binding.Action))
            .GroupBy(binding => binding.Action)
            .ToDictionary(group => group.Key, group => group.Last());
        return Enum.GetValues<KeyboardShortcutAction>()
            .Select(action => byAction.TryGetValue(action, out KeyboardShortcutBinding? binding)
                ? binding
                : CreateDefaults().Single(item => item.Action == action))
            .ToList();
    }

    private static bool IsValid(IReadOnlyList<KeyboardShortcutBinding>? bindings, out string error)
    {
        error = "";
        if (bindings == null || bindings.Count != Enum.GetValues<KeyboardShortcutAction>().Length ||
            bindings.Select(binding => binding.Action).Distinct().Count() != bindings.Count)
        {
            error = "Cấu hình phím tắt không đầy đủ hoặc có mục lặp.";
            return false;
        }

        foreach (KeyboardShortcutBinding binding in bindings)
        {
            if (!Options.TryGetValue(binding.Action, out string[]? allowed) || !allowed.Contains(binding.Gesture, StringComparer.Ordinal))
            {
                error = "Phím tắt không nằm trong danh sách tổ hợp được hỗ trợ.";
                return false;
            }
        }

        string[] active = bindings.Where(binding => binding.Enabled).Select(binding => binding.Gesture).ToArray();
        if (active.Distinct(StringComparer.Ordinal).Count() != active.Length)
        {
            error = "Không thể gán cùng một phím tắt cho nhiều thao tác đang bật.";
            return false;
        }
        return true;
    }

    private static string RegistryPath
    {
        get
        {
#if SECURITY_TEST
            return @"Software\TQLTK\SecurityRegression";
#else
            return @"Software\TQLTK";
#endif
        }
    }

    private static string? ReadStoredJson()
    {
#if SECURITY_TEST
        string path = TestSettingsPath;
        return File.Exists(path) && new FileInfo(path).Length <= 16 * 1024 ? File.ReadAllText(path) : null;
#else
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return key?.GetValue(RegistryValue)?.ToString();
#endif
    }

    private static void WriteStoredJson(string json)
    {
#if SECURITY_TEST
        AtomicFileService.WriteAllText(TestSettingsPath, json);
#else
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("Không thể mở khóa cài đặt phím tắt.");
        key.SetValue(RegistryValue, json, RegistryValueKind.String);
#endif
    }

#if SECURITY_TEST
    private static string TestSettingsPath => Path.Combine(
        ApplicationDataPath.Root, "PersonalInfoManager", "keyboard_shortcuts.json");
#endif

#if SECURITY_TEST
    public static void ResetForTesting()
    {
        lock (Sync)
        {
            try { AtomicFileService.TryDelete(TestSettingsPath); } catch { }
            _cached = null;
        }
    }
#endif
}
