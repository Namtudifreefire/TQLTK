using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using PersonalInfoManager.Services;

internal static partial class Program
{
    private static void LabWindowsPaths(string root, string reportPath)
    {
        var cases = new List<XElement>();
        int failures = 0;
        void Case(string name, Action test)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = new XElement("testcase", new XAttribute("classname", "TQLTK.Windows.GL09"), new XAttribute("name", name));
            try { test(); Console.WriteLine("PASS windows-path: " + name); }
            catch (Exception ex)
            {
                failures++;
                result.Add(new XElement("failure", new XAttribute("type", ex.GetType().Name), ex.Message));
                Console.Error.WriteLine("FAIL windows-path: " + name + ": " + ex.Message);
            }
            result.Add(new XAttribute("time", timer.Elapsed.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
            cases.Add(result);
        }
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        byte[] Zip(params (string name, string value)[] entries)
        {
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                foreach (var entry in entries)
                {
                    using var writer = new StreamWriter(archive.CreateEntry(entry.name).Open(), new UTF8Encoding(false));
                    writer.Write(entry.value);
                }
            return output.ToArray();
        }
        string Save(byte[] bytes)
        {
            string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".backup");
            try { File.WriteAllBytes(path, BackupCrypto.EncryptBytes(bytes, LabPassphrase)); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            return path;
        }
        string Manifest(int count, int length) => JsonSerializer.Serialize(new {
            FormatVersion = 1, ExportedBy = LabUser, ExportedAt = DateTime.UtcNow, FileCount = count, TotalBytes = length
        });
        string sentinel = Path.Combine(root, "escape.txt");
        File.WriteAllText(sentinel, "synthetic-original-sentinel");
        try
        {
            string baseline = BackupState(LabUser);
            string[] unsafeNames = { "..\\escape.txt", "nested\\..\\..\\escape.txt", "\\escape.txt",
                "\\\\server\\share\\escape.txt", "C:\\escape.txt", "C:escape.txt", "file.txt:stream", "CON.txt",
                "NUL", "LPT1.txt", "trailing.", "trailing ", "bad|name.txt", "bad?name.txt", "bad*name.txt" };
            foreach (var pair in unsafeNames.Select((name, index) => (name, index)))
                Case("personal-reject-" + pair.index, () =>
                {
                    string path = Save(Zip((pair.name, "synthetic-attack"), ("__tqltk__/manifest.json", Manifest(0, 0))));
                    byte[] original = File.ReadAllBytes(path);
                    Check(!FileBackupService.PreviewRestore(LabUser, path, LabPassphrase).ok, "Preview accepted an unsafe Windows name.");
                    Check(!FileBackupService.DrillRestore(LabUser, path, LabPassphrase).ok, "Drill accepted an unsafe Windows name.");
                    var restored = FileBackupService.RestoreFromFile(LabUser, path, LabPassphrase);
                    Check(!restored.ok && restored.added == 0, "Restore accepted an unsafe Windows name.");
                    Check(BackupState(LabUser) == baseline, "Rejected restore changed live data.");
                    Check(original.SequenceEqual(File.ReadAllBytes(path)), "Restore changed the input backup.");
                    Check(File.ReadAllText(sentinel) == "synthetic-original-sentinel", "Outside sentinel changed.");
                });
            Case("personal-mixed-dpapi-restore-and-drill", () =>
            {
                string path = Save(Zip(("windows-gl09-safe.txt", "synthetic-safe"), ("..\\escape.txt", "synthetic-attack"),
                    ("__tqltk__/manifest.json", Manifest(1, "synthetic-safe".Length))));
                var files = new LocalFileService();
                var originals = files.ListFilesReadOnly(LabUser).ToDictionary(f => f.FullName, f => File.ReadAllBytes(f.FullName));
                Check(FileBackupService.DrillRestore(LabUser, path, LabPassphrase).ok, "Valid mixed archive failed its encrypted drill.");
                Check(BackupState(LabUser) == baseline, "Drill changed live data.");
                var preview = FileBackupService.PreviewRestore(LabUser, path, LabPassphrase);
                Check(preview.ok && preview.preview!.InvalidCount == 1, "Preview did not identify unsafe entry.");
                var restored = FileBackupService.RestoreFromFile(LabUser, path, LabPassphrase);
                Check(restored.ok && restored.added == 1, "Mixed restore did not import exactly one safe file.");
                var current = files.ListFilesReadOnly(LabUser);
                Check(current.Count == originals.Count + 1, "Unexpected files were restored.");
                foreach (var prior in originals) Check(prior.Value.SequenceEqual(File.ReadAllBytes(prior.Key)), "An original file changed.");
                string stored = current.Single(f => f.Name == "windows-gl09-safe.txt").FullName;
                Check(LocalFileEncryptionService.IsEncryptedFile(stored), "Restored file is not encrypted locally.");
                using var expected = new MemoryStream(Encoding.UTF8.GetBytes("synthetic-safe"));
                Check(files.ContentEqualsReadOnly(stored, expected, expected.Length), "DPAPI-backed restored content differs.");
                Check(File.ReadAllText(sentinel) == "synthetic-original-sentinel", "Outside sentinel changed.");
            });
            string full = Path.Combine(root, "windows-path-control.tqltk");
            Case("complete-control", () =>
            {
                Check(CompleteBackupService.Create(LabUser, full, LabPassphrase).ok, "Cannot create full control backup.");
                Check(CompleteBackupService.PrepareRestore(LabUser, full, LabPassphrase).ok, "Valid full backup was rejected.");
            });
            foreach (var pair in new[] { "../escape.txt", "..\\escape.txt", "nested\\escape.txt", "C:\\escape.txt", "\\\\server\\share\\escape.txt" }.Select((name, index) => (name, index)))
                foreach (bool rogueEntry in new[] { false, true })
                    Case("complete-" + (rogueEntry ? "undeclared-entry-" : "manifest-path-") + pair.index, () =>
                    {
                        string before = BackupState(LabUser);
                        byte[] plain = BackupCrypto.DecryptBytes(File.ReadAllBytes(full), LabPassphrase);
                        byte[] changed;
                        try
                        {
                            using var stream = new MemoryStream();
                            stream.Write(plain);
                            using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, true))
                            {
                                if (rogueEntry)
                                {
                                    using var writer = new StreamWriter(archive.CreateEntry(pair.name).Open());
                                    writer.Write("synthetic-attack");
                                }
                                else
                                {
                                    var entry = archive.GetEntry("__tqltk__/manifest.json")!;
                                    JsonNode manifest;
                                    using (var reader = new StreamReader(entry.Open())) manifest = JsonNode.Parse(reader.ReadToEnd())!;
                                    manifest["PersonalFiles"]![0]!["FileName"] = pair.name;
                                    entry.Delete();
                                    using var writer = new StreamWriter(archive.CreateEntry("__tqltk__/manifest.json").Open());
                                    writer.Write(manifest.ToJsonString());
                                }
                            }
                            changed = stream.ToArray();
                        }
                        finally { CryptographicOperations.ZeroMemory(plain); }
                        string path = Save(changed);
                        Check(!CompleteBackupService.Inspect(path, LabPassphrase).ok, "Full inspection accepted a traversal path.");
                        Check(!CompleteBackupService.PrepareRestore(LabUser, path, LabPassphrase).ok, "Full restore planning accepted a traversal path.");
                        Check(BackupState(LabUser) == before, "Full restore planning changed live data.");
                        Check(File.ReadAllText(sentinel) == "synthetic-original-sentinel", "Outside sentinel changed.");
                    });
        }
        finally
        {
            string report = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            new XDocument(new XElement("testsuites", new XElement("testsuite", new XAttribute("name", "TQLTK.Windows.GL09"),
                new XAttribute("tests", cases.Count), new XAttribute("failures", failures), new XAttribute("errors", 0),
                new XAttribute("skipped", 0), cases))).Save(report);
        }
        if (failures > 0) throw new InvalidDataException($"Windows GL-09: {failures} failed cases.");
    }
}
