using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalInfoManager.Services
{







    public static class LoginHistory
    {
        private static readonly Regex LineRx = new(
            @"^\[(?<t>[^\]]+)\] \[(?<u>[^\]]*)\] \[(?<a>[^\]]+)\]",
            RegexOptions.Compiled);





        private const int IgnoreRecentMinutes = 2;






        public static string? BuildWarning(string username)
        {
            try { return BuildWarningCore(username, DateTime.Now); }
            catch { return null; }
        }

        private static string? BuildWarningCore(string username, DateTime now)
        {

            var files = ActivityLogger.ListLogFiles()
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            DateTime cutoff = now.AddMinutes(-IgnoreRecentMinutes);

            int passwordFails = 0;
            var twoFaStamps = new HashSet<string>();
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
                        lastFail ??= when;
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