using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

internal static partial class Program
{
    private static int RunGl13(string reportPath)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        string root = Path.Combine(Path.GetTempPath(), "TQLTK-SecurityRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", root);
        var cases = new List<XElement>();
        int failures = 0;
        void Case(string name, Action action)
        {
            var item = new XElement("testcase", new XAttribute("classname", "TQLTK.Windows.GL13"), new XAttribute("name", name));
            try { action(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; item.Add(new XElement("failure", new XAttribute("type", ex.GetType().Name), "Synthetic migration test failed.")); Console.Error.WriteLine("FAIL " + name + ": " + ex.GetType().Name); }
            cases.Add(item);
        }
        try
        {
            Gl13Check(string.Equals(ApplicationDataPath.Root, root, StringComparison.OrdinalIgnoreCase), "test isolation");
            Case("vault-migration-idempotency", () => TestGl13Vault(root));
            Case("users-protection-recovery-io", () => TestGl13Users(root));
            Case("vault-collision-preserves-source", () => TestGl13Collision(root));
            Case("users-corrupt-no-valid-backup", () => TestGl13CorruptUsers(root));
            string fullReport = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullReport)!);
            new XDocument(new XElement("testsuite", new XAttribute("name", "TQLTK.Windows.GL13"), new XAttribute("tests", cases.Count), new XAttribute("failures", failures), new XAttribute("skipped", 0), cases)).Save(fullReport);
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", null);
            AccountVaultService.ClearCache();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestGl13Collision(string parent)
    {
        Gl13Isolated(parent, "collision", folder =>
        {
            Directory.CreateDirectory(folder);
            var first = new User { Username = "synthetic/a" };
            var second = new User { Username = "synthetic?a" };
            File.WriteAllText(Path.Combine(folder, "users.json"), JsonSerializer.Serialize(new[] { first, second }));
            string source = UserStorageIdentity.GetLegacyPath(folder, first.Username, LegacyUserStorageKind.Vault);
            Gl13Check(source.Equals(UserStorageIdentity.GetLegacyPath(folder, second.Username, LegacyUserStorageKind.Vault), StringComparison.OrdinalIgnoreCase), "fixture collision");
            byte[] original = System.Text.Encoding.UTF8.GetBytes("synthetic-legacy-collision");
            File.WriteAllBytes(source, original);
            bool rejected = false;
            try { _ = new AccountVaultService().Load(first.Username); }
            catch (VaultUnreadableException) { rejected = true; }
            Gl13Check(rejected && File.ReadAllBytes(source).SequenceEqual(original), "collision rejected and source preserved");
            Gl13Check(!File.Exists(Path.Combine(folder, "vault_" + UserStorageIdentity.GetDirectoryName(first.Id) + ".dat")), "collision created no destination");
        });
    }

    private static void TestGl13CorruptUsers(string parent)
    {
        foreach (bool corruptBackup in new[] { false, true })
            Gl13Isolated(parent, corruptBackup ? "corrupt-backup" : "no-backup", folder =>
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "users.json");
                byte[] invalid = System.Text.Encoding.UTF8.GetBytes("{synthetic-invalid");
                File.WriteAllBytes(path, invalid);
                if (corruptBackup) File.WriteAllBytes(path + ".bak", invalid);
                bool rejected = false;
                try { _ = new UserService(); } catch (UsersFileUnreadableException) { rejected = true; }
                Gl13Check(rejected && File.ReadAllBytes(path).SequenceEqual(invalid), "corrupt users rejected and preserved");
                if (corruptBackup) Gl13Check(File.ReadAllBytes(path + ".bak").SequenceEqual(invalid), "corrupt backup preserved");
            });
    }
    private static void Gl13Check(bool condition, string scenario)
    {
        if (!condition) throw new InvalidDataException("GL13: " + scenario);
    }

    private static void Gl13Isolated(string parent, string name, Action<string> action)
    {
        string root = Path.Combine(parent, "gl13-" + name);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", root);
            AccountVaultService.ClearCache();
            action(Path.Combine(root, "PersonalInfoManager"));
        }
        finally
        {
            AccountVaultService.ClearCache();
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", previous);
        }
    }

    private static void TestGl13Vault(string parent)
    {
        Gl13Isolated(parent, "vault", folder =>
        {
            const string username = "gl13synthetic";
            var service = new UserService();
            Gl13Check(service.CreateInitialAdmin(username, "Synthetic-GL13-password-2026!", "Synthetic", "gl13@example.invalid", "").ok, "create synthetic user");
            var user = service.FindByUsername(username)!;
            var vault = new AccountVaultService();
            vault.Save(username, new List<SavedAccount> { new() { ServiceName = "synthetic-service", Username = "fixture", Password = "fixture-password" } });
            string target = Path.Combine(folder, "vault_" + UserStorageIdentity.GetDirectoryName(user.Id) + ".dat");
            string legacy = UserStorageIdentity.GetLegacyPath(folder, username, LegacyUserStorageKind.Vault);
            byte[] original = File.ReadAllBytes(target);
            File.Move(target, legacy);
            AccountVaultService.ClearCache();
            var loaded = vault.Load(username);
            Gl13Check(loaded.Count == 1 && loaded[0].ServiceName == "synthetic-service" && loaded[0].Password == "fixture-password", "legacy data readable");
            Gl13Check(File.Exists(target) && !File.Exists(legacy) && File.ReadAllBytes(target).SequenceEqual(original), "migration preserves bytes");
            var before = Directory.GetFiles(folder).OrderBy(x => x).ToArray();
            AccountVaultService.ClearCache();
            Gl13Check(vault.Load(username).Count == 1 && File.ReadAllBytes(target).SequenceEqual(original), "second read preserves data");
            Gl13Check(before.SequenceEqual(Directory.GetFiles(folder).OrderBy(x => x)), "second read creates no archive");
            File.WriteAllBytes(legacy, original);
            AccountVaultService.ClearCache();
            Gl13Check(vault.Load(username).Count == 1 && File.ReadAllBytes(target).SequenceEqual(original), "existing destination remains intact");
            string[] archived = Directory.GetFiles(folder, Path.GetFileName(legacy) + ".migrated-*");
            Gl13Check(archived.Length == 1 && File.ReadAllBytes(archived[0]).SequenceEqual(original), "legacy source archived intact");
            AccountVaultService.ClearCache();
            Gl13Check(vault.Load(username).Count == 1 && Directory.GetFiles(folder, Path.GetFileName(legacy) + ".migrated-*").Length == 1, "repeat after archive adds nothing");
        });
    }

    private static void TestGl13Users(string parent)
    {
        Gl13Isolated(parent, "users", folder =>
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "users.json");
            var user = new User { Username = "synthetic-user", FullName = "Synthetic Name", Email = "user@example.invalid" };
            string fixture = JsonSerializer.Serialize(new[] { user });
            File.WriteAllText(path, fixture);
            var loaded = new UserService().FindByUsername(user.Username);
            Gl13Check(loaded != null && loaded.Id == user.Id && loaded.FullName == user.FullName && loaded.Email == user.Email, "plaintext user fields preserved");
            string protectedData = File.ReadAllText(path);
            Gl13Check(protectedData.StartsWith("TQLTK.USERS.v1:", StringComparison.Ordinal) && !protectedData.Contains(user.Username), "users protected");
            byte[] first = File.ReadAllBytes(path);
            byte[] backup = File.ReadAllBytes(path + ".bak");
            Gl13Check(new UserService().FindByUsername(user.Username)?.Id == user.Id, "protected users read again");
            Gl13Check(first.SequenceEqual(File.ReadAllBytes(path)) && backup.SequenceEqual(File.ReadAllBytes(path + ".bak")), "second read does not rewrite files");
            File.WriteAllText(path, "{broken synthetic json");
            Gl13Check(new UserService().FindByUsername(user.Username)?.Id == user.Id, "valid backup recovers corrupt primary");
            Gl13Check(File.ReadAllText(path).StartsWith("TQLTK.USERS.v1:", StringComparison.Ordinal), "recovered primary protected");
        });
        Gl13Isolated(parent, "locked-users", folder =>
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "users.json");
            byte[] fixture = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { new User { Username = "synthetic-locked" } }));
            File.WriteAllBytes(path, fixture);
            bool rejected = false;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { _ = new UserService(); }
                catch (IOException) { rejected = true; }
                catch (UnauthorizedAccessException) { rejected = true; }
            }
            Gl13Check(rejected && File.ReadAllBytes(path).SequenceEqual(fixture), "locked users file retained after failure");
            Gl13Check(new UserService().FindByUsername("synthetic-locked") != null, "retry after lock release");
        });
    }
}
