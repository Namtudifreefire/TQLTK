using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Đọc nhật ký hoạt động (ActivityLogger) để báo cho người dùng biết có lần đăng nhập thất bại nào
    /// vào tài khoản của họ kể từ lần đăng nhập thành công trước. Chỉ đọc nhật ký, không ghi gì.
    ///
    /// Định dạng một dòng log: [yyyy-MM-dd HH:mm:ss] [tên người dùng] [HÀNH_ĐỘNG] chi tiết
    /// Nhật ký chỉ được giữ 30 ngày (ActivityLogger.CleanupOldLogs), nên không đếm được xa hơn.
    /// </summary>
    public static class LoginHistory
    {
        private static readonly Regex LineRx = new(
            @"^\[(?<t>[^\]]+)\] \[(?<u>[^\]]*)\] \[(?<a>[^\]]+)\]",
            RegexOptions.Compiled);

        /// <summary>
        /// Bỏ qua các lần sai trong N phút gần nhất: thường là chính người dùng vừa gõ nhầm
        /// rồi mới đăng nhập đúng, không phải người lạ.
        /// </summary>
        private const int IgnoreRecentMinutes = 2;

        /// <summary>
        /// Trả về nội dung cảnh báo, hoặc null nếu không có gì đáng báo.
        /// PHẢI gọi TRƯỚC khi ghi dòng LOGIN của lần đăng nhập hiện tại,
        /// nếu không "lần đăng nhập trước" sẽ bị hiểu là ngay bây giờ.
        /// </summary>
        public static string? BuildWarning(string username)
        {
            try { return BuildWarningCore(username, DateTime.Now); }
            catch { return null; }   // không để việc đọc nhật ký làm hỏng đăng nhập
        }

        private static string? BuildWarningCore(string username, DateTime now)
        {
            // Tên file activity_yyyy-MM-dd.log xếp theo chữ cái cũng là xếp theo ngày; đọc từ mới nhất về cũ
            var files = ActivityLogger.ListLogFiles()
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            DateTime cutoff = now.AddMinutes(-IgnoreRecentMinutes);

            int passwordFails = 0;
            var twoFaStamps = new HashSet<string>();   // LOGIN_2FA_FAILED được ghi hai lần (hộp thoại + LoginWindow) nên gộp theo giây
            DateTime? lastFail = null;
            DateTime? previousLogin = null;
            bool foundLogin = false;

            foreach (var file in files)
            {
                string[] lines;
                try { lines = File.ReadAllLines(file, Encoding.UTF8); }
                catch { continue; }

                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var m = LineRx.Match(lines[i]);
                    if (!m.Success) continue;

                    if (!string.Equals(m.Groups["u"].Value, username, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!DateTime.TryParseExact(m.Groups["t"].Value, "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
                        continue;

                    string action = m.Groups["a"].Value;

                    // Đăng nhập mật khẩu và Windows Hello đều ghi thêm dòng LOGIN khi vào được app
                    if (action == "LOGIN")
                    {
                        previousLogin = when;
                        foundLogin = true;
                        break;
                    }

                    if (when > cutoff) continue;

                    if (action == "LOGIN_FAILED")
                    {
                        passwordFails++;
                        lastFail ??= when;   // đang đọc từ mới về cũ nên lần gặp đầu tiên là lần gần nhất
                    }
                    else if (action == "LOGIN_2FA_FAILED" && twoFaStamps.Add(m.Groups["t"].Value))
                    {
                        lastFail ??= when;
                    }
                }

                if (foundLogin) break;
            }

            int twoFaFails = twoFaStamps.Count;
            if (passwordFails == 0 && twoFaFails == 0) return null;

            string since = foundLogin
                ? $"kể từ lần đăng nhập trước ({previousLogin:dd/MM/yyyy HH:mm})"
                : "trong nhật ký gần đây (tối đa 30 ngày)";

            var sb = new StringBuilder();
            sb.Append($"Có đăng nhập thất bại vào tài khoản '{username}' {since}:\n\n");
            if (passwordFails > 0) sb.Append($"• {passwordFails} lần sai mật khẩu\n");
            if (twoFaFails > 0) sb.Append($"• {twoFaFails} lần sai hoặc hủy mã xác thực 2 bước\n");
            if (lastFail is { } lf) sb.Append($"\nLần gần nhất: {lf:dd/MM/yyyy HH:mm:ss}\n");
            sb.Append("\nNếu không phải bạn, hãy đổi mật khẩu ngay (Hệ thống → Đổi mật khẩu) và xem lại nhật ký hoạt động.");

            return sb.ToString();
        }
    }
}