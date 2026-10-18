using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Dịch vụ Import/Export CSV cho tài khoản.
    /// Hỗ trợ định dạng: TQLTK, Chrome, Bitwarden, LastPass, Generic.
    /// </summary>
    public static class CsvService
    {
        // ================================================================
        // ================== EXPORT ==================
        // ================================================================

        /// <summary>
        /// Xuất danh sách tài khoản ra file CSV.
        /// </summary>
        public static (bool ok, string message) ExportToFile(
            List<SavedAccount> accounts, string filePath)
        {
            try
            {
                var sb = new StringBuilder();

                // Header
                sb.AppendLine("ServiceName,Username,Password,Note,CreatedAt,UpdatedAt");

                // Rows
                foreach (var acc in accounts)
                {
                    sb.AppendLine(string.Join(",",
                        EscapeCsv(acc.ServiceName),
                        EscapeCsv(acc.Username),
                        EscapeCsv(acc.Password),
                        EscapeCsv(acc.Note),
                        EscapeCsv(acc.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                        EscapeCsv(acc.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"))
                    ));
                }

                // Ghi file với UTF-8 BOM (để Excel đọc đúng tiếng Việt)
                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));

                return (true, $"Đã xuất {accounts.Count} tài khoản ra:\n{filePath}");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi xuất CSV: {ex.Message}");
            }
        }

        /// <summary>
        /// Escape 1 trường CSV theo chuẩn RFC 4180.
        /// </summary>
        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";

            // Nếu có dấu phẩy, xuống dòng, hoặc dấu ngoặc kép → bọc trong ""
            if (value.Contains(',') || value.Contains('"') ||
                value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        // ================================================================
        // ================== IMPORT ==================
        // ================================================================

        /// <summary>
        /// Đọc file CSV và parse thành danh sách tài khoản.
        /// Tự động phát hiện format: TQLTK, Chrome, Bitwarden, LastPass.
        /// </summary>
        public static (bool ok, string message, List<SavedAccount> accounts)
            ImportFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, "File không tồn tại.", new List<SavedAccount>());

                // Đọc toàn bộ file
                string content = File.ReadAllText(filePath, Encoding.UTF8);
                var lines = ParseCsvLines(content);

                if (lines.Count < 2)
                    return (false, "File CSV rỗng hoặc không có dữ liệu.", new List<SavedAccount>());

                // Parse header
                var header = lines[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
                var accounts = new List<SavedAccount>();

                // Phát hiện format
                string format = DetectFormat(header);

                // Parse từng dòng
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
                        // Bỏ qua dòng lỗi
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

        /// <summary>
        /// Phát hiện format dựa trên header.
        /// </summary>
        private static string DetectFormat(List<string> header)
        {
            var h = header.Select(x => x.Trim().ToLowerInvariant()).ToList();

            // Bitwarden: folder, favorite, type, name, notes, fields, reprompt, login_uri, login_username, login_password, login_totp
            if (h.Contains("login_username") || h.Contains("login_password"))
                return "Bitwarden";

            // LastPass: url, username, password, extra, name, grouping, fav
            if (h.Contains("url") && h.Contains("grouping"))
                return "LastPass";

            // Chrome: name, url, username, password, note
            if (h.Contains("url") && h.Contains("name") && h.Contains("username") && h.Contains("password"))
                return "Chrome";

            // TQLTK: servicename, username, password, note, createdat, updatedat
            if (h.Contains("servicename"))
                return "TQLTK";

            return "Generic";
        }

        /// <summary>
        /// Parse 1 dòng CSV thành SavedAccount.
        /// </summary>
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

            switch (format)
            {
                case "TQLTK":
                    serviceName = Get("servicename");
                    username = Get("username");
                    password = Get("password");
                    note = Get("note");
                    break;

                case "Bitwarden":
                    serviceName = Get("name");
                    username = Get("login_username");
                    password = Get("login_password");
                    note = Get("notes");
                    break;

                case "LastPass":
                    serviceName = Get("name");
                    if (string.IsNullOrEmpty(serviceName)) serviceName = Get("url");
                    username = Get("username");
                    password = Get("password");
                    note = Get("extra");
                    break;

                case "Chrome":
                    serviceName = Get("name");
                    if (string.IsNullOrEmpty(serviceName)) serviceName = Get("url");
                    username = Get("username");
                    password = Get("password");
                    note = Get("note");
                    break;

                default: // Generic
                    // Tự đoán: cột 1 = service, cột 2 = username, cột 3 = password
                    serviceName = row.Count > 0 ? row[0].Trim() : "";
                    username = row.Count > 1 ? row[1].Trim() : "";
                    password = row.Count > 2 ? row[2].Trim() : "";
                    note = row.Count > 3 ? row[3].Trim() : "";
                    break;
            }

            // Nếu không có gì → bỏ
            if (string.IsNullOrWhiteSpace(serviceName) &&
                string.IsNullOrWhiteSpace(username) &&
                string.IsNullOrWhiteSpace(password))
                return null;

            return new SavedAccount
            {
                Id = Guid.NewGuid(),              // ✅ Sửa: Guid, không phải string
                ServiceName = serviceName,
                Username = username,
                Password = password,
                Note = note,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        /// <summary>
        /// Parse nội dung CSV thành danh sách các dòng, mỗi dòng là list các cột.
        /// Hỗ trợ field có dấu ngoặc kép và xuống dòng bên trong.
        /// </summary>
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
                        // Kiểm tra nếu là "" (escape) thì thêm 1 dấu "
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
                        // Kết thúc dòng
                        if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                            i++; // skip \n

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

            // Dòng cuối cùng (nếu không kết thúc bằng \n)
            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString());
                if (currentRow.Count > 0)
                    result.Add(currentRow);
            }

            return result;
        }

        // ================================================================
        // ================== PREVIEW / MERGE ==================
        // ================================================================

        /// <summary>
        /// Loại bỏ tài khoản trùng lặp (theo ServiceName + Username).
        /// </summary>
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