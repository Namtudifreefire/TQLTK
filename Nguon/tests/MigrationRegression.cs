using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

internal static partial class Program
{
    private static int RunGl13(string reportPath, bool crossOnly = false)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        string root = Path.Combine(Path.GetTempPath(), "TQLTK-SecurityRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", root);
        var cases = new List<XElement>();
        int failures = 0;
        void Case(string name, Action action)
        {
            if (crossOnly && !name.StartsWith("twofactor-cross-process-", StringComparison.Ordinal)) return;
            var item = new XElement("testcase", new XAttribute("classname", "TQLTK.Windows.GL13"), new XAttribute("name", name));
            try { Gl13RejectReparsePoints(root); action(); Gl13RejectReparsePoints(root); Console.WriteLine("PASS " + name); }
            catch (Exception ex)
            {
                failures++;
                item.Add(new XElement("failure", new XAttribute("type", ex.GetType().Name), "Synthetic migration test failed."));
                string detail = ex is InvalidDataException or UnauthorizedAccessException ? ex.Message : "non-sensitive exception detail withheld";
                Console.Error.WriteLine("FAIL " + name + ": " + ex.GetType().Name + " (0x" + ex.HResult.ToString("X8") + ") — " + detail);
            }
            cases.Add(item);
        }
        try
        {
            Gl13Check(string.Equals(ApplicationDataPath.Root, root, StringComparison.OrdinalIgnoreCase), "test isolation");
            Case("vault-migration-idempotency", () => TestGl13Vault(root));
            Case("users-protection-recovery-io", () => TestGl13Users(root));
            Case("vault-collision-preserves-source", () => TestGl13Collision(root));
            Case("users-corrupt-no-valid-backup", () => TestGl13CorruptUsers(root));
            Case("legacy-personal-files-migration", () => TestGl13PersonalFiles(root));
            Case("legacy-twofactor-migration", () => TestGl13TwoFactor(root));
            Case("twofactor-root-guard", () => TestGl13TwoFactorRootGuard(root));
            Case("twofactor-corrupt-and-locked-source", () => TestGl13TwoFactorFailures(root));
            Case("twofactor-collision", () => TestGl13TwoFactorCollision(root));
            Case("twofactor-concurrent-migration", () => TestGl13TwoFactorConcurrent(root));
            Case("twofactor-locked-destination", () => TestGl13TwoFactorLockedDestination(root));
            Case("migration-parent-creation-failure", () => TestGl13ParentCreationFailure(root));
            Case("migration-locked-source-archive-retry", () => TestGl13LockedArchiveRetry(root));
            Case("migration-child-interruption-retry", () => TestGl13ChildInterruption(root));
            Case("migration-child-exit-and-timeout", () => TestGl13ChildFailure(root));
            Case("twofactor-cross-process-concurrent", () => TestGl13CrossProcess(root, 0));
            Case("twofactor-cross-process-ordered", () => TestGl13CrossProcess(root, 1));
            Case("twofactor-cross-process-failure-retry", () => TestGl13CrossProcess(root, 2));
            Case("twofactor-cross-process-migration-lock-contention", () => TestGl13MigrationLockContention(root, terminateHolder: false));
            Case("twofactor-cross-process-migration-lock-abandonment", () => TestGl13MigrationLockContention(root, terminateHolder: true));
            if (crossOnly && cases.Count != 5) throw new InvalidDataException("Expected five cross-process cases.");
            string fullReport = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullReport)!);
            new XDocument(new XElement("testsuite", new XAttribute("name", "TQLTK.Windows.GL13"), new XAttribute("tests", cases.Count), new XAttribute("failures", failures), new XAttribute("skipped", 0), cases)).Save(fullReport);
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", null);
            AccountVaultService.ClearCache();
            Gl13RejectReparsePoints(root);
            Directory.Delete(root, recursive: true);
        }
    }

    private static int RunGl13Child(string mode)
    {
        try
        {
            string root = ApplicationDataPath.Root;
            string? expected = Environment.GetEnvironmentVariable("TQLTK_GL13_CHILD_ROOT");
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(expected) ||
                !string.Equals(root, expected, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(root).StartsWith("TQLTK-SecurityRegression-", StringComparison.Ordinal) ||
                !File.Exists(Path.Combine(root, "child-fixture", "fixture.marker"))) return 80;
            Gl13RejectReparsePoints(root);
            string dir = Path.Combine(root, "child-fixture");
            if (mode == "abnormal") return 47;
            if (mode == "migration-lock-holder")
            {
                using var acquired = Gl13OpenChildEvent("lock-acquired");
                using var release = Gl13OpenChildEvent("lock-release");
                var user = new UserService().FindByUsername("p2race");
                if (user == null) return 86;
                using var migrationLock = UserStorageIdentity.AcquireMigrationLock(user.Id);
                acquired.Set();
                return release.WaitOne(TimeSpan.FromSeconds(20)) ? 0 : 87;
            }
            if (mode == "migration-lock-waiter")
            {
                using var attempted = Gl13OpenChildEvent("waiter-attempted");
                using var completed = Gl13OpenChildEvent("waiter-completed");
                attempted.Set();
                bool enabled = TwoFactorService.IsEnabled("p2race");
                bool usable = TwoFactorService.HasUsableConfiguration("p2race");
                if (!enabled || !usable) return 88;
                completed.Set();
                return 0;
            }
            if (mode == "race-a" || mode == "race-b")
            {
                string legacy = UserStorageIdentity.GetLegacyPath(Path.Combine(root, "PersonalInfoManager"), "p2race", LegacyUserStorageKind.TwoFactor);
                if (!File.Exists(legacy)) return 83;
                string suffix = mode == "race-a" ? "a" : "b";
                File.WriteAllText(Path.Combine(dir, "ready-" + suffix), "ready");
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (!File.Exists(Path.Combine(dir, "go-" + suffix)))
                {
                    if (DateTime.UtcNow >= deadline) return 84;
                    System.Threading.Thread.Sleep(25);
                }
                return TwoFactorService.IsEnabled("p2race") && TwoFactorService.HasUsableConfiguration("p2race") ? 0 : 85;
            }
            if (mode == "pause")
            {
                File.WriteAllText(Path.Combine(dir, "checkpoint"), "ready");
                System.Threading.Thread.Sleep(30000);
                return 48;
            }
            if (mode != "move") return 81;
            UserStorageIdentity.MoveLegacyFile(Path.Combine(dir, "source"), Path.Combine(dir, "destination"), Guid.Empty);
            return 0;
        }
        catch { return 82; }
    }

    private static EventWaitHandle Gl13OpenChildEvent(string suffix)
    {
        string? prefix = Environment.GetEnvironmentVariable("TQLTK_GL13_SYNC_PREFIX");
        if (string.IsNullOrWhiteSpace(prefix)) throw new InvalidOperationException("Missing GL13 child synchronization prefix.");
        return EventWaitHandle.OpenExisting(prefix + "." + suffix);
    }

    private static void TestGl13CrossProcess(string parent, int scenario)
    {
        string root = Path.Combine(parent, "TQLTK-SecurityRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", root);
            var users = new UserService();
            Gl13Check(users.CreateInitialAdmin("p2race", "Synthetic-GL13-password-2026!", "Synthetic", "p2@example.invalid", "").ok, "create race user");
            var user = users.FindByUsername("p2race")!;
            string folder = Path.Combine(root, "PersonalInfoManager");
            string legacy = UserStorageIdentity.GetLegacyPath(folder, user.Username, LegacyUserStorageKind.TwoFactor);
            string target = Path.Combine(folder, "2fa", UserStorageIdentity.GetDirectoryName(user.Id) + ".2fa");
            string dir = Path.Combine(root, "child-fixture");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            File.WriteAllText(Path.Combine(dir, "fixture.marker"), "synthetic");
            byte[] json = System.Text.Encoding.UTF8.GetBytes("{\"Secret\":\"JBSWY3DPEHPK3PXP\",\"RecoveryHashes\":[]}");
            byte[] data = System.Security.Cryptography.ProtectedData.Protect(json, System.Text.Encoding.UTF8.GetBytes("TQLTK.2FA.v1"), System.Security.Cryptography.DataProtectionScope.CurrentUser);
            File.WriteAllBytes(legacy, data);
            using var first = Gl13StartChild(root, "race-a");
            System.Diagnostics.Process? second = null;
            try
            {
                second = Gl13StartChild(root, "race-b");
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while ((!File.Exists(Path.Combine(dir, "ready-a")) || !File.Exists(Path.Combine(dir, "ready-b"))) &&
                    !first.HasExited && !second.HasExited && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(25);
                Gl13Check(File.Exists(Path.Combine(dir, "ready-a")) && File.Exists(Path.Combine(dir, "ready-b")) && !first.HasExited && !second.HasExited, "both children observed legacy data");
                Gl13Check(File.Exists(legacy) && !File.Exists(target), "source still present at barrier");
                if (scenario == 2)
                {
                    first.Kill();
                    Gl13Check(first.WaitForExit(5000) && first.ExitCode != 0, "first child interrupted");
                }
                else if (scenario == 1)
                {
                    File.WriteAllText(Path.Combine(dir, "go-a"), "go");
                    Gl13Check(Gl13WaitChild(first, 15000) == 0, "first migration completed");
                    Gl13Check(!File.Exists(legacy) && File.ReadAllBytes(target).SequenceEqual(data), "first destination retained");
                }
                else File.WriteAllText(Path.Combine(dir, "go-a"), "go");
                File.WriteAllText(Path.Combine(dir, "go-b"), "go");
                if (scenario == 0) Gl13Check(Gl13WaitChild(first, 15000) == 0, "first child completed");
                Gl13Check(Gl13WaitChild(second, 15000) == 0, "second child completed");
            }
            finally
            {
                foreach (var child in new[] { first, second }.Where(child => child != null))
                    if (!child!.HasExited) { child.Kill(); child.WaitForExit(); }
                second?.Dispose();
            }
            Gl13Check(!File.Exists(legacy) && File.ReadAllBytes(target).SequenceEqual(data), "final bytes preserved");
            Gl13Check(Directory.GetFiles(Path.GetDirectoryName(legacy)!, Path.GetFileName(legacy) + ".migrated-*").Length == 0, "no duplicate archive");
            Gl13Check(TwoFactorService.IsEnabled(user.Username) && TwoFactorService.HasUsableConfiguration(user.Username), "retry remains usable");
        }
        finally { Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", previous); }
    }

    private static void TestGl13MigrationLockContention(string parent, bool terminateHolder)
    {
        string root = Path.Combine(parent, "TQLTK-SecurityRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT");
        string eventPrefix = @"Local\TQLTK.GL13." + Guid.NewGuid().ToString("N");
        using var acquired = new EventWaitHandle(false, EventResetMode.ManualReset, eventPrefix + ".lock-acquired");
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, eventPrefix + ".lock-release");
        using var attempted = new EventWaitHandle(false, EventResetMode.ManualReset, eventPrefix + ".waiter-attempted");
        using var completed = new EventWaitHandle(false, EventResetMode.ManualReset, eventPrefix + ".waiter-completed");
        System.Diagnostics.Process? holder = null;
        System.Diagnostics.Process? waiter = null;
        try
        {
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", root);
            var users = new UserService();
            Gl13Check(users.CreateInitialAdmin("p2race", "Synthetic-GL13-password-2026!", "Synthetic", "p2@example.invalid", "").ok, "create contention user");
            var user = users.FindByUsername("p2race") ?? throw new InvalidDataException("Contention fixture user was not created.");
            string folder = Path.Combine(root, "PersonalInfoManager");
            string legacy = UserStorageIdentity.GetLegacyPath(folder, user.Username, LegacyUserStorageKind.TwoFactor);
            string target = Path.Combine(folder, "2fa", UserStorageIdentity.GetDirectoryName(user.Id) + ".2fa");
            string operationLock = Path.Combine(folder, "2fa", UserStorageIdentity.GetDirectoryName(user.Id) + ".2fa.lock");
            string fixture = Path.Combine(root, "child-fixture");
            Directory.CreateDirectory(fixture);
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            File.WriteAllText(Path.Combine(fixture, "fixture.marker"), "synthetic");
            byte[] json = System.Text.Encoding.UTF8.GetBytes("{\"Secret\":\"JBSWY3DPEHPK3PXP\",\"RecoveryHashes\":[]}");
            byte[] data = System.Security.Cryptography.ProtectedData.Protect(json, System.Text.Encoding.UTF8.GetBytes("TQLTK.2FA.v1"), System.Security.Cryptography.DataProtectionScope.CurrentUser);
            File.WriteAllBytes(legacy, data);

            holder = Gl13StartChild(root, "migration-lock-holder", eventPrefix);
            Gl13Check(acquired.WaitOne(TimeSpan.FromSeconds(10)) && !holder.HasExited, "holder acquired the migration mutex");
            waiter = Gl13StartChild(root, "migration-lock-waiter", eventPrefix);
            Gl13Check(attempted.WaitOne(TimeSpan.FromSeconds(10)) && !waiter.HasExited, "waiter requested migration while mutex was held");
            Gl13Check(Gl13WaitForExclusiveOperationLock(operationLock, 5000), "waiter acquired the 2FA operation lock before waiting for migration mutex");
            Gl13Check(!completed.WaitOne(TimeSpan.FromMilliseconds(300)) && File.Exists(legacy) && !File.Exists(target), "waiter remained outside migration until lock release");

            if (terminateHolder)
            {
                holder.Kill(entireProcessTree: true);
                Gl13Check(holder.WaitForExit(5000) && holder.ExitCode != 0, "holder terminated while owning migration mutex");
            }
            else
            {
                release.Set();
                Gl13Check(Gl13WaitChild(holder, 10000) == 0, "holder released migration mutex");
            }

            Gl13Check(completed.WaitOne(TimeSpan.FromSeconds(15)), "waiter continued after mutex was released or abandoned");
            Gl13Check(Gl13WaitChild(waiter, 5000) == 0, "waiter completed migration");
            Gl13Check(!File.Exists(legacy) && File.ReadAllBytes(target).SequenceEqual(data), "migration preserved destination bytes");
            Gl13Check(Directory.GetFiles(Path.GetDirectoryName(legacy)!, Path.GetFileName(legacy) + ".migrated-*").Length == 0, "migration created no unexpected archive");
            Gl13Check(TwoFactorService.IsEnabled(user.Username) && TwoFactorService.HasUsableConfiguration(user.Username), "post-contention migration is idempotent and usable");
        }
        finally
        {
            foreach (var child in new[] { holder, waiter }.Where(child => child != null))
                if (!child!.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
            waiter?.Dispose();
            holder?.Dispose();
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", previous);
        }
    }

    private static System.Diagnostics.Process Gl13StartChild(string root, string mode, string? eventPrefix = null)
    {
        Gl13Check(string.Equals(ApplicationDataPath.Root, root, StringComparison.OrdinalIgnoreCase), "child root isolation");
        Gl13RejectReparsePoints(root);
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Missing test executable.");
        var info = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(Program).Assembly.Location);
        info.ArgumentList.Add("--gl13-child");
        info.ArgumentList.Add(mode);
        info.Environment["TQLTK_SECURITY_TEST_ROOT"] = root;
        info.Environment["TQLTK_GL13_CHILD_ROOT"] = root;
        if (!string.IsNullOrWhiteSpace(eventPrefix)) info.Environment["TQLTK_GL13_SYNC_PREFIX"] = eventPrefix;
        return System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Could not start child.");
    }

    private static int Gl13WaitChild(System.Diagnostics.Process child, int timeoutMs)
    {
        if (!child.WaitForExit(timeoutMs))
        {
            child.Kill();
            child.WaitForExit();
            return -1;
        }
        return child.ExitCode;
    }

    private static bool Gl13WaitForExclusiveOperationLock(string lockPath, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (FileNotFoundException)
            {
                // The waiter has not created the service lock file yet.
            }
            catch (IOException) when (File.Exists(lockPath))
            {
                // The waiter owns TwoFactorService's exclusive operation lock.
                return true;
            }
            Thread.Sleep(15);
        }
        return false;
    }

    private static void TestGl13ChildInterruption(string root)
    {
        string dir = Path.Combine(root, "child-fixture");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "fixture.marker"), "synthetic");
        string source = Path.Combine(dir, "source"), destination = Path.Combine(dir, "destination");
        byte[] data = { 4, 2, 7 };
        File.WriteAllBytes(source, data);
        using (var child = Gl13StartChild(root, "pause"))
        {
            try
            {
                var until = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(Path.Combine(dir, "checkpoint")) && !child.HasExited && DateTime.UtcNow < until)
                    System.Threading.Thread.Sleep(25);
                Gl13Check(File.Exists(Path.Combine(dir, "checkpoint")) && !child.HasExited, "child reached checkpoint");
                child.Kill();
                Gl13Check(child.WaitForExit(5000) && child.ExitCode != 0, "interrupted child failed");
            }
            finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } }
        }
        Gl13Check(File.ReadAllBytes(source).SequenceEqual(data) && !File.Exists(destination), "source preserved after interruption");
        using (var retry = Gl13StartChild(root, "move")) Gl13Check(Gl13WaitChild(retry, 10000) == 0, "retry succeeded");
        Gl13Check(!File.Exists(source) && File.ReadAllBytes(destination).SequenceEqual(data), "retry preserved bytes");
        using (var again = Gl13StartChild(root, "move")) Gl13Check(Gl13WaitChild(again, 10000) == 0, "repeat succeeded");
        Gl13Check(File.ReadAllBytes(destination).SequenceEqual(data), "repeat retained destination");
        byte[] laterSource = { 8, 1 };
        File.WriteAllBytes(source, laterSource);
        using (var conflict = Gl13StartChild(root, "move")) Gl13Check(Gl13WaitChild(conflict, 10000) == 0, "existing destination handled");
        string[] archives = Directory.GetFiles(dir, "source.migrated-*");
        Gl13Check(archives.Length == 1 && File.ReadAllBytes(archives[0]).SequenceEqual(laterSource), "conflicting source archived");
        Gl13Check(File.ReadAllBytes(destination).SequenceEqual(data), "existing destination not overwritten");
    }

    private static void TestGl13ChildFailure(string root)
    {
        string dir = Path.Combine(root, "child-fixture");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "fixture.marker"), "synthetic");
        using (var abnormal = Gl13StartChild(root, "abnormal")) Gl13Check(Gl13WaitChild(abnormal, 10000) == 47, "abnormal exit propagated");
        using (var slow = Gl13StartChild(root, "pause")) Gl13Check(Gl13WaitChild(slow, 1000) == -1, "timeout terminated child");
    }

    private static void Gl13RejectReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("GL13 test directory contains a reparse point.");
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("GL13 test directory contains a reparse point.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    private static void TestGl13ParentCreationFailure(string parent)
    {
        Gl13Isolated(parent, "p2-parent-io", folder =>
        {
            Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "synthetic-source");
            string blocker = Path.Combine(folder, "synthetic-blocker");
            byte[] sourceBytes = { 42, 17, 8 };
            byte[] blockerBytes = { 9, 4 };
            File.WriteAllBytes(source, sourceBytes);
            File.WriteAllBytes(blocker, blockerBytes);
            bool rejected = false;
            try { UserStorageIdentity.MoveLegacyFile(source, Path.Combine(blocker, "target"), Guid.NewGuid()); }
            catch (IOException) { rejected = true; }
            catch (UnauthorizedAccessException) { rejected = true; }
            Gl13Check(rejected, "invalid destination parent rejected");
            Gl13Check(File.ReadAllBytes(source).SequenceEqual(sourceBytes), "source preserved");
            Gl13Check(File.ReadAllBytes(blocker).SequenceEqual(blockerBytes), "blocking file preserved");
            string target = Path.Combine(folder, "valid", "target");
            UserStorageIdentity.MoveLegacyFile(source, target, Guid.NewGuid());
            Gl13Check(!File.Exists(source) && File.ReadAllBytes(target).SequenceEqual(sourceBytes), "retry moves intact source");
        });
    }

    private static void TestGl13LockedArchiveRetry(string parent)
    {
        Gl13Isolated(parent, "locked-archive-retry", folder =>
        {
            Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "legacy-fixture");
            string destination = Path.Combine(folder, "destination-fixture");
            byte[] original = { 19, 27, 43 };
            byte[] existing = { 61, 73, 89 };
            File.WriteAllBytes(source, original);
            File.WriteAllBytes(destination, existing);
            Guid id = Guid.NewGuid();
            bool failed = false;
            using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { UserStorageIdentity.MoveLegacyFile(source, destination, id); }
                catch (IOException) { failed = true; }
            }
            Gl13Check(failed, "locked source must reject archive move");
            Gl13Check(File.ReadAllBytes(source).SequenceEqual(original), "failed move preserves source");
            Gl13Check(File.ReadAllBytes(destination).SequenceEqual(existing), "failed move preserves destination");
            Gl13Check(Directory.GetFiles(folder, "legacy-fixture.migrated-*").Length == 0, "failed move creates no archive");
            UserStorageIdentity.MoveLegacyFile(source, destination, id);
            string[] archives = Directory.GetFiles(folder, "legacy-fixture.migrated-*");
            Gl13Check(!File.Exists(source) && archives.Length == 1 && File.ReadAllBytes(archives[0]).SequenceEqual(original), "retry archives source intact");
            Gl13Check(File.ReadAllBytes(destination).SequenceEqual(existing), "retry preserves existing destination");
            UserStorageIdentity.MoveLegacyFile(source, destination, id);
            Gl13Check(Directory.GetFiles(folder, "legacy-fixture.migrated-*").Length == 1, "repeat creates no extra archive");
        });
    }

    private static void TestGl13TwoFactorConcurrent(string parent)
    {
        Gl13Isolated(parent, "p2-concurrent", folder =>
        {
            var users = new UserService();
            Gl13Check(users.CreateInitialAdmin("p2parallel", "Synthetic-GL13-password-2026!", "Synthetic", "p2@example.invalid", "").ok, "create user");
            var user = users.FindByUsername("p2parallel")!;
            string source = UserStorageIdentity.GetLegacyPath(folder, user.Username, LegacyUserStorageKind.TwoFactor);
            string target = Path.Combine(folder, "2fa", UserStorageIdentity.GetDirectoryName(user.Id) + ".2fa");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            byte[] fixture = { 7, 3, 1 };
            File.WriteAllBytes(source, fixture);
            var first = Task.Run(() => TwoFactorService.IsEnabled(user.Username));
            var second = Task.Run(() => TwoFactorService.IsEnabled(user.Username));
            Task.WaitAll(first, second);
            Gl13Check(first.Result && second.Result, "both operations report enabled");
            Gl13Check(!File.Exists(source) && File.ReadAllBytes(target).SequenceEqual(fixture), "one destination contains original bytes");
            Gl13Check(Directory.GetFiles(Path.GetDirectoryName(source)!, Path.GetFileName(source) + ".migrated-*").Length == 0, "no duplicate archive");
        });
    }

    private static void TestGl13TwoFactorLockedDestination(string parent)
    {
        Gl13Isolated(parent, "p2-locked-target", folder =>
        {
            var users = new UserService();
            Gl13Check(users.CreateInitialAdmin("p2target", "Synthetic-GL13-password-2026!", "Synthetic", "p2@example.invalid", "").ok, "create user");
            var user = users.FindByUsername("p2target")!;
            string source = UserStorageIdentity.GetLegacyPath(folder, user.Username, LegacyUserStorageKind.TwoFactor);
            string target = Path.Combine(folder, "2fa", UserStorageIdentity.GetDirectoryName(user.Id) + ".2fa");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            byte[] oldData = { 1, 3, 5 }, currentData = { 2, 4, 6 };
            File.WriteAllBytes(source, oldData);
            File.WriteAllBytes(target, currentData);
            using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
                Gl13Check(TwoFactorService.IsEnabled(user.Username), "existing locked destination remains enabled");
            string[] archives = Directory.GetFiles(Path.GetDirectoryName(source)!, Path.GetFileName(source) + ".migrated-*");
            Gl13Check(archives.Length == 1 && File.ReadAllBytes(archives[0]).SequenceEqual(oldData), "source archived intact");
            Gl13Check(File.ReadAllBytes(target).SequenceEqual(currentData), "locked destination unchanged");
        });
    }

    private static void TestGl13TwoFactorRootGuard(string parent)
    {
        Gl13Isolated(parent, "root-guard", folder =>
        {
            var property = typeof(TwoFactorService).GetProperty("Folder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            foreach (string? invalid in new string?[] { null, Path.GetPathRoot(Path.GetTempPath()) })
            {
                Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", invalid);
                bool rejected = false;
                try { property.GetValue(null); }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
                Gl13Check(rejected, "invalid test root rejected");
            }
        });
    }

    private static void TestGl13TwoFactorFailures(string parent)
    {
        Gl13Isolated(parent, "p2-locked", folder =>
        {
            var users = new UserService();
            Gl13Check(users.CreateInitialAdmin("p2locked", "Synthetic-GL13-password-2026!", "Synthetic", "p2@example.invalid", "").ok, "create user");
            var user = users.FindByUsername("p2locked")!;
            string source = UserStorageIdentity.GetLegacyPath(folder, user.Username, LegacyUserStorageKind.TwoFactor);
            string target = Path.Combine(folder, "2fa", UserStorageIdentity.GetDirectoryName(user.Id) + ".2fa");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            byte[] corrupt = { 1, 2, 3 };
            File.WriteAllBytes(source, corrupt);
            bool rejected = false;
            using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { TwoFactorService.IsEnabled(user.Username); }
                catch (IOException) { rejected = true; }
                catch (UnauthorizedAccessException) { rejected = true; }
            }
            Gl13Check(rejected && File.ReadAllBytes(source).SequenceEqual(corrupt) && !File.Exists(target), "locked source preserved");
            Gl13Check(TwoFactorService.IsEnabled(user.Username), "retry retains enabled state");
            Gl13Check(File.ReadAllBytes(target).SequenceEqual(corrupt) && !TwoFactorService.HasUsableConfiguration(user.Username), "corrupt file retained and unusable");
        });
    }

    private static void TestGl13TwoFactorCollision(string parent)
    {
        Gl13Isolated(parent, "p2-collision", folder =>
        {
            Directory.CreateDirectory(folder);
            var first = new User { Username = "synthetic/a" };
            var second = new User { Username = "synthetic?a" };
            File.WriteAllText(Path.Combine(folder, "users.json"), JsonSerializer.Serialize(new[] { first, second }));
            string source = UserStorageIdentity.GetLegacyPath(folder, first.Username, LegacyUserStorageKind.TwoFactor);
            Gl13Check(source.Equals(UserStorageIdentity.GetLegacyPath(folder, second.Username, LegacyUserStorageKind.TwoFactor), StringComparison.OrdinalIgnoreCase), "collision fixture");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            byte[] data = { 7, 8, 9 };
            File.WriteAllBytes(source, data);
            bool rejected = false;
            try { TwoFactorService.IsEnabled(first.Username); }
            catch (AmbiguousLegacyUserStorageException) { rejected = true; }
            Gl13Check(rejected && File.ReadAllBytes(source).SequenceEqual(data), "collision source retained");
            Gl13Check(!File.Exists(Path.Combine(folder, "2fa", UserStorageIdentity.GetDirectoryName(first.Id) + ".2fa")), "no destination");
        });
    }

    private static void TestGl13TwoFactor(string parent)
    {
        Gl13Isolated(parent, "p2-twofactor", folder =>
        {
            var users = new UserService();
            Gl13Check(users.CreateInitialAdmin("p2totp", "Synthetic-GL13-password-2026!", "Synthetic", "p2@example.invalid", "").ok, "create user");
            var user = users.FindByUsername("p2totp")!;
            string legacy = UserStorageIdentity.GetLegacyPath(folder, user.Username, LegacyUserStorageKind.TwoFactor);
            string target = Path.Combine(folder, "2fa", UserStorageIdentity.GetDirectoryName(user.Id) + ".2fa");
            string allowed = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Gl13Check(Path.GetFullPath(legacy).StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetFullPath(target).StartsWith(allowed, StringComparison.OrdinalIgnoreCase), "paths confined");
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            byte[] json = System.Text.Encoding.UTF8.GetBytes("{\"Secret\":\"JBSWY3DPEHPK3PXP\",\"RecoveryHashes\":[]}");
            byte[] data = System.Security.Cryptography.ProtectedData.Protect(json, System.Text.Encoding.UTF8.GetBytes("TQLTK.2FA.v1"), System.Security.Cryptography.DataProtectionScope.CurrentUser);
            File.WriteAllBytes(legacy, data);
            Gl13Check(TwoFactorService.IsEnabled(user.Username), "enabled after migration");
            Gl13Check(!File.Exists(legacy) && File.ReadAllBytes(target).SequenceEqual(data), "bytes preserved");
            Gl13Check(TwoFactorService.HasUsableConfiguration(user.Username), "configuration readable");
            Gl13Check(TwoFactorService.IsEnabled(user.Username) && File.ReadAllBytes(target).SequenceEqual(data), "idempotency");
            File.WriteAllBytes(legacy, new byte[] { 1, 2, 3 });
            Gl13Check(TwoFactorService.IsEnabled(user.Username), "existing destination");
            string[] archives = Directory.GetFiles(Path.GetDirectoryName(legacy)!, Path.GetFileName(legacy) + ".migrated-*");
            Gl13Check(archives.Length == 1 && File.ReadAllBytes(archives[0]).SequenceEqual(new byte[] { 1, 2, 3 }), "legacy retained in archive");
            Gl13Check(File.ReadAllBytes(target).SequenceEqual(data), "destination retained");
        });
    }

    private static void TestGl13PersonalFiles(string parent)
    {
        Gl13Isolated(parent, "p2-files", folder =>
        {
            var users = new UserService();
            Gl13Check(users.CreateInitialAdmin("p2files", "Synthetic-GL13-password-2026!", "Synthetic", "p2@example.invalid", "").ok, "create user");
            var user = users.FindByUsername("p2files")!;
            string legacy = UserStorageIdentity.GetLegacyPath(folder, user.Username, LegacyUserStorageKind.LocalFiles);
            Directory.CreateDirectory(Path.Combine(legacy, "con"));
            File.WriteAllBytes(Path.Combine(legacy, "tệp.txt"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(legacy, "con", "empty"), Array.Empty<byte>());
            var service = new LocalFileService();
            string target = service.GetUserFolder(user.Username);
            Gl13Check(!Directory.Exists(legacy) && File.ReadAllBytes(Path.Combine(target, "tệp.txt")).SequenceEqual(new byte[] { 1, 2, 3 }), "content moved");
            Gl13Check(File.Exists(Path.Combine(target, "con", "empty")) && new FileInfo(Path.Combine(target, "con", "empty")).Length == 0, "nested empty file retained");
            Gl13Check(service.GetUserFolder(user.Username) == target, "repeat migration");
            Directory.CreateDirectory(legacy);
            File.WriteAllBytes(Path.Combine(legacy, "tệp.txt"), new byte[] { 4 });
            service.GetUserFolder(user.Username);
            string[] archives = Directory.GetDirectories(Path.GetDirectoryName(legacy)!, Path.GetFileName(legacy) + ".migrated-*");
            Gl13Check(archives.Length == 1 && File.ReadAllBytes(Path.Combine(archives[0], "tệp.txt")).SequenceEqual(new byte[] { 4 }), "legacy archived");
            Gl13Check(File.ReadAllBytes(Path.Combine(target, "tệp.txt")).SequenceEqual(new byte[] { 1, 2, 3 }), "destination preserved");
        });
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
