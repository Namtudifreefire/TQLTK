using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

internal static partial class Program
{
    private const string RfcSha1Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private const string RfcSha256Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA";
    private const string RfcSha512Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNA";

    private static int RunGl10(string[] args)
    {
        bool linux = args.Length > 0 && args[0] == "--require-linux";
        if (linux) args = args[1..];
        if (args.Length != 2 || args[0] != "--junit") return 2;

        string report = Path.GetFullPath(args[1]);
        var results = new List<XElement>();
        int failed = 0;

        void Check(bool condition) { if (!condition) throw new InvalidDataException("GL-10 TOTP invariant failed."); }
        void Case(string name, Action test)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var item = new XElement("testcase", new XAttribute("classname", "TQLTK.GL10"), new XAttribute("name", name));
            try
            {
                test();
                Console.WriteLine("PASS GL-10 " + name);
            }
            catch (Exception ex)
            {
                failed++;
                item.Add(new XElement("failure", new XAttribute("type", ex.GetType().Name),
                    "GL-10 TOTP check failed; test input is synthetic or RFC public data."));
                Console.Error.WriteLine("FAIL GL-10 " + name + ": " + ex.GetType().Name);
            }
            item.Add(new XAttribute("time", timer.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)));
            results.Add(item);
        }

        try
        {
            if (linux && !OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("This CI invocation requires an actual Linux runtime.");

            foreach (var vector in RfcVectors())
            {
                Case($"rfc6238-{vector.Algorithm.ToLowerInvariant()}-{vector.UnixTime}", () =>
                {
                    var actual = TotpService.GetCurrentCode(vector.Secret, vector.Algorithm, 8, 30,
                        DateTimeOffset.FromUnixTimeSeconds(vector.UnixTime));
                    Check(actual.Code == vector.Expected);
                    Check(actual.Code.Length == 8 && actual.Code.All(char.IsAsciiDigit));
                });
            }

            Case("fixed-clock-cycle-boundaries", () =>
            {
                var atStart = TotpService.GetCurrentCode(RfcSha1Secret, "SHA1", 8, 30, DateTimeOffset.FromUnixTimeSeconds(0));
                var beforeBoundary = TotpService.GetCurrentCode(RfcSha1Secret, "SHA1", 8, 30, DateTimeOffset.FromUnixTimeSeconds(29));
                var atBoundary = TotpService.GetCurrentCode(RfcSha1Secret, "SHA1", 8, 30, DateTimeOffset.FromUnixTimeSeconds(30));
                Check(atStart.Code == beforeBoundary.Code);
                Check(atStart.Code != atBoundary.Code);
                Check(atStart.SecondsRemaining == 30 && beforeBoundary.SecondsRemaining == 1 && atBoundary.SecondsRemaining == 30);
            });

            Case("custom-period-cycle-boundaries", () =>
            {
                var atStart = TotpService.GetCurrentCode(RfcSha256Secret, "SHA256", 8, 60, DateTimeOffset.FromUnixTimeSeconds(0));
                var beforeBoundary = TotpService.GetCurrentCode(RfcSha256Secret, "SHA256", 8, 60, DateTimeOffset.FromUnixTimeSeconds(59));
                var atBoundary = TotpService.GetCurrentCode(RfcSha256Secret, "SHA256", 8, 60, DateTimeOffset.FromUnixTimeSeconds(60));
                Check(atStart.Code == beforeBoundary.Code);
                Check(atStart.SecondsRemaining == 60 && beforeBoundary.SecondsRemaining == 1 && atBoundary.SecondsRemaining == 60);
            });

            Case("six-digit-leading-zero-format", () =>
            {
                var actual = TotpService.GetCurrentCode(RfcSha1Secret, "SHA1", 6, 30,
                    DateTimeOffset.FromUnixTimeSeconds(1_111_111_109));
                Check(actual.Code == "081804");
                Check(actual.Code.Length == 6 && actual.Code.All(char.IsAsciiDigit));
            });

            Case("base32-normalizes-separators-and-case", () =>
            {
                var canonical = TotpService.GetCurrentCode(RfcSha1Secret, "SHA1", 8, 30, DateTimeOffset.FromUnixTimeSeconds(59));
                var normalized = TotpService.GetCurrentCode("gez dgnbv-gy3tqojq gezdgnbvgy3tqojq", "SHA1", 8, 30,
                    DateTimeOffset.FromUnixTimeSeconds(59));
                Check(canonical == normalized);
            });

            Case("base32-accepts-trailing-padding", () =>
            {
                var canonical = TotpService.GetCurrentCode(RfcSha1Secret, "SHA1", 8, 30, DateTimeOffset.FromUnixTimeSeconds(59));
                var padded = TotpService.GetCurrentCode(RfcSha1Secret + "====", "SHA1", 8, 30, DateTimeOffset.FromUnixTimeSeconds(59));
                Check(canonical == padded);
            });

            foreach (var invalid in new[]
            {
                ("base32-empty", ""),
                ("base32-too-short", "JBSWY3DP"),
                ("base32-invalid-character", "JBSWY3DPEHPK3PX!"),
                ("base32-interior-padding", "JBSWY3DP=EHPK3PXP"),
                ("base32-too-long", new string('A', 129))
            })
            {
                Case(invalid.Item1, () => Expect<FormatException>(() =>
                    TotpService.GetCurrentCode(invalid.Item2, "SHA1", 6, 30, DateTimeOffset.FromUnixTimeSeconds(59))));
            }

            Case("rejects-unsupported-algorithm-digits-and-period", () =>
            {
                Expect<FormatException>(() => TotpService.GetCurrentCode(RfcSha1Secret, "MD5", 6, 30, DateTimeOffset.FromUnixTimeSeconds(59)));
                Expect<ArgumentOutOfRangeException>(() => TotpService.GetCurrentCode(RfcSha1Secret, "SHA1", 7, 30, DateTimeOffset.FromUnixTimeSeconds(59)));
                Expect<ArgumentOutOfRangeException>(() => TotpService.GetCurrentCode(RfcSha1Secret, "SHA1", 6, 4, DateTimeOffset.FromUnixTimeSeconds(59)));
            });

            Case("legacy-raw-base32-defaults", () =>
            {
                var credential = TotpService.ParseImportText(RfcSha1Secret, "Legacy account").Single();
                Check(credential.Name == "Legacy account" && credential.Algorithm == "SHA1" && credential.Digits == 6 && credential.PeriodSeconds == 30);
                Check(TotpService.GetCurrentCode(credential.Secret, credential.Algorithm, credential.Digits, credential.PeriodSeconds,
                    DateTimeOffset.FromUnixTimeSeconds(1_111_111_109)).Code == "081804");
            });

            Case("legacy-otpauth-defaults", () =>
            {
                var credential = TotpService.ParseImportText(
                    "otpauth://totp/Legacy%3Aalice?secret=" + RfcSha1Secret + "&issuer=Legacy", "Ignored").Single();
                Check(credential.Name == "alice" && credential.Issuer == "Legacy");
                Check(credential.Algorithm == "SHA1" && credential.Digits == 6 && credential.PeriodSeconds == 30);
            });

            Case("otpauth-preserves-supported-explicit-options", () =>
            {
                var credential = TotpService.ParseImportText(
                    "otpauth://totp/Example%3Abob?secret=" + RfcSha512Secret + "&issuer=Example&algorithm=sha512&digits=8&period=60", "Ignored").Single();
                Check(credential.Name == "bob" && credential.Issuer == "Example");
                Check(credential.Algorithm == "SHA512" && credential.Digits == 8 && credential.PeriodSeconds == 60);
                Check(TotpService.GetCurrentCode(credential.Secret, credential.Algorithm, credential.Digits, credential.PeriodSeconds,
                    DateTimeOffset.FromUnixTimeSeconds(59)).Code.Length == 8);
            });

            Case("migration-payload-missing-options-uses-legacy-defaults", () =>
            {
                var credential = TotpService.ParseImportText(MigrationUri(Encoding.ASCII.GetBytes("12345678901234567890"), "Legacy:carol", "Legacy"), "Ignored").Single();
                Check(credential.Name == "carol" && credential.Issuer == "Legacy");
                Check(credential.Algorithm == "SHA1" && credential.Digits == 6 && credential.PeriodSeconds == 30);
                Check(TotpService.GetCurrentCode(credential.Secret, credential.Algorithm, credential.Digits, credential.PeriodSeconds,
                    DateTimeOffset.FromUnixTimeSeconds(1_111_111_109)).Code == "081804");
            });

            Case("migration-payload-preserves-sha512-and-eight-digits", () =>
            {
                var credential = TotpService.ParseImportText(MigrationUri(Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234"),
                    "Example:dave", "Example", algorithm: 3, digits: 2, type: 2), "Ignored").Single();
                Check(credential.Name == "dave" && credential.Issuer == "Example");
                Check(credential.Algorithm == "SHA512" && credential.Digits == 8 && credential.PeriodSeconds == 30);
                Check(TotpService.GetCurrentCode(credential.Secret, credential.Algorithm, credential.Digits, credential.PeriodSeconds,
                    DateTimeOffset.FromUnixTimeSeconds(59)).Code == "90693936");
            });

            Case("migration-rejects-hotp", () => Expect<FormatException>(() =>
                TotpService.ParseImportText(MigrationUri(Encoding.ASCII.GetBytes("12345678901234567890"), "Legacy:hotp", "Legacy", type: 1), "Ignored")));

            Case("legacy-saved-account-uses-totp-defaults", () =>
            {
                var account = JsonSerializer.Deserialize<SavedAccount>("{\"ServiceName\":\"Legacy\",\"TotpSecret\":\"" + RfcSha1Secret + "\",\"TotpIssuer\":\"Legacy\"}")
                    ?? throw new InvalidDataException("Legacy account fixture did not deserialize.");
                Check(account.TotpAlgorithm == "SHA1" && account.TotpDigits == 6 && account.TotpPeriodSeconds == 30);
                Check(TotpService.GetCurrentCode(account.TotpSecret, account.TotpAlgorithm, account.TotpDigits, account.TotpPeriodSeconds,
                    DateTimeOffset.FromUnixTimeSeconds(1_111_111_109)).Code == "081804");
            });
        }
        catch (Exception ex)
        {
            failed++;
            var item = new XElement("testcase", new XAttribute("classname", "TQLTK.GL10"), new XAttribute("name", "harness-infrastructure"),
                new XElement("failure", new XAttribute("type", ex.GetType().Name), "GL-10 harness infrastructure failed."));
            results.Add(item);
            Console.Error.WriteLine("FAIL GL-10 harness-infrastructure: " + ex.GetType().Name);
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            new XDocument(new XElement("testsuites", new XElement("testsuite", new XAttribute("name", "TQLTK.GL-10"),
                new XAttribute("tests", results.Count), new XAttribute("failures", failed), new XAttribute("errors", 0), new XAttribute("skipped", 0),
                new XElement("properties", new XElement("property", new XAttribute("name", "os"),
                    new XAttribute("value", System.Runtime.InteropServices.RuntimeInformation.OSDescription))), results))).Save(report);
        }

        Console.WriteLine($"GL-10 PASS={results.Count - failed} FAIL={failed} SKIPPED=0");
        return failed == 0 ? 0 : 1;
    }

    private static IEnumerable<(long UnixTime, string Algorithm, string Secret, string Expected)> RfcVectors()
    {
        var expected = new Dictionary<long, (string SHA1, string SHA256, string SHA512)>
        {
            [59] = ("94287082", "46119246", "90693936"),
            [1_111_111_109] = ("07081804", "68084774", "25091201"),
            [1_111_111_111] = ("14050471", "67062674", "99943326"),
            [1_234_567_890] = ("89005924", "91819424", "93441116"),
            [2_000_000_000] = ("69279037", "90698825", "38618901"),
            [20_000_000_000] = ("65353130", "77737706", "47863826")
        };
        foreach (var (time, codes) in expected)
        {
            yield return (time, "SHA1", RfcSha1Secret, codes.SHA1);
            yield return (time, "SHA256", RfcSha256Secret, codes.SHA256);
            yield return (time, "SHA512", RfcSha512Secret, codes.SHA512);
        }
    }

    private static void Expect<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidDataException("Expected validation failure did not occur.");
    }

    private static string MigrationUri(byte[] secret, string name, string issuer, int? algorithm = null, int? digits = null, int? type = null)
    {
        var entry = new List<byte>();
        AddBytes(entry, 1, secret);
        AddBytes(entry, 2, Encoding.UTF8.GetBytes(name));
        AddBytes(entry, 3, Encoding.UTF8.GetBytes(issuer));
        if (algorithm.HasValue) AddVarintField(entry, 4, (ulong)algorithm.Value);
        if (digits.HasValue) AddVarintField(entry, 5, (ulong)digits.Value);
        if (type.HasValue) AddVarintField(entry, 6, (ulong)type.Value);
        var payload = new List<byte>();
        AddBytes(payload, 1, entry.ToArray());
        string encoded = Convert.ToBase64String(payload.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "otpauth-migration://offline?data=" + Uri.EscapeDataString(encoded);
    }

    private static void AddBytes(List<byte> destination, int field, byte[] value)
    {
        AddVarint(destination, (ulong)((field << 3) | 2));
        AddVarint(destination, (ulong)value.Length);
        destination.AddRange(value);
    }

    private static void AddVarintField(List<byte> destination, int field, ulong value)
    {
        AddVarint(destination, (ulong)(field << 3));
        AddVarint(destination, value);
    }

    private static void AddVarint(List<byte> destination, ulong value)
    {
        do
        {
            byte next = (byte)(value & 0x7F);
            value >>= 7;
            destination.Add(value == 0 ? next : (byte)(next | 0x80));
        }
        while (value != 0);
    }
}
