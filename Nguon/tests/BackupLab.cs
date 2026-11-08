using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;
internal static partial class Program
{
    private const string LabUser = "backup-lab-admin";
    private const string LabPassphrase = "Synthetic-CI-backup-passphrase-2026!";
    private static int RunBackupLab(string[] args)
    {
        string? root = null;
        bool ownsRoot = false;
        try
        {
            if (!OperatingSystem.IsWindows() || args.Length != 3 || args[0] != "--backup-lab")
                throw new ArgumentException("Usage on Windows: --backup-lab <mode> <path>");
            bool child = args[1] == "crash-child";
            root = child ? Path.GetFullPath(args[2]) : Path.Combine(Path.GetTempPath(), "TQLTK-BackupLab-" + Guid.NewGuid().ToString("N"));
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("TQLTK-BackupLab-", StringComparison.Ordinal))
                throw new IOException("Unsafe backup lab root.");
            if (child)
            {
                if (!File.Exists(Path.Combine(root, "synthetic-lab.marker"))) throw new IOException("Missing synthetic lab marker.");
            }
            else
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "synthetic-lab.marker"), "synthetic backup test only");
                ownsRoot = true;
            }
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", root);
            if (ApplicationDataPath.Root != root) throw new InvalidOperationException("Isolated SECURITY_TEST build required.");
            if (child) { LabCrashChild(root); return 3; }
            LabSetup(args[1] == "transfer-import" ? "consumer" : "producer");
            switch (args[1])
            {
                case "rollback": LabRollbackFailure(root); break;
                case "crash": LabCrash(root); break;
                case "disk-full": LabDiskFull(args[2]); break;
                case "transfer-export": LabTransferExport(args[2]); break;
                case "transfer-import": LabTransferImport(args[2]); break;
                default: throw new ArgumentException("Unsupported backup lab mode.");
            }
            Console.WriteLine("PASS backup-lab: " + args[1]);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL backup-lab: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
        finally
        {
            CompleteBackupService.RestoreTestCheckpoint = null;
            if (ownsRoot && root != null)
            {
                string full = Path.GetFullPath(root);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("TQLTK-BackupLab-", StringComparison.Ordinal))
                    throw new IOException("Refusing unsafe lab cleanup.");
                Directory.Delete(full, recursive: true);
            }
        }
    }
    private static void LabSetup(string label)
    {
        var created = new UserService().CreateInitialAdmin(LabUser, "Synthetic-user-password-2026!", "Synthetic Lab", "lab@example.invalid", "");
        if (!created.ok) throw new InvalidOperationException(created.message);
        new AccountVaultService().Save(LabUser, new List<SavedAccount> {
            new() { ServiceName = label, Username = label + "@example.invalid", Password = "Synthetic-" + label + "-password!", TotpSecret = "JBSWY3DPEHPK3PXP" }
        });
        new NoteService().Save(new List<Note> { new() { Title = label, Content = "synthetic-" + label + "-note" } });
        new LocalFileService().SaveTextFile(LabUser, label + ".txt", "synthetic-" + label + "-file");
        BackupCrypto.SaveStoredPassphrase(LabUser, LabPassphrase);
    }
    private static string LabIncomingPackage(string root)
    {
        var vault = new AccountVaultService();
        var notes = new NoteService();
        var original = vault.Load(LabUser).Select(a => a.Clone()).ToList();
        var originalNotes = notes.Load();
        string path = Path.Combine(root, "incoming.tqltk");
        try
        {
            vault.Save(LabUser, original.Concat(new[] { new SavedAccount { ServiceName = "incoming", Username = "incoming@example.invalid", Password = "Synthetic-incoming-password!" } }).ToList());
            notes.Save(originalNotes.Concat(new[] { new Note { Title = "incoming", Content = "synthetic-incoming-note" } }).ToList());
            if (!CompleteBackupService.Create(LabUser, path, LabPassphrase).ok) throw new IOException("Could not prepare incoming fixture.");
        }
        finally { vault.Save(LabUser, original); notes.Save(originalNotes); }
        return path;
    }
    private static CompleteBackupPreview LabPreview(string path)
    {
        var result = CompleteBackupService.PrepareRestore(LabUser, path, LabPassphrase);
        return result.ok && result.preview != null ? result.preview : throw new InvalidDataException(result.message);
    }
    private static void LabRollbackFailure(string root)
    {
        string path = LabIncomingPackage(root);
        string baseline = BackupState(LabUser);
        var preview = LabPreview(path);
        var original = new AccountVaultService().Load(LabUser).Single().Clone();
        CompleteBackupService.RestoreTestCheckpoint = checkpoint =>
        {
            if (checkpoint is "after-vault-save" or "before-vault-rollback") throw new IOException("Synthetic " + checkpoint + " failure");
        };
        var result = CompleteBackupService.Restore(LabUser, preview, LabPassphrase, false);
        CompleteBackupService.RestoreTestCheckpoint = null;
        RequireBackup(!result.ok && result.message.Contains("Không hoàn tác được hoàn toàn", StringComparison.Ordinal), "rollback failure is explicitly reported, not silently called successful");
        AccountVaultService.ClearCache();
        var after = new AccountVaultService().Load(LabUser);
        RequireBackup(after.Single(a => a.Id == original.Id).Password == original.Password && after.Count == 2,
            "rollback failure retains original account but leaves an explicitly observed partial import");
        RequireBackup(BackupState(LabUser) != baseline, "rollback-failed state is partial, not incorrectly claimed unchanged");
        Console.WriteLine("LIMITATION: rollback can fail; state is partial and requires recovery. This test checks error reporting and original-data retention.");
        var retry = CompleteBackupService.Restore(LabUser, LabPreview(path), LabPassphrase, false);
        RequireBackup(retry.ok && retry.accountsAdded == 0 && retry.notesAdded == 1, "explicit retry completes remaining data without duplicate accounts");
    }
    private static void LabCrashChild(string root)
    {
        CompleteBackupService.RestoreTestCheckpoint = checkpoint =>
        {
            if (checkpoint != "after-vault-save") return;
            File.WriteAllText(Path.Combine(root, "crash-ready.marker"), "vault saved");
            Thread.Sleep(Timeout.Infinite);
        };
        var result = CompleteBackupService.Restore(LabUser, LabPreview(Path.Combine(root, "incoming.tqltk")), LabPassphrase, false);
        throw new InvalidOperationException("Child returned without being killed: " + result.ok);
    }
    private static void LabCrash(string root)
    {
        string path = LabIncomingPackage(root);
        string baseline = BackupState(LabUser);
        string originalPassword = new AccountVaultService().Load(LabUser).Single().Password;
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--backup-lab"); start.ArgumentList.Add("crash-child"); start.ArgumentList.Add(root);
        using var child = Process.Start(start) ?? throw new IOException("Could not start restore child.");
        try
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(root, "crash-ready.marker")) && !child.HasExited && watch.Elapsed < TimeSpan.FromSeconds(60)) Thread.Sleep(50);
            if (child.HasExited || !File.Exists(Path.Combine(root, "crash-ready.marker"))) throw new TimeoutException("Child did not reach the persisted-vault checkpoint.");
            child.Kill(entireProcessTree: true);
            if (!child.WaitForExit(15000)) throw new TimeoutException("Restore child did not terminate.");
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(15000); } }
        AccountVaultService.ClearCache();
        var after = new AccountVaultService().Load(LabUser);
        RequireBackup(after.Single(a => a.ServiceName == "producer").Password == originalPassword && after.Count == 2,
            "fresh read after forced process termination keeps originals and observes persisted partial import");
        RequireBackup(new NoteService().Load().Count == 1 && BackupState(LabUser) != baseline, "forced termination does not run automatic full-backup rollback");
        Console.WriteLine("LIMITATION: no automatic atomic crash rollback; explicit restore retry is required.");
        var retry = CompleteBackupService.Restore(LabUser, LabPreview(path), LabPassphrase, false);
        RequireBackup(retry.ok && retry.accountsAdded == 0 && retry.notesAdded == 1 && new AccountVaultService().Load(LabUser).Count == 2,
            "restore after terminated process completes remaining data without duplication");
    }
    private static void LabTransferExport(string bundle)
    {
        Directory.CreateDirectory(bundle);
        RequireBackup(CompleteBackupService.Create(LabUser, Path.Combine(bundle, "complete.tqltk"), LabPassphrase).ok, "export synthetic portable complete backup");
        string machineCipher = AesCrypto.Encrypt("synthetic machine-bound marker");
        File.WriteAllText(Path.Combine(bundle, "machine-bound.txt"), machineCipher);
        File.WriteAllText(Path.Combine(bundle, "producer.json"), JsonSerializer.Serialize(new { Machine = Environment.MachineName, Sid = WindowsIdentity.GetCurrent().User!.Value }));
        File.WriteAllText(Path.Combine(bundle, "legacy-v3p.vault"), LabLegacyFixture());
    }
    private static void LabTransferImport(string bundle)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "producer.json")));
        RequireBackup(metadata.RootElement.GetProperty("Machine").GetString() != Environment.MachineName, "portable backup consumer runs on a different Windows machine");
        RequireBackup(metadata.RootElement.GetProperty("Sid").GetString() != WindowsIdentity.GetCurrent().User!.Value, "portable backup consumer has a different Windows SID");
        bool rejected = false;
        try { AesCrypto.Decrypt(File.ReadAllText(Path.Combine(bundle, "machine-bound.txt"))); }
        catch (CryptographicException) { rejected = true; }
        RequireBackup(rejected, "machine-bound ciphertext cannot be decrypted with consumer machine key");
        var result = CompleteBackupService.Restore(LabUser, LabPreview(Path.Combine(bundle, "complete.tqltk")), LabPassphrase, false);
        RequireBackup(result.ok && result.accountsAdded == 1 && result.notesAdded == 1 && result.filesAdded == 1, "portable full backup restores all categories on consumer machine");
        var accounts = new AccountVaultService().Load(LabUser);
        RequireBackup(accounts.Count == 2 && accounts.Single(a => a.ServiceName == "producer").TotpSecret == "JBSWY3DPEHPK3PXP", "cross-machine restore keeps consumer data and producer TOTP");
        var old = VaultBackupService.ImportFromFile(LabUser, Path.Combine(bundle, "legacy-v3p.vault"), LabPassphrase);
        RequireBackup(old.ok && new AccountVaultService().Load(LabUser).Any(a => a.ServiceName == "legacy-service"), "legacy-schema v3p 100000-iteration fixture imports across Windows machines");
        Console.WriteLine("COVERAGE: legacy fixture is independently generated, not an archived real-user backup or proof for every historical release.");
    }
    private static string LabLegacyFixture()
    {
        byte[] header = RandomNumberGenerator.GetBytes(20);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), 100000);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plain = Encoding.UTF8.GetBytes("{\"ExportedBy\":\"backup-lab-admin\",\"AppVersion\":\"3.6.2\",\"AccountCount\":1,\"Accounts\":[{\"ServiceName\":\"legacy-service\",\"Username\":\"legacy@example.invalid\",\"Password\":\"Synthetic-legacy-password!\"}]}");
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(LabPassphrase, header[..16], 100000, HashAlgorithmName.SHA256, 32);
        byte[] cipher = new byte[plain.Length]; byte[] tag = new byte[16];
        try
        {
            using var gcm = new AesGcm(key, 16); gcm.Encrypt(nonce, plain, cipher, tag, header);
            return "v3p:" + Convert.ToBase64String(header.Concat(nonce).Concat(tag).Concat(cipher).ToArray());
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    private static void LabDiskFull(string volumeRoot)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(volumeRoot))!);
        if (!Path.GetFullPath(volumeRoot).Equals(drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase) ||
            !drive.VolumeLabel.StartsWith("TQLTKLAB", StringComparison.Ordinal) || drive.TotalSize > 128L * 1024 * 1024 ||
            drive.RootDirectory.FullName.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Refusing disk-full test outside the verified small lab volume.");
        string backup = Path.Combine(volumeRoot, "previous.tqltk");
        RequireBackup(CompleteBackupService.Create(LabUser, backup, LabPassphrase).ok, "create valid backup on isolated test volume");
        byte[] before = File.ReadAllBytes(backup);
        string state = BackupState(LabUser);
        string filler = Path.Combine(volumeRoot, "bounded-lab-filler.bin");
        bool full = false;
        try
        {
            using (var file = new FileStream(filler, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] block = RandomNumberGenerator.GetBytes(4096);
                try { for (long size = 0; size <= drive.TotalSize; size += block.Length) { file.Write(block); file.Flush(true); } }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 112 or 39) { full = true; }
            }
            RequireBackup(full, "isolated Windows volume produced a real disk-full error");
            RequireBackup(!CompleteBackupService.Create(LabUser, backup, LabPassphrase).ok && File.ReadAllBytes(backup).SequenceEqual(before) && BackupState(LabUser) == state,
                "real disk-full export failure preserves previous backup and live data");
        }
        finally { if (File.Exists(filler)) File.Delete(filler); }
        RequireBackup(CompleteBackupService.Inspect(backup, LabPassphrase).ok, "previous backup still decrypts after space is released");
    }
}
