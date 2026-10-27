using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Ném ra khi users.json tồn tại nhưng không đọc được và cũng không có bản .bak dùng được.
    /// App không tự tạo admin mặc định trong trường hợp này, để không ghi đè dữ liệu cũ.
    /// </summary>
    public class UsersFileUnreadableException : Exception
    {
        public UsersFileUnreadableException(string message, Exception? inner = null)
            : base(message, inner) { }
    }

    /// <summary>
    /// Quản lý tài khoản người dùng.
    /// Dữ liệu lưu tại %AppData%\PersonalInfoManager\users.json, được bảo vệ bằng Windows DPAPI CurrentUser.
    /// File JSON cũ được đọc tương thích và tự mã hóa lại khi nạp.
    ///
    /// Mỗi lần gọi, danh sách được ĐỌC LẠI từ file (nhiều cửa sổ cùng tạo UserService riêng,
    /// nếu giữ danh sách cũ trong bộ nhớ thì lần Save sau sẽ ghi đè mất thay đổi của cửa sổ khác).
    /// Ghi file qua file tạm, có bản .bak của lần ghi trước.
    /// </summary>
    public class UserService
    {
        private static readonly object Sync = new();
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
        private static readonly byte[] UsersEntropy = Encoding.UTF8.GetBytes("TQLTK.Users.v1");
        private const string ProtectedUsersPrefix = "TQLTK.USERS.v1:";

        private readonly string _usersFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "users.json");

        // Chỉ giữ để nhận diện tài khoản cũ đã được tạo bằng thông tin mặc định.
        private const string LegacyDefaultAdminPassword = "admin123";

        private bool RequireAdmin(out string message)
        {
            var current = SessionService.CurrentUser;
            if (current != null)
            {
                lock (Sync)
                {
                    Reload();
                    var fresh = FindByIdCore(current.Id);
                    if (fresh is { IsActive: true, Role: UserRole.Admin })
                    {
                        message = "";
                        return true;
                    }
                }
            }

            message = "Từ chối thao tác: chỉ quản trị viên đang hoạt động mới được phép thực hiện.";
            return false;
        }

        private bool CanManageProfile(Guid targetUserId, out string message)
        {
            var current = SessionService.CurrentUser;
            if (current != null)
            {
                lock (Sync)
                {
                    Reload();
                    var fresh = FindByIdCore(current.Id);
                    if (fresh is { IsActive: true } &&
                        (fresh.Id == targetUserId || fresh.Role == UserRole.Admin))
                    {
                        message = "";
                        return true;
                    }
                }
            }

            message = "Từ chối thao tác: chỉ được sửa hồ sơ của chính mình hoặc hồ sơ khác khi là quản trị viên đang hoạt động.";
            return false;
        }

        private bool IsCurrentActiveUser(Guid userId, out string message)
        {
            var current = SessionService.CurrentUser;
            if (current != null && current.Id == userId)
            {
                lock (Sync)
                {
                    Reload();
                    if (FindByIdCore(userId) is { IsActive: true })
                    {
                        message = "";
                        return true;
                    }
                }
            }

            message = "Từ chối thao tác: chỉ người dùng đang hoạt động mới được thay đổi thiết lập đăng nhập của chính mình.";
            return false;
        }

        /// <summary>Sai mật khẩu bấy nhiêu lần liên tiếp thì bị khóa tạm.</summary>
        public const int MaxFailedAttempts = 5;

        /// <summary>Thời gian khóa tạm.</summary>
        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

        private List<User> _users = new();

        public UserService()
        {
            lock (Sync)
            {
                Reload();
            }
        }

        // ================================================================
        // ================== NẠP / LƯU FILE ==================
        // ================================================================

        private void Reload()
        {
            lock (Sync)
            {
                _users = ReadUsers(out bool needsProtection);
                if (needsProtection)
                    Save();
            }
        }

        /// <summary>
        /// Đọc file chính; hỏng thì thử .bak; cả hai đều hỏng thì ném lỗi (không trả danh sách rỗng).
        /// File chưa tồn tại → danh sách rỗng (lần chạy đầu).
        /// Lỗi I/O (file đang bị khóa...) được ném ra ngoài, không coi là file hỏng.
        /// </summary>
        private List<User> ReadUsers(out bool needsProtection)
        {
            needsProtection = false;
            if (!File.Exists(_usersFile)) return new List<User>();

            var main = TryParse(_usersFile, out Exception? mainError, out bool mainNeedsProtection);
            if (main != null)
            {
                needsProtection = mainNeedsProtection;
                return main;
            }

            var bak = TryParse(_usersFile + ".bak", out _);
            if (bak != null)
            {
                // Rebuild the main file from the validated backup using the protected format.
                needsProtection = true;
                return bak;
            }

            // Giữ lại bản hỏng để có thể cứu bằng tay
            try
            {
                File.Copy(_usersFile,
                    _usersFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
                    overwrite: true);
            }
            catch { }

            throw new UsersFileUnreadableException(
                "Không đọc được danh sách tài khoản (users.json) và cũng không có bản dự phòng dùng được.\n\n" +
                "Ứng dụng KHÔNG tự tạo lại tài khoản admin mặc định để tránh ghi đè dữ liệu cũ.\n" +
                "Hãy đóng ứng dụng, mở thư mục %AppData%\\PersonalInfoManager rồi khôi phục users.json " +
                "từ users.json.bak nếu có. File mới được mã hóa theo tài khoản Windows hiện tại; " +
                "không chỉnh sửa thủ công nội dung file đã mã hóa.\n\n" +
                (mainError != null ? "Chi tiết: " + mainError.Message : ""),
                mainError);
        }

        private static List<User>? TryParse(string file, out Exception? error)
            => TryParse(file, out error, out _);

        private static List<User>? TryParse(
            string file, out Exception? error, out bool needsProtection)
        {
            error = null;
            needsProtection = false;
            if (!File.Exists(file)) return null;

            string stored = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(stored))
            {
                error = new InvalidDataException("File trống.");
                return null;
            }

            try
            {
                string json;
                if (stored.StartsWith(ProtectedUsersPrefix, StringComparison.Ordinal))
                {
                    byte[] encrypted = Convert.FromBase64String(stored.Substring(ProtectedUsersPrefix.Length));
                    byte[] plaintext = ProtectedData.Unprotect(
                        encrypted, UsersEntropy, DataProtectionScope.CurrentUser);
                    try { json = Encoding.UTF8.GetString(plaintext); }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(encrypted);
                        CryptographicOperations.ZeroMemory(plaintext);
                    }
                }
                else
                {
                    // Existing installations store plain JSON. Load it once, then migrate on Reload.
                    json = stored;
                    needsProtection = true;
                }

                return JsonSerializer.Deserialize<List<User>>(json);
            }
            catch (Exception ex) when (
                ex is JsonException || ex is CryptographicException || ex is FormatException)
            {
                error = ex;
                return null;
            }
        }

        private void Save()
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_usersFile)!);

                if (File.Exists(_usersFile))
                {
                    var previous = TryParse(_usersFile, out _);
                    if (previous != null)
                    {
                        // Keep a protected copy of the previous valid contents, including during migration.
                        WriteProtectedUsersFile(_usersFile + ".bak", previous);
                    }
                    else
                    {
                        // File hiện tại hỏng → giữ riêng một bản, không đè lên .bak tốt
                        File.Copy(_usersFile,
                            _usersFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"),
                            overwrite: true);
                        // The in-memory list came from the validated backup or a deliberate recovery.
                        WriteProtectedUsersFile(_usersFile + ".bak", _users);
                    }
                }

                WriteProtectedUsersFile(_usersFile, _users);
            }
        }

        private static void WriteProtectedUsersFile(string path, List<User> users)
        {
            string json = JsonSerializer.Serialize(users, JsonOpts);
            byte[] plaintext = Encoding.UTF8.GetBytes(json);
            byte[]? encrypted = null;
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                encrypted = ProtectedData.Protect(
                    plaintext, UsersEntropy, DataProtectionScope.CurrentUser);
                string content = ProtectedUsersPrefix + Convert.ToBase64String(encrypted);
                File.WriteAllText(temp, content, Encoding.UTF8);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                if (encrypted != null) CryptographicOperations.ZeroMemory(encrypted);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        // ================================================================
        // ================== TRUY VẤN ==================
        // ================================================================

        private User? FindByUsernameCore(string username)
            => _users.FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        private User? FindByIdCore(Guid id) => _users.FirstOrDefault(u => u.Id == id);

        private bool EmailExistsCore(string email, Guid? excludeUserId = null)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            return _users.Any(u =>
                u.Id != excludeUserId &&
                !string.IsNullOrEmpty(u.Email) &&
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<User> GetAll()
        {
            lock (Sync)
            {
                if (!RequireAdmin(out _)) return Array.Empty<User>();
                Reload();
                return _users.ToList().AsReadOnly();
            }
        }

        public User? FindByUsername(string username)
        {
            lock (Sync)
            {
                Reload();
                return FindByUsernameCore(username);
            }
        }

        public User? FindById(Guid id)
        {
            lock (Sync)
            {
                Reload();
                return FindByIdCore(id);
            }
        }

        public bool UsernameExists(string username) => FindByUsername(username) != null;

        public bool HasAnyUsers()
        {
            lock (Sync)
            {
                Reload();
                return _users.Count > 0;
            }
        }

        internal IReadOnlyList<string> GetUsernamesForSecurityMigration()
        {
            lock (Sync)
            {
                Reload();
                return _users.Select(user => user.Username).ToList().AsReadOnly();
            }
        }

        public bool EmailExists(string email, Guid? excludeUserId = null)
        {
            lock (Sync)
            {
                Reload();
                return EmailExistsCore(email, excludeUserId);
            }
        }

        // ================================================================
        // ================== VALIDATE ==================
        // ================================================================

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return true;
            return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }

        private static bool IsValidUsername(string username)
            => !string.IsNullOrWhiteSpace(username) && username.Trim().Length >= 3;

        private static int MinimumPasswordLength(bool twoFactorEnabled)
            => twoFactorEnabled ? 8 : 15;

        private static bool IsValidPassword(string password, bool twoFactorEnabled)
            => !string.IsNullOrWhiteSpace(password) &&
                password.Length >= MinimumPasswordLength(twoFactorEnabled);

        private static string PasswordLengthMessage(bool twoFactorEnabled)
            => $"Mật khẩu phải từ {MinimumPasswordLength(twoFactorEnabled)} ký tự trở lên.";

        public int GetMinimumPasswordLength(string username)
        {
            var user = FindByUsername(username);
            return MinimumPasswordLength(user?.TwoFactorEnabled == true);
        }

        private static bool IsDefaultPassword(string password)
            => password == LegacyDefaultAdminPassword;

        private const string DefaultPasswordMessage =
            "Không được dùng mật khẩu mặc định. Hãy chọn mật khẩu khác.";

        // ================================================================
        // ================== TẠO TÀI KHOẢN ==================
        // ================================================================

        public (bool ok, string message) CreateUser(
            string username, string password, UserRole role)
        {
            return CreateUserFull(username, password, role, username, "", "");
        }

        /// <summary>
        /// Creates the first account as administrator, using credentials chosen by the user.
        /// The empty user list check and save are protected by the same process-wide lock.
        /// </summary>
        public (bool ok, string message) CreateInitialAdmin(
            string username, string password, string fullName, string email, string phone)
        {
            lock (Sync)
            {
                Reload();
                if (_users.Count != 0)
                    return (false, "Tài khoản đầu tiên đã được tạo. Hãy đăng ký tài khoản người dùng thông thường.");

                if (!IsValidUsername(username))
                    return (false, "Tên đăng nhập phải từ 3 ký tự trở lên.");
                if (!IsValidPassword(password, twoFactorEnabled: false))
                    return (false, PasswordLengthMessage(twoFactorEnabled: false));
                if (IsDefaultPassword(password))
                    return (false, DefaultPasswordMessage);
                if (string.IsNullOrWhiteSpace(fullName))
                    return (false, "Họ tên không được để trống.");
                if (!IsValidEmail(email))
                    return (false, "Email không hợp lệ.");

                var admin = new User
                {
                    Username = username.Trim(),
                    PasswordHash = PasswordHasher.Hash(password),
                    Role = UserRole.Admin,
                    FullName = fullName.Trim(),
                    Email = email.Trim(),
                    Phone = phone.Trim()
                };

                _users.Add(admin);
                try
                {
                    Save();
                    return (true, "Đã tạo tài khoản quản trị viên đầu tiên.");
                }
                catch (Exception ex)
                {
                    _users.Remove(admin);
                    return (false, $"Không lưu được tài khoản quản trị viên: {ex.Message}");
                }
            }
        }

        public (bool ok, string message) CreateUserFull(
            string username, string password, UserRole role,
            string fullName, string email, string phone)
        {
            lock (Sync)
            {
                if (!RequireAdmin(out string denied)) return (false, denied);
                Reload();

                if (!IsValidUsername(username))
                    return (false, "Tên đăng nhập phải từ 3 ký tự trở lên.");

                if (!IsValidPassword(password, twoFactorEnabled: false))
                    return (false, PasswordLengthMessage(twoFactorEnabled: false));

                if (IsDefaultPassword(password))
                    return (false, DefaultPasswordMessage);

                if (string.IsNullOrWhiteSpace(fullName))
                    return (false, "Họ tên không được để trống.");

                if (FindByUsernameCore(username) != null)
                    return (false, "Tên đăng nhập đã tồn tại.");

                if (!IsValidEmail(email))
                    return (false, "Email không hợp lệ.");

                if (EmailExistsCore(email))
                    return (false, "Email đã được sử dụng.");

                _users.Add(new User
                {
                    Username = username.Trim(),
                    PasswordHash = PasswordHasher.Hash(password),
                    Role = role,
                    FullName = fullName.Trim(),
                    Email = email.Trim(),
                    Phone = phone.Trim()
                });
                Save();
                return (true, "Tạo tài khoản thành công.");
            }
        }

        // ================================================================
        // ================== ĐĂNG NHẬP ==================
        // ================================================================

        /// <summary>
        /// Kiểm tra tên đăng nhập + mật khẩu.
        /// Sai MaxFailedAttempts lần liên tiếp thì khóa tạm LockoutDuration (lưu trong users.json,
        /// nên tắt mở lại app không xóa được). Đăng nhập đúng thì về 0.
        /// Nếu mật khẩu đúng nhưng là mật khẩu mặc định cũ thì đánh dấu phải đổi.
        /// </summary>
        public (bool ok, string message, User? user) Authenticate(
            string username, string password)
        {
            lock (Sync)
            {
                Reload();

                var user = FindByUsernameCore(username);
                if (user == null)
                    return (false, "Tài khoản không tồn tại.", null);

                if (!user.IsActive)
                    return (false, "Tài khoản đã bị khóa.", null);

                // Còn trong thời gian khóa tạm
                if (user.LockedUntil is { } until)
                {
                    if (until > DateTime.Now)
                    {
                        int mins = Math.Max(1, (int)Math.Ceiling((until - DateTime.Now).TotalMinutes));
                        return (false,
                            $"Nhập sai mật khẩu quá nhiều lần. Tài khoản bị khóa tạm thời, hãy thử lại sau khoảng {mins} phút.",
                            null);
                    }

                    // Hết hạn khóa → cho thử lại từ đầu
                    user.LockedUntil = null;
                    user.FailedLoginCount = 0;
                }

                if (!PasswordHasher.Verify(password, user.PasswordHash))
                {
                    user.FailedLoginCount++;

                    if (user.FailedLoginCount >= MaxFailedAttempts)
                    {
                        user.LockedUntil = DateTime.Now + LockoutDuration;
                        user.FailedLoginCount = 0;
                        Save();
                        return (false,
                            $"Sai mật khẩu {MaxFailedAttempts} lần liên tiếp. Tài khoản bị khóa tạm thời {LockoutDuration.TotalMinutes:0} phút.",
                            null);
                    }

                    Save();
                    int left = MaxFailedAttempts - user.FailedLoginCount;
                    return (false,
                        $"Sai mật khẩu. Còn {left} lần thử trước khi bị khóa tạm thời.",
                        null);
                }

                user.FailedLoginCount = 0;
                user.LockedUntil = null;

                // Tài khoản từ bản cũ dùng mật khẩu mặc định → bắt buộc đổi.
                if (IsDefaultPassword(password))
                    user.MustChangePassword = true;

                // Upgrade legacy hashes at the next successful sign-in without invalidating them.
                if (PasswordHasher.NeedsRehash(user.PasswordHash))
                    user.PasswordHash = PasswordHasher.Hash(password);

                // Existing short passwords remain usable once, then must meet the current policy.
                if (password.Length < MinimumPasswordLength(user.TwoFactorEnabled))
                    user.MustChangePassword = true;

                user.LastLoginAt = DateTime.Now;
                Save();
                return (true, "Đăng nhập thành công.", user);
            }
        }

        // ================================================================
        // ================== ĐỔI MẬT KHẨU ==================
        // ================================================================

        public (bool ok, string message) ChangePassword(
            string username, string oldPwd, string newPwd)
        {
            lock (Sync)
            {
                Reload();

                var user = FindByUsernameCore(username);
                if (user == null)
                    return (false, "Không tìm thấy user.");

                if (!PasswordHasher.Verify(oldPwd, user.PasswordHash))
                    return (false, "Mật khẩu cũ không đúng.");

                if (!IsValidPassword(newPwd, user.TwoFactorEnabled))
                    return (false, PasswordLengthMessage(user.TwoFactorEnabled));

                if (IsDefaultPassword(newPwd))
                    return (false, DefaultPasswordMessage);

                if (oldPwd == newPwd)
                    return (false, "Mật khẩu mới phải khác mật khẩu cũ.");

                user.PasswordHash = PasswordHasher.Hash(newPwd);
                user.MustChangePassword = false;
                user.FailedLoginCount = 0;
                user.LockedUntil = null;
                Save();
                return (true, "Đổi mật khẩu thành công.");
            }
        }

        /// <summary>
        /// Admin đặt lại mật khẩu cho user. Người dùng phải đổi lại ở lần đăng nhập tới
        /// (vì admin đã biết mật khẩu này). Muốn bỏ ràng buộc, xóa dòng MustChangePassword = true.
        /// </summary>
        public (bool ok, string message) ResetPassword(Guid userId, string newPassword)
        {
            lock (Sync)
            {
                if (!RequireAdmin(out string denied)) return (false, denied);
                Reload();

                var user = FindByIdCore(userId);
                if (user == null)
                    return (false, "Không tìm thấy user.");

                if (!IsValidPassword(newPassword, user.TwoFactorEnabled))
                    return (false, PasswordLengthMessage(user.TwoFactorEnabled));

                if (IsDefaultPassword(newPassword))
                    return (false, DefaultPasswordMessage);

                user.PasswordHash = PasswordHasher.Hash(newPassword);
                user.MustChangePassword = true;
                user.FailedLoginCount = 0;
                user.LockedUntil = null;
                Save();
                return (true, "Đã reset mật khẩu. Người dùng sẽ phải đổi mật khẩu khi đăng nhập lần tới.");
            }
        }

        // ================================================================
        // ================== CẬP NHẬT PROFILE ==================
        // ================================================================

        public (bool ok, string message) UpdateProfile(
            Guid userId, string fullName, string email, string phone)
        {
            lock (Sync)
            {
                if (!CanManageProfile(userId, out string denied)) return (false, denied);
                Reload();

                var user = FindByIdCore(userId);
                if (user == null)
                    return (false, "Không tìm thấy user.");

                if (string.IsNullOrWhiteSpace(fullName))
                    return (false, "Họ tên không được để trống.");

                if (!IsValidEmail(email))
                    return (false, "Email không hợp lệ.");

                if (EmailExistsCore(email, excludeUserId: userId))
                    return (false, "Email đã được sử dụng bởi user khác.");

                user.FullName = fullName.Trim();
                user.Email = email.Trim();
                user.Phone = phone.Trim();
                Save();
                return (true, "Cập nhật thành công.");
            }
        }

        public (bool ok, string message) AdminUpdateUser(
            Guid userId, string fullName, string email, string phone,
            UserRole role, bool isActive)
        {
            lock (Sync)
            {
                if (!RequireAdmin(out string denied)) return (false, denied);
                Reload();

                var user = FindByIdCore(userId);
                if (user == null)
                    return (false, "Không tìm thấy user.");

                if (string.IsNullOrWhiteSpace(fullName))
                    return (false, "Họ tên không được để trống.");

                if (!IsValidEmail(email))
                    return (false, "Email không hợp lệ.");

                if (EmailExistsCore(email, excludeUserId: userId))
                    return (false, "Email đã được sử dụng bởi user khác.");

                user.FullName = fullName.Trim();
                user.Email = email.Trim();
                user.Phone = phone.Trim();
                user.Role = role;
                user.IsActive = isActive;
                if (isActive)
                {
                    user.FailedLoginCount = 0;
                    user.LockedUntil = null;
                }
                Save();
                return (true, "Cập nhật thành công.");
            }
        }

        // ================================================================
        // ================== KHÓA / MỞ ==================
        // ================================================================

        /// <summary>Mở tài khoản (active = true) cũng xóa luôn khóa tạm do nhập sai mật khẩu.</summary>
        public void SetActive(Guid userId, bool active)
        {
            lock (Sync)
            {
                if (!RequireAdmin(out string denied)) throw new UnauthorizedAccessException(denied);
                Reload();

                var user = FindByIdCore(userId);
                if (user == null) return;

                user.IsActive = active;
                if (active)
                {
                    user.FailedLoginCount = 0;
                    user.LockedUntil = null;
                }
                Save();
            }
        }

        // ================================================================
        // ================== XÓA ==================
        // ================================================================

        /// <summary>
        /// Chỉ xóa dòng người dùng. Dữ liệu của họ (vault, file cá nhân, 2FA...) do
        /// UserDataCleaner xử lý riêng, sau khi quản trị viên đồng ý.
        /// </summary>
        public (bool ok, string message) DeleteUser(Guid userId, Guid currentUserId)
        {
            lock (Sync)
            {
                if (!RequireAdmin(out string denied)) return (false, denied);
                Reload();

                if (userId == currentUserId)
                    return (false, "Không thể xóa tài khoản đang đăng nhập.");

                var user = FindByIdCore(userId);
                if (user == null)
                    return (false, "Không tìm thấy user.");

                if (user.Role == UserRole.Admin &&
                    _users.Count(u => u.Role == UserRole.Admin) <= 1)
                    return (false, "Không thể xóa admin cuối cùng.");

                _users.Remove(user);
                Save();
                return (true, "Đã xóa tài khoản.");
            }
        }

        // ================================================================
        // ================== PHÂN QUYỀN ==================
        // ================================================================

        /// <summary>
        /// Đổi vai trò. Không thu quyền được admin cuối cùng (trả về ok = false kèm lý do,
        /// để giao diện báo cho người dùng và không ghi nhật ký "đã đổi").
        /// </summary>
        public (bool ok, string message) SetRole(Guid userId, UserRole role)
        {
            lock (Sync)
            {
                if (!RequireAdmin(out string denied)) return (false, denied);
                Reload();

                var user = FindByIdCore(userId);
                if (user == null)
                    return (false, "Không tìm thấy user.");

                if (user.Role == UserRole.Admin &&
                    role == UserRole.User &&
                    _users.Count(u => u.Role == UserRole.Admin) <= 1)
                    return (false, "Không thể thu quyền admin cuối cùng.");

                user.Role = role;
                Save();
                return (true, "Đã đổi vai trò.");
            }
        }

        // ================================================================
        // ================== THỐNG KÊ ==================
        // ================================================================

        public int CountAdmins() { lock (Sync) { Reload(); return _users.Count(u => u.Role == UserRole.Admin); } }
        public int CountUsers() { lock (Sync) { Reload(); return _users.Count(u => u.Role == UserRole.User); } }
        public int CountActive() { lock (Sync) { Reload(); return _users.Count(u => u.IsActive); } }
        public int CountInactive() { lock (Sync) { Reload(); return _users.Count(u => !u.IsActive); } }

        internal bool SetTwoFactorEnabled(string username, bool enabled)
        {
            lock (Sync)
            {
                Reload();
                var user = FindByUsernameCore(username);
                if (user == null) return false;
                if (user.TwoFactorEnabled == enabled) return true;

                bool previous = user.TwoFactorEnabled;
                user.TwoFactorEnabled = enabled;
                try
                {
                    Save();
                    return true;
                }
                catch
                {
                    user.TwoFactorEnabled = previous;
                    throw;
                }
            }
        }

        // ================================================================
        // ================== WINDOWS HELLO ==================
        // ================================================================

        /// <summary>
        /// Bật/tắt tính năng đăng nhập bằng Windows Hello cho user.
        /// </summary>
        public (bool ok, string message) SetWindowsHello(
            Guid userId, bool enabled, string sid)
        {
            lock (Sync)
            {
                if (!IsCurrentActiveUser(userId, out string denied)) return (false, denied);
                Reload();

                var user = FindByIdCore(userId);
                if (user == null)
                    return (false, "Không tìm thấy user.");

                if (enabled && string.IsNullOrWhiteSpace(sid))
                    return (false, "Không lấy được SID tài khoản Windows.");

                if (enabled && !string.Equals(
                    sid, WindowsHelloService.GetCurrentWindowsSid(), StringComparison.OrdinalIgnoreCase))
                    return (false, "Chỉ có thể liên kết Windows Hello với tài khoản Windows hiện tại.");

                user.WindowsHelloEnabled = enabled;
                user.WindowsHelloSid = enabled ? sid : "";
                Save();

                return (true, enabled
                    ? "Đã bật đăng nhập bằng Windows Hello."
                    : "Đã tắt đăng nhập bằng Windows Hello.");
            }
        }

        /// <summary>
        /// Tìm user đã bật Windows Hello và khớp SID tài khoản Windows hiện tại.
        /// </summary>
        public User? FindByWindowsHelloSid(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid)) return null;

            lock (Sync)
            {
                Reload();

                return _users.FirstOrDefault(u =>
                    u.WindowsHelloEnabled &&
                    u.IsActive &&
                    !string.IsNullOrEmpty(u.WindowsHelloSid) &&
                    string.Equals(u.WindowsHelloSid, sid,
                        StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Lấy danh sách user đã bật Windows Hello.
        /// </summary>
        public List<User> GetWindowsHelloUsers()
        {
            lock (Sync)
            {
                Reload();
                return _users.Where(u => u.WindowsHelloEnabled && u.IsActive).ToList();
            }
        }
    }
}
