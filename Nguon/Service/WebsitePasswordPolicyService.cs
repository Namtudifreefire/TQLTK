using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace PersonalInfoManager.Services;

public sealed record WebsitePasswordPolicy(
    bool Enabled,
    int MinimumLength,
    int MaximumLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigits,
    bool RequireSymbols,
    string ExcludedCharacters)
{
    public static WebsitePasswordPolicy Default { get; } = new(false, 8, 64, false, false, false, false, "");
}

public static class WebsitePasswordPolicyService
{
    private const string RegistryValue = "WebsitePasswordPoliciesJson";
    private const int MaximumPolicies = 256;

    public static string NormalizeHost(string? websiteUrl)
    {
        if (string.IsNullOrWhiteSpace(websiteUrl) ||
            !WebsiteDomainService.TryNormalizeUrl(websiteUrl, out _, out string host)) return "";
        return host;
    }

    public static string? Validate(int requestedLength, PasswordGeneratorOptions options, WebsitePasswordPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.Enabled) return null;
        if (policy.MinimumLength is < 8 or > 64 || policy.MaximumLength is < 8 or > 64 ||
            policy.MinimumLength > policy.MaximumLength)
            return "Giới hạn độ dài mật khẩu của website không hợp lệ.";
        if (requestedLength < policy.MinimumLength || requestedLength > policy.MaximumLength)
            return $"Hãy chọn độ dài từ {policy.MinimumLength} đến {policy.MaximumLength} ký tự cho website này.";

        if (policy.RequireUppercase || policy.RequireLowercase || policy.RequireDigits || policy.RequireSymbols)
        {
            int requiredGroups = (policy.RequireUppercase ? 1 : 0) + (policy.RequireLowercase ? 1 : 0) +
                                 (policy.RequireDigits ? 1 : 0) + (policy.RequireSymbols ? 1 : 0);
            if (requestedLength < requiredGroups)
                return "Độ dài đã chọn không đủ để đáp ứng các nhóm ký tự bắt buộc của website.";
        }

        return null;
    }

    public static WebsitePasswordPolicy Get(string websiteUrl)
    {
        string host = NormalizeHost(websiteUrl);
        if (host.Length == 0) return WebsitePasswordPolicy.Default;
        try
        {
            string? json = ReadStoredJson();
            if (string.IsNullOrWhiteSpace(json) || json.Length > 256 * 1024) return WebsitePasswordPolicy.Default;
            var policies = JsonSerializer.Deserialize<Dictionary<string, WebsitePasswordPolicy>>(json);
            return policies != null && policies.TryGetValue(host, out WebsitePasswordPolicy? policy)
                ? Sanitize(policy)
                : WebsitePasswordPolicy.Default;
        }
        catch { return WebsitePasswordPolicy.Default; }
    }

    public static bool Save(string websiteUrl, WebsitePasswordPolicy policy)
    {
        string host = NormalizeHost(websiteUrl);
        if (host.Length == 0) throw new ArgumentException("Hãy nhập URL website HTTP hoặc HTTPS hợp lệ trước.", nameof(websiteUrl));
        policy = Sanitize(policy);
        var policies = LoadAll();
        if (!policy.Enabled)
        {
            policies.Remove(host);
        }
        else
        {
            if (policy.MinimumLength is < 8 or > 64 || policy.MaximumLength is < 8 or > 64 ||
                policy.MinimumLength > policy.MaximumLength)
                throw new ArgumentException("Giới hạn độ dài mật khẩu của website không hợp lệ.", nameof(policy));
            int requiredGroups = (policy.RequireUppercase ? 1 : 0) + (policy.RequireLowercase ? 1 : 0) +
                                 (policy.RequireDigits ? 1 : 0) + (policy.RequireSymbols ? 1 : 0);
            if (requiredGroups > policy.MaximumLength)
                throw new ArgumentException("Độ dài tối đa không đủ cho tất cả nhóm ký tự bắt buộc.", nameof(policy));
            if (!policies.ContainsKey(host) && policies.Count >= MaximumPolicies)
                throw new InvalidOperationException("Đã đạt số lượng tối đa các quy tắc mật khẩu website được lưu.");
            policies[host] = policy;
        }

        string json = JsonSerializer.Serialize(policies);
        if (json.Length > 256 * 1024) throw new InvalidOperationException("Dữ liệu quy tắc mật khẩu website vượt giới hạn kích thước.");
        WriteStoredJson(json);
        return true;
    }

    private static Dictionary<string, WebsitePasswordPolicy> LoadAll()
    {
        try
        {
            string? json = ReadStoredJson();
            if (string.IsNullOrWhiteSpace(json) || json.Length > 256 * 1024)
                return new Dictionary<string, WebsitePasswordPolicy>(StringComparer.OrdinalIgnoreCase);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, WebsitePasswordPolicy>>(json);
            return loaded?.Take(MaximumPolicies).ToDictionary(pair => pair.Key, pair => Sanitize(pair.Value), StringComparer.OrdinalIgnoreCase)
                   ?? new Dictionary<string, WebsitePasswordPolicy>(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, WebsitePasswordPolicy>(StringComparer.OrdinalIgnoreCase); }
    }

    private static WebsitePasswordPolicy Sanitize(WebsitePasswordPolicy? policy)
    {
        if (policy == null) return WebsitePasswordPolicy.Default;
        string excluded = new((policy.ExcludedCharacters ?? "").Distinct().Take(128).ToArray());
        return policy with { ExcludedCharacters = excluded };
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
        return File.Exists(path) && new FileInfo(path).Length <= 256 * 1024 ? File.ReadAllText(path) : null;
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
            ?? throw new InvalidOperationException("Không thể mở khóa cài đặt của ứng dụng.");
        key.SetValue(RegistryValue, json, RegistryValueKind.String);
#endif
    }

#if SECURITY_TEST
    private static string TestSettingsPath => Path.Combine(
        ApplicationDataPath.Root, "PersonalInfoManager", "website_password_policies.json");
#endif

#if SECURITY_TEST
    public static void ResetForTesting()
    {
        try { AtomicFileService.TryDelete(TestSettingsPath); } catch { }
    }
#endif
}
