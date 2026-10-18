using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Xác thực 2 bước bằng ứng dụng Authenticator (TOTP - RFC 6238).
    /// Tương thích Google Authenticator, Microsoft Authenticator, Authy, 2FAS...
    /// Secret được mã hóa bằng Windows DPAPI (gắn với tài khoản Windows hiện tại)
    /// và lưu tại %AppData%\PersonalInfoManager\2fa\{username}.2fa
    /// </summary>
    public static class TwoFactorService
    {
        public const string Issuer = "TQLTK";

        private const int StepSeconds = 30;
        private const int CodeModulus = 1_000_000;   // 6 chữ số
        private const int AllowedDrift = 1;          // chấp nhận lệch ±1 bước (±30 giây)
        private const int RecoveryCodeCount = 8;
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TQLTK.2FA.v1");

        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "2fa");

        private class TwoFactorData
        {
            public string Secret { get; set; } = "";
            public List<string> RecoveryHashes { get; set; } = new();
            public long LastUsedStep { get; set; } = -1;
        }

        // ================================================================
        // ================== TRẠNG THÁI ==================
        // ================================================================

        public static bool IsEnabled(string username) => File.Exists(GetPath(username));

        // ================================================================
        // ================== BẬT 2FA ==================
        // ================================================================

        /// <summary>Tạo secret ngẫu nhiên 160-bit, trả về dạng Base32.</summary>
        public static string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

        /// <summary>Chuỗi otpauth:// để tạo mã QR cho app Authenticator.</summary>
        public static string BuildOtpAuthUri(string username, string secret)
        {
            string label = Uri.EscapeDataString($"{Issuer}:{username}");
            string issuer = Uri.EscapeDataString(Issuer);
            return $"otpauth://totp/{label}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period={StepSeconds}";
        }

        /// <summary>Hiển thị secret theo nhóm 4 ký tự cho dễ nhập tay.</summary>
        public static string FormatSecret(string secret)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < secret.Length; i++)
            {
                if (i > 0 && i % 4 == 0) sb.Append(' ');
                sb.Append(secret[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Kiểm tra mã người dùng nhập khớp với secret vừa tạo, nếu đúng thì lưu lại
        /// và sinh mã khôi phục (chỉ trả về MỘT LẦN, trong file chỉ lưu bản băm).
        /// </summary>
        public static (bool ok, string message, List<string> recoveryCodes) Enable(string username, string secret, string code)
        {
            string normalized = NormalizeCode(code);
            if (normalized.Length != 6 || !normalized.All(char.IsDigit))
                return (false, "Mã phải gồm đúng 6 chữ số.", new List<string>());

            if (!TryMatch(secret, normalized, -1, out long matchedStep))
                return (false, "Mã không đúng hoặc đã hết hạn. Kiểm tra giờ trên điện thoại và máy tính rồi thử lại.", new List<string>());

            var recoveryCodes = GenerateRecoveryCodes();
            var data = new TwoFactorData
            {
                Secret = secret,
                RecoveryHashes = recoveryCodes.Select(HashRecoveryCode).ToList(),
                LastUsedStep = matchedStep
            };

            try { Save(username, data); }
            catch (Exception ex) { return (false, $"Không lưu được cấu hình 2FA: {ex.Message}", new List<string>()); }

            return (true, "Đã bật xác thực 2 bước.", recoveryCodes);
        }

        // ================================================================
        // ================== XÁC THỰC (ĐĂNG NHẬP) ==================
        // ================================================================

        /// <summary>
        /// Chấp nhận mã 6 số từ Authenticator HOẶC một mã khôi phục (dùng 1 lần).
        /// Mỗi mã TOTP chỉ dùng được 1 lần (chống dùng lại).
        /// </summary>
        public static (bool ok, string message) Validate(string username, string input)
        {
            var data = Load(username);
            if (data == null)
                return (false, "Không đọc được dữ liệu 2FA của tài khoản này.");

            string code = NormalizeCode(input);
            if (code.Length == 0)
                return (false, "Vui lòng nhập mã.");

            // ---- Mã TOTP 6 số ----
            if (code.Length == 6 && code.All(char.IsDigit))
            {
                if (!TryMatch(data.Secret, code, data.LastUsedStep, out long matchedStep))
                    return (false, "Mã không đúng hoặc đã được dùng.");

                data.LastUsedStep = matchedStep;
                try { Save(username, data); } catch { }
                return (true, "");
            }

            // ---- Mã khôi phục (10 ký tự) ----
            if (code.Length == 10)
            {
                string hash = HashRecoveryCode(code);
                string? found = data.RecoveryHashes.FirstOrDefault(h => FixedTimeEquals(h, hash));
                if (found == null)
                    return (false, "Mã khôi phục không đúng hoặc đã được dùng.");

                data.RecoveryHashes.Remove(found);
                try { Save(username, data); }
                catch (Exception ex) { return (false, $"Lỗi lưu dữ liệu 2FA: {ex.Message}"); }

                return (true, $"Bạn vừa dùng 1 mã khôi phục. Còn lại {data.RecoveryHashes.Count} mã.\nHãy tắt rồi bật lại 2FA nếu bạn đã mất điện thoại.");
            }

            return (false, "Nhập mã 6 số từ Authenticator hoặc mã khôi phục (10 ký tự).");
        }

        // ================================================================
        // ================== TẮT 2FA ==================
        // ================================================================

        /// <summary>Tắt 2FA — bắt buộc nhập đúng mã hiện tại (hoặc mã khôi phục).</summary>
        public static (bool ok, string message) Disable(string username, string input)
        {
            var result = Validate(username, input);
            if (!result.ok) return result;

            try { File.Delete(GetPath(username)); }
            catch (Exception ex) { return (false, $"Không xóa được cấu hình 2FA: {ex.Message}"); }

            return (true, "");
        }

        // ================================================================
        // ================== TẠO LẠI MÃ KHÔI PHỤC ==================
        // ================================================================

        /// <summary>Số mã khôi phục còn lại (chưa dùng). Trả về -1 nếu không đọc được dữ liệu.</summary>
        public static int GetRecoveryCodeCount(string username)
        {
            var data = Load(username);
            return data?.RecoveryHashes.Count ?? -1;
        }

        /// <summary>
        /// Bước 1: kiểm tra mã 6 số HIỆN TẠI từ Authenticator (KHÔNG nhận mã khôi phục),
        /// rồi sinh 8 mã mới. CHƯA lưu và CHƯA vô hiệu hóa mã cũ: chỉ khi người dùng
        /// xác nhận đã lưu mã mới thì mới gọi CommitRecoveryCodes.
        /// </summary>
        public static (bool ok, string message, List<string> recoveryCodes) PrepareRecoveryCodes(string username, string input)
        {
            var data = Load(username);
            if (data == null)
                return (false, "Không đọc được dữ liệu 2FA của tài khoản này.", new List<string>());

            string code = NormalizeCode(input);
            if (code.Length != 6 || !code.All(char.IsDigit))
                return (false, "Nhập mã 6 số hiện tại từ Authenticator (không dùng mã khôi phục).", new List<string>());

            if (!TryMatch(data.Secret, code, data.LastUsedStep, out long matchedStep))
                return (false, "Mã không đúng hoặc đã được dùng. Hãy đợi mã mới (tối đa 30 giây) rồi nhập lại.", new List<string>());

            // Ghi nhận mã TOTP đã dùng (chống dùng lại)
            data.LastUsedStep = matchedStep;
            try { Save(username, data); } catch { }

            return (true, "", GenerateRecoveryCodes());
        }

        /// <summary>
        /// Bước 2: lưu bộ mã mới (trong file chỉ lưu bản băm) và vô hiệu hóa TOÀN BỘ mã cũ.
        /// </summary>
        public static (bool ok, string message) CommitRecoveryCodes(string username, List<string> newCodes)
        {
            if (newCodes == null || newCodes.Count == 0)
                return (false, "Không có mã khôi phục để lưu.");

            var data = Load(username);
            if (data == null)
                return (false, "Không đọc được dữ liệu 2FA của tài khoản này.");

            data.RecoveryHashes = newCodes.Select(HashRecoveryCode).ToList();

            try { Save(username, data); }
            catch (Exception ex) { return (false, $"Không lưu được mã khôi phục mới: {ex.Message}"); }

            return (true, "");
        }

        // ================================================================
        // ================== TOTP / HOTP ==================
        // ================================================================

        private static long CurrentStep() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;

        private static bool TryMatch(string secret, string code, long lastUsedStep, out long matchedStep)
        {
            matchedStep = -1;

            byte[] key;
            try { key = Base32Decode(secret); }
            catch { return false; }

            byte[] input = Encoding.ASCII.GetBytes(code);
            long now = CurrentStep();
            bool found = false;

            for (long step = now - AllowedDrift; step <= now + AllowedDrift; step++)
            {
                byte[] expected = Encoding.ASCII.GetBytes(ComputeCode(key, step));
                bool equal = CryptographicOperations.FixedTimeEquals(expected, input);

                if (equal && step > lastUsedStep)
                {
                    matchedStep = step;
                    found = true;
                }
            }
            return found;
        }

        private static string ComputeCode(byte[] key, long step)
        {
            byte[] counter = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian) Array.Reverse(counter);

            using var hmac = new HMACSHA1(key);
            byte[] hash = hmac.ComputeHash(counter);

            int offset = hash[^1] & 0x0F;
            int binary =
                ((hash[offset] & 0x7F) << 24) |
                (hash[offset + 1] << 16) |
                (hash[offset + 2] << 8) |
                hash[offset + 3];

            return (binary % CodeModulus).ToString("D6");
        }

        // ================================================================
        // ================== MÃ KHÔI PHỤC ==================
        // ================================================================

        private static List<string> GenerateRecoveryCodes()
        {
            var list = new List<string>();
            for (int i = 0; i < RecoveryCodeCount; i++)
            {
                var chars = new char[10];
                for (int j = 0; j < chars.Length; j++)
                    chars[j] = Base32Alphabet[RandomNumberGenerator.GetInt32(Base32Alphabet.Length)];

                string raw = new string(chars);
                list.Add($"{raw.Substring(0, 5)}-{raw.Substring(5)}"); // dạng XXXXX-XXXXX
            }
            return list;
        }

        private static string HashRecoveryCode(string code)
        {
            string normalized = NormalizeCode(code);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(hash);
        }

        private static bool FixedTimeEquals(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

        private static string NormalizeCode(string? input) =>
            (input ?? "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

        // ================================================================
        // ================== BASE32 ==================
        // ================================================================

        private static string Base32Encode(byte[] data)
        {
            var sb = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0, bitsLeft = 0;

            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;
                while (bitsLeft >= 5)
                {
                    sb.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                    bitsLeft -= 5;
                }
                buffer &= (1 << bitsLeft) - 1;
            }

            if (bitsLeft > 0)
                sb.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);

            return sb.ToString();
        }

        private static byte[] Base32Decode(string input)
        {
            string s = input.Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
            var bytes = new List<byte>();
            int buffer = 0, bitsLeft = 0;

            foreach (char c in s)
            {
                int val = Base32Alphabet.IndexOf(c);
                if (val < 0) throw new FormatException("Ký tự Base32 không hợp lệ.");

                buffer = (buffer << 5) | val;
                bitsLeft += 5;
                if (bitsLeft >= 8)
                {
                    bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                    bitsLeft -= 8;
                    buffer &= (1 << bitsLeft) - 1;
                }
            }
            return bytes.ToArray();
        }

        // ================================================================
        // ================== LƯU TRỮ (DPAPI) ==================
        // ================================================================

        private static string GetPath(string username)
        {
            // Làm sạch tên user để làm tên file an toàn
            string safe = string.Concat(username.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(Folder, safe + ".2fa");
        }

        private static void Save(string username, TwoFactorData data)
        {
            Directory.CreateDirectory(Folder);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(data);
            byte[] encrypted = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(GetPath(username), encrypted);
        }

        private static TwoFactorData? Load(string username)
        {
            try
            {
                string path = GetPath(username);
                if (!File.Exists(path)) return null;

                byte[] encrypted = File.ReadAllBytes(path);
                byte[] json = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<TwoFactorData>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}