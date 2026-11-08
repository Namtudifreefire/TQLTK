using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace PersonalInfoManager.Services;
public sealed record ActivityLogRecord(int Version, Guid Id, DateTimeOffset Timestamp,
    string Username, string EventId, string Detail, string? MessageId, JsonElement[] Arguments);
internal static class ActivityLogCodec
{
    internal const int MaximumLineLength = 65536;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 8 };
    private static readonly Regex EventCode = new("^[A-Z][A-Z0-9_]{0,79}$", RegexOptions.CultureInvariant);
    internal static ActivityLogRecord Create(DateTimeOffset timestamp, string username, string action,
        string detail = "", string? messageId = null, object?[]? arguments = null)
    {
        var record = new ActivityLogRecord(2, Guid.NewGuid(), timestamp, username, action, detail, messageId,
            (arguments ?? Array.Empty<object?>()).Select(value => JsonSerializer.SerializeToElement(value)).ToArray());
        Validate(record);
        return record;
    }
    internal static string Serialize(ActivityLogRecord record)
    {
        Validate(record);
        string line = JsonSerializer.Serialize(record, Options);
        if (line.Length > MaximumLineLength) throw new InvalidDataException("Activity record is too large.");
        return line;
    }
    internal static bool TryParse(string line, out ActivityLogRecord? record)
    {
        record = null;
        if (line.Length > MaximumLineLength || !line.TrimStart().StartsWith('{')) return false;
        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) return false;
            string[] required = { "Version", "Id", "Timestamp", "Username", "EventId", "Detail", "MessageId", "Arguments" };
            if (required.Any(name => !names.Contains(name))) return false;
            var parsed = JsonSerializer.Deserialize<ActivityLogRecord>(line, Options);
            if (parsed == null) return false;
            Validate(parsed);
            record = parsed;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidDataException or FormatException) { return false; }
    }
    internal static object?[] GetArguments(ActivityLogRecord record) => record.Arguments.Select(value => value.ValueKind switch
    {
        JsonValueKind.String => (object?)value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out long integer) ? integer : (object)value.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    }).ToArray();
    private static void Validate(ActivityLogRecord record)
    {
        if (record.Version != 2 || record.Id == Guid.Empty || record.Timestamp == default ||
            record.Username == null || record.Username.Length > 512 || record.EventId == null || !EventCode.IsMatch(record.EventId) ||
            record.Detail == null || record.Detail.Length > 16000 || record.Arguments == null || record.Arguments.Length > 16 ||
            record.MessageId?.Length > 160 || (record.MessageId != null && record.Detail.Length != 0))
            throw new InvalidDataException("Invalid activity record.");
        foreach (var argument in record.Arguments)
            if (argument.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null) ||
                (argument.ValueKind == JsonValueKind.String && argument.GetString()!.Length > 4000))
                throw new InvalidDataException("Invalid activity argument.");
        if (record.MessageId == null && record.Arguments.Length != 0) throw new InvalidDataException("Arguments require a message ID.");
    }
}
