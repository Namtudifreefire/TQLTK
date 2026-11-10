using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PersonalInfoManager.Services;

internal static partial class Program
{
    private static readonly Regex Gl12Placeholder = new(@"(?<!\{)\{(?<name>[A-Za-z_][A-Za-z0-9_]*|\d+)(?<suffix>(?:,[^{}]*)?(?::[^{}]*)?)\}(?!\})", RegexOptions.CultureInvariant);
    private static readonly Regex LegacyHashId = new(@"^ui\.[a-f0-9]{64}$", RegexOptions.CultureInvariant);

    private static int RunGl12(string[] args)
    {
        bool requireLinux = args.Contains("--require-linux", StringComparer.Ordinal);
        string? sourceRoot = ValueAfter(args, "--source-root");
        string? report = ValueAfter(args, "--junit");
        if (sourceRoot == null || report == null) return 2;

        var results = new List<XElement>();
        int failed = 0;
        int stableKeys = 0;
        int legacyHashKeys = 0;
        int legacyEntries = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
        void Case(string name, Action test)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var item = new XElement("testcase", new XAttribute("classname", "TQLTK.GL12"), new XAttribute("name", name));
            try
            {
                test();
                Console.WriteLine("PASS GL-12 " + name);
            }
            catch (Exception ex)
            {
                failed++;
                item.Add(new XElement("failure", new XAttribute("type", ex.GetType().Name), ex.Message));
                Console.Error.WriteLine("FAIL GL-12 " + name + ": " + ex.GetType().Name);
            }
            item.Add(new XAttribute("time", timer.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)));
            results.Add(item);
        }

        try
        {
            if (requireLinux && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("GL-12 Linux CI requires a Linux runtime.");
            sourceRoot = Path.GetFullPath(sourceRoot);
            Check(File.Exists(Path.Combine(sourceRoot, "PersonalInfoManager.csproj")), "The supplied source root is not the TQLTK application source directory.");

            Case("catalog-production-audit", () =>
            {
                IReadOnlyList<LocalizationAuditIssue> issues = LocalizationAuditService.Audit();
                Check(issues.Count == 0, "Production localization audit reported " + issues.Count.ToString(CultureInfo.InvariantCulture) + " issue(s).");
            });
            Case("translation-key-shape-and-classification", () =>
            {
                var catalog = LocalizationAuditService.Catalog.ToArray();
                Check(catalog.Length > 0, "The localization catalog is empty.");
                Check(catalog.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == catalog.Length, "The localization catalog has duplicate IDs.");
                foreach (LocalizedMessageDefinition item in catalog)
                {
                    Check(!string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Vietnamese) && !string.IsNullOrWhiteSpace(item.English), "A catalog definition contains an empty required value.");
                    if (LegacyHashId.IsMatch(item.Id)) legacyHashKeys++;
                    else
                    {
                        Check(!item.Id.StartsWith("ui.", StringComparison.Ordinal) || item.Id.Count(c => c == '.') >= 2, "A ui.* ID is neither a legacy hash nor a named UI key.");
                        stableKeys++;
                    }
                }
            });
            Case("legacy-catalog-shape", () =>
            {
                legacyEntries = EnglishCatalog.Entries.Count + EnglishCatalog.Fragments.Count;
                Check(EnglishCatalog.Entries.Count > 0 && EnglishCatalog.Fragments.Count > 0, "The legacy catalog or fragment list is unexpectedly empty.");
                Check(EnglishCatalog.ConflictingDuplicateSources.Count == 0, "The legacy catalog contains conflicting duplicate source values.");
                foreach ((string source, string english) in EnglishCatalog.Entries.Concat(EnglishCatalog.Fragments))
                    Check(!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(english), "A legacy catalog item is empty.");
                bool conflictingFragments = HasConflictingLegacySources(EnglishCatalog.Fragments);
                Check(!conflictingFragments, "The legacy fragment catalog contains conflicting duplicate source values.");
            });
            Case("legacy-conflict-detector-negative-fixture", () =>
            {
                var conflictingFixture = new[]
                {
                    new KeyValuePair<string, string>("GL12 fixture source", "First English value"),
                    new KeyValuePair<string, string>("GL12 fixture source", "Different English value")
                };
                var compatibleFixture = new[]
                {
                    new KeyValuePair<string, string>("GL12 fixture source", "Same English value"),
                    new KeyValuePair<string, string>("GL12 fixture source", "Same English value")
                };
                Check(HasConflictingLegacySources(conflictingFixture), "The conflict detector did not reject a fixture with two translations for one source.");
                Check(!HasConflictingLegacySources(compatibleFixture), "The conflict detector rejected identical duplicate fixture values.");
            });
            Case("placeholder-parity-and-escaped-braces", () =>
            {
                foreach (LocalizedMessageDefinition item in LocalizationAuditService.Catalog)
                    Check(HasCompatibleTemplate(item.Vietnamese, item.English), "A production catalog item has incompatible placeholders.");
                foreach ((string source, string english) in EnglishCatalog.Fragments)
                    Check(HasCompatibleTemplate(source, english), "A legacy fragment has incompatible placeholders.");
                Check(HasCompatibleTemplate("Giá trị {{x}}: {count:N0}", "Value {{x}}: {count:N0}"), "Escaped braces were rejected.");
            });
            Case("placeholder-detector-negative-fixture", () =>
            {
                Check(!HasCompatibleTemplate("Mục {count}", "Entry {total}"), "The GL-12 detector did not reject a named-placeholder mismatch.");
                Check(!HasCompatibleTemplate("Mục {0}", "Entry {1}"), "The GL-12 detector did not reject a positional-placeholder mismatch.");
                Check(!HasCompatibleTemplate("Mục {", "Entry {"), "The GL-12 detector did not reject malformed braces.");
            });
            Case("unicode-and-invalid-value-audit", () =>
            {
                bool vietnameseUnicode = false;
                foreach (LocalizedMessageDefinition item in LocalizationAuditService.Catalog)
                {
                    Check(IsValidUnicode(item.Vietnamese) && IsValidUnicode(item.English), "A catalog value has invalid UTF-16 data.");
                    Check(!item.Vietnamese.Contains('\uFFFD') && !item.English.Contains('\uFFFD'), "A catalog value contains the replacement character.");
                    vietnameseUnicode |= item.Vietnamese.Any(c => c is 'Đ' or 'đ' || (c >= '\u00C0' && c <= '\u024F') || (c >= '\u1E00' && c <= '\u1EFF'));
                }
                Check(vietnameseUnicode, "No Vietnamese Unicode text was found in the catalog.");
            });
            Case("known-and-unknown-fallback", () =>
            {
                LocalizedMessageDefinition stable = LocalizationAuditService.Catalog.First(item => !LegacyHashId.IsMatch(item.Id));
                Check(LocalizationAuditService.TryTranslateKnown(stable.Id, "en", out string byId) && byId == stable.English, "A stable key did not resolve to English.");
                Check(LocalizationAuditService.TryTranslateKnown(stable.Vietnamese, "en", out string bySource) && bySource == stable.English, "A known Vietnamese source did not resolve to English.");
                var legacy = EnglishCatalog.Entries.First();
                Check(LocalizationAuditService.TryTranslateKnown(legacy.Key, "en", out string legacyResult) && legacyResult == legacy.Value, "A legacy catalog source did not resolve.");
                Check(!LocalizationAuditService.TryTranslateKnown("GL12 synthetic missing translation", "en", out string missing) && missing.Length == 0, "An unknown source unexpectedly resolved.");
            });
            Case("missing-translation-detector-negative-fixture", () =>
            {
                IReadOnlyList<LocalizationAuditIssue> issues = LocalizationAuditService.AuditSources(new[] { "GL12 synthetic missing translation" });
                Check(issues.Count == 1, "The missing-translation detector did not flag its synthetic fixture.");
            });
            Case("static-key-references", () =>
            {
                var ids = LocalizationAuditService.Catalog.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                var referenced = new HashSet<string>(StringComparer.Ordinal);
                foreach (string file in ProductionSources(sourceRoot))
                {
                    string content = File.ReadAllText(file);
                    foreach (Match match in Regex.Matches(content, "(?:LocalizationAuditService\\.(?:Get|Format|FormatForLanguage|FormatNamed)|LanguageService\\.Translate|LanguageService\\.SetLocalized(?:Key|FormattedKey|FormattedText))\\s*\\([^\\r\\n]*?\\\"(?<key>(?:[a-z][a-z0-9_]*(?:\\.[A-Za-z0-9_]+)+|ui\\.[a-f0-9]{64}))\\\"", RegexOptions.CultureInvariant))
                        referenced.Add(match.Groups["key"].Value);
                }
                foreach (string file in ProductionXamlSources(sourceRoot))
                {
                    string content = File.ReadAllText(file);
                    foreach (Match match in Regex.Matches(content, @"\{DynamicResource\s+(?<key>xaml\.[A-Za-z0-9_.-]+)\}", RegexOptions.CultureInvariant))
                        referenced.Add(match.Groups["key"].Value);
                }
                Check(referenced.Count > 0, "No static localization keys were found in production sources.");
                string[] missing = referenced.Where(key => !ids.Contains(key)).ToArray();
                Check(missing.Length == 0, "A static production localization key is not registered.");
            });
            Case("legacy-source-usage-inventory", () =>
            {
                int legacyCalls = 0;
                foreach (string file in ProductionSources(sourceRoot))
                {
                    string content = File.ReadAllText(file);
                    legacyCalls += Regex.Matches(content, "LanguageService\\.(?:Translate|Format)\\s*\\(\\s*\\\"[^\\\"]*[\\u00C0-\\u024F\\u1E00-\\u1EFF][^\\\"]*\\\"", RegexOptions.CultureInvariant).Count;
                }
                Check(legacyCalls >= 0, "Legacy source inventory failed.");
                Console.WriteLine("INFO GL-12 legacy-source-calls=" + legacyCalls.ToString(CultureInfo.InvariantCulture));
            });
            Case("translated-text-not-used-as-control", () =>
            {
                var evidence = new List<string>();
                foreach (string file in ProductionSources(sourceRoot))
                {
                    string content = File.ReadAllText(file);
                    if (Regex.IsMatch(content, @"\b(?:if|while)\s*\(\s*(?:LanguageService\.Translate|LocalizationAuditService\.(?:Get|Format))\s*\(", RegexOptions.CultureInvariant))
                        evidence.Add(Path.GetFileName(file));
                }
                Check(evidence.Count == 0, "A translated display string is directly used as a control expression.");
            });
        }
        catch (Exception ex)
        {
            Case("harness-infrastructure", () => throw new InvalidOperationException("GL-12 setup failed.", ex));
        }
        finally
        {
            string fullReport = Path.GetFullPath(report);
            Directory.CreateDirectory(Path.GetDirectoryName(fullReport)!);
            var properties = new XElement("properties",
                new XElement("property", new XAttribute("name", "stable_translation_keys"), new XAttribute("value", stableKeys)),
                new XElement("property", new XAttribute("name", "legacy_hash_translation_keys"), new XAttribute("value", legacyHashKeys)),
                new XElement("property", new XAttribute("name", "legacy_catalog_items"), new XAttribute("value", legacyEntries)),
                new XElement("property", new XAttribute("name", "runtime_os"), new XAttribute("value", System.Runtime.InteropServices.RuntimeInformation.OSDescription)));
            var suite = new XElement("testsuite", new XAttribute("name", "TQLTK.GL-12"),
                new XAttribute("tests", results.Count), new XAttribute("failures", failed), new XAttribute("errors", 0), new XAttribute("skipped", 0), properties, results);
            new XDocument(new XElement("testsuites", suite)).Save(fullReport);
            Console.WriteLine($"GL-12 PASS={results.Count - failed} FAIL={failed} SKIPPED=0; JUnit: {fullReport}");
        }
        return failed == 0 ? 0 : 1;

        static string? ValueAfter(string[] values, string name)
        {
            int index = Array.IndexOf(values, name);
            return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
        }
    }

    private static IEnumerable<string> ProductionSources(string sourceRoot) =>
        Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Regex.IsMatch(file, @"[\\/](?:bin|obj|artifacts|\.vs|\.git|tests)[\\/]", RegexOptions.IgnoreCase));

    private static IEnumerable<string> ProductionXamlSources(string sourceRoot) =>
        Directory.EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(file => !Regex.IsMatch(file, @"[\\/](?:bin|obj|artifacts|\.vs|\.git|tests)[\\/]", RegexOptions.IgnoreCase));

    private static bool HasCompatibleTemplate(string vietnamese, string english)
    {
        if (!IsTemplateValid(vietnamese) || !IsTemplateValid(english)) return false;
        string[] vi = Placeholders(vietnamese);
        string[] en = Placeholders(english);
        return vi.SequenceEqual(en, StringComparer.Ordinal);
    }

    private static string[] Placeholders(string template) => Gl12Placeholder.Matches(template)
        .Select(match => match.Groups["name"].Value)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    private static bool IsTemplateValid(string template)
    {
        try
        {
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            string normalized = Gl12Placeholder.Replace(template, match =>
            {
                string name = match.Groups["name"].Value;
                if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return match.Value;
                if (!names.TryGetValue(name, out int index)) names[name] = index = names.Count;
                return "{" + index.ToString(CultureInfo.InvariantCulture) + match.Groups["suffix"].Value + "}";
            });
            _ = string.Format(CultureInfo.InvariantCulture, normalized, new object?[32]);
            return true;
        }
        catch (FormatException) { return false; }
    }

    private static bool IsValidUnicode(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index])) continue;
            if (!char.IsHighSurrogate(value[index]) || index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return false;
            index++;
        }
        return true;
    }

    private static bool HasConflictingLegacySources(IEnumerable<KeyValuePair<string, string>> entries) =>
        entries.GroupBy(item => item.Key, StringComparer.Ordinal)
            .Any(group => group.Select(item => item.Value).Distinct(StringComparer.Ordinal).Skip(1).Any());
}
