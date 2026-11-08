using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using PersonalInfoManager;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;
internal static partial class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly string? TestFilter = Environment.GetEnvironmentVariable("TQLTK_REGRESSION_FILTER");
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0) return RunBackupLab(args);
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
        Directory.CreateDirectory(tempRoot);
        Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", tempRoot);
        try
        {
            if (!string.Equals(ApplicationDataPath.Root, tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The app assembly is not using the isolated temporary test-data root.");
            Run("Argon2id, FIPS PBKDF2, legacy hash verification", TestPasswordHashes);
            Run("English translations for security features", TestEnglishSecurityTranslations);
            Run("Localization keys, date and placeholder audit", TestLocalizationAudit);
            Run("Registered translations for XAML and direct UI messages", TestUiTranslationSources);
            Run("Activity-log translations and isolated test logging", () => TestActivityLogTranslations(tempRoot));
            Run("Vault search and combined filters", TestAccountSearch);
            Run("Per-website password rules and generation", TestWebsitePasswordPolicy);
            Run("JSON and KeePass XML import safety", () => TestCredentialImport(tempRoot));
            Run("Configurable keyboard shortcut validation and persistence", TestKeyboardShortcuts);
            Run("Responsive window sizing at 100–200 percent DPI", TestWindowLayoutScaling);
            Run("Calendar backup retention across days, weeks, and years", TestCalendarRetention);
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
            Run("Encrypted edit drafts stay local and round-trip safely", () => TestEditAccountDraft(username));
            Run("Encrypted HIBP audit persistence, staleness, and reminder timing", () => TestPwnedAudit(username, account));
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
                WithBackupProbe(backupDirectory, folder =>
                {
                    string machineBackup = Path.Combine(folder, "machine-version.vault");
                    if (BackupCrypto.HasStoredPassphrase(username)) throw new InvalidOperationException("Machine-key metadata fixture requires no stored passphrase.");
                    var machineExport = VaultBackupService.ExportToFile(username, machineBackup);
                    if (!machineExport.ok) throw new IOException(machineExport.message);
                    AssertVaultExportVersion(machineBackup, null);
                });
                BackupCrypto.SaveStoredPassphrase(username, backupPassphrase);
                var export = VaultBackupService.ExportToFile(username, vaultBackupPath);
                if (!export.ok) throw new InvalidDataException(export.message);
                AssertVaultExportVersion(vaultBackupPath, backupPassphrase);
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
            Run("Full encrypted backup, preview guard, safe restore, and periodic policy", () =>
                TestCompleteBackup(username, backupDirectory, backupPassphrase));
            Run("Selective backup recovery preserves current data", () =>
                TestSelectiveBackupRecovery(username, backupDirectory, backupPassphrase));
            Run("Backup failure matrix preserves original data", () => WithBackupProbe(backupDirectory, folder => TestBackupFailureMatrix(username, folder, backupPassphrase)));
            Run("Backup compatibility fixtures and format rejection", () => WithBackupProbe(backupDirectory, folder => TestBackupCompatibility(username, folder, backupPassphrase)));
            Run("Backup write failures preserve existing backup", () => WithBackupProbe(backupDirectory, folder => TestBackupWriteFailure(username, folder, backupPassphrase)));
            Run("Restore write failure rolls back an already saved vault", () => WithBackupProbe(backupDirectory, folder => TestRestoreWriteFailure(username, folder, backupPassphrase)));
            Run("Verified pinning and backup retention policy", () =>
                TestBackupRetentionPolicy(username, backupDirectory, backupPassphrase));
            Run("Vault-key rotation and interrupted transaction recovery", () =>
                TestVaultKeyRotation(username, tempRoot, backupDirectory, vaultBackupPath, backupPassphrase));
            Run("Security-finding review persistence and version reset", () =>
                TestFindingReview(username, account));
            Run("Backup-health decisions", TestBackupHealth);
            Run("Windows lock and sleep event policies", TestAutoLockPolicies);
            Run("Vault-key rotation requires an active administrator", TestRotationAuthorization);
            Run("Bilingual backup and editor dialogs at 100–200 percent DPI", () => UiLayoutRegression.Run(username));
            Run("Release scripts parse and reject unsigned artifacts", () => TestReleaseScripts(tempRoot));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Regression suite setup failed: " + ex);
            _failed++;
        }
        finally
        {
            KeyboardShortcutService.ResetForTesting();
            WebsitePasswordPolicyService.ResetForTesting();
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
    private static void TestAccountSearch()
    {
        SavedAccount account = new()
        {
            ServiceName = "Example Portal",
            Username = "alice@example.test",
            Password = "Synthetic-Unique-Search-Password-2026!",
            WebsiteUrl = "https://login.example.test/path",
            Folder = "Personal",
            Tags = new List<string> { "Finance", "Important" },
            IsFavorite = true,
            TotpSecret = "JBSWY3DPEHPK3PXP",
            Note = "recovery phrase label",
            CustomFields =
            {
                new AccountCustomField { Name = "Customer ID", Value = "C-100", IsSensitive = false },
                new AccountCustomField { Name = "Recovery secret", Value = "hidden-search-marker", IsSensitive = true }
            }
        };
        SavedAccount missingTotp = new()
        {
            ServiceName = "Example Backup",
            Username = "backup@example.test",
            Password = "Another-Synthetic-Search-Password-2026!",
            WebsiteUrl = "https://example.test/",
            Folder = "Work",
            Tags = new List<string> { "Finance" }
        };
        SavedAccount deleted = new() { ServiceName = "Deleted", IsDeleted = true };
        var all = new[] { account, missingTotp, deleted };
        if (!AccountSearchService.MatchesQuery(account, "recovery phrase") ||
            !AccountSearchService.MatchesQuery(account, "C-100") ||
            !AccountSearchService.MatchesQuery(account, "customer id"))
            throw new InvalidDataException("Search did not cover notes or non-sensitive custom fields.");
        if (AccountSearchService.MatchesQuery(account, "hidden-search-marker"))
            throw new InvalidDataException("Search exposed a sensitive custom-field value.");
        var filtered = AccountSearchService.Filter(all, new AccountSearchFilters(
            Query: "alice",
            Domain: "login.example.test",
            Folder: "Personal",
            Tag: "finance",
            FavoritesOnly: true,
            MissingTotpOnly: false,
            HasSecurityWarningOnly: true), new HashSet<Guid> { account.Id });
        if (filtered.Count != 1 || filtered[0].Id != account.Id)
            throw new InvalidDataException("Combined domain, folder, tag, favorites, and warning filters returned the wrong entry.");
        if (AccountSearchService.Filter(all, new AccountSearchFilters(MissingTotpOnly: true)).Count != 1)
            throw new InvalidDataException("Missing-TOTP filtering failed.");
        if (AccountSearchService.Filter(all, new AccountSearchFilters(Domain: "example.test")).Count != 1 ||
            AccountSearchService.Filter(all, new AccountSearchFilters(Domain: "login.example.test")).Count != 1)
            throw new InvalidDataException("Normalized exact-host filtering failed.");
        SavedAccount undated = new()
        {
            ServiceName = "Undated but strong",
            Password = "SyntheticStrongPassword!9271",
            PasswordChangedAt = null
        };
        var findings = PasswordSecurityAuditService.Analyze(new[] { undated }, new HashSet<Guid>(), false, DateTime.Now);
        if (findings.Any(finding => finding.Kind is PasswordSecurityFindingKind.OlderThanYear or PasswordSecurityFindingKind.UnknownChangeDate))
            throw new InvalidDataException("Password change dates still create fixed-calendar rotation warnings.");
    }
    private static void TestWebsitePasswordPolicy()
    {
        const string websiteUrl = "https://www.example.test/account";
        var policy = new WebsitePasswordPolicy(true, 12, 20, true, false, true, true, "A7!O0");
        WebsitePasswordPolicyService.Save(websiteUrl, policy);
        WebsitePasswordPolicy loaded = WebsitePasswordPolicyService.Get("https://www.example.test/other");
        var credentialBearingUrl = new UriBuilder(Uri.UriSchemeHttp, "example.test")
        {
            UserName = "test-user",
            Password = "test-password"
        }.Uri.AbsoluteUri;
        if (loaded != policy || WebsitePasswordPolicyService.Get(credentialBearingUrl).Enabled)
            throw new InvalidDataException("Per-host rules did not persist or an invalid credential-bearing URL was accepted.");
        var options = new PasswordGeneratorOptions(16, false, false, false, false, true, false, 8, "-");
        string password = PasswordGeneratorService.Generate(options, loaded);
        if (password.Length != 16 || !password.Any(char.IsUpper) || !password.Any(char.IsDigit) ||
            !password.Any(character => "!@#$%^&*_-+=?".Contains(character)) ||
            password.Any(character => loaded.ExcludedCharacters.Contains(character)))
            throw new InvalidDataException("Generated password did not obey the website's required groups and exclusions.");
        if (WebsitePasswordPolicyService.Validate(7, options, loaded) == null)
            throw new InvalidDataException("A password length outside the website policy was accepted.");
        try
        {
            PasswordGeneratorService.Generate(options with { Length = 21 }, loaded);
            throw new InvalidDataException("Generation accepted a length above the website's maximum.");
        }
        catch (InvalidOperationException) { }
    }
    private static void TestCredentialImport(string tempRoot)
    {
        const string json = """
            {"items":[{"name":"Example service","login":{"username":"alice","password":"Synthetic-Import-Password!2026","uris":[{"uri":"https://example.test/login"}],"totp":"JBSWY3DPEHPK3PXP"},"fields":[{"name":"Recovery code","value":"synthetic-secret","type":1},{"name":"Region","value":"north","type":0}]}]}
            """;
        CredentialImportResult jsonResult = CredentialImportService.ParseJson(json);
        if (!jsonResult.Ok || jsonResult.Accounts.Count != 1)
            throw new InvalidDataException(jsonResult.Message);
        SavedAccount imported = jsonResult.Accounts[0];
        if (imported.Username != "alice" || imported.WebsiteUrl != "https://example.test/login" ||
            string.IsNullOrWhiteSpace(imported.TotpSecret) ||
            imported.CustomFields.Single(field => field.Name == "Recovery code") is not { IsSensitive: true, Value: "synthetic-secret" } ||
            imported.CustomFields.Single(field => field.Name == "Region") is not { IsSensitive: false, Value: "north" })
            throw new InvalidDataException("JSON import lost website, TOTP, or custom-field sensitivity metadata.");
        string filePath = Path.Combine(tempRoot, "synthetic-import.json");
        File.WriteAllText(filePath, json, Encoding.UTF8);
        CredentialImportResult fileResult = CredentialImportService.ImportFromFile(filePath);
        if (!fileResult.Ok || fileResult.Accounts.Count != 1)
            throw new InvalidDataException("Bounded JSON file reading failed: " + fileResult.Message);
        const string keepassXml = """
            <KeePassFile><Root><Group><Name>Root</Name><Group><Name>Personal</Name><Entry><String><Key>Title</Key><Value>Email</Value></String><String><Key>UserName</Key><Value>alice</Value></String><String><Key>Password</Key><Value>Synthetic-KeePass-Password!2026</Value></String><String><Key>URL</Key><Value>https://example.test</Value></String><String><Key>Notes</Key><Value>fixture</Value></String></Entry></Group></Group></Root></KeePassFile>
            """;
        CredentialImportResult xmlResult = CredentialImportService.ParseKeePassXml(keepassXml);
        if (!xmlResult.Ok || xmlResult.Accounts.Single() is not { ServiceName: "Email", Username: "alice", Folder: "Personal" })
            throw new InvalidDataException("KeePass XML import did not preserve entry fields or group path.");
        CredentialImportResult dtdResult = CredentialImportService.ParseKeePassXml(
            "<!DOCTYPE KeePassFile [<!ENTITY x SYSTEM 'file:///Windows/win.ini'>]><KeePassFile>&x;</KeePassFile>");
        if (dtdResult.Ok || !dtdResult.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("KeePass XML import did not reject a DTD/external entity.");
        CredentialImportResult protectedResult = CredentialImportService.ParseKeePassXml(
            "<KeePassFile><Root><Group><Name>Root</Name><Entry><String><Key>Password</Key><Value Protected='True'>opaque</Value></String></Entry></Group></Root></KeePassFile>");
        if (protectedResult.Ok || !protectedResult.Message.Contains("Protected", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("KeePass XML importer silently accepted a protected value it cannot decode.");
        string kdbxPath = Path.Combine(tempRoot, "synthetic.kdbx");
        File.WriteAllBytes(kdbxPath, new byte[] { 1, 2, 3 });
        CredentialImportResult kdbxResult = CredentialImportService.ImportFromFile(kdbxPath);
        if (kdbxResult.Ok || !kdbxResult.Message.Contains("XML", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported KDBX input did not explain the safe KeePass XML export path.");
        string vaultPath = Path.Combine(tempRoot, "synthetic.vault");
        File.WriteAllBytes(vaultPath, new byte[] { 1, 2, 3 });
        CredentialImportResult vaultResult = CredentialImportService.ImportFromFile(vaultPath);
        if (vaultResult.Ok || !vaultResult.Message.Contains("Nhập kho mật khẩu", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A TQLTK vault backup did not point to its dedicated preview/import flow.");
        string personalFileBackupPath = Path.Combine(tempRoot, "synthetic.tqf");
        File.WriteAllBytes(personalFileBackupPath, new byte[] { 1, 2, 3 });
        CredentialImportResult personalFileBackupResult = CredentialImportService.ImportFromFile(personalFileBackupPath);
        if (personalFileBackupResult.Ok || !personalFileBackupResult.Message.Contains("không phải tệp tài khoản", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A personal-file backup was misidentified as an account import.");
    }
    private static void TestKeyboardShortcuts()
    {
        IReadOnlyList<KeyboardShortcutBinding> defaults = KeyboardShortcutService.GetBindings();
        if (defaults.Count != 4 || !KeyboardShortcutService.TryMatch(Key.L, ModifierKeys.Control, out KeyboardShortcutAction action) ||
            action != KeyboardShortcutAction.LockVault)
            throw new InvalidDataException("Default keyboard shortcuts were not loaded.");
        var updated = defaults.Select(binding => binding.Action == KeyboardShortcutAction.LockVault
                ? binding with { Gesture = "Ctrl+Shift+L" }
                : binding.Action == KeyboardShortcutAction.CopyUsername
                    ? binding with { Enabled = false }
                    : binding)
            .ToList();
        KeyboardShortcutService.Save(updated);
        if (!KeyboardShortcutService.GetBindings().Single(binding => binding.Action == KeyboardShortcutAction.LockVault).Gesture.Equals("Ctrl+Shift+L", StringComparison.Ordinal) ||
            KeyboardShortcutService.TryMatch(Key.U, ModifierKeys.Control | ModifierKeys.Shift, out _))
            throw new InvalidDataException("A changed or disabled shortcut did not persist.");
        var conflict = updated.Select(binding => binding.Action == KeyboardShortcutAction.SearchAccounts
            ? binding with { Gesture = "Ctrl+Shift+L" }
            : binding).ToList();
        try
        {
            KeyboardShortcutService.Save(conflict);
            throw new InvalidDataException("Conflicting active shortcuts were accepted.");
        }
        catch (ArgumentException) { }
    }
    private static void TestPwnedAudit(string username, SavedAccount account)
    {
        DateTime checkedAt = DateTime.UtcNow.AddDays(-31);
        PwnedPasswordAuditState state = PwnedPasswordAuditStore.CreateState(
            new[] { account }, new[] { account.Id }, 1, 1, checkedAt);
        PwnedPasswordAuditStore.Save(username, state);
        PwnedPasswordAuditState loaded = PwnedPasswordAuditStore.Load(username)
            ?? throw new InvalidDataException("Encrypted audit state was not saved.");
        if (!PwnedPasswordAuditStore.MatchesCurrentVault(loaded, new[] { account }) ||
            !PwnedPasswordAuditStore.IsReminderDue(loaded, 30, DateTime.UtcNow) ||
            PwnedPasswordAuditStore.IsReminderDue(loaded, 90, DateTime.UtcNow) ||
            loaded.BreachedAccountIds.Single() != account.Id)
            throw new InvalidDataException("HIBP result validity, reminder interval, or breached-account state was incorrect.");
        SavedAccount changed = account.Clone();
        changed.UpdatedAt = account.UpdatedAt.AddMinutes(1);
        if (PwnedPasswordAuditStore.MatchesCurrentVault(loaded, new[] { changed }))
            throw new InvalidDataException("An audit result remained current after the vault entry changed.");
        string auditDirectory = Path.Combine(
            Environment.GetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT")!, "PersonalInfoManager", "PwnedPasswordAudit");
        string auditFile = Directory.GetFiles(auditDirectory, "*.dat").Single();
        string encoded = Encoding.UTF8.GetString(File.ReadAllBytes(auditFile));
        if (encoded.Contains(account.Password, StringComparison.Ordinal) || encoded.Contains(account.TotpSecret, StringComparison.Ordinal))
            throw new InvalidDataException("HIBP audit state stored a password or TOTP secret in plaintext.");
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
            ("Trạng thái cảnh báo bảo mật", "Security finding status"),
            ("Tất cả tên miền", "All domains"),
            ("Tất cả thư mục", "All folders"),
            ("Tất cả nhãn", "All tags"),
            ("Thiếu TOTP", "Missing TOTP"),
            ("Cảnh báo bảo mật", "Security warning"),
            ("Chỉ cảnh báo bảo mật", "Security warnings only"),
            ("Chọn phím cho từng thao tác. Thao tác bị tắt sẽ không phản hồi với phím tắt.",
                "Choose a shortcut for each action. Disabled actions will not respond to keyboard shortcuts."),
            ("Áp dụng quy tắc đã lưu cho website này", "Apply saved rules for this website"),
            ("Hãy chọn độ dài từ {0} đến {1} ký tự cho website này.", "Choose a password length from {0} to {1} for this website."),
            ("Đây là bản sao lưu kho mật khẩu TQLTK. Hãy dùng Tiện ích → Nhập kho mật khẩu để xem trước và nhập dữ liệu.",
                "This is a TQLTK vault backup. Use Utilities → Import vault to preview and import its data."),
            ("Đây là bản sao lưu File cá nhân TQLTK, không phải tệp tài khoản. Hãy dùng Tiện ích → Kiểm tra bản sao lưu hoặc Diễn tập khôi phục.",
                "This is a TQLTK personal-file backup, not an account export. Use Utilities → Verify backup or Restore drill."),
            ("Vault đã thay đổi sau lần kiểm tra này; kết quả đã cũ.", "The vault changed after this check; results are outdated."),
            ("Đã đến hạn nhắc kiểm tra lại theo lựa chọn của bạn.", "Your chosen recheck reminder is due."),
            ("Kết quả khớp với vault hiện tại.", "Results match the current vault."),
            ("Lần kiểm tra HIBP gần nhất: {0} • đã kiểm tra {1} mật khẩu duy nhất • {2} giá trị bị lộ • {3}",
                "Last HIBP check: {0} • {1} unique passwords checked • {2} breached values • {3}"),
            ("(tìm cả ghi chú và trường tùy chỉnh không nhạy cảm)", "(also searches notes and non-sensitive custom fields)"),
            ("Nhập dữ liệu tài khoản", "Import account data"),
            ("Đã hoàn tác lần nhập.", "The import batch was undone."),
            ("Đã thêm {0}, ghi đè {1}, bỏ qua {2} tài khoản. Có thể hoàn tác lô này bằng nút Hoàn tác nhập.",
                "Added {0}, replaced {1}, and skipped {2} accounts. Use Undo import to reverse this batch."),
            ("Đưa {0} tài khoản đã chọn vào thùng rác trong 30 ngày?", "Move the {0} selected accounts to Trash for 30 days?"),
            ("Hoàn tác lần nhập gồm {0} tài khoản? Thay đổi sau lần nhập sẽ bị từ chối để tránh mất dữ liệu.",
                "Undo the import of {0} accounts? Changes made since the import will be rejected to prevent data loss."),
            ("Hoàn tác nhập", "Undo import"),
            ("Kết quả nhập dữ liệu", "Import result"),
            ("Không có lần nhập nào có thể hoàn tác trên máy này.", "There is no import batch available to undo on this computer."),
            ("Không đọc được dữ liệu hoàn tác nhập: ", "Could not read import undo data: "),
            ("Lỗi chia sẻ", "Sharing error"),
            ("Lỗi nhập dữ liệu", "Import error"),
            ("Lỗi nhập dữ liệu: ", "Import error: "),
            ("Xác nhận xóa hàng loạt", "Confirm bulk deletion"),
            ("Không thể kiểm tra tài khoản Google Drive: {0}", "Could not check the Google Drive account: {0}"),
            ("Không thể mở tài khoản", "Could not open account"),
            ("Không thể sao chép mật khẩu vào bộ nhớ tạm an toàn.", "Could not safely copy the password to the clipboard."),
            ("Không thể ngắt kết nối Google Drive: ", "Could not disconnect from Google Drive: "),
            ("Windows chuyển sang chế độ ngủ.", "Windows entered sleep mode."),
            ("Windows vừa tiếp tục sau chế độ ngủ.", "Windows resumed from sleep."),
            ("Chỉ quản trị viên đang hoạt động được xoay khóa dùng chung.", "Only an active administrator can rotate the shared key."),
            ("xoay khóa vault", "rotate the vault key"),
            ("quản lý tệp đính kèm", "manage attachments"),
            ("xem và hoàn tác lịch sử chỉnh sửa", "view and undo edit history"),
            ("thay đổi nhiều tài khoản cùng lúc", "change multiple accounts at once"),
            ("xuất các tài khoản đã chọn", "export selected accounts"),
            ("Xuất tài khoản đã chọn", "Export selected accounts"),
            ("hoàn tác lần nhập gần nhất", "undo the most recent import"),
            ("nhập dữ liệu tài khoản", "import account data"),
            ("diễn tập khôi phục bản sao lưu", "run a backup restore drill"),
            ("kiểm tra tài khoản Google Drive đã liên kết", "check the linked Google Drive account"),
            ("ngắt kết nối Google Drive trên máy này", "disconnect Google Drive on this device"),
            ("tạo liên kết chia sẻ tạm thời cho tài khoản", "create a temporary share for an account"),
            ("xem hoặc thu hồi liên kết chia sẻ Drive", "view or revoke Drive shares"),
            ("mở gói tài khoản được chia sẻ", "open a shared account package"),
            ("Xóa token Google Drive đã mã hóa đang lưu trên máy này? Bản sao lưu trên Drive không bị xóa. Quyền ứng dụng trong tài khoản Google vẫn còn và cần thu hồi riêng nếu bạn muốn.",
                "Remove the encrypted Google Drive token saved on this device? Drive backups will not be deleted. The app's permission in your Google account will remain and must be revoked separately if desired."),
            ("Không có tệp nào bị ghi đè: tệp trùng nội dung được bỏ qua, tệp trùng tên nhưng khác nội dung được lưu thêm với tên mới.",
                "No files were overwritten: files with identical contents were skipped, and files with the same name but different contents were saved under new names."),
            ("Một tài khoản có quá 50 tệp đính kèm.", "An account cannot have more than 50 attachments."),
            ("Một tệp đính kèm không hợp lệ hoặc vượt quá 10 MB.", "An attachment is invalid or exceeds 10 MB."),
            ("Tổng tệp đính kèm của tài khoản vượt quá 25 MB.", "The account's total attachment size exceeds 25 MB."),
            ("Không thể giải mã bằng khóa hiện tại hoặc khóa lịch sử. File được giữ nguyên để khôi phục.", "Could not decrypt with the current or historical key. The file was left unchanged for recovery."),
            ("Đang có thao tác khóa vault khác; hãy thử lại sau.", "Another vault locking operation is in progress. Try again later."),
            ("Định dạng mã hóa không được hỗ trợ.", "The encryption format is not supported."),
            ("Google Drive không trả về thông tin file vừa tải lên.", "Google Drive did not return metadata for the uploaded file."),
            ("Quyền Drive đã bị thu hồi, nhưng gói mã hóa chưa chuyển được vào thùng rác: share-123", "Drive access was revoked, but the encrypted package could not be moved to the trash: share-123"),
            ("Không tìm thấy gói chia sẻ đã mã hóa.", "The encrypted share package was not found."),
            ("Không thể phân tích file KeePass XML.", "Could not parse the KeePass XML file."),
            ("Tệp xuất mật khẩu JSON", "Password manager JSON export"),
            ("Thông tin hoàn tác nhập CSV có kích thước không hợp lệ.", "The CSV import undo data has an invalid size."),
            ("Thông tin hoàn tác nhập CSV không hợp lệ.", "The CSV import undo data is invalid."),
            ("Thông tin lô nhập CSV không hợp lệ.", "The CSV import batch is invalid."),
            ("Đường dẫn trạng thái hoàn tác CSV không an toàn.", "The CSV import undo state path is unsafe."),
            ("Bản sao lưu có tên file trùng nhau nên không thể diễn tập an toàn.", "The backup contains duplicate file names and cannot be safely restore-tested."),
            ("Thời điểm nhắc lại phải nằm trong tương lai.", "The reminder time must be in the future."),
            ("Cần ghi lý do khi bỏ qua cảnh báo.", "Enter a reason before ignoring this warning."),
            ("Tệp trạng thái xem xét bảo mật vượt giới hạn kích thước.", "The security review state file exceeds the size limit."),
            ("Tệp trạng thái xem xét bảo mật không hợp lệ.", "The security review state file is invalid."),
            ("Nội dung tài khoản, TOTP hoặc tệp đính kèm sau khôi phục thử không khớp bản sao lưu.", "The accounts, TOTP data, or attachments from the restore drill do not match the backup."),
            ("Xác minh dữ liệu sau khi mã hóa lại thất bại.", "Data verification failed after re-encryption."),
            ("Không xoay bản sao lưu nằm trong thư mục liên kết hệ thống.", "Cannot rotate a backup in a reparse-point directory."),
            ("Phục hồi xoay khóa bị dừng vì key.dat đã đổi ngoài giao dịch. Dữ liệu và nhật ký được giữ nguyên.", "Key rotation recovery stopped because key.dat changed outside the transaction. Data and the journal were left unchanged."),
            ("Journal xoay khóa không hợp lệ.", "The key rotation journal is invalid."),
            ("Trạng thái đồng bộ Drive không hợp lệ.", "The Drive sync state is invalid."),
            ("Không xoay tệp liên kết ngoài kho: backup.vault.", "Cannot rotate an externally linked file: backup.vault."),
            ("Không thể xoay khóa an toàn vì 'legacy.vault' không dùng định dạng AES-GCM được hỗ trợ. File vẫn được giữ nguyên.", "Cannot safely rotate the key because 'legacy.vault' does not use a supported AES-GCM format. The file was left unchanged."),
            ("Phục hồi xoay khóa bị dừng vì 'vault.dat' đã đổi ngoài giao dịch. File gốc và nhật ký vẫn được giữ nguyên.", "Key rotation recovery stopped because 'vault.dat' changed outside the transaction. The original file and journal were left unchanged."),
            ("Thiếu bản đã mã hóa để phục hồi 'vault.dat'. File gốc vẫn được giữ nguyên.", "The encrypted recovery copy is missing for 'vault.dat'. The original file was left unchanged."),
            ("Thiếu bản phục hồi trước khi xoay khóa cho 'vault.dat'.", "The pre-rotation recovery copy is missing for 'vault.dat'."),
            ("Không xác minh được tệp sau khi thay thế 'vault.dat'. Khóa mới chưa được kích hoạt.", "Could not verify the file after replacing 'vault.dat'. The new key has not been activated."),
            ("Nội dung kiểm tra sau khôi phục không khớp: backup.zip", "Content verification after restore did not match: backup.zip"),
            ("Đã đặt cụm mật khẩu sao lưu.\n\nTừ giờ các bản sao lưu và tệp xuất kho mật khẩu mới sẽ mở được trên máy khác bằng cụm này. Hãy ghi lại cụm ở nơi an toàn.",
                "Backup passphrase set.\n\nNew backups and password vault exports can now be opened on another computer with this passphrase. Write it down and keep it somewhere safe."),
            ("Định dạng tài khoản được hỗ trợ là CSV, JSON và KeePass XML. File .vault có luồng nhập riêng; file .tqf là bản sao lưu File cá nhân.",
                "Supported account formats are CSV, JSON, and KeePass XML. .vault files have a separate import flow; .tqf files are personal-file backups."),
            ("Tệp tài khoản (*.csv;*.json;*.xml;*.kdbx)|*.csv;*.json;*.xml;*.kdbx|CSV (*.csv)|*.csv|JSON (*.json)|*.json|KeePass XML (*.xml)|*.xml|Tất cả tệp (*.*)|*.*",
                "Account files (*.csv;*.json;*.xml;*.kdbx)|*.csv;*.json;*.xml;*.kdbx|CSV (*.csv)|*.csv|JSON (*.json)|*.json|KeePass XML (*.xml)|*.xml|All files (*.*)|*.*")
        };
        foreach ((string source, string expected) in cases)
        {
            string actual = LanguageService.Translate(source, "en");
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidDataException($"English translation mismatch for '{source}': '{actual}'.");
            string secondPass = LanguageService.Translate(actual, "en");
            if (!string.Equals(secondPass, actual, StringComparison.Ordinal))
                throw new InvalidDataException($"English translation was not idempotent for '{source}': '{secondPass}'.");
        }
    }
    private static void TestActivityLogTranslations(string tempRoot)
    {
        const string timestampAndUser = "[2026-11-05 18:20:01] [admin] ";
        var cases = new (string Source, string Expected)[]
        {
            (timestampAndUser + "[CREATE_COMPLETE_BACKUP] Đã tạo gói chuyển máy: full.tqltk",
                timestampAndUser + "[Create full backup] Created migration package: full.tqltk"),
            (timestampAndUser + "[RESTORE_COMPLETE_BACKUP] Đã thêm 1 tài khoản, 0 ghi chú và 2 tệp.",
                timestampAndUser + "[Restore full backup] Accounts restored: 1; private notes: 0; files: 2."),
            (timestampAndUser + "[RECOVER_SELECTED_BACKUP_COPIES] Đã khôi phục thành bản sao 2 tài khoản, 1 ghi chú và 0 tệp.",
                timestampAndUser + "[Recover backup entries as copies] Accounts restored: 2; private notes: 1; files: 0."),
            (timestampAndUser + "[EDIT_DRAFT_SAVE_FAILED] Không lưu được bản nháp khi khóa phiên.",
                timestampAndUser + "[Encrypted edit draft save failed] Could not save the edit draft when locking the session."),
            (timestampAndUser + "[VAULT_KEY_ROTATION] Đã mã hóa lại 4 tệp; giữ 1 backup dùng cụm mật khẩu.",
                timestampAndUser + "[Vault key rotation] Re-encrypted 4 files; retained 1 passphrase-protected backup."),
            (timestampAndUser + "[VAULT_KEY_ROTATION] Đã mã hóa lại 1 tệp; giữ 0 backup dùng cụm mật khẩu.",
                timestampAndUser + "[Vault key rotation] Re-encrypted 1 file; retained 0 passphrase-protected backups."),
            (timestampAndUser + "[FILE_BACKUP_RESTORE_DRILL] recovery.tqf: True",
                timestampAndUser + "[Personal-file backup restore drill] recovery.tqf: True"),
            (timestampAndUser + "[VAULT_BACKUP_RESTORE_DRILL] recovery.vault: True",
                timestampAndUser + "[Vault backup restore drill] recovery.vault: True"),
            (timestampAndUser + "[IMPORT_CREDENTIALS] Thêm 3, ghi đè 1, bỏ qua 0 (Đã đọc 3 mục đăng nhập từ JSON.)",
                timestampAndUser + "[Import credentials] Added 3, overwrote 1, and skipped 0 (Read 3 login entries from JSON.)"),
            (timestampAndUser + "[UNDO_IMPORT] 0123456789abcdef",
                timestampAndUser + "[Undo import] 0123456789abcdef"),
            (timestampAndUser + "[AUTO_LOCK] Không hoạt động 5 phút",
                timestampAndUser + "[Automatic lock] After 5 minutes of inactivity"),
            (timestampAndUser + "[BULK_ACCOUNT_ACTION] Thay đổi 2 tài khoản",
                timestampAndUser + "[Bulk account action] Changed 2 accounts"),
            (timestampAndUser + "[EXPORT_CSV] Xuất 2 tài khoản ra CSV",
                timestampAndUser + "[Export CSV] Exported 2 accounts to CSV"),
            (timestampAndUser + "[AUTO_BACKUP] Khi thoát: backup.vault",
                timestampAndUser + "[Automatic backup] On exit: backup.vault"),
            (timestampAndUser + "[AUTO_BACKUP] Định kỳ: complete_auto_backup.tqltk",
                timestampAndUser + "[Automatic backup] Scheduled: complete_auto_backup.tqltk"),
            (timestampAndUser + "[AUTO_THEME_SWITCH] Tự động đổi giao diện sang Sáng lúc 06:00",
                timestampAndUser + "[Automatic theme change] Automatically switched theme to Light at 06:00"),
            (timestampAndUser + "[AUTO_THEME_SWITCH] Tự động đổi giao diện sang Tối lúc 18:00",
                timestampAndUser + "[Automatic theme change] Automatically switched theme to Dark at 18:00"),
            (timestampAndUser + "[LOGIN_WINDOWS_HELLO] Đăng nhập bằng Windows Hello",
                timestampAndUser + "[Windows Hello sign-in] Sign in with Windows Hello"),
            (timestampAndUser + "[LOGIN_2FA_FAILED] Sai mã hoặc hủy xác thực 2 bước",
                timestampAndUser + "[Two-factor sign-in failed] Incorrect code or cancelled two-factor authentication"),
            (timestampAndUser + "[APP_EXIT] OnExit đã chạy, mã thoát 0",
                timestampAndUser + "[Application exit] OnExit ran; exit code 0"),
            (timestampAndUser + "[ADMIN_DELETE_USER_DATA] demo-admin: xóa 1 mục, lỗi 0",
                timestampAndUser + "[Delete user data] demo-admin: deleted 1 entry, 0 errors"),
            (timestampAndUser + "[EXPORT_VAULT] Đã xuất 1 tài khoản ra backup.vault",
                timestampAndUser + "[Export vault] Exported 1 account to backup.vault"),
            (timestampAndUser + "[IMPORT_VAULT] Đã nhập 1 tài khoản (bỏ qua 0 trùng)",
                timestampAndUser + "[Import vault] Imported 1 account (skipped 0 duplicates)"),
            (timestampAndUser + "[RESTORE_FILES_GOOGLE_DRIVE] Khôi phục File cá nhân từ Drive: files.tqf (1 file, bỏ qua 0)",
                timestampAndUser + "[Restore personal files from Google Drive] Backup files.tqf: restored 1 personal file; skipped 0 files."),
            (timestampAndUser + "[SYNC_FILES_GOOGLE_DRIVE] Tải lên 1 tệp cá nhân (1 MB)",
                timestampAndUser + "[Sync personal files to Google Drive] Uploaded 1 personal file (1 MB)"),
            (timestampAndUser + "[PWNED_PASSWORD_CHECK] Đã kiểm tra 3 giá trị; ảnh hưởng 1 mục",
                timestampAndUser + "[Check for breached passwords] Checked 3 unique passwords; 1 account affected.")
        };
        foreach ((string source, string expected) in cases)
        {
            string actual = LanguageService.TranslateLogContent(source, "en");
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidDataException($"Activity-log translation mismatch for '{source}': '{actual}'.");
            if (actual.Contains("[VAULT_KEY_ROTATION]", StringComparison.Ordinal) ||
                actual.Contains("[IMPORT_CREDENTIALS]", StringComparison.Ordinal) ||
                actual.Contains("[UNDO_IMPORT]", StringComparison.Ordinal))
                throw new InvalidDataException($"An activity action code was not translated: '{actual}'.");
        }
        var timestamp = new DateTimeOffset(2026, 11, 5, 18, 20, 1, TimeSpan.FromHours(7));
        const string hostile = "Người dùng%5B[admin]\r\n[LOGIN] \u2028 \u202E";
        var structured = ActivityLogCodec.Create(timestamp, hostile, "EXPORT_CSV", messageId: "log.detail.export_csv.1", arguments: new object?[] { 3 });
        string jsonLine = ActivityLogCodec.Serialize(structured);
        if (jsonLine.Contains('\n') || jsonLine.Contains('\r') || !ActivityLogCodec.TryParse(jsonLine, out var parsed) || parsed!.Username != hostile || parsed.Timestamp != timestamp)
            throw new InvalidDataException("Structured log round-trip or single-line framing failed.");
        foreach (string language in new[] { "vi", "en", "vi" })
        {
            string display = LanguageService.TranslateLogContent(jsonLine, language);
            if (display.Contains('\n') || display.Contains('\r') || !display.Contains(ActivityLogger.EscapeField(hostile), StringComparison.Ordinal) ||
                !display.Contains(LocalizationAuditService.FormatForLanguage("log.detail.export_csv.1", language, 3), StringComparison.Ordinal))
                throw new InvalidDataException("Structured log translation changed fields or allowed line injection.");
        }
        string opaque = ActivityLogCodec.Serialize(ActivityLogCodec.Create(timestamp, "admin", "EXPORT_FILE", "Được thêm [file] %5B"));
        if (!LanguageService.TranslateLogContent(opaque, "en").EndsWith(ActivityLogger.EscapeField("Được thêm [file] %5B"), StringComparison.Ordinal))
            throw new InvalidDataException("Opaque log data was translated or decoded.");
        foreach (string broken in new[] { "{", "{}", jsonLine.Replace("\"Version\":2", "\"Version\":999"), jsonLine.Replace("\"Version\":2", "\"Version\":2,\"Version\":2") })
            if (ActivityLogCodec.TryParse(broken, out _) || LanguageService.TranslateLogContent(broken, "en") != LocalizationAuditService.Get("log.unreadable", "en"))
                throw new InvalidDataException("Malformed or unsupported structured log was accepted.");
        var unknown = structured with { MessageId = "log.unknown.future" };
        if (!LanguageService.TranslateLogContent(ActivityLogCodec.Serialize(unknown), "en").Contains(ActivityLogger.EscapeField(LocalizationAuditService.Get("log.unknown_message", "en")), StringComparison.Ordinal))
            throw new InvalidDataException("Unknown event template did not fall back safely.");
        DateTime now = DateTime.Now;
        string oldLogin = ActivityLogger.FormatLine(now.AddHours(-2), "mixed", "LOGIN", "");
        string failed = ActivityLogCodec.Serialize(ActivityLogCodec.Create(new DateTimeOffset(now.AddMinutes(-10)), "mixed", "LOGIN_FAILED"));
        if (LoginHistory.BuildWarningFromLogs("mixed", now, new[] { new[] { oldLogin, "{broken", failed } }) == null ||
            LoginHistory.BuildWarningFromLogs("other", now, new[] { new[] { oldLogin, failed } }) != null)
            throw new InvalidDataException("Mixed legacy and structured sign-in history was misclassified.");
        ActivityLogger.Log("regressionadmin", "VAULT_KEY_ROTATION", "synthetic translation test");
        ActivityLogger.LogMessage("regressionadmin", "EXPORT_CSV", "log.detail.export_csv.1", 2);
        string activeLog = ActivityLogger.ListLogFiles().Single();
        File.AppendAllText(activeLog, "{partial");
        ActivityLogger.Log("regressionadmin", "EXPORT_FILE", "safe-after-partial");
        var afterPartial = File.ReadAllLines(activeLog);
        if (!afterPartial.Contains("{partial") || !ActivityLogCodec.TryParse(afterPartial[^1], out var recoveredLine) || recoveredLine!.Detail != "safe-after-partial")
            throw new InvalidDataException("Appending after an interrupted line corrupted the next record.");
        int beforeConcurrent = afterPartial.Length;
        System.Threading.Tasks.Parallel.For(0, 8, index => ActivityLogger.Log("regressionadmin", "EXPORT_FILE", "concurrent-" + index));
        string[] concurrent = File.ReadAllLines(activeLog).Skip(beforeConcurrent).ToArray();
        if (concurrent.Length != 8 || concurrent.Any(line => !ActivityLogCodec.TryParse(line, out _)))
            throw new InvalidDataException("Concurrent activity writes were lost or interleaved.");
        string[] logFiles = ActivityLogger.ListLogFiles();
        string expectedLogRoot = Path.Combine(tempRoot, "PersonalInfoManager", "Logs");
        if (logFiles.Length == 0 || logFiles.Any(path => !path.StartsWith(expectedLogRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Security regression logs were written outside the isolated temporary test-data directory.");
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
    private static void TestLocalizationAudit()
    {
        string projectRoot = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(projectRoot, "PersonalInfoManager.csproj"))) projectRoot = Path.Combine(projectRoot, "Nguon");
        string declaredVersion = XDocument.Load(Path.Combine(projectRoot, "PersonalInfoManager.csproj"))
            .Descendants("Version").Single().Value;
        if (AppVersion.Current != declaredVersion)
            throw new InvalidDataException("Runtime version differs from the project version.");
        foreach (string language in new[] { "vi", "en" })
        {
            string footer = LocalizationAuditService.FormatForLanguage("app.footer", language, AppVersion.Current);
            if (!footer.Contains("v" + declaredVersion, StringComparison.Ordinal) ||
                (language == "en" && (!footer.StartsWith("Made by", StringComparison.Ordinal) || !footer.EndsWith("MIT License", StringComparison.Ordinal))))
                throw new InvalidDataException("The versioned footer is not localized correctly.");
        }
        foreach (BackupItemStatus status in Enum.GetValues<BackupItemStatus>())
            foreach (string language in new[] { "vi", "en" })
                if (string.IsNullOrWhiteSpace(LocalizationAuditService.Get("backup.status." + status, language)))
                    throw new InvalidDataException("A typed backup status is missing its label.");
        foreach (BackupRecoverySource source in Enum.GetValues<BackupRecoverySource>())
            foreach (string language in new[] { "vi", "en" })
                _ = LocalizationAuditService.Get("backup.source." + source, language);
        var choice = new BackupComparisonEntry
        {
            Name = "Synthetic choice", Status = BackupItemStatus.Modified, Details = "Synthetic details",
            Kind = BackupRecoveryItemKind.Account, OlderKey = "older", NewerKey = "newer",
            AvailableSources = new[] { BackupRecoverySource.None, BackupRecoverySource.Older, BackupRecoverySource.Newer },
            SelectedSource = BackupRecoverySource.Older
        };
        string priorLanguage = LanguageService.CurrentCode;
        try
        {
            foreach (string language in new[] { "vi", "en", "vi" })
            {
                LanguageService.SetLanguageForTesting(language);
                foreach (BackupRecoverySource source in choice.AvailableSources)
                {
                    choice.SelectedSource = source;
                    var selected = BackupComparisonWindow.GetSelectedRecoveryItems(new[] { choice });
                    if (source == BackupRecoverySource.None ? selected.Count != 0 :
                        selected.Count != 1 || selected[0].FromOlder != (source == BackupRecoverySource.Older) ||
                        selected[0].Key != (source == BackupRecoverySource.Older ? "older" : "newer"))
                        throw new InvalidDataException("Recovery choices depend on the display language.");
                }
            }
            choice.SelectedSource = BackupRecoverySource.Unsupported;
            if (BackupComparisonWindow.GetSelectedRecoveryItems(new[] { choice }).Count != 0)
                throw new InvalidDataException("An unavailable recovery choice was accepted.");
        }
        finally { LanguageService.SetLanguageForTesting(priorLanguage); }
        foreach (var message in LocalizationAuditService.Catalog.Where(item =>
                     item.Id.StartsWith("auth.") || item.Id.StartsWith("storage.") || item.Id.StartsWith("ui.mainwindow.")))
            foreach (string language in new[] { "vi", "en" })
                if (LanguageService.Translate(message.Id, language) != LocalizationAuditService.Get(message.Id, language))
                    throw new InvalidDataException("A stable message ID did not resolve in both languages.");
        IReadOnlyList<LocalizationAuditIssue> issues = LocalizationAuditService.Audit();
        if (issues.Count > 0)
            throw new InvalidDataException("Localization audit found issues: " +
                string.Join(" | ", issues.Take(20).Select(issue => issue.Key + ": " + issue.Problem)));
        string id = LocalizationAuditService.GetMessageId("Hướng dẫn chuyển máy");
        if (LocalizationAuditService.Get(id, "en") != "Computer migration guide" ||
            LocalizationAuditService.Get(id, "vi") != "Hướng dẫn chuyển máy" ||
            LocalizationAuditService.Catalog.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != LocalizationAuditService.Catalog.Count ||
            LocalizationAuditService.AuditSources(new[] { "A missing synthetic translation" }).Count != 1)
            throw new InvalidDataException("Message IDs, bilingual lookup, or missing-translation detection failed.");
        var sampleDate = new DateTime(2026, 11, 7, 13, 5, 0, DateTimeKind.Local);
        string viDate = LocalizationAuditService.FormatDateTime(sampleDate, "vi");
        string enDate = LocalizationAuditService.FormatDateTime(sampleDate, "en");
        if (string.IsNullOrWhiteSpace(viDate) || string.IsNullOrWhiteSpace(enDate) || viDate == enDate ||
            LocalizationAuditService.FormatDate(sampleDate, "vi") == LocalizationAuditService.FormatDate(sampleDate, "en") ||
            LocalizationAuditService.FormatCount(1, "account", "accounts", "tài khoản", "en") != "1 account" ||
            LocalizationAuditService.FormatCount(2, "account", "accounts", "tài khoản", "en") != "2 accounts")
            throw new InvalidDataException("English date or plural formatting is incorrect.");
        DateTime unspecifiedDate = DateTime.SpecifyKind(sampleDate, DateTimeKind.Unspecified);
        DateTime utcDate = sampleDate.ToUniversalTime();
        if (LocalizationAuditService.FormatDateTime(unspecifiedDate, "en") != enDate ||
            LocalizationAuditService.FormatDateTime(utcDate, "en") != enDate ||
            string.IsNullOrWhiteSpace(LocalizationAuditService.FormatDateTime(new DateTimeOffset(sampleDate), "en")))
            throw new InvalidDataException("Date and time formatting did not preserve local, UTC, and offset values.");
        string formattedEnglish = LocalizationAuditService.FormatSource("Đã lưu {0:N0} mục.", "en", 1234);
        string formattedVietnamese = LocalizationAuditService.FormatSource("Đã lưu {0:N0} mục.", "vi", 1234);
        if (formattedEnglish != "Saved 1,234 entries." || formattedEnglish == formattedVietnamese)
            throw new InvalidDataException("Formatted numbers do not follow the selected language culture.");
        if (!LocalizationAuditService.Get("backup.migration.guide", "en").StartsWith("1. Set a dedicated backup passphrase", StringComparison.Ordinal) ||
            !LocalizationAuditService.Get("backup.migration.guide", "vi").StartsWith("1. Đặt cụm mật khẩu", StringComparison.Ordinal))
            throw new InvalidDataException("The migration guide is missing one of its language versions.");
        if (LocalizationAuditService.FormatNamed("import.preview.count",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = 3 }, "vi") !=
            "Đọc được 3 mục. Chọn một mục để xem thông tin.")
            throw new InvalidDataException("Named localization placeholders did not render correctly.");
        if (LocalizationAuditService.FormatNamed("import.preview.count",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = 3 }, "en") !=
            "Found 3 entries. Select one to view its details.")
            throw new InvalidDataException("English named localization placeholders did not render correctly.");
        try
        {
            _ = LocalizationAuditService.Format("backup.restore.summary", 1);
            throw new InvalidDataException("A missing format argument was not rejected.");
        }
        catch (FormatException) { }
    }
    private static void TestUiTranslationSources()
    {
        string sourceRoot = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(sourceRoot, "PersonalInfoManager.csproj"))) sourceRoot = Path.Combine(sourceRoot, "Nguon");
        if (!File.Exists(Path.Combine(sourceRoot, "PersonalInfoManager.csproj")))
            throw new DirectoryNotFoundException("Run the regression suite from the project or repository root to audit UI sources.");
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var vietnamese = new Regex(@"[\u00C0-\u024F\u1E00-\u1EFF]", RegexOptions.CultureInvariant);
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                     .Where(file => !Regex.IsMatch(file, @"[\\/](?:bin|obj|artifacts|\.vs|\.git|tests)[\\/]", RegexOptions.IgnoreCase)))
        {
            if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (XAttribute attribute in XDocument.Load(file).Descendants().Attributes()
                             .Where(attribute => attribute.Name.LocalName is "Text" or "Content" or "Header" or "ToolTip" or "Title"))
                    if (attribute.Value.StartsWith("{DynamicResource xaml.", StringComparison.Ordinal))
                    {
                        string key = attribute.Value[17..^1].Trim();
                        foreach (string language in new[] { "vi", "en" }) _ = LocalizationAuditService.Get(key, language);
                    }
                    else if (!attribute.Value.StartsWith('{') && attribute.Value.Any(char.IsLetter) &&
                        !Regex.IsMatch(attribute.Value, @"^(?:Gmail:?|Windows Hello(?: \(BETA\))?|Google Drive(?: \(BETA\))?|Ctrl\+Shift\+[,A-Z]|[0-9]+ ?[KMGT]B|TQLTK)$"))
                        sources.Add(attribute.Value);
            }
            else if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                     !file.EndsWith("EnglishCatalog.cs", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(file),
                             "(?:LanguageService\\.(?:Translate|Format)|(?:LocalizedMessageBox|MessageBox)\\.Show)\\(\\s*(?:this,\\s*)?(\"(?:\\\\.|[^\"\\\\])*\"(?:\\s*\\+\\s*\"(?:\\\\.|[^\"\\\\])*\")*)"))
                {
                    string source = string.Concat(Regex.Matches(match.Groups[1].Value, "\"(?:\\\\.|[^\"\\\\])*\"")
                        .Select(literal => JsonSerializer.Deserialize<string>(literal.Value)));
                    if (vietnamese.IsMatch(source)) sources.Add(source);
                }
            }
        }
        string languageSource = File.ReadAllText(Path.Combine(sourceRoot, "Service", "LanguageService.cs"));
        var actionLabels = Regex.Matches(languageSource, "\\[\"(?<action>[A-Z][A-Z0-9_]+)\"\\]\\s*=")
            .Select(match => match.Groups["action"].Value).ToHashSet(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !Regex.IsMatch(file, @"[\\/](?:bin|obj|artifacts|\.vs|\.git|tests)[\\/]", RegexOptions.IgnoreCase)))
        {
            string code = File.ReadAllText(file);
            foreach (Match assignment in Regex.Matches(code,
                         "(?:\\.(?:Text|Content|Header|Title|ToolTip)|\\b(?:Text|Content|Header|Title|ToolTip))\\s*=\\s*(\"(?:\\\\.|[^\"\\\\])*\"(?:\\s*\\+\\s*\"(?:\\\\.|[^\"\\\\])*\")*)"))
            {
                string source = string.Concat(Regex.Matches(assignment.Groups[1].Value, "\"(?:\\\\.|[^\"\\\\])*\"")
                    .Select(literal => JsonSerializer.Deserialize<string>(literal.Value)));
                if (vietnamese.IsMatch(source) && vietnamese.IsMatch(LanguageService.Translate(source, "en")))
                    throw new InvalidDataException("Untranslated dynamic UI text in " + Path.GetFileName(file) + ": " + source);
            }
            foreach (Match action in Regex.Matches(code, "ActivityLogger\\.Log\\([^;]{0,300}?,\\s*\"(?<action>[A-Z][A-Z0-9_]+)\""))
                if (!actionLabels.Contains(action.Groups["action"].Value))
                    throw new InvalidDataException("Missing activity action translation: " + action.Groups["action"].Value);
            foreach (Match key in Regex.Matches(code, "LanguageService\\.SetLocalized(?:Key|FormattedText|FormattedKey)\\([^,]+,\\s*\"(?<key>[a-z][a-z0-9_.]+)\""))
                _ = LocalizationAuditService.Get(key.Groups["key"].Value, "en");
        }
        var issues = LocalizationAuditService.AuditSources(sources);
        issues = issues.Concat(LocalizationAuditService.AuditSources(new[]
        {
            "Được thêm", "Đã sửa", "Đã xóa", "Có trong gói", "Không có trong gói", "Luôn có", "Đã ghim", "Thông thường"
        })).ToArray();
        if (issues.Count > 0)
            throw new InvalidDataException("Unregistered UI translations: " + string.Join(" | ", issues.Select(issue => issue.Key)));
    }
    private static void TestCompleteBackup(string username, string backupDirectory, string passphrase)
    {
        var vault = new AccountVaultService();
        var notes = new NoteService();
        var localFiles = new LocalFileService();
        List<SavedAccount> originalAccounts = vault.Load(username);
        List<Note> originalNotes = notes.Load();
        List<FileInfo> originalFiles = localFiles.ListFiles(username);
        if (originalAccounts.Count != 1 || originalNotes.Count != 1 || originalFiles.Count != 1)
            throw new InvalidDataException("The isolated full-backup fixture was not in the expected state.");
        string packagePath = Path.Combine(backupDirectory, "full-migration-test.tqltk");
        var created = CompleteBackupService.Create(username, packagePath, passphrase);
        if (!created.ok || created.info is not { AccountCount: 1, NoteCount: 1, PersonalFileCount: 1, IncludesSettings: true })
            throw new InvalidDataException("The full encrypted package omitted expected data: " + created.message);
        if (created.info!.AppVersion != AppVersion.Current) throw new InvalidDataException("Backup version differs from the running app.");
        if (!CompleteBackupService.Inspect(packagePath, passphrase).ok ||
            CompleteBackupService.Inspect(packagePath, "incorrect passphrase").ok)
            throw new CryptographicException("The full backup verification accepted an invalid passphrase or rejected a valid package.");
        string partialPackagePath = Path.Combine(backupDirectory, "partial-migration-test.tqltk");
        var partialPackage = CompleteBackupService.Create(username, partialPackagePath, passphrase,
            includeNotes: false, includePersonalFiles: false, includeSettings: false);
        if (!partialPackage.ok || partialPackage.info is not { IncludesNotes: false, IncludesPersonalFiles: false, IncludesSettings: false })
            throw new InvalidDataException("The backup did not preserve the selected component exclusions.");
        var partialComparison = CompleteBackupService.Compare(username, partialPackagePath, passphrase,
            partialPackagePath, passphrase);
        if (!partialComparison.ok || partialComparison.result == null ||
            !partialComparison.result.Entries.Any(entry => entry.Name == "Ghi chú riêng") ||
            !partialComparison.result.Entries.Any(entry => entry.Name == "Tệp cá nhân") ||
            !partialComparison.result.Entries.Any(entry => entry.Name == "Cài đặt an toàn"))
            throw new InvalidDataException("Comparison did not disclose backup components that were excluded from both packages.");
        var prepared = CompleteBackupService.PrepareRestore(username, packagePath, passphrase);
        if (!prepared.ok || prepared.preview is null || prepared.preview.AccountsToAdd != 0 ||
            prepared.preview.NotesToAdd != 0 || prepared.preview.FilesToAdd != 0)
            throw new InvalidDataException("The full-backup preview did not recognize existing data.");
        SavedAccount changedAccount = vault.Load(username).Single();
        changedAccount.ServiceName += " changed after preview";
        vault.Save(username, new List<SavedAccount> { changedAccount });
        var stale = CompleteBackupService.Restore(username, prepared.preview, passphrase, restoreSettings: false);
        vault.Save(username, originalAccounts.Select(account => account.Clone()).ToList());
        if (stale.ok)
            throw new InvalidDataException("Restore did not reject a changed local state after preview.");
        vault.Save(username, new List<SavedAccount>());
        notes.Save(new List<Note>());
        foreach (FileInfo file in localFiles.ListFiles(username)) localFiles.DeleteFile(file.FullName);
        var missingData = CompleteBackupService.PrepareRestore(username, packagePath, passphrase);
        if (!missingData.ok || missingData.preview is null || missingData.preview.AccountsToAdd != 1 ||
            missingData.preview.NotesToAdd != 1 || missingData.preview.FilesToAdd != 1)
            throw new InvalidDataException("Restore preview did not identify missing account, note, and personal file data.");
        var restored = CompleteBackupService.Restore(username, missingData.preview, passphrase, restoreSettings: false);
        if (!restored.ok || restored.accountsAdded != 1 || restored.notesAdded != 1 || restored.filesAdded != 1)
            throw new InvalidDataException("The full restore did not restore every selected data category: " + restored.message);
        SavedAccount restoredAccount = vault.Load(username).Single();
        if (restoredAccount.TotpSecret != "JBSWY3DPEHPK3PXP" || restoredAccount.Attachments.Count != 1 ||
            notes.Load().Single().Content != "synthetic note content")
            throw new InvalidDataException("The full restore lost TOTP, attachment, or encrypted-note content.");
        FileInfo restoredFile = localFiles.ListFiles(username).Single();
        using var restoredPlaintext = new MemoryStream();
        localFiles.CopyToPlaintext(restoredFile.FullName, restoredPlaintext);
        if (Encoding.UTF8.GetString(restoredPlaintext.ToArray()) != "synthetic personal file data")
            throw new InvalidDataException("The full restore changed personal-file content.");
        string? folderError = AutoBackupService.TrySetFolder(username, backupDirectory);
        if (folderError != null) throw new IOException(folderError);
        AutoBackupService.SetRetentionCount(username, 3);
        AutoBackupService.SetRetentionDays(username, 30);
        AutoBackupService.SetStorageLimitMb(username, 512);
        AutoBackupService.SetPeriodicIntervalHours(username, 24);
        AutoBackupService.SetPeriodicEnabled(username, true);
        var periodic = AutoBackupService.RunPeriodicIfDue(username);
        if (!periodic.Attempted || !periodic.Succeeded || periodic.Path is null || !File.Exists(periodic.Path))
            throw new InvalidDataException("The scheduled full backup did not create an encrypted file: " + periodic.Message);
        if (!CompleteBackupService.Inspect(periodic.Path, passphrase).ok)
            throw new CryptographicException("The scheduled backup could not be verified.");
        int previousBackupCount = AutoBackupService.ListCompleteBackups(username).Count;
        var unchanged = AutoBackupService.RunPeriodicIfDue(username, default, DateTime.UtcNow.AddDays(2));
        if (unchanged.Attempted || AutoBackupService.ListCompleteBackups(username).Count != previousBackupCount)
            throw new InvalidDataException("A due scheduled backup created another copy despite unchanged data.");
        List<SavedAccount> savedBeforeChange = vault.Load(username).Select(item => item.Clone()).ToList();
        var changedAccounts = savedBeforeChange.Select(item => item.Clone()).ToList();
        changedAccounts[0].Note += " synthetic scheduled-backup change";
        vault.Save(username, changedAccounts);
        var changedBackup = AutoBackupService.RunPeriodicIfDue(username, default, DateTime.UtcNow.AddDays(2));
        if (!changedBackup.Attempted || !changedBackup.Succeeded || changedBackup.Path == null ||
            AutoBackupService.ListCompleteBackups(username).Count != previousBackupCount + 1)
            throw new InvalidDataException("A due scheduled backup did not capture changed vault data.");
        vault.Save(username, savedBeforeChange);
        AutoBackupService.RecordCompleteBackupFailure(username, "Synthetic temporary backup failure");
        var retry = AutoBackupService.RunPeriodicIfDue(username, default, DateTime.UtcNow.AddHours(1));
        if (retry.Attempted || retry.Succeeded)
            throw new InvalidDataException("A failed scheduled backup was retried before its retry delay.");
        var changedInfo = CompleteBackupService.Inspect(changedBackup.Path, passphrase).info!;
        AutoBackupService.RecordCompleteBackup(username, changedBackup.Path, changedInfo);
        using (var heldLock = new FileStream(Path.Combine(backupDirectory,
                   ".tqltk-periodic-" + BackupCrypto.UserTag(username) + ".lock"),
                   FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var concurrent = AutoBackupService.RunPeriodicIfDue(username);
            if (concurrent.Attempted) throw new InvalidOperationException("A second periodic backup ran while the first instance held the lock.");
        }
        if (!AutoBackupService.IsPeriodicEnabled(username) || AutoBackupService.GetRetentionCount(username) != 3 ||
            AutoBackupService.GetRetentionDays(username) != 30 || AutoBackupService.GetStorageLimitMb(username) != 512)
            throw new InvalidDataException("Periodic backup policy settings did not persist.");
        AutoBackupService.SetPeriodicEnabled(username, false);
    }
    private static void TestEditAccountDraft(string username)
    {
        Guid accountId = Guid.NewGuid();
        string path = EditAccountDraftService.GetDraftPath(username, accountId);
        var draft = new EditAccountDraft
        {
            ServiceName = "Synthetic draft service",
            Username = "draft@example.invalid",
            Password = "Synthetic-draft-password-2026!",
            Note = "draft recovery data",
            TotpSecret = "JBSWY3DPEHPK3PXP",
            CustomFields = { new AccountCustomField { Name = "private field", Value = "synthetic custom value", IsSensitive = true } }
        };
        try
        {
            EditAccountDraftService.Save(username, accountId, draft);
            byte[] onDisk = File.ReadAllBytes(path);
            try
            {
                string diskText = Encoding.UTF8.GetString(onDisk);
                if (diskText.Contains(draft.Password, StringComparison.Ordinal) ||
                    diskText.Contains(draft.Note, StringComparison.Ordinal))
                    throw new CryptographicException("An edit draft leaked plaintext fields to disk.");
            }
            finally { CryptographicOperations.ZeroMemory(onDisk); }
            if (!EditAccountDraftService.TryLoad(username, accountId, out EditAccountDraft? loaded, out string? error) ||
                loaded == null || loaded.Password != draft.Password || loaded.Note != draft.Note ||
                loaded.TotpSecret != draft.TotpSecret || loaded.CustomFields.Single().Value != "synthetic custom value")
                throw new InvalidDataException("An encrypted edit draft did not round-trip: " + error);
            EditAccountDraftService.Delete(username, accountId);
            if (File.Exists(path)) throw new IOException("The edit draft was not deleted after the test.");
        }
        finally
        {
            EditAccountDraftService.Delete(username, accountId);
            CryptographicOperations.ZeroMemory(Encoding.UTF8.GetBytes(draft.Password));
            CryptographicOperations.ZeroMemory(Encoding.UTF8.GetBytes(draft.TotpSecret));
        }
    }
    private static void TestSelectiveBackupRecovery(string username, string backupDirectory, string passphrase)
    {
        LanguageService.SetLanguage("vi");
        var vault = new AccountVaultService();
        var notes = new NoteService();
        var files = new LocalFileService();
        List<SavedAccount> baselineAccounts = vault.Load(username);
        List<Note> baselineNotes = notes.Load();
        FileInfo baselineFile = files.ListFiles(username).Single();
        byte[]? baselineFileContent = null;
        using (var original = new MemoryStream())
        {
            files.CopyToPlaintext(baselineFile.FullName, original);
            baselineFileContent = original.ToArray();
        }
        string olderPath = Path.Combine(backupDirectory, "selective-older.tqltk");
        string newerPath = Path.Combine(backupDirectory, "selective-newer.tqltk");
        try
        {
            var older = CompleteBackupService.Create(username, olderPath, passphrase);
            if (!older.ok) throw new InvalidDataException("Could not create the older selective-recovery fixture: " + older.message);
            List<SavedAccount> changedAccounts = baselineAccounts.Select(account => account.Clone()).ToList();
            string currentPassword = "Synthetic-newer-password-2026!";
            changedAccounts[0].Password = currentPassword;
            vault.Save(username, changedAccounts);
            List<Note> changedNotes = notes.Load();
            changedNotes[0].Content = "newer note content";
            notes.Save(changedNotes);
            files.DeleteFile(baselineFile.FullName);
            byte[] newerFileContent = Encoding.UTF8.GetBytes("newer personal file data");
            try
            {
                using var input = new MemoryStream(newerFileContent, writable: false);
                files.ImportStream(username, baselineFile.Name, input, newerFileContent.Length);
            }
            finally { CryptographicOperations.ZeroMemory(newerFileContent); }
            var newer = CompleteBackupService.Create(username, newerPath, passphrase);
            if (!newer.ok) throw new InvalidDataException("Could not create the newer selective-recovery fixture: " + newer.message);
            var comparison = CompleteBackupService.Compare(username, olderPath, passphrase, newerPath, passphrase);
            if (!comparison.ok || comparison.result == null)
                throw new InvalidDataException("The test backups could not be compared: " + comparison.message);
            BackupComparisonEntry[] recoverable = comparison.result.Entries
                .Where(entry => entry.Kind is BackupRecoveryItemKind.Account or BackupRecoveryItemKind.Note or BackupRecoveryItemKind.PersonalFile &&
                    entry.OlderKey != null && entry.Status == BackupItemStatus.Modified)
                .ToArray();
            if (recoverable.Length != 3 || recoverable.Select(entry => entry.Kind).Distinct().Count() != 3)
                throw new InvalidDataException("The comparison did not identify the changed account, note, and file.");
            BackupRecoverySelection[] selections = recoverable
                .Select(entry => new BackupRecoverySelection(entry.Kind, entry.OlderKey!, FromOlder: true)).ToArray();
            var recovered = CompleteBackupService.RecoverSelectedAsCopies(username, comparison.result,
                passphrase, passphrase, selections);
            if (!recovered.ok || recovered.accountsAdded != 1 || recovered.notesAdded != 1 || recovered.filesAdded != 1)
                throw new InvalidDataException("Selective recovery did not add one copy of each chosen item: " + recovered.message);
            List<SavedAccount> afterAccounts = vault.Load(username);
            if (afterAccounts.Count != 2 ||
                afterAccounts.Single(account => account.Id == baselineAccounts[0].Id).Password != currentPassword ||
                afterAccounts.Single(account => account.Id != baselineAccounts[0].Id).Password != baselineAccounts[0].Password)
                throw new InvalidDataException("Selective recovery overwrote the newer account or failed to add the older copy.");
            List<Note> afterNotes = notes.Load();
            if (afterNotes.Count != 2 || !afterNotes.Any(note => note.Content == "newer note content") ||
                !afterNotes.Any(note => note.Content == baselineNotes[0].Content && note.Title.Contains("phục hồi", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Selective recovery overwrote the newer note or failed to label the recovered copy.");
            var fileContents = new HashSet<string>(StringComparer.Ordinal);
            foreach (FileInfo file in files.ListFiles(username))
            {
                using var plaintext = new MemoryStream();
                files.CopyToPlaintext(file.FullName, plaintext);
                fileContents.Add(Encoding.UTF8.GetString(plaintext.ToArray()));
            }
            if (fileContents.Count != 2 || !fileContents.Contains(Encoding.UTF8.GetString(baselineFileContent)) ||
                !fileContents.Contains("newer personal file data"))
                throw new InvalidDataException("Selective recovery overwrote the newer personal file or failed to add the older copy.");
        }
        finally
        {
            vault.Save(username, baselineAccounts);
            notes.Save(baselineNotes);
            foreach (FileInfo file in files.ListFiles(username)) files.DeleteFile(file.FullName);
            if (baselineFileContent != null)
            {
                using var original = new MemoryStream(baselineFileContent, writable: false);
                files.ImportStream(username, baselineFile.Name, original, baselineFileContent.LongLength,
                    baselineFile.LastWriteTimeUtc);
                CryptographicOperations.ZeroMemory(baselineFileContent);
            }
        }
    }
    private static void TestBackupRetentionPolicy(string username, string backupDirectory, string passphrase)
    {
        string? previousFolder = AutoBackupService.GetFolder(username);
        int previousCount = AutoBackupService.GetRetentionCount(username);
        int previousDays = AutoBackupService.GetRetentionDays(username);
        int previousLimit = AutoBackupService.GetStorageLimitMb(username);
        int previousDaily = AutoBackupService.GetDailyRetention(username);
        int previousWeekly = AutoBackupService.GetWeeklyRetention(username);
        int previousMonthly = AutoBackupService.GetMonthlyRetention(username);
        string testFolder = Path.Combine(backupDirectory, "retention-fixtures");
        Directory.CreateDirectory(testFolder);
        string sourcePackage = Path.Combine(backupDirectory, "full-migration-test.tqltk");
        string prefix = "complete_auto_" + BackupCrypto.UserTag(username) + "_";
        var createdPaths = new List<string>();
        try
        {
            string? error = AutoBackupService.TrySetFolder(username, testFolder);
            if (error != null) throw new IOException("Could not isolate retention fixtures: " + error);
            AutoBackupService.SetRetentionCount(username, 14);
            AutoBackupService.SetRetentionDays(username, 30);
            AutoBackupService.SetStorageLimitMb(username, 512);
            AutoBackupService.SetCalendarRetention(username, 0, 0, 0);
            for (int index = 0; index < 4; index++)
            {
                string path = Path.Combine(testFolder, prefix + DateTime.UtcNow.AddDays(-index).ToString("yyyyMMdd_HHmmss") + "_" + index + CompleteBackupService.FileExtension);
                File.Copy(sourcePackage, path);
                File.SetLastWriteTimeUtc(path, index == 3 ? DateTime.UtcNow : DateTime.UtcNow.AddDays(-100 + (index * 20)));
                createdPaths.Add(path);
            }
            string pinnedOld = createdPaths[0];
            DateTime pinnedOldTimestamp = File.GetLastWriteTimeUtc(pinnedOld);
            var pin = AutoBackupService.SetPinned(pinnedOld, passphrase, pinned: true);
            if (!pin.ok || !AutoBackupService.IsPinned(pinnedOld))
                throw new InvalidDataException("A valid backup could not be pinned after verification: " + pin.message);
            byte[] validBackup = File.ReadAllBytes(pinnedOld);
            try
            {
                validBackup[validBackup.Length / 2] ^= 0x01;
                File.WriteAllBytes(pinnedOld, validBackup);
                if (AutoBackupService.IsPinned(pinnedOld))
                    throw new InvalidDataException("A modified backup remained marked as verified and pinned.");
                File.Copy(sourcePackage, pinnedOld, overwrite: true);
                File.SetLastWriteTimeUtc(pinnedOld, pinnedOldTimestamp);
                if (!AutoBackupService.IsPinned(pinnedOld))
                    throw new InvalidDataException("The verified backup did not regain its pin after the original content was restored.");
            }
            finally { CryptographicOperations.ZeroMemory(validBackup); }
            AutoBackupService.PruneCompleteBackups(username);
            if (!File.Exists(pinnedOld) || !File.Exists(createdPaths[3]) || File.Exists(createdPaths[1]) || File.Exists(createdPaths[2]))
                throw new InvalidDataException("Age-based retention did not keep the pinned/newest backup and remove expired unpinned copies.");
            AutoBackupService.SetRetentionCount(username, 3);
            AutoBackupService.SetRetentionDays(username, 365);
            for (int index = 4; index < 7; index++)
            {
                string path = Path.Combine(testFolder, prefix + DateTime.UtcNow.AddMinutes(index).ToString("yyyyMMdd_HHmmss") + "_" + index + CompleteBackupService.FileExtension);
                File.Copy(sourcePackage, path);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-index));
                createdPaths.Add(path);
            }
            AutoBackupService.PruneCompleteBackups(username);
            if (AutoBackupService.ListCompleteBackups(username).Count != 3 || !File.Exists(pinnedOld))
                throw new InvalidDataException("Count-based retention did not keep the configured number of backups and the pinned one.");
            var secondPin = AutoBackupService.SetPinned(createdPaths[3], passphrase, pinned: true);
            if (!secondPin.ok) throw new InvalidDataException("A second valid backup could not be pinned: " + secondPin.message);
            for (int index = 7; index <= 8; index++)
            {
                string path = Path.Combine(testFolder, prefix + DateTime.UtcNow.AddSeconds(index).ToString("yyyyMMdd_HHmmss") + "_" + index + CompleteBackupService.FileExtension);
                File.Copy(sourcePackage, path);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(index));
                var overflowPin = AutoBackupService.SetPinned(path, passphrase, pinned: true);
                if (!overflowPin.ok) throw new InvalidDataException("An overflow backup could not be verified and pinned: " + overflowPin.message);
            }
            AutoBackupService.RetentionResult overflow = AutoBackupService.PruneCompleteBackups(username);
            string retentionStatus = AutoBackupService.GetPeriodicStatusLine(username);
            if (!overflow.LimitReached || string.IsNullOrWhiteSpace(overflow.Message) ||
                !retentionStatus.Contains("warning", StringComparison.OrdinalIgnoreCase) &&
                !retentionStatus.Contains("cảnh báo", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The retention policy did not report when verified pinned backups exceed configured limits.");
            string calendarFolder = Path.Combine(backupDirectory, "calendar-retention-fixtures");
            Directory.CreateDirectory(calendarFolder);
            if (AutoBackupService.TrySetFolder(username, calendarFolder) != null)
                throw new IOException("Could not set the calendar retention test folder.");
            AutoBackupService.SetCalendarRetention(username, 0, 0, 3);
            AutoBackupService.SetRetentionDays(username, 30);
            DateTime currentMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1, 12, 0, 0, DateTimeKind.Local);
            var monthlyPaths = new List<string>();
            for (int index = 0; index < 4; index++)
            {
                string path = Path.Combine(calendarFolder, prefix + "month_" + index + CompleteBackupService.FileExtension);
                File.Copy(sourcePackage, path);
                File.SetLastWriteTimeUtc(path, currentMonth.AddMonths(-index).ToUniversalTime());
                monthlyPaths.Add(path);
            }
            AutoBackupService.RetentionResult calendarResult = AutoBackupService.PruneCompleteBackups(username);
            if (calendarResult.LimitReached || monthlyPaths.Take(3).Any(path => !File.Exists(path)) || File.Exists(monthlyPaths[3]) ||
                AutoBackupService.GetMonthlyRetention(username) != 3)
                throw new InvalidDataException("Calendar retention did not keep the selected older months beyond the normal age cutoff.");
        }
        finally
        {
            AutoBackupService.TrySetFolder(username, previousFolder);
            AutoBackupService.SetRetentionCount(username, previousCount);
            AutoBackupService.SetRetentionDays(username, previousDays);
            AutoBackupService.SetStorageLimitMb(username, previousLimit);
            AutoBackupService.SetCalendarRetention(username, previousDaily, previousWeekly, previousMonthly);
        }
    }
    private static void TestCalendarRetention()
    {
        static DateTime Utc(int year, int month, int day, int hour = 12) =>
            new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Local).ToUniversalTime();
        (string Path, DateTime CreatedUtc)[] copies =
        [
            ("morning", Utc(2026, 11, 8, 9)), ("newest", Utc(2026, 11, 8, 15)),
            ("yesterday", Utc(2026, 11, 7)), ("same-week", Utc(2026, 11, 3)),
            ("last-week", Utc(2026, 10, 28)), ("same-month", Utc(2026, 10, 5)),
            ("expired-month", Utc(2026, 9, 23)), ("future", Utc(2026, 11, 10))
        ];
        HashSet<string> selected = AutoBackupService.SelectCalendarRetention(copies, Utc(2026, 11, 8, 18), 2, 2, 2);
        if (!selected.SetEquals(new[] { "newest", "yesterday", "last-week" }))
            throw new InvalidDataException("Calendar selection did not choose the newest distinct copy in each local calendar period.");
        var yearBoundary = AutoBackupService.SelectCalendarRetention(
            new[] { ("december", Utc(2025, 12, 31)), ("january", Utc(2026, 1, 1)) }, Utc(2026, 1, 2), 0, 1, 2);
        if (!yearBoundary.SetEquals(new[] { "december", "january" }) ||
            AutoBackupService.SelectCalendarRetention(copies, Utc(2026, 11, 8), 0, 0, 0).Count != 0)
            throw new InvalidDataException("Calendar retention failed at the year boundary or when disabled.");
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
    private static void TestWindowLayoutScaling()
    {
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var workArea = new System.Windows.Rect(0, 0, 1920 / scale, 1080 / scale);
            WindowLayoutService.WindowFit fit = WindowLayoutService.CalculateFit(
                width: 1100, height: 820, minWidth: 680, minHeight: 650,
                maxWidth: double.PositiveInfinity, maxHeight: double.PositiveInfinity,
                left: -300, top: 5000, workArea);
            if (fit.Width > workArea.Width * 0.96 + 0.01 || fit.Height > workArea.Height * 0.94 + 0.01 ||
                fit.MinWidth > fit.Width || fit.MinHeight > fit.Height || fit.Left < workArea.Left ||
                fit.Top < workArea.Top || fit.Left + fit.Width > workArea.Right + 0.01 ||
                fit.Top + fit.Height > workArea.Bottom + 0.01)
                throw new InvalidDataException($"Window fit exceeded the available work area at {scale:P0} scaling.");
        }
        var compactWorkArea = new System.Windows.Rect(0, 0, 640, 360);
        WindowLayoutService.WindowFit compactFit = WindowLayoutService.CalculateFit(
            width: 1100, height: 820, minWidth: 680, minHeight: 650,
            maxWidth: double.PositiveInfinity, maxHeight: double.PositiveInfinity,
            left: 0, top: 0, compactWorkArea);
        if (compactFit.Width >= 680 || compactFit.Height >= 360 ||
            compactFit.MinWidth > compactFit.Width || compactFit.MinHeight > compactFit.Height)
            throw new InvalidDataException("Window minimum sizes were not reduced on a compact high-DPI work area.");
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
        var signingCheck = RunPowerShell(releaseScript, "-CertificateThumbprint", "");
        if (signingCheck.ExitCode == 0 || !signingCheck.Output.Contains("code-signing certificate is required", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Signed release packaging accepted a missing certificate.");
        var result = RunPowerShell(releaseScript, "-Version", version);
        if (result.ExitCode == 0 || Directory.Exists(expectedOutput) ||
            !result.Output.Contains("Release version must match", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Release packaging accepted a version that differs from the project.");
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
        if (!string.IsNullOrWhiteSpace(TestFilter) && !name.Contains(TestFilter, StringComparison.OrdinalIgnoreCase)) return;
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
        return _failed == 0 && _passed > 0 ? 0 : 1;
    }
}
