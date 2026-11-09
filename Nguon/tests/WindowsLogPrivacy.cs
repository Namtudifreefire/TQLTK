using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

internal static partial class Program
{
    private static void LabWindowsLogPrivacy(string root, string reportPath)
    {
        var results = new List<XElement>();
        int failed = 0;
        string marker = "SYNTHETIC-GL11-" + Guid.NewGuid().ToString("N");
        string totp = "JBSWY3DPEHPK3PXPKRSXG5A7";
        string[] secrets = { marker, marker + "-password", marker + "-note", marker + "-custom", marker + "-attachment", marker + "-passphrase", totp };
        string[] needles = secrets.SelectMany(s => new[] { s, Convert.ToBase64String(Encoding.UTF8.GetBytes(s)),
            Convert.ToHexString(Encoding.UTF8.GetBytes(s)), Uri.EscapeDataString(s) }).Distinct().ToArray();
        bool Leaks(string s) => needles.Any(needle => s.Contains(needle, StringComparison.Ordinal));
        void Check(bool ok) { if (!ok) throw new InvalidDataException("GL-11 synthetic privacy invariant failed; values withheld."); }
        void Scan()
        {
            var lines = ActivityLogger.ListLogFiles().SelectMany(File.ReadAllLines).ToArray();
            Check(lines.Length > 0);
            foreach (string line in lines)
            {
                Check(ActivityLogCodec.TryParse(line, out var item));
                Check(!Leaks(line) && !Leaks(item!.Detail));
                foreach (var arg in item!.Arguments) Check(!Leaks(arg.ToString()));
            }
        }
        void Case(string name, Action action)
        {
            var item = new XElement("testcase", new XAttribute("classname", "TQLTK.Windows.GL11"), new XAttribute("name", name));
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try { action(); Console.WriteLine("PASS Windows GL-11 " + name); }
            catch (Exception ex)
            {
                failed++;
                item.Add(new XElement("failure", new XAttribute("type", ex.GetType().Name), "Synthetic privacy assertion failed; secret values withheld."));
                Console.Error.WriteLine("FAIL Windows GL-11 " + name);
            }
            item.Add(new XAttribute("time", timer.Elapsed.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
            results.Add(item);
        }
        try
        {
            var vault = new AccountVaultService();
            var account = vault.Load(LabUser).Single();
            account.Password = marker + "-password";
            account.TotpSecret = totp;
            account.Note = marker + "-note";
            account.CustomFields.Add(new AccountCustomField { Name = "Recovery", Value = marker + "-custom", IsSensitive = true });
            account.Attachments.Add(new AccountAttachment { FileName = "private.txt", Content = Encoding.UTF8.GetBytes(marker + "-attachment") });
            vault.Save(LabUser, new List<SavedAccount> { account });
            new NoteService().Save(new List<Note> { new() { Title = "Synthetic note", Content = marker + "-note" } });
            new LocalFileService().SaveTextFile(LabUser, "synthetic-secret.txt", marker + "-attachment");
            BackupCrypto.SaveStoredPassphrase(LabUser, marker + "-passphrase");
            Case("logger-positive-and-detector-negative-controls", () =>
            {
                ActivityLogger.Log(LabUser, "GL11_CONTROL", "public metadata");
                Check(ActivityLogger.ListLogFiles().SelectMany(File.ReadAllLines).Any(line => line.Contains("GL11_CONTROL", StringComparison.Ordinal)));
                foreach (string secret in secrets) Check(Leaks(secret));
                Check(Leaks(ActivityLogCodec.Serialize(ActivityLogCodec.Create(DateTimeOffset.Now, LabUser, "GL11_DETECTOR", detail: marker))));
                Scan();
            });
            foreach (string operation in new[] { "vault-export", "full-create", "full-restore", "wrong-passphrase", "corrupt-backup" })
                Case(operation + "-no-log-or-console-disclosure", () =>
                {
                    var stdout = Console.Out; var stderr = Console.Error;
                    using var captured = new StringWriter();
                    try
                    {
                        Console.SetOut(captured); Console.SetError(captured);
                        string path = Path.Combine(root, "synthetic-complete.tqltk");
                        if (operation == "vault-export") Check(VaultBackupService.ExportToFile(LabUser, Path.Combine(root, "synthetic.vault")).ok);
                        if (operation == "full-create") Check(CompleteBackupService.Create(LabUser, path, marker + "-passphrase").ok);
                        if (operation == "full-restore")
                        {
                            var plan = CompleteBackupService.PrepareRestore(LabUser, path, marker + "-passphrase");
                            Check(plan.ok && plan.preview != null);
                            Check(CompleteBackupService.Restore(LabUser, plan.preview!, marker + "-passphrase", false).ok);
                        }
                        if (operation == "wrong-passphrase") Check(!CompleteBackupService.PrepareRestore(LabUser, path, "incorrect-synthetic-passphrase").ok);
                        if (operation == "corrupt-backup")
                        {
                            string invalid = Path.Combine(root, "invalid.tqltk");
                            File.WriteAllText(invalid, marker);
                            Check(!CompleteBackupService.PrepareRestore(LabUser, invalid, marker + "-passphrase").ok);
                        }
                    }
                    finally { Console.SetOut(stdout); Console.SetError(stderr); }
                    Check(!Leaks(captured.ToString()));
                    Scan();
                });
            Case("expected-business-events-were-written", () =>
            {
                var events = ActivityLogger.ListLogFiles().SelectMany(File.ReadAllLines).Select(line => {
                    Check(ActivityLogCodec.TryParse(line, out var item)); return item!.EventId;
                }).ToHashSet();
                Check(new[] { "EXPORT_VAULT", "CREATE_COMPLETE_BACKUP", "RESTORE_COMPLETE_BACKUP" }.All(events.Contains));
                Check(vault.Load(LabUser).Single().Password == marker + "-password");
                Scan();
            });
        }
        catch (Exception) { Case("harness-infrastructure", () => Check(false)); }
        finally
        {
            string path = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            new XDocument(new XElement("testsuites", new XElement("testsuite", new XAttribute("name", "TQLTK.Windows.GL11"),
                new XAttribute("tests", results.Count), new XAttribute("failures", failed), new XAttribute("errors", 0), new XAttribute("skipped", 0), results))).Save(path);
        }
        if (failed > 0) throw new InvalidDataException("Windows GL-11 failed; synthetic values withheld.");
    }
}
