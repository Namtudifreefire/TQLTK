using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PersonalInfoManager.Models;
namespace PersonalInfoManager.Services
{
         public static class CsvService
    {
                 public static (bool ok, string message) ExportToFile(
            List<SavedAccount> accounts, string filePath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("ServiceName,Username,Password,Note,CreatedAt,UpdatedAt,PasswordChangedAt,TotpSecret,TotpIssuer,TotpAlgorithm,TotpDigits,TotpPeriodSeconds,WebsiteUrl,Folder,Tags,IsFavorite,CustomFieldsJson");
                foreach (var acc in accounts)
                {
                    sb.AppendLine(string.Join(",",
                        EscapeCsv(acc.ServiceName),
                        EscapeCsv(acc.Username),
                        EscapeCsv(acc.Password),
                        EscapeCsv(acc.Note),
                        EscapeCsv(acc.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                        EscapeCsv(acc.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                        EscapeCsv(acc.PasswordChangedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""),
                        EscapeCsv(acc.TotpSecret),
                        EscapeCsv(acc.TotpIssuer),
                        EscapeCsv(acc.TotpAlgorithm),
                        EscapeCsv(acc.TotpDigits.ToString()),
                        EscapeCsv(acc.TotpPeriodSeconds.ToString(CultureInfo.InvariantCulture)),
                        EscapeCsv(acc.WebsiteUrl),
                        EscapeCsv(acc.Folder),
                        EscapeCsv(string.Join(",", acc.Tags ?? new List<string>())),
                        EscapeCsv(acc.IsFavorite.ToString(CultureInfo.InvariantCulture)),
                        EscapeCsv(JsonSerializer.Serialize(acc.CustomFields ?? new List<AccountCustomField>()))
                    ));
                }
                AtomicFileService.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                return (true, $"Đã xuất {accounts.Count} tài khoản ra:\n{filePath}");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi xuất CSV: {ex.Message}");
            }
        }
                 private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (StartsWithSpreadsheetFormula(value))
                value = "'" + value;
            if (value.Contains(',') || value.Contains('"') ||
                value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }
        private static bool StartsWithSpreadsheetFormula(string value)
        {
            int index = 0;
            while (index < value.Length &&
                   (char.IsWhiteSpace(value[index]) || char.IsControl(value[index]) || value[index] == '\uFEFF'))
                index++;
            return index < value.Length && value[index] is '=' or '+' or '-' or '@';
        }
                 public static (bool ok, string message, List<SavedAccount> accounts)
            ImportFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, "File không tồn tại.", new List<SavedAccount>());
                string content = File.ReadAllText(filePath, Encoding.UTF8);
                var lines = ParseCsvLines(content);
                if (lines.Count < 2)
                    return (false, "File CSV rỗng hoặc không có dữ liệu.", new List<SavedAccount>());
                var header = lines[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
                var accounts = new List<SavedAccount>();
                string format = DetectFormat(header);
                for (int i = 1; i < lines.Count; i++)
                {
                    var row = lines[i];
                    if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace)) continue;
                    try
                    {
                        var acc = ParseRow(row, header, format);
                        if (acc != null) accounts.Add(acc);
                    }
                    catch
                    {
                    }
                }
                if (accounts.Count == 0)
                    return (false, "Không tìm thấy tài khoản hợp lệ trong file.", new List<SavedAccount>());
                return (true, $"Đã đọc {accounts.Count} tài khoản (format: {format}).", accounts);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi đọc CSV: {ex.Message}", new List<SavedAccount>());
            }
        }
                 private static string DetectFormat(List<string> header)
        {
            var h = header.Select(x => x.Trim().ToLowerInvariant()).ToList();
            if (h.Contains("login_username") || h.Contains("login_password"))
                return "Bitwarden";
            if (h.Contains("url") && h.Contains("grouping"))
                return "LastPass";
            if (h.Contains("url") && h.Contains("username") && h.Contains("password") &&
                (h.Contains("httprealm") || h.Contains("formactionorigin")))
                return "Firefox";
            if (h.Contains("group") && h.Contains("title") && h.Contains("username") &&
                h.Contains("password") && h.Contains("url"))
                return "KeePass";
            if (h.Contains("title") && (h.Contains("website") || h.Contains("url")) &&
                h.Contains("username") && h.Contains("password") &&
                (h.Contains("otp") || h.Contains("otpauth") || h.Contains("category") ||
                 h.Contains("type") || h.Contains("favorite")))
                return "1Password";
            if (h.Contains("url") && h.Contains("name") && h.Contains("username") && h.Contains("password"))
                return "Chrome";
            if (h.Contains("servicename"))
                return "TQLTK";
            return "Generic";
        }
                 private static SavedAccount? ParseRow(
            List<string> row, List<string> header, string format)
        {
            string Get(string colName)
            {
                int idx = header.IndexOf(colName.ToLowerInvariant());
                if (idx < 0 || idx >= row.Count) return "";
                return row[idx].Trim();
            }
            string serviceName = "";
            string username = "";
            string password = "";
            string note = "";
            string totp = "";
            string websiteUrl = "";
            string folder = "";
            string tagsValue = "";
            string customFieldsJson = "";
            bool isFavorite = false;
            string GetFirst(params string[] names)
            {
                foreach (string name in names)
                {
                    string value = Get(name);
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                return "";
            }
            switch (format)
            {
                case "TQLTK":
                    serviceName = Get("servicename");
                    username = Get("username");
                    password = Get("password");
                    note = Get("note");
                    totp = Get("totpsecret");
                    websiteUrl = GetFirst("websiteurl", "url");
                    folder = Get("folder");
                    tagsValue = Get("tags");
                    isFavorite = ParseBoolean(Get("isfavorite"));
                    customFieldsJson = Get("customfieldsjson");
                    break;
                case "Bitwarden":
                    serviceName = Get("name");
                    username = Get("login_username");
                    password = Get("login_password");
                    note = Get("notes");
                    totp = Get("login_totp");
                    websiteUrl = Get("login_uri");
                    folder = Get("folder");
                    isFavorite = ParseBoolean(Get("favorite"));
                    customFieldsJson = Get("fields");
                    break;
                case "LastPass":
                    serviceName = Get("name");
                    if (string.IsNullOrEmpty(serviceName)) serviceName = Get("url");
                    username = Get("username");
                    password = Get("password");
                    note = Get("extra");
                    totp = Get("totp");
                    websiteUrl = Get("url");
                    folder = Get("grouping");
                    isFavorite = ParseBoolean(Get("fav"));
                    break;
                case "Firefox":
                    websiteUrl = Get("url");
                    serviceName = GetFirst("name", "httprealm", "url");
                    username = Get("username");
                    password = Get("password");
                    note = Get("note");
                    break;
                case "KeePass":
                    serviceName = GetFirst("title", "name");
                    username = Get("username");
                    password = Get("password");
                    websiteUrl = Get("url");
                    note = Get("notes");
                    folder = Get("group");
                    tagsValue = Get("tags");
                    break;
                case "1Password":
                    serviceName = Get("title");
                    username = Get("username");
                    password = Get("password");
                    websiteUrl = GetFirst("website", "url");
                    note = Get("notes");
                    totp = GetFirst("otpauth", "otp", "totp");
                    folder = Get("category");
                    tagsValue = Get("tags");
                    isFavorite = ParseBoolean(Get("favorite"));
                    break;
                case "Chrome":
                    serviceName = Get("name");
                    if (string.IsNullOrEmpty(serviceName)) serviceName = Get("url");
                    username = Get("username");
                    password = Get("password");
                    note = Get("note");
                    totp = Get("totp");
                    websiteUrl = Get("url");
                    break;
                default:
                    serviceName = row.Count > 0 ? row[0].Trim() : "";
                    username = row.Count > 1 ? row[1].Trim() : "";
                    password = row.Count > 2 ? row[2].Trim() : "";
                    note = row.Count > 3 ? row[3].Trim() : "";
                    break;
            }
            if (string.IsNullOrWhiteSpace(serviceName) &&
                string.IsNullOrWhiteSpace(username) &&
                string.IsNullOrWhiteSpace(password))
                return null;
            var account = new SavedAccount
            {
                Id = Guid.NewGuid(),
                ServiceName = serviceName,
                Username = username,
                Password = password,
                Note = note,
                WebsiteUrl = websiteUrl,
                Folder = folder,
                Tags = ParseTags(tagsValue),
                IsFavorite = isFavorite,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            if (!string.IsNullOrWhiteSpace(account.WebsiteUrl))
            {
                if (WebsiteDomainService.TryNormalizeUrl(account.WebsiteUrl, out var parsedUrl, out _) && parsedUrl != null)
                    account.WebsiteUrl = parsedUrl.AbsoluteUri;
                else
                {
                    account.WebsiteUrl = "";
                    account.Note = string.IsNullOrWhiteSpace(account.Note)
                        ? "[URL không hợp lệ đã được bỏ qua khi nhập.]"
                        : account.Note + Environment.NewLine + "[URL không hợp lệ đã được bỏ qua khi nhập.]";
                }
            }
            if (!string.IsNullOrWhiteSpace(customFieldsJson))
            {
                try
                {
                    if (customFieldsJson.TrimStart().StartsWith("[", StringComparison.Ordinal))
                        account.CustomFields = (JsonSerializer.Deserialize<List<AccountCustomField?>>(
                                customFieldsJson, new JsonSerializerOptions { MaxDepth = 16 }) ?? new())
                            .Where(field => field != null && !string.IsNullOrWhiteSpace(field.Name))
                            .Take(100)
                            .Select(field =>
                            {
                                field!.Name = field.Name.Trim();
                                field.Value ??= "";
                                if (field.Name.Length > 200) field.Name = field.Name[..200];
                                if (field.Value.Length > 10_000) field.Value = field.Value[..10_000];
                                return field;
                            })
                            .ToList();
                    else if (format == "Bitwarden")
                        account.CustomFields = ParseBitwardenFields(customFieldsJson);
                }
                catch (JsonException)
                {
                    account.CustomFields = format == "Bitwarden" ? ParseBitwardenFields(customFieldsJson) : new();
                }
            }
            string issuer = Get("totpissuer");
            string algorithm = Get("totpalgorithm");
            if (DateTime.TryParse(Get("passwordchangedat"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out DateTime changedAt))
                account.PasswordChangedAt = changedAt;
            if (!string.IsNullOrWhiteSpace(totp))
            {
                try
                {
                    var imported = TotpService.ParseImportText(totp, serviceName).First();
                    account.TotpSecret = imported.Secret;
                    account.TotpIssuer = string.IsNullOrWhiteSpace(issuer) ? imported.Issuer : issuer;
                    string normalizedAlgorithm = algorithm.Trim().ToUpperInvariant();
                    account.TotpAlgorithm = normalizedAlgorithm is "SHA1" or "SHA256" or "SHA512"
                        ? normalizedAlgorithm : imported.Algorithm;
                    account.TotpDigits = int.TryParse(Get("totpdigits"), NumberStyles.None,
                        CultureInfo.InvariantCulture, out int configuredDigits) && configuredDigits is 6 or 8
                        ? configuredDigits : imported.Digits;
                    account.TotpPeriodSeconds = int.TryParse(Get("totpperiodseconds"), NumberStyles.None,
                        CultureInfo.InvariantCulture, out int configuredPeriod) && configuredPeriod is >= 5 and <= 120
                        ? configuredPeriod : imported.PeriodSeconds;
                    if (totp.StartsWith("otpauth", StringComparison.OrdinalIgnoreCase))
                    {
                        account.TotpAlgorithm = imported.Algorithm;
                        account.TotpDigits = imported.Digits;
                        account.TotpPeriodSeconds = imported.PeriodSeconds;
                    }
                }
                catch (FormatException)
                {
                    account.Note = string.IsNullOrWhiteSpace(account.Note)
                        ? "[TOTP không nhập được: kiểm tra secret trong file CSV.]"
                        : account.Note + Environment.NewLine + "[TOTP không nhập được: kiểm tra secret trong file CSV.]";
                }
            }
            return account;
        }
        private static List<string> ParseTags(string input) => input
            .Split(new[] { ',', ';', '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        private static bool ParseBoolean(string value) =>
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "1";
        private static List<AccountCustomField> ParseBitwardenFields(string input)
        {
            var fields = new List<AccountCustomField>();
            foreach (string line in input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = line.IndexOf(':');
                if (separator <= 0) continue;
                string name = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                if (name.Length > 0)
                    fields.Add(new AccountCustomField { Name = name, Value = value, IsSensitive = true });
            }
            return fields;
        }
                 private static List<List<string>> ParseCsvLines(string content)
        {
            var result = new List<List<string>>();
            var currentRow = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < content.Length && content[i + 1] == '"')
                        {
                            currentField.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                    }
                    else if (c == '\n' || c == '\r')
                    {
                        if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                            i++;
                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                        if (currentRow.Count > 0)
                            result.Add(new List<string>(currentRow));
                        currentRow.Clear();
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
            }
            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString());
                if (currentRow.Count > 0)
                    result.Add(currentRow);
            }
            return result;
        }
                 public static List<SavedAccount> RemoveDuplicates(
            List<SavedAccount> imported, List<SavedAccount> existing)
        {
            var existingKeys = new HashSet<string>(
                existing.Select(a => $"{a.ServiceName.ToLower()}|{a.Username.ToLower()}"));
            return imported
                .Where(a => !existingKeys.Contains($"{a.ServiceName.ToLower()}|{a.Username.ToLower()}"))
                .ToList();
        }
    }
}
