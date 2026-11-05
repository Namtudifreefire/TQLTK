using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services;

public sealed record CredentialImportResult(bool Ok, string SourceFormat, string Message, List<SavedAccount> Accounts);

public static class CredentialImportService
{
    public const int MaximumFileBytes = 64 * 1024 * 1024;
    public const int MaximumEntries = 20_000;

    public static CredentialImportResult ImportFromFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return Failure("Unknown", "Không tìm thấy tệp đã chọn.");
            if (file.Length <= 0 || file.Length > MaximumFileBytes)
                return Failure("Unknown", "Tệp đã chọn trống hoặc vượt giới hạn nhập 64 MB.");

            string extension = file.Extension.ToLowerInvariant();
            if (extension == ".csv")
            {
                var result = CsvService.ImportFromFile(path);
                return new CredentialImportResult(result.ok, "CSV", result.message, result.accounts);
            }

            if (extension == ".json")
                return ParseJson(ReadFileTextBounded(path));
            if (extension is ".xml")
                return ParseKeePassXml(ReadFileTextBounded(path));
            if (extension is ".kdbx")
                return Failure("KeePass KDBX", "Chưa hỗ trợ mở trực tiếp file .kdbx đã mã hóa. Hãy xuất dữ liệu thành KeePass XML trong KeePass rồi nhập file XML.");
            if (extension == ".vault")
                return Failure("TQLTK vault backup", "Đây là bản sao lưu kho mật khẩu TQLTK. Hãy dùng Tiện ích → Nhập kho mật khẩu để xem trước và nhập dữ liệu.");
            if (extension == ".tqf")
                return Failure("TQLTK personal-file backup", "Đây là bản sao lưu File cá nhân TQLTK, không phải tệp tài khoản. Hãy dùng Tiện ích → Kiểm tra bản sao lưu hoặc Diễn tập khôi phục.");
            return Failure("Unknown", "Định dạng tài khoản được hỗ trợ là CSV, JSON và KeePass XML. File .vault có luồng nhập riêng; file .tqf là bản sao lưu File cá nhân.");
        }
        catch
        {
            return Failure("Unknown", "Không thể đọc tệp nhập. Hãy kiểm tra định dạng, quyền truy cập và giới hạn 64 MB.");
        }
    }

    public static CredentialImportResult ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumFileBytes)
            return Failure("JSON", "Tệp JSON trống hoặc vượt giới hạn nhập 64 MB.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow });
            JsonElement root = document.RootElement;
            JsonElement entries = FindEntryArray(root);
            Dictionary<string, string> folders = ReadFolders(root);
            var accounts = new List<SavedAccount>();
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (accounts.Count >= MaximumEntries)
                    return Failure("JSON", $"Tệp nhập có hơn {MaximumEntries:N0} mục được hỗ trợ.");
                if (entry.ValueKind != JsonValueKind.Object) continue;

                JsonElement login = GetProperty(entry, "login", out JsonElement nestedLogin) && nestedLogin.ValueKind == JsonValueKind.Object
                    ? nestedLogin : entry;
                string name = FirstString(entry, "name", "title", "service", "site");
                string username = FirstString(login, "username", "userName", "email", "login");
                string password = FirstString(login, "password", "secret");
                if (name.Length == 0 && username.Length == 0 && password.Length == 0) continue;

                string website = FirstString(entry, "url", "website", "websiteUrl", "uri");
                if (GetProperty(login, "uris", out JsonElement uris) && uris.ValueKind == JsonValueKind.Array && uris.GetArrayLength() > 0)
                    website = FirstString(uris[0], "uri", "url") is string firstUri && firstUri.Length > 0 ? firstUri : website;
                string folder = FirstString(entry, "folder", "folderName");
                if (folder.Length == 0)
                {
                    string folderId = FirstString(entry, "folderId");
                    if (folderId.Length > 0 && folders.TryGetValue(folderId, out string? resolvedFolder))
                        folder = resolvedFolder;
                }

                var account = new SavedAccount
                {
                    ServiceName = name.Length > 0 ? name : website.Length > 0 ? website : "Tài khoản đã nhập",
                    Username = username,
                    Password = password,
                    WebsiteUrl = website,
                    Note = FirstString(entry, "notes", "note", "comments"),
                    Folder = folder,
                    IsFavorite = GetBoolean(entry, "favorite")
                };

                string totp = FirstString(login, "totp", "totpSecret", "otp");
                if (totp.Length > 0)
                {
                    try
                    {
                        TotpCredential credential = TotpService.ParseImportText(totp, account.ServiceName).First();
                        account.TotpSecret = credential.Secret;
                        account.TotpIssuer = credential.Issuer;
                        account.TotpAlgorithm = credential.Algorithm;
                        account.TotpDigits = credential.Digits;
                        account.TotpPeriodSeconds = credential.PeriodSeconds;
                    }
                    catch (FormatException)
                    {
                        account.CustomFields.Add(new AccountCustomField
                        {
                            Name = "TOTP chưa phân tích được",
                            Value = totp,
                            IsSensitive = true
                        });
                    }
                }

                if (GetProperty(entry, "fields", out JsonElement customFields) && customFields.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement field in customFields.EnumerateArray().Take(200))
                    {
                        if (field.ValueKind != JsonValueKind.Object) continue;
                        string fieldName = FirstString(field, "name", "label");
                        string fieldValue = FirstString(field, "value", "content");
                        if (fieldName.Length == 0 || fieldValue.Length == 0) continue;
                        bool sensitive = GetNumber(field, "type") == 1 || GetBoolean(field, "hidden") || GetBoolean(field, "isSensitive");
                        account.CustomFields.Add(new AccountCustomField { Name = fieldName, Value = fieldValue, IsSensitive = sensitive });
                    }
                }

                accounts.Add(account);
            }

            if (accounts.Count == 0) return Failure("JSON", "Không tìm thấy mục đăng nhập được hỗ trợ trong tệp JSON.");
            return new CredentialImportResult(true, DetectJsonFormat(root), $"Đã đọc {accounts.Count:N0} mục đăng nhập từ JSON.", accounts);
        }
        catch (JsonException)
        {
            return Failure("JSON", "Tệp không phải JSON hợp lệ hoặc dữ liệu lồng quá sâu so với giới hạn an toàn.");
        }
        catch
        {
            return Failure("JSON", "Không thể phân tích dữ liệu đăng nhập JSON.");
        }
    }

    public static CredentialImportResult ParseKeePassXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaximumFileBytes)
            return Failure("KeePass XML", "Tệp XML trống hoặc vượt giới hạn nhập 64 MB.");

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumFileBytes,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };
            using var stringReader = new StringReader(xml);
            using XmlReader reader = XmlReader.Create(stringReader, settings);
            XDocument doc = XDocument.Load(reader, LoadOptions.None);
            var accounts = new List<SavedAccount>();
            XElement? root = doc.Root;
            if (root == null || root.Name.LocalName != "KeePassFile")
                return Failure("KeePass XML", "Đây không phải file xuất KeePass XML.");

            XElement? rootGroup = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "Root")?
                .Elements().FirstOrDefault(element => element.Name.LocalName == "Group");
            if (rootGroup == null) return Failure("KeePass XML", "File xuất KeePass XML không có nhóm gốc.");
            VisitGroups(rootGroup, accounts);
            if (accounts.Count == 0) return Failure("KeePass XML", "Không tìm thấy mục đăng nhập trong file KeePass XML.");
            return new CredentialImportResult(true, "KeePass XML", $"Đã đọc {accounts.Count:N0} mục đăng nhập từ KeePass XML.", accounts);
        }
        catch (XmlException)
        {
            return Failure("KeePass XML", "Tệp không phải KeePass XML hợp lệ. DTD và thực thể bên ngoài bị từ chối.");
        }
        catch (InvalidDataException ex)
        {
            return Failure("KeePass XML", ex.Message);
        }
        catch { return Failure("KeePass XML", "Không thể phân tích file KeePass XML."); }
    }

    private static void VisitGroups(XElement rootGroup, List<SavedAccount> accounts)
    {
        var pending = new Stack<(XElement Group, string Path, int Depth)>();
        pending.Push((rootGroup, "", 0));
        while (pending.Count > 0)
        {
            (XElement group, string parentPath, int depth) = pending.Pop();
            if (depth > 64) throw new InvalidDataException("Nhóm KeePass lồng nhau vượt giới hạn 64 cấp.");
            string groupName = group.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value.Trim() ?? "";
            string path = groupName.Length > 0 && !string.Equals(groupName, "Root", StringComparison.OrdinalIgnoreCase)
                ? string.IsNullOrEmpty(parentPath) ? groupName : parentPath + " / " + groupName
                : parentPath;

            foreach (XElement entry in group.Elements().Where(element => element.Name.LocalName == "Entry"))
            {
                if (accounts.Count >= MaximumEntries) throw new InvalidDataException($"Tệp nhập có hơn {MaximumEntries:N0} mục được hỗ trợ.");
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (XElement node in entry.Elements().Where(element => element.Name.LocalName == "String").Take(200))
                {
                    string key = node.Elements().FirstOrDefault(element => element.Name.LocalName == "Key")?.Value ?? "";
                    XElement? valueElement = node.Elements().FirstOrDefault(element => element.Name.LocalName == "Value");
                    if (key.Length == 0 || valueElement == null) continue;
                    if (string.Equals(valueElement.Attribute("Protected")?.Value, "True", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("KeePass XML có trường Protected không thể giải mã an toàn. Hãy xuất bản XML dạng chữ thường bằng KeePass.");
                    fields[key] = valueElement.Value;
                }

                string title = fields.GetValueOrDefault("Title", "").Trim();
                string username = fields.GetValueOrDefault("UserName", "");
                string password = fields.GetValueOrDefault("Password", "");
                if (title.Length > 0 || username.Length > 0 || password.Length > 0)
                {
                    accounts.Add(new SavedAccount
                    {
                        ServiceName = title.Length > 0 ? title : "Tài khoản đã nhập",
                        Username = username,
                        Password = password,
                        WebsiteUrl = fields.GetValueOrDefault("URL", ""),
                        Note = fields.GetValueOrDefault("Notes", ""),
                        Folder = path
                    });
                }
            }

            foreach (XElement child in group.Elements().Where(element => element.Name.LocalName == "Group").Reverse())
                pending.Push((child, path, depth + 1));
        }
    }

    private static string ReadFileTextBounded(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length <= 0 || input.Length > MaximumFileBytes)
            throw new InvalidDataException("Tệp trống hoặc vượt giới hạn nhập 64 MB.");

        using var buffer = new MemoryStream((int)input.Length);
        byte[] chunk = new byte[64 * 1024];
        try
        {
            int read;
            while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaximumFileBytes)
                    throw new InvalidDataException("Tệp vượt giới hạn nhập 64 MB.");
                buffer.Write(chunk, 0, read);
            }

            byte[] bytes = buffer.ToArray();
            try
            {
                using var textReader = new StreamReader(new MemoryStream(bytes, writable: false), System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true);
                return textReader.ReadToEnd();
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chunk);
            if (buffer.TryGetBuffer(out ArraySegment<byte> segment) && segment.Array != null)
                CryptographicOperations.ZeroMemory(segment.Array.AsSpan());
        }
    }

    private static JsonElement FindEntryArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        foreach (string name in new[] { "items", "passwords", "entries", "logins", "credentials" })
            if (GetProperty(root, name, out JsonElement array) && array.ValueKind == JsonValueKind.Array) return array;
        throw new JsonException();
    }

    private static Dictionary<string, string> ReadFolders(JsonElement root)
    {
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!GetProperty(root, "folders", out JsonElement array) || array.ValueKind != JsonValueKind.Array) return folders;
        foreach (JsonElement item in array.EnumerateArray().Take(MaximumEntries))
        {
            string id = FirstString(item, "id");
            string name = FirstString(item, "name", "title");
            if (id.Length > 0 && name.Length > 0) folders[id] = name;
        }
        return folders;
    }

    private static string DetectJsonFormat(JsonElement root) =>
        GetProperty(root, "items", out _) ? "Bitwarden JSON" :
        GetProperty(root, "passwords", out _) ? "Tệp xuất mật khẩu JSON" : "JSON";

    private static string FirstString(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (!GetProperty(element, name, out JsonElement value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString()?.Trim() ?? "";
            if (value.ValueKind == JsonValueKind.Number) return value.ToString();
        }
        return "";
    }

    private static int GetNumber(JsonElement element, string name) =>
        GetProperty(element, name, out JsonElement value) && value.TryGetInt32(out int number) ? number : 0;

    private static bool GetBoolean(JsonElement element, string name) =>
        GetProperty(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static bool GetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
        }
        value = default;
        return false;
    }

    private static CredentialImportResult Failure(string format, string message) =>
        new(false, format, message, new List<SavedAccount>());
}
