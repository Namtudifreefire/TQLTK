using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;
internal static partial class Program
{
    private static void WithBackupProbe(string parent, Action<string> test)
    {
        string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string folder = Path.GetFullPath(Path.Combine(root, "backup-probe-" + Guid.NewGuid().ToString("N")));
        if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe test directory.");
        Directory.CreateDirectory(folder);
        try { test(folder); }
        finally
        {
            if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(folder).StartsWith("backup-probe-", StringComparison.Ordinal))
                throw new IOException("Refusing to clean an unexpected test directory.");
            Directory.Delete(folder, recursive: true);
        }
    }
    private static void RequireBackup(bool condition, string scenario)
    {
        if (!condition) throw new InvalidDataException(scenario);
        Console.WriteLine("PASS backup-case: " + scenario);
    }
    private static string BackupState(string username) => CompleteBackupService.GetCurrentStateFingerprint(username);
    private static void TestBackupFailureMatrix(string username, string folder, string passphrase)
    {
        string baseline = BackupState(username);
        string full = Path.Combine(folder, "failure-matrix.tqltk");
        string vault = Path.Combine(folder, "failure-matrix.vault");
        string files = Path.Combine(folder, "failure-matrix.tqf");
        RequireBackup(CompleteBackupService.Create(username, full, passphrase).ok, "create full failure fixture");
        RequireBackup(VaultBackupService.ExportToFile(username, vault).ok, "create vault failure fixture");
        RequireBackup(FileBackupService.CreateArchive(username, files, passphrase).ok, "create file failure fixture");
        byte[] completeFixture = File.ReadAllBytes(full);
        foreach (string format in new[] { "full", "vault", "files" })
        {
            string source = format == "full" ? full : format == "vault" ? vault : files;
            byte[] original = File.ReadAllBytes(source);
            foreach (string damage in new[] { "empty", "truncated", "header", "payload" })
            {
                byte[] broken = damage == "empty" ? Array.Empty<byte>() :
                    damage == "truncated" ? original[..(original.Length / 2)] : original.ToArray();
                if (damage == "header") broken[0] ^= 0x7f;
                if (damage == "payload") broken[^5] ^= 1;
                string path = Path.Combine(folder, "broken-" + format + "-" + damage);
                File.WriteAllBytes(path, broken);
                bool accepted = format == "full" ? CompleteBackupService.PrepareRestore(username, path, passphrase).ok :
                    format == "vault" ? VaultBackupService.ImportFromFile(username, path, passphrase).ok :
                    FileBackupService.RestoreFromFile(username, path, passphrase).ok;
                RequireBackup(!accepted && BackupState(username) == baseline && File.ReadAllBytes(path).SequenceEqual(broken),
                    format + " rejects " + damage + " and preserves source/live data");
            }
            bool wrongAccepted = format == "full" ? CompleteBackupService.PrepareRestore(username, source, "incorrect synthetic passphrase").ok :
                format == "vault" ? VaultBackupService.ImportFromFile(username, source, "incorrect synthetic passphrase").ok :
                FileBackupService.RestoreFromFile(username, source, "incorrect synthetic passphrase").ok;
            RequireBackup(!wrongAccepted && BackupState(username) == baseline && File.ReadAllBytes(source).SequenceEqual(original),
                format + " rejects wrong passphrase and preserves data");
        }
        var preview = CompleteBackupService.PrepareRestore(username, full, passphrase);
        RequireBackup(preview.ok && preview.preview != null, "prepare full preview for commit guards");
        RequireBackup(!CompleteBackupService.Restore(username, preview.preview!, "incorrect synthetic passphrase", false).ok && BackupState(username) == baseline,
            "full restore rejects wrong passphrase at commit without writes");
        File.AppendAllText(full, "changed after preview");
        RequireBackup(!CompleteBackupService.Restore(username, preview.preview!, passphrase, false).ok && BackupState(username) == baseline,
            "full restore rejects backup modified after preview");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        RequireBackup(!FileBackupService.RestoreFromFile(username, files, passphrase, cancellationToken: cancelled.Token).ok && BackupState(username) == baseline,
            "pre-cancelled file restore preserves data");
        string malformed = Path.Combine(folder, "authenticated-but-not-zip.tqltk");
        File.WriteAllBytes(malformed, BackupCrypto.EncryptBytes(Encoding.UTF8.GetBytes("synthetic non-ZIP payload"), passphrase));
        RequireBackup(!CompleteBackupService.PrepareRestore(username, malformed, passphrase).ok && BackupState(username) == baseline,
            "full restore rejects authenticated invalid archive");
        foreach (string corruption in new[] { "future-version", "count", "missing-accounts", "duplicate-manifest", "invalid-json" })
        {
            byte[] zipBytes = BackupCrypto.DecryptBytes(completeFixture, passphrase);
            using var stream = new MemoryStream();
            stream.Write(zipBytes);
            CryptographicOperations.ZeroMemory(zipBytes);
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            {
                var entry = zip.GetEntry("__tqltk__/manifest.json")!;
                string manifest;
                using (var reader = new StreamReader(entry.Open())) manifest = reader.ReadToEnd();
                if (corruption == "missing-accounts") zip.GetEntry("data/accounts.json")!.Delete();
                else if (corruption == "duplicate-manifest")
                {
                    using var writer = new StreamWriter(zip.CreateEntry(entry.FullName).Open());
                    writer.Write(manifest);
                }
                else
                {
                    var parsed = JsonNode.Parse(manifest)!;
                    if (corruption == "future-version") parsed["FormatVersion"] = 999;
                    if (corruption == "count") parsed["AccountCount"] = 999;
                    entry.Delete();
                    using var writer = new StreamWriter(zip.CreateEntry("__tqltk__/manifest.json").Open());
                    writer.Write(corruption == "invalid-json" ? "{" : parsed.ToJsonString());
                }
            }
            string path = Path.Combine(folder, "invalid-" + corruption + ".tqltk");
            File.WriteAllBytes(path, BackupCrypto.EncryptBytes(stream.ToArray(), passphrase));
            RequireBackup(!CompleteBackupService.PrepareRestore(username, path, passphrase).ok && BackupState(username) == baseline,
                "full restore rejects authenticated " + corruption + " without changing live data");
        }
    }
    private static void TestBackupCompatibility(string username, string folder, string passphrase)
    {
        string baseline = BackupState(username);
        var accounts = new AccountVaultService().Load(username);
        string json = JsonSerializer.Serialize(new { ExportedBy = username, AppVersion = "3.6.2", AccountCount = accounts.Count, Accounts = accounts });
        byte[] header = new byte[20];
        RandomNumberGenerator.Fill(header.AsSpan(0, 16));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), 100_000);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase.Normalize(NormalizationForm.FormC), header.AsSpan(0, 16).ToArray(), 100_000, HashAlgorithmName.SHA256, 32);
        byte[] plain = Encoding.UTF8.GetBytes(json);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        try
        {
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag, header);
            string old = "v3p:" + Convert.ToBase64String(header.Concat(nonce).Concat(tag).Concat(cipher).ToArray());
            string path = Path.Combine(folder, "legacy-v3p-100k.vault");
            File.WriteAllText(path, old);
            RequireBackup(VaultBackupService.InspectBackup(username, path, passphrase).ok &&
                VaultBackupService.ImportFromFile(username, path, passphrase).ok && BackupState(username) == baseline,
                "synthetic legacy v3p 100000-iteration fixture imports without duplicating originals");
            string machine = Path.Combine(folder, "legacy-machine-v2.vault");
            File.WriteAllText(machine, AesCrypto.Encrypt(json));
            RequireBackup(VaultBackupService.InspectBackup(username, machine).ok &&
                VaultBackupService.ImportFromFile(username, machine).ok && BackupState(username) == baseline,
                "machine-key GCM v2 fixture remains readable");
            string cbcPath = Path.Combine(folder, "unsupported-cbc.vault");
            using var cbc = Aes.Create();
            byte[] cbcBytes = cbc.IV.Concat(cbc.EncryptCbc(plain, cbc.IV)).ToArray();
            File.WriteAllText(cbcPath, Convert.ToBase64String(cbcBytes));
            byte[] before = File.ReadAllBytes(cbcPath);
            RequireBackup(!VaultBackupService.ImportFromFile(username, cbcPath).ok && BackupState(username) == baseline && File.ReadAllBytes(cbcPath).SequenceEqual(before),
                "unsupported CBC remains rejected and source retained by design");
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    private static void TestBackupWriteFailure(string username, string folder, string passphrase)
    {
        string baseline = BackupState(username);
        foreach (string format in new[] { "full", "vault", "files" })
        {
            string destination = Path.Combine(folder, "locked-export-" + format);
            bool created = format == "full" ? CompleteBackupService.Create(username, destination, passphrase).ok :
                format == "vault" ? VaultBackupService.ExportToFile(username, destination).ok :
                FileBackupService.CreateArchive(username, destination, passphrase).ok;
            RequireBackup(created, format + " creates valid previous backup before locking destination");
            byte[] sentinel = File.ReadAllBytes(destination);
            using (var held = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool succeeded = format == "full" ? CompleteBackupService.Create(username, destination, passphrase).ok :
                    format == "vault" ? VaultBackupService.ExportToFile(username, destination).ok :
                    FileBackupService.CreateArchive(username, destination, passphrase).ok;
                RequireBackup(!succeeded, format + " reports locked destination write failure");
            }
            RequireBackup(File.ReadAllBytes(destination).SequenceEqual(sentinel) && BackupState(username) == baseline,
                format + " keeps previous backup and live data after write failure");
            bool readable = format == "full" ? CompleteBackupService.Inspect(destination, passphrase).ok :
                format == "vault" ? VaultBackupService.InspectBackup(username, destination, passphrase).ok :
                FileBackupService.InspectBackup(username, destination, passphrase).ok;
            RequireBackup(readable, format + " previous backup still decrypts after failed replacement");
            RequireBackup(!Directory.EnumerateFiles(folder, "." + Path.GetFileName(destination) + ".*.tmp").Any(),
                format + " removes staging file after export failure");
        }
    }
    private static void TestRestoreWriteFailure(string username, string folder, string passphrase)
    {
        var vault = new AccountVaultService();
        var notes = new NoteService();
        var originals = vault.Load(username).Select(a => a.Clone()).ToList();
        var originalNotes = notes.Load();
        string path = Path.Combine(folder, "restore-write-failure.tqltk");
        try
        {
            var incoming = originals.Select(a => a.Clone()).ToList();
            incoming.Add(new SavedAccount { ServiceName = "Synthetic rollback incoming", Username = "rollback@example.invalid", Password = "Synthetic-Rollback-2026!" });
            vault.Save(username, incoming);
            notes.Save(originalNotes.Concat(new[] { new Note { Title = "Synthetic incoming note", Content = "rollback fixture" } }).ToList());
            RequireBackup(CompleteBackupService.Create(username, path, passphrase).ok, "create rollback fixture with new account and note");
        }
        finally { vault.Save(username, originals); notes.Save(originalNotes); }
        string before = BackupState(username);
        var prepared = CompleteBackupService.PrepareRestore(username, path, passphrase);
        RequireBackup(prepared.ok && prepared.preview is { AccountsToAdd: 1, NotesToAdd: 1 }, "rollback fixture requires both vault and note writes");
        string notePath = Path.Combine(ApplicationDataPath.Root, "PersonalInfoManager", "notes.json");
        byte[] originalNoteBytes = File.ReadAllBytes(notePath);
        using (var held = new FileStream(notePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = CompleteBackupService.Restore(username, prepared.preview!, passphrase, restoreSettings: false);
            RequireBackup(!result.ok && result.accountsAdded == 0 && result.notesAdded == 0,
                "mid-restore note write failure is reported as failure");
        }
        AccountVaultService.ClearCache();
        RequireBackup(BackupState(username) == before && File.ReadAllBytes(notePath).SequenceEqual(originalNoteBytes),
            "vault already saved before note failure is rolled back; original notes/files/settings survive");
    }
}
