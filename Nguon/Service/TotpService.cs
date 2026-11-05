using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalInfoManager.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using FormatException = System.FormatException;

namespace PersonalInfoManager.Services;

public static class TotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int MaxMigrationPayloadBytes = 1_000_000;

    public static IReadOnlyList<TotpCredential> ParseImportText(string input, string fallbackName)
    {
        string value = (input ?? "").Trim();
        if (value.Length == 0) throw new FormatException("Nhập khóa Base32 hoặc URI otpauth trước.");

        if (value.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase))
            return ParseMigrationUri(value);
        if (value.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            return new[] { ParseOtpAuthUri(value) };

        byte[] secret = DecodeAndValidateSecret(value);
        try
        {
            return new[] { new TotpCredential(
                string.IsNullOrWhiteSpace(fallbackName) ? "Tài khoản" : fallbackName,
                fallbackName ?? "", EncodeBase32(secret), "SHA1", 6, 30) };
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    public static IReadOnlyList<TotpCredential> DecodeQrImage(string filePath)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists || file.Length <= 0 || file.Length > 20 * 1024 * 1024)
            throw new InvalidDataException("Ảnh QR không tồn tại, rỗng hoặc vượt quá 20 MB.");

        BitmapSource frame;
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new InvalidDataException("Không tìm thấy ảnh trong file.");
            frame = decoder.Frames[0];
        }

        double scale = Math.Min(1d, Math.Min(2048d / frame.PixelWidth, 2048d / frame.PixelHeight));
        BitmapSource sized = frame;
        if (scale < 1d)
        {
            var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            scaled.Freeze();
            sized = scaled;
        }

        var bgra = new FormatConvertedBitmap(sized, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();
        int stride = checked(bgra.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * bgra.PixelHeight)];
        bgra.CopyPixels(pixels, stride, 0);

        try
        {
            var luminance = new RGBLuminanceSource(
                pixels, bgra.PixelWidth, bgra.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32);
            var reader = new BarcodeReaderGeneric { AutoRotate = true };
            reader.Options.TryHarder = true;
            reader.Options.PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE };
            var result = reader.Decode(luminance)
                ?? throw new InvalidDataException("Không đọc được mã QR trong ảnh.");
            return ParseImportText(result.Text, "Tài khoản");
        }
        finally { CryptographicOperations.ZeroMemory(pixels); }
    }

    public static IReadOnlyList<TotpCredential> ImportAuthenticatorFile(string filePath, string fallbackName)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists || file.Length <= 0 || file.Length > 10 * 1024 * 1024)
            throw new InvalidDataException("Tệp Authenticator không tồn tại, rỗng hoặc vượt quá 10 MB.");

        string content = File.ReadAllText(filePath, Encoding.UTF8).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (content.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase) ||
            content.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase))
            return ParseImportText(content, fallbackName);

        if (Path.GetExtension(filePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var textEntries = new List<TotpCredential>();
            foreach (string line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string uri = line.Trim();
                if (!uri.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase) &&
                    !uri.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase)) continue;
                textEntries.AddRange(ParseImportText(uri, fallbackName));
            }
            if (textEntries.Count > 0) return textEntries;
        }

        JsonDocument document;
        try { document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException ex) { throw new FormatException("Tệp không phải URI TOTP hoặc JSON export được hỗ trợ.", ex); }

        using (document)
        {
            JsonElement root = document.RootElement;
            JsonElement entries;

            if (root.ValueKind == JsonValueKind.Object && TryProperty(root, "db", out var db))
            {
                if (TryProperty(root, "header", out var header) && IsEncryptedAegisExport(header))
                    throw new FormatException("Aegis export đang được mã hóa. Hãy mở khóa trong Aegis rồi xuất bản JSON không mã hóa; ứng dụng không nhận mật khẩu export.");
                if (!TryProperty(db, "entries", out entries) || entries.ValueKind != JsonValueKind.Array)
                    throw new FormatException("Không tìm thấy danh sách mục trong Aegis export.");
            }
            else if (root.ValueKind == JsonValueKind.Object && TryProperty(root, "services", out var services) && services.ValueKind == JsonValueKind.Array)
            {
                entries = services;
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                entries = root;
            }
            else
            {
                throw new FormatException("JSON export không thuộc định dạng Aegis, 2FAS hoặc danh sách Authy không mã hóa được hỗ trợ.");
            }

            var imported = new List<TotpCredential>();
            foreach (var item in entries.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (TryProperty(item, "type", out var typeElement) &&
                    typeElement.ValueKind == JsonValueKind.String &&
                    !typeElement.GetString()!.Equals("totp", StringComparison.OrdinalIgnoreCase)) continue;

                if (TryProperty(item, "otp", out var otp) && TryProperty(otp, "link", out var link) &&
                    link.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(link.GetString()))
                {
                    try { imported.AddRange(ParseImportText(link.GetString()!, fallbackName)); }
                    catch (FormatException) { }
                    continue;
                }

                JsonElement info = TryProperty(item, "info", out var aegisInfo) ? aegisInfo :
                    (TryProperty(item, "otp", out var twofasOtp) ? twofasOtp : item);
                string secretValue = GetString(info, "secret");
                if (secretValue.Length == 0) secretValue = GetString(item, "secret");
                if (secretValue.Length == 0) continue;

                string issuer = GetString(item, "issuer");
                if (issuer.Length == 0) issuer = GetString(info, "issuer");
                string name = GetString(item, "name");
                string account = GetString(item, "account");
                if (account.Length == 0) account = GetString(info, "account");
                if (name.Length == 0) name = issuer;
                if (name.Length == 0) name = fallbackName;

                string algorithm = GetString(info, "algo");
                if (algorithm.Length == 0) algorithm = GetString(info, "algorithm");
                if (algorithm.Length == 0) algorithm = "SHA1";
                algorithm = algorithm.ToUpperInvariant();
                int digits = GetInt(info, "digits", 6);
                int period = GetInt(info, "period", 30);
                ValidateOptions(algorithm, digits, period);

                byte[] secret = DecodeAndValidateSecret(secretValue);
                try
                {
                    imported.Add(new TotpCredential(
                        account.Length == 0 ? name : $"{name} ({account})",
                        issuer, EncodeBase32(secret), algorithm, digits, period));
                }
                finally { CryptographicOperations.ZeroMemory(secret); }
            }

            if (imported.Count == 0)
                throw new FormatException("Không tìm thấy mục TOTP hợp lệ trong tệp Authenticator.");
            return imported;
        }
    }

    public static (string Code, int SecondsRemaining) GetCurrentCode(
        string secretBase32, string algorithm, int digits, int periodSeconds, DateTimeOffset? now = null)
    {
        if (digits is not (6 or 8)) throw new ArgumentOutOfRangeException(nameof(digits));
        if (periodSeconds is < 5 or > 120) throw new ArgumentOutOfRangeException(nameof(periodSeconds));

        byte[] secret = DecodeAndValidateSecret(secretBase32);
        try
        {
            long unix = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            long step = Math.DivRem(unix, periodSeconds, out long remainder);
            byte[] counter = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian) Array.Reverse(counter);

            using HMAC hmac = algorithm.ToUpperInvariant() switch
            {
                "SHA1" => new HMACSHA1(secret),
                "SHA256" => new HMACSHA256(secret),
                "SHA512" => new HMACSHA512(secret),
                _ => throw new FormatException("Thuật toán TOTP không được hỗ trợ.")
            };

            byte[] hash = hmac.ComputeHash(counter);
            try
            {
                int offset = hash[^1] & 0x0F;
                uint binary = (uint)((hash[offset] & 0x7F) << 24 |
                                     (hash[offset + 1] & 0xFF) << 16 |
                                     (hash[offset + 2] & 0xFF) << 8 |
                                     (hash[offset + 3] & 0xFF));
                ulong modulus = digits == 6 ? 1_000_000UL : 100_000_000UL;
                string code = (binary % modulus).ToString(digits == 6 ? "D6" : "D8", CultureInfo.InvariantCulture);
                return (code, periodSeconds - (int)remainder);
            }
            finally { CryptographicOperations.ZeroMemory(hash); }
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    private static TotpCredential ParseOtpAuthUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Chỉ hỗ trợ URI otpauth://totp.");

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("secret", out string? rawSecret))
            throw new FormatException("URI otpauth không có secret.");

        string label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        query.TryGetValue("issuer", out string? issuer);
        if (string.IsNullOrWhiteSpace(issuer) && label.Contains(':'))
            issuer = label[..label.IndexOf(':')].Trim();
        string name = label.Contains(':') ? label[(label.IndexOf(':') + 1)..].Trim() : label;
        if (name.Length == 0) name = issuer ?? "Tài khoản";

        string algorithm = query.GetValueOrDefault("algorithm", "SHA1").ToUpperInvariant();
        int digits = ParseInt(query.GetValueOrDefault("digits", "6"), "digits");
        int period = ParseInt(query.GetValueOrDefault("period", "30"), "period");
        ValidateOptions(algorithm, digits, period);

        byte[] secret = DecodeAndValidateSecret(rawSecret);
        try { return new TotpCredential(name, issuer ?? "", EncodeBase32(secret), algorithm, digits, period); }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    private static IReadOnlyList<TotpCredential> ParseMigrationUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new FormatException("URI chuyển mã Authenticator không hợp lệ.");
        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("data", out string? encoded) || encoded.Length == 0)
            throw new FormatException("URI chuyển mã không có dữ liệu.");

        string base64 = encoded.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        byte[] payload;
        try { payload = Convert.FromBase64String(base64); }
        catch (FormatException ex) { throw new FormatException("Dữ liệu chuyển mã không phải Base64 hợp lệ.", ex); }
        if (payload.Length == 0 || payload.Length > MaxMigrationPayloadBytes)
            throw new FormatException("Kích thước dữ liệu chuyển mã không hợp lệ.");

        try
        {
            var entries = new List<TotpCredential>();
            int position = 0;
            while (position < payload.Length)
            {
                ulong tag = ReadVarint(payload, ref position);
                int field = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                if (field == 1 && wire == 2)
                {
                    byte[] item = ReadLengthDelimited(payload, ref position);
                    var entry = ParseMigrationItem(item);
                    if (entry != null) entries.Add(entry);
                    CryptographicOperations.ZeroMemory(item);
                }
                else SkipField(payload, ref position, wire);
            }

            if (entries.Count == 0)
                throw new FormatException("QR không chứa mục TOTP được hỗ trợ. Mã HOTP hoặc MD5 không được nhập.");
            return entries;
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }

    private static TotpCredential? ParseMigrationItem(byte[] payload)
    {
        byte[]? secret = null;
        string name = "";
        string issuer = "";
        ulong algorithm = 0, digits = 0, type = 0;
        int position = 0;

        while (position < payload.Length)
        {
            ulong tag = ReadVarint(payload, ref position);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (wire == 2 && field is 1 or 2 or 3)
            {
                byte[] bytes = ReadLengthDelimited(payload, ref position);
                if (field == 1) secret = bytes;
                else
                {
                    string text = Encoding.UTF8.GetString(bytes);
                    if (field == 2) name = text;
                    else issuer = text;
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            else if (wire == 0)
            {
                ulong number = ReadVarint(payload, ref position);
                if (field == 4) algorithm = number;
                else if (field == 5) digits = number;
                else if (field == 6) type = number;
            }
            else SkipField(payload, ref position, wire);
        }

        if (secret == null || secret.Length is < 10 or > 64)
        {
            if (secret != null) CryptographicOperations.ZeroMemory(secret);
            return null;
        }

        try
        {
            if (type == 1) return null;
            string hashAlgorithm = algorithm switch { 0 or 1 => "SHA1", 2 => "SHA256", 3 => "SHA512", _ => "" };
            int digitCount = digits switch { 0 or 1 => 6, 2 => 8, _ => 0 };
            if (hashAlgorithm.Length == 0 || digitCount == 0) return null;

            if (name.Length == 0) name = issuer;
            if (issuer.Length == 0 && name.Contains(':'))
                issuer = name[..name.IndexOf(':')].Trim();
            if (name.Contains(':')) name = name[(name.IndexOf(':') + 1)..].Trim();
            return new TotpCredential(name.Length == 0 ? "Tài khoản" : name,
                issuer, EncodeBase32(secret), hashAlgorithm, digitCount, 30);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            string value = Uri.UnescapeDataString(separator < 0 ? "" : pair[(separator + 1)..]);
            values[key] = value;
        }
        return values;
    }

    private static bool TryProperty(JsonElement value, string name, out JsonElement property)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in value.EnumerateObject())
            {
                if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }
        property = default;
        return false;
    }

    private static string GetString(JsonElement value, string name) =>
        TryProperty(value, name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? "" : "";

    private static int GetInt(JsonElement value, string name, int fallback) =>
        TryProperty(value, name, out var property) && property.TryGetInt32(out int result) ? result : fallback;

    private static bool IsEncryptedAegisExport(JsonElement header)
    {
        if (header.ValueKind != JsonValueKind.Object) return false;
        bool slotsSet = TryProperty(header, "slots", out var slots) && slots.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
        bool paramsSet = TryProperty(header, "params", out var parameters) && parameters.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
        return slotsSet || paramsSet;
    }

    private static int ParseInt(string value, string name) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed : throw new FormatException($"Tham số {name} trong URI không hợp lệ.");

    private static void ValidateOptions(string algorithm, int digits, int period)
    {
        if (algorithm is not ("SHA1" or "SHA256" or "SHA512"))
            throw new FormatException("TOTP chỉ hỗ trợ SHA1, SHA256 hoặc SHA512.");
        if (digits is not (6 or 8)) throw new FormatException("TOTP chỉ hỗ trợ mã 6 hoặc 8 chữ số.");
        if (period is < 5 or > 120) throw new FormatException("Chu kỳ TOTP phải từ 5 đến 120 giây.");
    }

    private static byte[] DecodeAndValidateSecret(string input)
    {
        string normalized = (input ?? "").Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
        if (normalized.Length is < 16 or > 128 || normalized.Any(c => !Base32Alphabet.Contains(c)))
            throw new FormatException("Secret phải là chuỗi Base32 hợp lệ dài 16–128 ký tự.");

        var bytes = new List<byte>(normalized.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (char c in normalized)
        {
            buffer = (buffer << 5) | Base32Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
                buffer &= (1 << bits) - 1;
            }
        }
        if (bytes.Count is < 10 or > 64)
            throw new FormatException("Secret TOTP phải giải mã thành 10–64 byte.");
        return bytes.ToArray();
    }

    private static string EncodeBase32(byte[] bytes)
    {
        var output = new StringBuilder((bytes.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (byte value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) output.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    private static ulong ReadVarint(byte[] data, ref int position)
    {
        ulong result = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (position >= data.Length) throw new FormatException("Dữ liệu Protobuf bị cắt.");
            byte current = data[position++];
            result |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0) return result;
        }
        throw new FormatException("Varint Protobuf quá dài.");
    }

    private static byte[] ReadLengthDelimited(byte[] data, ref int position)
    {
        ulong length = ReadVarint(data, ref position);
        if (length > (ulong)(data.Length - position)) throw new FormatException("Trường Protobuf vượt quá dữ liệu.");
        int size = checked((int)length);
        byte[] result = data.AsSpan(position, size).ToArray();
        position += size;
        return result;
    }

    private static void SkipField(byte[] data, ref int position, int wire)
    {
        switch (wire)
        {
            case 0: _ = ReadVarint(data, ref position); break;
            case 1: Advance(data, ref position, 8); break;
            case 2:
                ulong length = ReadVarint(data, ref position);
                if (length > int.MaxValue) throw new FormatException("Trường Protobuf quá lớn.");
                Advance(data, ref position, (int)length);
                break;
            case 5: Advance(data, ref position, 4); break;
            default: throw new FormatException("Loại trường Protobuf không được hỗ trợ.");
        }
    }

    private static void Advance(byte[] data, ref int position, int count)
    {
        if (count < 0 || count > data.Length - position) throw new FormatException("Dữ liệu Protobuf bị cắt.");
        position += count;
    }
}
