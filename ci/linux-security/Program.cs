using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using PersonalInfoManager.Services;

internal static partial class Program
{
    private const string Owner = "synthetic-gl09-user";
    private const string Passphrase = "Synthetic GL09 backup phrase only!";
    private const string ManifestName = "__tqltk__/manifest.json";
    private static readonly List<XElement> Results = new();
    private static MethodInfo Guard => typeof(FileBackupService).GetMethod(
        "IsUserFileEntry", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("Production ZIP entry guard was renamed; update coverage explicitly.");
    private static string root = "";
    private static int failures;

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--gl10") return RunGl10(args[1..]);
        if (args.Length > 0 && args[0] == "--gl11") return RunGl11(args[1..]);
        bool requireLinux = args.Length > 0 && args[0] == "--require-linux";
        if (requireLinux) args = args[1..];
        if (args.Length != 2 || args[0] != "--junit")
        {
            Console.Error.WriteLine("Usage: Tqltk.LinuxSecurity [--require-linux] --junit <report.xml>");
            return 2;
        }
        string report = Path.GetFullPath(args[1]);
        root = Path.Combine(Path.GetTempPath(), "TQLTK-GL09-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (requireLinux && !OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("This CI invocation requires an actual Linux runtime.");
            Directory.CreateDirectory(root);
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", root);
            Console.WriteLine($"GL-09 runtime: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            foreach (string name in new[] { "safe.txt", "tài liệu 日本語.txt", "with spaces.txt", ".hidden", "a..b.txt",
                "%2e%2e%2fescape.txt" })
                Run("accept-flat-" + CaseId(name), () => AssertEntry(name, true));
            foreach (string name in new[] {
                "../escape.txt", "../../escape.txt", "a/../../escape.txt", "a/b.txt", "./safe.txt",
                "/tmp/escape.txt", "//server/share/escape.txt", "C:/escape.txt", ".", "..", "folder/",
                "trailing.", "trailing ", "NUL", "con.txt", "PRN", "AUX.dat", "COM1", "LPT9.txt", "nul\0.txt",
                ManifestName })
                Run("reject-nonflat-or-reserved-" + CaseId(name), () => AssertEntry(name, false));

            Run("encrypted-valid-flat-control", ValidArchive);
            foreach (string name in new[] { "../escape.txt", "../../escape.txt", "folder/../../escape.txt",
                "/tmp/escape.txt", "C:/escape.txt", "//server/share/escape.txt", "nested/file.txt", "NUL.txt", "trailing." })
                Run("encrypted-inspection-excludes-" + CaseId(name), () => MixedArchive(name));
            Run("encrypted-traversal-only-rejected", TraversalOnly);
            Run("inspection-never-calls-local-storage", () => Check(UnsupportedStorage.Calls == 0,
                "Unexpected local-storage dependency; Linux coverage must be reviewed."));
            Skip("windows-path-semantics", "Backslash, UNC, drive-relative names, ADS and invalid Windows filename characters need the Windows runtime; do not infer their behavior from Linux Path APIs.");
            Skip("dpapi-restore-and-drill", "Actual encrypted local-store writes and DrillRestore depend on Windows DPAPI; run these in GitHub Actions.");
            Skip("complete-backup-restore-paths", "CompleteBackupService/LocalFileService integration is not linked into this Linux inspection suite; add a Windows regression matrix in GitHub Actions.");
        }
        catch (Exception ex)
        {
            Fail("harness-infrastructure", ex, 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", null);
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (Exception ex) { Fail("temporary-data-cleanup", ex, 0); }
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            int skipped = Results.Count(item => item.Element("skipped") != null);
            var suite = new XElement("testsuite", new XAttribute("name", "TQLTK.GL-09"),
                new XAttribute("tests", Results.Count), new XAttribute("failures", failures),
                new XAttribute("errors", 0), new XAttribute("skipped", skipped),
                new XAttribute("time", Results.Sum(item => double.Parse(item.Attribute("time")!.Value, CultureInfo.InvariantCulture)).ToString("F3", CultureInfo.InvariantCulture)),
                new XElement("properties", new XElement("property", new XAttribute("name", "os"),
                    new XAttribute("value", System.Runtime.InteropServices.RuntimeInformation.OSDescription))), Results);
            new XDocument(new XElement("testsuites", suite)).Save(report);
            Console.WriteLine($"PASS={Results.Count - failures - skipped} FAIL={failures} SKIPPED={skipped}; JUnit: {report}");
        }
        return failures == 0 ? 0 : 1;
    }

    private static void AssertEntry(string name, bool accepted)
    {
        using var stream = Zip((name, "synthetic-content"));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Check((bool)Guard.Invoke(null, new object[] { archive.Entries[0] })! == accepted,
            "Production entry classification differs from the expected flat-file policy.");
    }

    private static void ValidArchive()
    {
        using var zip = Zip(("safe.txt", "safe-content"), (ManifestName, Manifest(1, "safe-content".Length)));
        string path = SaveEncrypted(zip);
        var result = FileBackupService.InspectBackup(Owner, path, Passphrase);
        Check(result.ok && result.count == 1 && result.bytes == "safe-content".Length,
            "Valid encrypted control was not inspected successfully.");
    }

    private static void MixedArchive(string unsafeName)
    {
        string sentinel = Path.Combine(root, "escape.txt");
        File.WriteAllText(sentinel, "original-synthetic-sentinel");
        using var zip = Zip(("safe.txt", "safe-content"), (unsafeName, "ATTACK-SYNTHETIC"),
            (ManifestName, Manifest(1, "safe-content".Length)));
        string path = SaveEncrypted(zip);
        byte[] before = SHA256.HashData(File.ReadAllBytes(path));
        var result = FileBackupService.InspectBackup(Owner, path, Passphrase);
        Check(result.ok && result.count == 1 && result.bytes == "safe-content".Length,
            "Inspection did not exclude the unsafe entry, or the safe control was lost.");
        Check(File.ReadAllText(sentinel) == "original-synthetic-sentinel", "Inspection changed the sentinel.");
        Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Inspection changed the input backup.");
        Check(!Directory.Exists(Path.Combine(root, "PersonalInfoManager")), "Inspection wrote application storage.");
    }

    private static void TraversalOnly()
    {
        using var zip = Zip(("../escape.txt", "ATTACK-SYNTHETIC"), (ManifestName, Manifest(0, 0)));
        var result = FileBackupService.InspectBackup(Owner, SaveEncrypted(zip), Passphrase);
        Check(!result.ok && result.count == 0, "A backup with only traversal payloads was accepted.");
    }

    private static string Manifest(int count, long bytes) => JsonSerializer.Serialize(new {
        FormatVersion = 1, ExportedBy = Owner, ExportedAt = DateTime.UtcNow, FileCount = count, TotalBytes = bytes
    });

    private static MemoryStream Zip(params (string name, string content)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        stream.Position = 0;
        return stream;
    }

    private static string SaveEncrypted(MemoryStream zip)
    {
        string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tqfb");
        byte[] plain = zip.ToArray();
        try { File.WriteAllBytes(path, BackupCrypto.EncryptBytes(plain, Passphrase)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        return path;
    }

    private static string CaseId(string name) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..10];
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Run(string name, Action test)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            test();
            Results.Add(Case(name, timer.Elapsed.TotalSeconds));
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex) { Fail(name, ex, timer.Elapsed.TotalSeconds); }
    }
    private static XElement Case(string name, double time) => new("testcase",
        new XAttribute("classname", "TQLTK.GL09"), new XAttribute("name", name),
        new XAttribute("time", time.ToString("F3", CultureInfo.InvariantCulture)));
    private static void Fail(string name, Exception ex, double time)
    {
        failures++;
        var result = Case(name, time);
        result.Add(new XElement("failure", new XAttribute("type", ex.GetType().Name), ex.Message));
        Results.Add(result);
        Console.Error.WriteLine("FAIL " + name + ": " + ex.GetType().Name);
    }
    private static void Skip(string name, string reason)
    {
        var result = Case(name, 0);
        result.Add(new XElement("skipped", new XAttribute("message", reason)));
        Results.Add(result);
        Console.WriteLine("SKIPPED " + name);
    }
}
