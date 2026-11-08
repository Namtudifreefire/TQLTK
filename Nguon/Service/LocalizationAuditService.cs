using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace PersonalInfoManager.Services;
public sealed record LocalizationAuditIssue(string Key, string Problem);
public sealed record LocalizedMessageDefinition(string Id, string Vietnamese, string English, IReadOnlyList<string> Parameters);
public static class LocalizationAuditService
{
    private sealed record Message(string Vietnamese, string English);
    private static readonly IReadOnlyDictionary<string, Message> Messages =
        new Dictionary<string, Message>(StringComparer.Ordinal)
        {
            ["app.footer"] = new("Được tạo bởi 天地と月の造化 - TQLTK v{0} (BETA) | © 2026 Namtudifreefire | Giấy phép MIT", "Made by 天地と月の造化 - TQLTK v{0} (BETA) | © 2026 Namtudifreefire | MIT License"),
            ["backup.periodic.disabled"] = new("Sao lưu định kỳ đang tắt.", "Scheduled backups are off."),
            ["backup.periodic.failure"] = new("Sao lưu gần nhất thất bại: {0}", "Most recent backup failed: {0}"),
            ["backup.periodic.last_success"] = new("Sao lưu đầy đủ gần nhất: {0}.", "Most recent full backup: {0}."),
            ["backup.periodic.enabled_empty"] = new("Sao lưu định kỳ đã bật; chưa có bản sao lưu đầy đủ nào.", "Scheduled backups are on; no full backup has been created yet."),
            ["backup.retention.warning"] = new("Cảnh báo giữ bản sao lưu: còn {0} bản ({1}); giới hạn là {2} bản, {3} ngày và {4}.", "Backup retention warning: {0} copies remain ({1}); limits are {2} copies, {3} days, and {4}."),
            ["backup.retention.failed"] = new("Không thể áp dụng chính sách giữ bản sao lưu; hãy kiểm tra quyền truy cập thư mục sao lưu.", "Could not apply the backup retention policy; check access to the backup folder."),
            ["backup.restore.summary"] = new("Đã khôi phục: thêm {0} tài khoản, {1} ghi chú và {2} tệp.", "Restored: added {0} accounts, {1} notes, and {2} files."),
            ["backup.migration.next_steps"] = new("Đã xác minh gói trước khi lưu. Chuyển tệp {0} và cụm mật khẩu sang máy mới qua kênh an toàn, nhưng cất chúng riêng nhau. Trên máy mới, đăng nhập hoặc tạo tài khoản TQLTK, chọn Khôi phục / chuyển máy và kiểm tra dữ liệu. Giữ nguyên dữ liệu máy cũ cho đến khi xác minh xong; sau đó thiết lập lại Windows Hello hoặc 2FA.", "The package was verified before saving. Transfer {0} and its passphrase to the new computer through a secure channel, but store them separately. On the new computer, sign in to or create a TQLTK account, choose Restore / migrate, and verify the restored data. Keep the old computer's data until verification is complete; then set up Windows Hello or two-factor sign-in again."),
            ["backup.migration.guide"] = new("1. Đặt cụm mật khẩu sao lưu riêng, khác mật khẩu đăng nhập; cất cụm này an toàn.\n2. Chọn ghi chú riêng, tệp cá nhân và cài đặt an toàn cần đưa vào gói. Tài khoản luôn gồm mật khẩu, TOTP, tệp đính kèm, trường tùy chỉnh và lịch sử.\n3. Tạo gói .tqltk. Ứng dụng giải mã và xác minh gói ngay sau khi tạo; kiểm tra danh sách thành phần trong thông báo.\n4. Chuyển gói và cụm mật khẩu sang máy mới bằng kênh an toàn, lưu riêng nhau. Không gửi cả hai trong cùng một tin nhắn hoặc thư mục chia sẻ.\n5. Trên máy mới, cài và mở TQLTK, đăng nhập hoặc tạo tài khoản ứng dụng rồi chọn Khôi phục / chuyển máy.\n6. Chọn gói, nhập cụm mật khẩu, xem trước các thành phần và xác nhận khôi phục. Mục trùng được giữ nguyên; mục còn thiếu được thêm.\n7. Kiểm tra tài khoản, TOTP, ghi chú, tệp đính kèm và tệp cá nhân; thiết lập lại Windows Hello hoặc 2FA trên máy mới.\n8. Chỉ xóa dữ liệu cũ và bản trung gian sau khi đã xác minh đầy đủ bản khôi phục.", "1. Set a dedicated backup passphrase that differs from your sign-in password, and store it safely.\n2. Choose whether to include private notes, personal files, and safe settings. Accounts always include passwords, TOTP, attachments, custom fields, and history.\n3. Create the .tqltk package. The app decrypts and verifies it immediately; review the component summary.\n4. Transfer the package and passphrase to the new computer through a secure channel and store them separately. Do not send both in one message or shared folder.\n5. On the new computer, install and open TQLTK, sign in to or create an app account, then choose Restore / migrate.\n6. Select the package, enter its passphrase, review the preview, and confirm the restore. Existing matching items are kept; missing items are added.\n7. Check accounts, TOTP, notes, attachments, and personal files; set up Windows Hello or two-factor sign-in again on the new computer.\n8. Delete old data and temporary copies only after you have fully verified the restore."),
            ["backup.recovered_copy_suffix"] = new("(bản phục hồi {0})", "(recovered copy {0})"),
            ["backup.selective.success"] = new("Đã khôi phục thành bản sao: {0} tài khoản, {1} ghi chú và {2} tệp. Dữ liệu hiện tại không bị ghi đè.", "Recovered as separate copies — accounts: {0}, notes: {1}, files: {2}. Current data was not overwritten."),
            ["backup.created.contents_summary"] = new("Đã tạo và xác minh gói mã hóa: {0}\nTài khoản gồm mật khẩu, TOTP, tệp đính kèm và trường tùy chỉnh: {1}. Ghi chú riêng: {2} ({3}). Tệp cá nhân: {4} ({5}). Cài đặt an toàn: {6}.", "Created and verified encrypted package: {0}\nAccounts include passwords, TOTP, attachments, and custom fields: {1}. Private notes: {2} ({3}). Personal files: {4} ({5}). Safe settings: {6}."),
            ["import.preview.count"] = new("Đọc được {count} mục. Chọn một mục để xem thông tin.", "Found {count} entries. Select one to view its details.")
        };
    private static readonly Regex Placeholder = new(@"(?<!\{)\{(?<name>[A-Za-z_][A-Za-z0-9_]*|\d+)(?<suffix>(?:,[^{}]*)?(?::[^{}]*)?)\}(?!\})", RegexOptions.CultureInvariant);
    private static readonly Lazy<IReadOnlyDictionary<string, LocalizedMessageDefinition>> Definitions = new(BuildDefinitions);
    private static readonly Lazy<IReadOnlyDictionary<string, LocalizedMessageDefinition>> SourceDefinitions = new(() =>
        Definitions.Value.Values.ToDictionary(item => item.Vietnamese, StringComparer.Ordinal));
    public static IReadOnlyCollection<LocalizedMessageDefinition> Catalog => Definitions.Value.Values.ToArray();
    public static string GetMessageId(string source)
    {
        return SourceDefinitions.Value.TryGetValue(source, out LocalizedMessageDefinition? definition)
            ? definition.Id
            : throw new KeyNotFoundException("The UI message has no registered translation: " + source);
    }
    internal static bool TryTranslateKnown(string sourceOrId, string languageCode, out string translation)
    {
        LocalizedMessageDefinition? definition = Definitions.Value.GetValueOrDefault(sourceOrId)
            ?? SourceDefinitions.Value.GetValueOrDefault(sourceOrId);
        if (definition == null)
        {
            translation = "";
            return false;
        }
        translation = IsEnglish(languageCode) ? definition.English : definition.Vietnamese;
        return true;
    }
    private static IReadOnlyDictionary<string, LocalizedMessageDefinition> BuildDefinitions()
    {
        var definitions = new Dictionary<string, LocalizedMessageDefinition>(StringComparer.Ordinal);
        var registeredSources = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string id, Message message) in Messages)
        {
            definitions.Add(id, new(id, message.Vietnamese, message.English, GetPlaceholders(message.Vietnamese)));
            registeredSources.Add(message.Vietnamese);
        }
        foreach ((string source, string english) in EnglishCatalog.Entries.Concat(EnglishCatalog.Fragments))
        {
            if (!registeredSources.Add(source)) continue;
            string id = "ui." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
            definitions.Add(id, new(id, source, english, GetPlaceholders(source)));
        }
        return definitions;
    }
    public static string Get(string key) => Get(key, LanguageService.IsEnglish);
    public static string Get(string key, string? languageCode) => Get(key, IsEnglish(languageCode));
    private static string Get(string key, bool isEnglish)
    {
        if (!Definitions.Value.TryGetValue(key, out LocalizedMessageDefinition? message))
            throw new KeyNotFoundException($"Localization key '{key}' is not registered.");
        return isEnglish ? message.English : message.Vietnamese;
    }
    public static string FormatForLanguage(string key, string languageCode, params object?[] arguments) =>
        FormatTemplate(key, Get(key, languageCode), arguments, GetCulture(languageCode));
    public static string Format(string key, params object?[] arguments)
    {
        string template = Get(key);
        return FormatTemplate(key, template, arguments, GetCulture(null));
    }
    public static string FormatSource(string source, string languageCode, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(arguments);
        string template = LanguageService.Translate(source, languageCode);
        CultureInfo culture = GetCulture(languageCode);
        string[] placeholders = GetPlaceholders(template);
        if (placeholders.Any(placeholder => !int.TryParse(placeholder, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            if (arguments.Length != 1 || arguments[0] is not IReadOnlyDictionary<string, object?> namedArguments)
                throw new FormatException("This message uses named placeholders and requires a named-argument dictionary.");
            return FormatNamedTemplate(source, template, namedArguments, culture);
        }
        return FormatTemplate(source, template, arguments, culture);
    }
    private static string FormatTemplate(string key, string template, object?[] arguments, CultureInfo culture)
    {
        string[] placeholders = GetPlaceholders(template);
        if (placeholders.Any(placeholder => !int.TryParse(placeholder, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            if (arguments.Length != 1 || arguments[0] is not IReadOnlyDictionary<string, object?> namedArguments)
                throw new FormatException($"Localization key '{key}' uses named placeholders and requires one named-argument dictionary.");
            return FormatNamedTemplate(key, template, namedArguments, culture);
        }
        ValidateArguments(key, template, arguments.Length);
        return string.Format(culture, template, arguments);
    }
    public static string FormatNamed(string key, IReadOnlyDictionary<string, object?> arguments, string? languageCode = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool isEnglish = IsEnglish(languageCode);
        return FormatNamedTemplate(key, Get(key, isEnglish), arguments, GetCulture(languageCode));
    }
    public static string FormatDate(DateTime value, string? languageCode = null)
    {
        CultureInfo culture = GetCulture(languageCode);
        return NormalizeForDisplay(value).ToString("d", culture);
    }
    public static string FormatDateTime(DateTime value, string? languageCode = null, bool includeSeconds = false)
    {
        CultureInfo culture = GetCulture(languageCode);
        return NormalizeForDisplay(value).ToString(includeSeconds ? "G" : "g", culture);
    }
    public static string FormatDateTime(DateTimeOffset value, string? languageCode = null, bool includeSeconds = false) =>
        value.ToLocalTime().ToString(includeSeconds ? "G" : "g", GetCulture(languageCode));
    public static string FormatDate(DateTimeOffset value, string? languageCode = null) =>
        value.ToLocalTime().ToString("d", GetCulture(languageCode));
    private static DateTime NormalizeForDisplay(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value.ToLocalTime(),
        DateTimeKind.Local => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local)
    };
    public static string FormatCount(int count, string singularEnglish, string pluralEnglish, string unitVietnamese,
        string? languageCode = null)
    {
        CultureInfo culture = GetCulture(languageCode);
        bool isEnglish = IsEnglish(languageCode);
        if (!isEnglish) return $"{count.ToString(culture)} {unitVietnamese}";
        return count.ToString(CultureInfo.GetCultureInfo("en-US")) + " " + (count == 1 ? singularEnglish : pluralEnglish);
    }
    private static bool IsEnglish(string? languageCode) => languageCode == null
        ? LanguageService.IsEnglish
        : string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase);
    private static CultureInfo GetCulture(string? languageCode) => IsEnglish(languageCode)
        ? CultureInfo.GetCultureInfo("en-US")
        : CultureInfo.GetCultureInfo("vi-VN");
    public static IReadOnlyList<LocalizationAuditIssue> Audit()
    {
        var issues = new List<LocalizationAuditIssue>();
        foreach ((string key, Message message) in Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Vietnamese)) issues.Add(new(key, "Thiếu nội dung tiếng Việt."));
            if (string.IsNullOrWhiteSpace(message.English)) issues.Add(new(key, "Thiếu nội dung tiếng Anh."));
            ValidateTemplate(issues, key + ".vi", message.Vietnamese);
            ValidateTemplate(issues, key + ".en", message.English);
            if (!GetPlaceholders(message.Vietnamese).SequenceEqual(GetPlaceholders(message.English)))
                issues.Add(new(key, "Hai bản dịch không có cùng tập tham số."));
        }
        foreach (Message message in Messages.Values)
        {
            foreach (string template in new[] { message.Vietnamese, message.English })
            {
                int[] indexes = GetPlaceholders(template)
                    .Where(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
                    .OrderBy(index => index)
                    .ToArray();
                if (indexes.Length > 0 && !indexes.SequenceEqual(Enumerable.Range(0, indexes.Length)))
                    issues.Add(new("LocalizationKeys", "Tham số phải liên tục từ {0} và không được bỏ số."));
            }
        }
        foreach ((string source, string translation) in EnglishCatalog.Entries)
        {
            if (string.IsNullOrWhiteSpace(source)) issues.Add(new("EnglishCatalog", "Có khóa tiếng Việt rỗng."));
            if (string.IsNullOrWhiteSpace(translation)) issues.Add(new(source, "Thiếu bản dịch tiếng Anh."));
            ValidateTemplate(issues, source, source);
            ValidateTemplate(issues, source, translation);
            if (!GetPlaceholders(source).SequenceEqual(GetPlaceholders(translation)))
                issues.Add(new(source, "Bản dịch tiếng Anh không giữ nguyên các tham số."));
        }
        foreach (string source in EnglishCatalog.ConflictingDuplicateSources)
            issues.Add(new(source, "Khóa bản dịch nguồn được khai báo nhiều lần với các bản dịch tiếng Anh khác nhau."));
        foreach (LocalizedMessageDefinition definition in Catalog)
            if (definition.Parameters.Count != GetPlaceholders(definition.English).Length)
                issues.Add(new(definition.Id, "Số tham số mô tả không khớp bản dịch."));
        return issues;
    }
    public static IReadOnlyList<LocalizationAuditIssue> AuditSources(IEnumerable<string> sources) =>
        sources.Distinct(StringComparer.Ordinal)
            .Where(source => !TryTranslateKnown(source, "en", out _))
            .Select(source => new LocalizationAuditIssue(source, "Thiếu mã thông báo hoặc bản dịch đã đăng ký."))
            .ToArray();
    private static void ValidateArguments(string key, string template, int argumentCount)
    {
        int[] indexes = GetPlaceholders(template)
            .Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ? index : -1)
            .Where(index => index >= 0)
            .ToArray();
        if (indexes.Length == 0 && argumentCount == 0) return;
        if (indexes.Any(index => index >= argumentCount) || indexes.Distinct().Count() != argumentCount)
            throw new FormatException($"Localization key '{key}' expects placeholders [{string.Join(",", indexes)}], but received {argumentCount} arguments.");
    }
    private static void ValidateTemplate(List<LocalizationAuditIssue> issues, string key, string template)
    {
        try { _ = string.Format(CultureInfo.InvariantCulture, NormalizeNamedPlaceholders(template), new object?[32]); }
        catch (FormatException ex) { issues.Add(new(key, "Định dạng tham số không hợp lệ: " + ex.Message)); }
    }
    private static string[] GetPlaceholders(string value) => Placeholder.Matches(value)
        .Select(match => match.Groups["name"].Value)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    private static string NormalizeNamedPlaceholders(string template)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        return Placeholder.Replace(template, match =>
        {
            string name = match.Groups["name"].Value;
            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return match.Value;
            if (!indexes.TryGetValue(name, out int index)) indexes[name] = index = indexes.Count;
            return "{" + index.ToString(CultureInfo.InvariantCulture) + match.Groups["suffix"].Value + "}";
        });
    }
    private static string FormatNamedTemplate(string key, string template,
        IReadOnlyDictionary<string, object?> arguments, CultureInfo culture)
    {
        string[] placeholders = GetPlaceholders(template);
        if (placeholders.Any(name => int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            throw new FormatException($"Localization key '{key}' mixes named and positional placeholders.");
        string? missing = placeholders.FirstOrDefault(name => !arguments.ContainsKey(name));
        if (missing != null)
            throw new FormatException($"Localization key '{key}' is missing named argument '{missing}'.");
        if (arguments.Keys.Any(name => !placeholders.Contains(name, StringComparer.Ordinal)))
            throw new FormatException($"Localization key '{key}' received an unregistered named argument.");
        return Placeholder.Replace(template, match =>
        {
            string name = match.Groups["name"].Value;
            object? value = arguments[name];
            string suffix = match.Groups["suffix"].Value;
            return string.Format(culture, "{0" + suffix + "}", value);
        });
    }
}
