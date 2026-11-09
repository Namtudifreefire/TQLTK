using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using PersonalInfoManager.Services;

internal static partial class Program
{
    private static int RunGl11(string[] args)
    {
        bool linux = args.Length > 0 && args[0] == "--require-linux";
        if (linux) args = args[1..];
        if (args.Length != 2 || args[0] != "--junit") return 2;
        string report = Path.GetFullPath(args[1]);
        string temp = Path.Combine(Path.GetTempPath(), "TQLTK-GL11-" + Guid.NewGuid().ToString("N"));
        var results = new List<XElement>();
        int failed = 0;
        string secret = "SYNTHETIC-GL11-" + Guid.NewGuid().ToString("N");
        string payload = JsonSerializer.Serialize(new { Password = secret + "-password", TotpSecret = secret + "-totp",
            RecoveryCode = secret + "-recovery", Note = secret + "-note", SensitiveField = secret + "-custom",
            Attachment = secret + "-attachment", AccessToken = secret + "-token" });
        string phrase = secret + "-passphrase";
        void Check(bool condition) { if (!condition) throw new InvalidDataException("GL-11 privacy invariant failed (synthetic values withheld)."); }
        string[] values = new[] { secret, payload, phrase }.Concat(new[] { "password", "totp", "recovery", "note", "custom", "attachment", "token" }
            .Select(suffix => secret + "-" + suffix)).ToArray();
        string[] needles = values.SelectMany(value => new[] { value, Uri.EscapeDataString(value),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value)), Convert.ToHexString(Encoding.UTF8.GetBytes(value)),
            Convert.ToHexString(Encoding.UTF8.GetBytes(value)).ToLowerInvariant() }).Distinct().ToArray();
        bool Leaks(string content) => needles.Any(value => content.Contains(value, StringComparison.Ordinal));
        void Case(string name, Action test)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var item = new XElement("testcase", new XAttribute("classname", "TQLTK.GL11"), new XAttribute("name", name));
            try { test(); Console.WriteLine("PASS GL-11 " + name); }
            catch (Exception ex)
            {
                failed++;
                item.Add(new XElement("failure", new XAttribute("type", ex.GetType().Name), "Synthetic privacy check failed; secret values withheld."));
                Console.Error.WriteLine("FAIL GL-11 " + name);
            }
            item.Add(new XAttribute("time", timer.Elapsed.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
            results.Add(item);
        }
        void Scan()
        {
            var logs = ActivityLogger.ListLogFiles();
            Check(logs.Length == 1);
            Check(Path.GetFullPath(logs[0]).StartsWith(Path.GetFullPath(temp) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            foreach (string line in File.ReadAllLines(logs[0]))
            {
                Check(ActivityLogCodec.TryParse(line, out var record));
                Check(!Leaks(line));
                Check(!Leaks(record!.Detail));
                foreach (var argument in record.Arguments) Check(!Leaks(argument.ToString()));
            }
        }
        try
        {
            if (linux && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            Directory.CreateDirectory(temp);
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", temp);
            Case("real-log-sink-positive-control", () =>
            {
                ActivityLogger.Log("synthetic-user", "GL11_CONTROL", "public-metadata-only");
                ActivityLogger.LogMessage("synthetic-user", "EXPORT_CSV", "log.detail.export_csv.1", 7);
                var lines = ActivityLogger.ListLogFiles().SelectMany(File.ReadAllLines).ToArray();
                Check(lines.Length == 2);
                Check(lines.All(line => ActivityLogCodec.TryParse(line, out _)));
                Check(lines.Any(line => line.Contains("GL11_CONTROL", StringComparison.Ordinal)));
                Check(lines.Any(line => line.Contains("EXPORT_CSV", StringComparison.Ordinal)));
                Scan();
            });
            Case("leak-detector-negative-controls", () =>
            {
                Check(Leaks(secret));
                Check(Leaks(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))));
                Check(Leaks(Convert.ToHexString(Encoding.UTF8.GetBytes(secret))));
                foreach (string value in values)
                {
                    Check(Leaks(Convert.ToBase64String(Encoding.UTF8.GetBytes(value))));
                    Check(Leaks(Uri.EscapeDataString(value)));
                }
                string deliberate = ActivityLogCodec.Serialize(ActivityLogCodec.Create(DateTimeOffset.Now,
                    "synthetic-user", "GL11_DETECTOR", detail: secret));
                Check(Leaks(deliberate));
                Check(!Leaks("public-metadata-only"));
            });
            foreach (bool binary in new[] { false, true })
                Case(binary ? "binary-backup-roundtrip-no-log-disclosure" : "text-backup-roundtrip-no-log-disclosure", () =>
                {
                    byte[] data = Encoding.UTF8.GetBytes(payload);
                    try
                    {
                        if (binary)
                        {
                            byte[] encrypted = BackupCrypto.EncryptBytes(data, phrase);
                            byte[] decoded = BackupCrypto.DecryptBytes(encrypted, phrase);
                            try { Check(data.SequenceEqual(decoded)); } finally { CryptographicOperations.ZeroMemory(decoded); }
                        }
                        else Check(BackupCrypto.Decrypt(BackupCrypto.Encrypt(payload, phrase), phrase) == payload);
                    }
                    finally { CryptographicOperations.ZeroMemory(data); }
                    Scan();
                });
            foreach (string scenario in new[] { "valid", "wrong-passphrase", "tampered", "invalid-zip" })
                Case("file-inspection-" + scenario + "-no-log-or-console-disclosure", () =>
                {
                    byte[] content;
                    using (var stream = new MemoryStream())
                    {
                        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                        {
                            using (var writer = new StreamWriter(zip.CreateEntry("private.txt").Open(), new UTF8Encoding(false))) writer.Write(payload);
                            using var manifest = new StreamWriter(zip.CreateEntry("__tqltk__/manifest.json").Open(), new UTF8Encoding(false));
                            manifest.Write(JsonSerializer.Serialize(new { FormatVersion = 1, ExportedBy = "synthetic-user", ExportedAt = DateTime.UtcNow,
                                FileCount = 1, TotalBytes = Encoding.UTF8.GetByteCount(payload) }));
                        }
                        content = stream.ToArray();
                    }
                    if (scenario == "invalid-zip") content = Encoding.UTF8.GetBytes(payload);
                    byte[] encrypted = BackupCrypto.EncryptBytes(content, phrase);
                    CryptographicOperations.ZeroMemory(content);
                    if (scenario == "tampered") encrypted[^1] ^= 1;
                    string path = Path.Combine(temp, "synthetic.backup");
                    File.WriteAllBytes(path, encrypted);
                    var stdout = Console.Out; var stderr = Console.Error;
                    using var captured = new StringWriter();
                    (bool ok, string message, int count, long bytes) result;
                    try
                    {
                        Console.SetOut(captured); Console.SetError(captured);
                        result = FileBackupService.InspectBackup("synthetic-user", path, scenario == "wrong-passphrase" ? "wrong-synthetic-passphrase" : phrase);
                    }
                    finally { Console.SetOut(stdout); Console.SetError(stderr); }
                    Check(result.ok == (scenario == "valid"));
                    Check(!Leaks(result.message) && !Leaks(captured.ToString()));
                    Scan();
                });
            Case("no-local-storage-used", () => Check(UnsupportedStorage.Calls == 0));
        }
        catch (Exception) { Case("harness-infrastructure", () => Check(false)); }
        finally
        {
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", null);
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
            catch { Case("cleanup", () => Check(false)); }
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            new XDocument(new XElement("testsuites", new XElement("testsuite", new XAttribute("name", "TQLTK.GL-11"),
                new XAttribute("tests", results.Count), new XAttribute("failures", failed), new XAttribute("errors", 0), new XAttribute("skipped", 0),
                new XElement("properties", new XElement("property", new XAttribute("name", "os"),
                    new XAttribute("value", System.Runtime.InteropServices.RuntimeInformation.OSDescription))), results))).Save(report);
        }
        Console.WriteLine($"GL-11 PASS={results.Count - failed} FAIL={failed} SKIPPED=0");
        return failed == 0 ? 0 : 1;
    }
}
