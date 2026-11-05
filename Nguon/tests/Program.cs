using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
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

        Directory.CreateDirectory(tempRoot);
        Environment.SetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT", tempRoot);
        try
        {
            if (!string.Equals(ApplicationDataPath.Root, tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The app assembly is not using the isolated temporary test-data root.");
            Run("Argon2id, FIPS PBKDF2, legacy hash verification", TestPasswordHashes);
            Run("English translations for security features", TestEnglishSecurityTranslations);
            Run("Vault search and combined filters", TestAccountSearch);
            Run("Per-website password rules and generation", TestWebsitePasswordPolicy);
            Run("JSON and KeePass XML import safety", () => TestCredentialImport(tempRoot));
            Run("Configurable keyboard shortcut validation and persistence", TestKeyboardShortcuts);
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
        if (loaded != policy || WebsitePasswordPolicyService.Get("http://user:password@example.test").Enabled)
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
            ("Cảnh báo bảo mật", "Security warnings"),
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
