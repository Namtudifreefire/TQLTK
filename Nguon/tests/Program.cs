using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using PersonalInfoManager;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;
internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static int Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The security regression suite requires Windows DPAPI and WPF.");
            return 2;
        }
        string tempRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "TQLTK-SecurityRegression-" + Guid.NewGuid().ToString("N")));
        string systemTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!tempRoot.StartsWith(systemTemp, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Refusing to use a test directory outside the system temp directory.");
            return 2;
        }
        Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", tempRoot);
        try
        {
            if (!string.Equals(ApplicationDataPath.Root, tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The app assembly is not using the isolated temporary test-data root.");
            Run("Argon2id, FIPS PBKDF2, legacy hash verification", TestPasswordHashes);
            Run("English translations for security features", TestEnglishSecurityTranslations);
            string username = "regressionadmin";
            var createResult = new UserService().CreateInitialAdmin(
                username,
                "Tqltk-regression-password-2026!x",
                "Regression Admin",
                "regression@example.com",
                "");
            Run("Create isolated regression account", () =>
            {
                if (!createResult.ok) throw new InvalidOperationException(createResult.message);
                if (new UserService().FindByUsername(username) is not { IsActive: true })
                    throw new InvalidOperationException("The created account could not be read back.");
            });
            if (!createResult.ok) return Finish();
            SavedAccount account = new()
            {
                ServiceName = "Regression Example",
                Username = "regression@example.com",
                Password = "Synthetic-test-password-2026!",
                WebsiteUrl = "https://example.com/login",
                TotpSecret = "JBSWY3DPEHPK3PXP",
                TotpIssuer = "Regression Test",
                Note = "synthetic note",
                Attachments =
                {
                    new AccountAttachment
                    {
                        FileName = "recovery.txt",
                        ContentType = "text/plain",
                        Content = Encoding.UTF8.GetBytes("synthetic recovery data")
                    }
                },
                CustomFields =
                {
                    new AccountCustomField { Name = "recovery hint", Value = "synthetic value", IsSensitive = true }
                }
            };
            string backupDirectory = Path.Combine(tempRoot, "backup-fixtures");
            Directory.CreateDirectory(backupDirectory);
            string vaultBackupPath = Path.Combine(backupDirectory, "vault-recovery.vault");
            string fileBackupPath = Path.Combine(backupDirectory, "files-recovery.tqf");
            Run("Vault and notes round trip", () =>
            {
                var vault = new AccountVaultService();
                vault.Save(username, new List<SavedAccount> { account });
                vault.Save(username, new List<SavedAccount> { account.Clone() });
                var loaded = vault.Load(username);
                if (loaded.Count != 1 || loaded[0].TotpSecret != account.TotpSecret ||
                    loaded[0].Attachments.Count != 1 || loaded[0].CustomFields.Count != 1)
                    throw new InvalidDataException("Vault round trip lost TOTP, attachment, or custom-field data.");
                var notes = new NoteService();
                notes.Save(new List<Note> { new() { Title = "Regression note", Content = "synthetic note content" } });
                if (notes.Load() is not [{ Title: "Regression note", Content: "synthetic note content" }])
                    throw new InvalidDataException("Encrypted notes did not round trip.");
            });
            string backupPassphrase = "Regression backup passphrase 2026!";
            Run("Vault backup inspect and isolated restore drill", () =>
            {
                BackupCrypto.SaveStoredPassphrase(username, backupPassphrase);
                var export = VaultBackupService.ExportToFile(username, vaultBackupPath);
                if (!export.ok) throw new InvalidDataException(export.message);
                var inspect = VaultBackupService.InspectBackup(username, vaultBackupPath);
                if (!inspect.ok || inspect.accountCount != 1)
                    throw new InvalidDataException(inspect.message);
                var drill = VaultBackupService.DrillRestore(username, vaultBackupPath);
                if (!drill.ok || drill.accountCount != 1)
                    throw new InvalidDataException(drill.message);
            });
            Run("Personal-file backup and isolated restore drill", () =>
            {
                var files = new LocalFileService();
                files.SaveTextFile(username, "regression.txt", "synthetic personal file data");
                var archive = FileBackupService.CreateArchive(username, fileBackupPath, backupPassphrase);
                if (!archive.ok || archive.count != 1) throw new InvalidDataException(archive.message);
                var drill = FileBackupService.DrillRestore(username, fileBackupPath, backupPassphrase);
                if (!drill.ok || drill.count != 1) throw new InvalidDataException(drill.message);
                var invalid = Path.Combine(backupDirectory, "tampered.tqf");
                File.Copy(fileBackupPath, invalid);
                File.AppendAllText(invalid, "tamper");
                if (FileBackupService.DrillRestore(username, invalid, backupPassphrase).ok)
                    throw new InvalidDataException("A tampered file backup unexpectedly passed its drill.");
            });
            Run("Vault-key rotation and interrupted transaction recovery", () =>
                TestVaultKeyRotation(username, tempRoot, backupDirectory, vaultBackupPath, backupPassphrase));
            Run("Security-finding review persistence and version reset", () =>
                TestFindingReview(username, account));
            Run("Backup-health decisions", TestBackupHealth);
            Run("Windows lock and sleep event policies", TestAutoLockPolicies);
            Run("Vault-key rotation requires an active administrator", TestRotationAuthorization);
            Run("Release scripts parse and reject unsigned artifacts", () => TestReleaseScripts(tempRoot));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Regression suite setup failed: " + ex);
            _failed++;
        }
        finally
        {
            VaultKeyRotationService.TestCheckpoint = null;
            VaultKeyRotationService.PreserveJournalAfterTestFailure = false;
            Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", null);
            try
            {
                string full = Path.GetFullPath(tempRoot);
                if (full.StartsWith(systemTemp, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(full).StartsWith("TQLTK-SecurityRegression-", StringComparison.Ordinal))
                    Directory.Delete(full, recursive: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Temporary regression data cleanup failed: " + ex.Message);
                _failed++;
            }
        }
        return Finish();
    }
    private static void TestPasswordHashes()
    {
        const string password = "synthetic password for hash regression";
        string argon = PasswordHasher.HashArgon2idForTesting(password);
        if (!argon.StartsWith("argon2id$v=19$m=19456,t=2,p=1$", StringComparison.Ordinal))
            throw new CryptographicException("Argon2id encoding or parameters are incorrect.");
        if (!PasswordHasher.Verify(password, argon) || PasswordHasher.Verify(password + "x", argon))
            throw new CryptographicException("Argon2id verification accepted incorrect input or rejected the correct password.");
        string pbkdf2 = PasswordHasher.HashPbkdf2Fips(password);
        if (!pbkdf2.StartsWith("pbkdf2-sha256$i=600000$", StringComparison.Ordinal) ||
            !PasswordHasher.Verify(password, pbkdf2) || PasswordHasher.Verify(password + "x", pbkdf2))
            throw new CryptographicException("FIPS-compatible PBKDF2 verification failed.");
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        try
        {
            string legacy = $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
            if (!PasswordHasher.Verify(password, legacy) || !PasswordHasher.NeedsRehash(legacy))
                throw new CryptographicException("Legacy PBKDF2 verification or migration policy failed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(hash);
        }
    }
    private static void TestEnglishSecurityTranslations()
    {
        (string Source, string Expected)[] cases =
        {
            ("Chỉ quản trị viên đang hoạt động mới được phép xoay khóa vault.", "Only an active administrator can rotate the vault key."),
            ("Chọn thời gian tự khóa hợp lệ.", "Select a valid auto-lock duration."),
            ("Đã lưu cài đặt tự khóa.", "Auto-lock settings saved."),
            ("Mặc định dùng Argon2id. Có thể bật PBKDF2-HMAC-SHA256 cho môi trường cần tương thích FIPS; hash cũ được nâng cấp sau lần đăng nhập thành công.",
                "Argon2id is used by default. PBKDF2-HMAC-SHA256 is available for environments that require FIPS-compatible hashing; existing hashes are upgraded after a successful sign-in."),
            ("Trạng thái sao lưu: chưa từng tạo bản sao lưu.", "Backup health: no backup has been created."),
            ("Xoay khóa hoàn tất", "Key rotation complete"),
            ("Diễn tập hoàn tất", "Restore drill complete"),
            ("Trạng thái cảnh báo bảo mật", "Security finding status")
        };
        foreach ((string source, string expected) in cases)
        {
            string actual = LanguageService.Translate(source, "en");
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidDataException($"English translation mismatch for '{source}': '{actual}'.");
        }
    }
    private static void TestVaultKeyRotation(
        string username, string tempRoot, string backupDirectory, string passphraseBackupPath, string backupPassphrase)
    {
        string appData = Path.Combine(tempRoot, "PersonalInfoManager");
        string vaultPath = Directory.GetFiles(appData, "vault_*.dat", SearchOption.TopDirectoryOnly).Single();
        string oldVaultCiphertext = File.ReadAllText(vaultPath);
        string oldVaultPlaintext = AesCrypto.Decrypt(oldVaultCiphertext);
        string passphraseBackupHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(passphraseBackupPath)));
        string machineBackupPath = Path.Combine(backupDirectory, "machine-key-backup.vault");
        File.WriteAllText(machineBackupPath, AesCrypto.Encrypt("synthetic local backup"));
        string oldMachineBackup = File.ReadAllText(machineBackupPath);
        var rotated = VaultKeyRotationService.RotateForTesting(username, new[] { backupDirectory });
        if (rotated.ReencryptedFiles < 3 || rotated.PreservedLegacyBackups < 1)
            throw new CryptographicException("Rotation did not include the vault, notes, and machine-key backup.");
        AccountVaultService.ClearCache();
        if (new AccountVaultService().Load(username).Single().TotpSecret != "JBSWY3DPEHPK3PXP")
            throw new CryptographicException("Vault data did not survive key rotation.");
        if (!new NoteService().Load().Any(note => note.Title == "Regression note"))
            throw new CryptographicException("Notes did not survive key rotation.");
        if (AesCrypto.Decrypt(File.ReadAllText(machineBackupPath)) != "synthetic local backup")
            throw new CryptographicException("Machine-key backup did not survive key rotation.");
        if (BackupCrypto.Decrypt(File.ReadAllText(passphraseBackupPath), backupPassphrase).Length == 0)
            throw new CryptographicException("Passphrase backup did not survive rotation.");
        string afterPassphraseHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(passphraseBackupPath)));
        if (passphraseBackupHash != afterPassphraseHash)
            throw new CryptographicException("Passphrase backup was modified by key rotation.");
        if (AesCrypto.Decrypt(oldVaultCiphertext) != oldVaultPlaintext)
            throw new CryptographicException("Historical key fallback did not return stable plaintext.");
        if (AesCrypto.Decrypt(oldMachineBackup) != "synthetic local backup")
            throw new CryptographicException("Historical key could not decrypt the old local-backup copy.");
        VaultKeyRotationService.PreserveJournalAfterTestFailure = true;
        bool injected = false;
        VaultKeyRotationService.TestCheckpoint = name =>
        {
            if (!injected && name == "file-replaced")
            {
                injected = true;
                throw new IOException("simulated abrupt process termination");
            }
        };
        try
        {
            try
            {
                VaultKeyRotationService.RotateForTesting(username, new[] { backupDirectory });
                throw new InvalidOperationException("The simulated interruption did not occur.");
            }
            catch (IOException) when (injected) { }
        }
        finally
        {
            VaultKeyRotationService.TestCheckpoint = null;
            VaultKeyRotationService.PreserveJournalAfterTestFailure = false;
        }
        if (!File.Exists(Path.Combine(appData, "key-rotation.journal")))
            throw new CryptographicException("Interrupted rotation did not retain its recovery journal.");
        AccountVaultService.ClearCache();
        _ = AesCrypto.Decrypt(File.ReadAllText(vaultPath));
        if (File.Exists(Path.Combine(appData, "key-rotation.journal")))
            throw new CryptographicException("Reading after interruption did not finish journal recovery.");
        if (new AccountVaultService().Load(username).Single().TotpSecret != "JBSWY3DPEHPK3PXP" ||
            new NoteService().Load().Single().Content != "synthetic note content")
            throw new CryptographicException("Interrupted rotation recovery lost vault or note data.");
        if (AesCrypto.Decrypt(oldVaultCiphertext).Length == 0)
            throw new CryptographicException("Historical ciphertext could not be read after recovery.");
    }
    private static void TestFindingReview(string username, SavedAccount original)
    {
        SavedAccount account = original.Clone();
        var accounts = new Dictionary<Guid, SavedAccount> { [account.Id] = account };
        var findings = new[] { new PasswordSecurityFinding(account.Id, PasswordSecurityFindingKind.Weak) };
        var state = SecurityFindingReviewService.SyncFindings(username, findings, accounts);
        string key = SecurityFindingReviewService.Key(account.Id, PasswordSecurityFindingKind.Weak);
        if (state[key].Status != SecurityFindingReviewStatus.Open)
            throw new InvalidDataException("New findings must start open.");
        const string reason = "Reviewed regression finding";
        SecurityFindingReviewService.Set(username, account, PasswordSecurityFindingKind.Weak,
            SecurityFindingReviewStatus.Ignored, null, reason);
        state = SecurityFindingReviewService.SyncFindings(username, findings, accounts);
        if (state[key].Status != SecurityFindingReviewStatus.Ignored || state[key].Reason != reason)
            throw new InvalidDataException("Ignored status and reason did not persist.");
        try
        {
            SecurityFindingReviewService.Set(username, account, PasswordSecurityFindingKind.Weak,
                SecurityFindingReviewStatus.Ignored, null, "  ");
            throw new InvalidDataException("Ignoring a finding without a reason was accepted.");
        }
        catch (ArgumentException) { }
        string reviewFile = Directory.GetFiles(
            Path.Combine(Environment.GetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT")!, "PersonalInfoManager"),
            "security_reviews_*.dat").Single();
        if (Encoding.UTF8.GetString(File.ReadAllBytes(reviewFile)).Contains(reason, StringComparison.Ordinal))
            throw new InvalidDataException("The finding reason was stored without DPAPI protection.");
        account.Password += "changed";
        account.UpdatedAt = DateTime.UtcNow.AddSeconds(1);
        state = SecurityFindingReviewService.SyncFindings(username, findings, accounts);
        if (state[key].Status != SecurityFindingReviewStatus.Open || state[key].Reason.Length != 0)
            throw new InvalidDataException("A changed account did not reopen the finding.");
        SecurityFindingReviewService.Set(username, account, PasswordSecurityFindingKind.Weak,
            SecurityFindingReviewStatus.Snoozed, DateTime.UtcNow.AddMilliseconds(250), null);
        state = SecurityFindingReviewService.SyncFindings(username, findings, accounts);
        if (state[key].Status != SecurityFindingReviewStatus.Snoozed)
            throw new InvalidDataException("Snoozed finding state did not persist.");
        Thread.Sleep(350);
        state = SecurityFindingReviewService.SyncFindings(username, findings, accounts);
        if (state[key].Status != SecurityFindingReviewStatus.Open)
            throw new InvalidDataException("Expired snooze did not reopen the finding.");
        SecurityFindingReviewService.Set(username, account, PasswordSecurityFindingKind.Weak,
            SecurityFindingReviewStatus.Fixed, null, null);
        state = SecurityFindingReviewService.SyncFindings(username, findings, accounts);
        if (state[key].Status != SecurityFindingReviewStatus.Fixed)
            throw new InvalidDataException("Fixed status did not persist.");
    }
    private static void TestBackupHealth()
    {
        DateTime now = DateTime.UtcNow;
        var none = AutoBackupService.EvaluateHealth(null, false, "", false, "", false,
            null, false, "", null, "", now);
        var stale = AutoBackupService.EvaluateHealth(now.AddDays(-31), false, "h", true, "h", true,
            now.AddDays(-1), true, "h", null, "", now);
        var modified = AutoBackupService.EvaluateHealth(now.AddDays(-1), false, "h", false, "h", true,
            now.AddDays(-1), true, "h", null, "", now);
        var noDrill = AutoBackupService.EvaluateHealth(now.AddDays(-1), false, "h", true, "h", true,
            null, false, "", null, "", now);
        var oldDrill = AutoBackupService.EvaluateHealth(now.AddDays(-1), false, "h", true, "h", true,
            now.AddDays(-91), true, "h", null, "", now);
        var healthy = AutoBackupService.EvaluateHealth(now.AddDays(-1), false, "h", true, "h", true,
            now.AddDays(-10), true, "h", null, "", now);
        if (!none.Warn || !stale.Warn || !modified.Warn || !noDrill.Warn || !oldDrill.Warn || healthy.Warn)
            throw new InvalidDataException("One or more backup-health states were misclassified.");
    }
    private static void TestAutoLockPolicies()
    {
        if (!MainWindow.ShouldLockForSessionSwitch(SessionSwitchReason.SessionLock) ||
            !MainWindow.ShouldLockForSessionSwitch(SessionSwitchReason.ConsoleDisconnect) ||
            !MainWindow.ShouldLockForSessionSwitch(SessionSwitchReason.RemoteDisconnect) ||
            MainWindow.ShouldLockForSessionSwitch(SessionSwitchReason.SessionLogon))
            throw new InvalidOperationException("Windows session-switch lock policy is incorrect.");
        if (!MainWindow.ShouldLockForPowerMode(PowerModes.Suspend) ||
            !MainWindow.ShouldLockForPowerMode(PowerModes.Resume) ||
            MainWindow.ShouldLockForPowerMode(PowerModes.StatusChange))
            throw new InvalidOperationException("Suspend/resume lock policy is incorrect.");
    }
    private static void TestRotationAuthorization()
    {
        if (!MainWindow.CanRotateVaultKey(new User { Role = UserRole.Admin, IsActive = true }) ||
            MainWindow.CanRotateVaultKey(new User { Role = UserRole.User, IsActive = true }) ||
            MainWindow.CanRotateVaultKey(new User { Role = UserRole.Admin, IsActive = false }) ||
            MainWindow.CanRotateVaultKey(null))
            throw new UnauthorizedAccessException("Vault-key rotation authorization policy is incorrect.");
    }
    private static void TestReleaseScripts(string tempRoot)
    {
        string projectRoot = FindProjectRoot();
        string releaseScript = Path.Combine(projectRoot, "release-artifacts.ps1");
        string verifyScript = Path.Combine(projectRoot, "verify-release.ps1");
        if (!File.Exists(releaseScript) || !File.Exists(verifyScript))
            throw new FileNotFoundException("Release signing or verification script is missing.");
        string fakePackageDirectory = Path.Combine(tempRoot, "unsigned-package");
        Directory.CreateDirectory(fakePackageDirectory);
        string fakeExe = Path.Combine(fakePackageDirectory, "PersonalInfoManager.exe");
        string license = Path.Combine(fakePackageDirectory, "LICENSE");
        File.WriteAllBytes(fakeExe, new byte[] { 0x4D, 0x5A, 0, 0, 1, 2, 3 });
        File.WriteAllText(license, "Synthetic test license.");
        string zipPath = Path.Combine(tempRoot, "unsigned.zip");
        ZipFile.CreateFromDirectory(fakePackageDirectory, zipPath, CompressionLevel.Optimal, false);
        string thumbprint = new string('A', 40);
        string manifestPath = Path.Combine(tempRoot, "unsigned-manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Product = "TQLTK",
            Version = "3.6.7",
            Runtime = "win-x64",
            Architecture = "x64",
            PackageFileName = Path.GetFileName(zipPath),
            PackageSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zipPath))).ToLowerInvariant(),
            ExecutableFileName = "PersonalInfoManager.exe",
            ExecutableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fakeExe))).ToLowerInvariant(),
            ProductVersion = "3.6.7",
            SignerThumbprint = thumbprint,
            AuthenticodeStatus = "Valid"
        }));
        var verifyOutput = RunPowerShell(
            verifyScript,
            "-PackagePath", zipPath,
            "-ManifestPath", manifestPath,
            "-ExpectedVersion", "3.6.7",
            "-ExpectedPublisherThumbprint", thumbprint);
        if (verifyOutput.ExitCode == 0)
            throw new InvalidDataException("The release verifier accepted an unsigned executable.");
        string version = $"99.{DateTime.UtcNow.DayOfYear}.{DateTime.UtcNow.Ticks % 100000000}";
        string expectedOutput = Path.Combine(projectRoot, "artifacts", "v" + version);
        if (Directory.Exists(expectedOutput))
            throw new IOException("The unique fail-closed test output path already exists.");
        var result = RunPowerShell(releaseScript, "-Version", version);
        if (result.ExitCode == 0 || Directory.Exists(expectedOutput) ||
            !result.Output.Contains("code-signing certificate is required", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Release packaging did not fail closed before creating its output directory.");
    }
    private static (int ExitCode, string Output) RunPowerShell(string script, params string[] args)
    {
        string? executable = Environment.GetEnvironmentVariable("TQLTK_REGRESSION_POWERSHELL");
        string windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (string.IsNullOrWhiteSpace(executable))
            executable = File.Exists(windowsPowerShell)
                ? windowsPowerShell
                : "pwsh.exe";
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.Environment["TQLTK_SIGNING_CERT_THUMBPRINT"] = "";
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("PowerShell regression check timed out.");
        }
        Task.WaitAll(stdout, stderr);
        return (process.ExitCode, stdout.Result + Environment.NewLine + stderr.Result);
    }
    private static string FindProjectRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PersonalInfoManager.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the project root.");
    }
    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            _failed++;
            Console.Error.WriteLine("FAIL " + name + ": " + ex.Message);
        }
    }
    private static int Finish()
    {
        Console.WriteLine($"Security regression result: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }
}
