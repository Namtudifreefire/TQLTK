using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{

    public class UsersFileUnreadableException : Exception
    {
        public UsersFileUnreadableException(string message, Exception? inner = null)
            : base(message, inner) { }
    }

    public class UserService
    {
        private static readonly object Sync = new();
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
        private static readonly byte[] UsersEntropy = Encoding.UTF8.GetBytes("TQLTK.Users.v1");
        private const string ProtectedUsersPrefix = "TQLTK.USERS.v1:";

        private readonly string _usersFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "users.json");

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

        public const int MaxFailedAttempts = 5;

        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

        private List<User> _users = new();

        public UserService()
        {
            lock (Sync)
            {
                Reload();
            }
        }

        private void Reload()
        {
            lock (Sync)
            {
                _users = ReadUsers(out bool needsProtection);
                if (needsProtection)
                    Save();
            }
        }

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

                needsProtection = true;
                return bak;
            }

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

                        WriteProtectedUsersFile(_usersFile + ".bak", previous);
                    }
                    else
                    {

                        File.Copy(_usersFile,
                            _usersFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"),
                            overwrite: true);

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
                return _users.Select(user => new User
                {
                    Id = user.Id,
                    Username = user.Username,
                    Role = user.Role,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt,
                    IsActive = user.IsActive,
                    FullName = user.FullName,
                    Email = user.Email,
                    Phone = user.Phone
                }).ToList().AsReadOnly();
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

        internal bool HasLegacyStoragePathCollision(
            Guid userId, string username, LegacyUserStorageKind storageKind)
        {
            lock (Sync)
            {
                Reload();
                var user = FindByIdCore(userId) ?? new User
                {
                    Id = userId,
                    Username = username
                };
                return UserStorageIdentity.HasLegacyPathCollision(user, _users, storageKind);
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

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return true;
            return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }

        private static bool IsValidUsername(string username)
            => !string.IsNullOrWhiteSpace(username) &&
                username == username.Trim() &&
                username.Length is >= 3 and <= 64 &&
                !username.Any(char.IsControl);

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

        private const string LegacyStorageMessage =
            "Tên đăng nhập này còn dữ liệu cũ chưa thể gắn an toàn với tài khoản. " +
            "Dữ liệu cũ được giữ nguyên; hãy dùng tên khác hoặc khôi phục dữ liệu sau khi xác định đúng chủ sở hữu.";

        private IDisposable AcquireCrossProcessAuthenticationLock()
        {
            string identity = Path.GetFullPath(_usersFile)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
            byte[] identityHash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            var mutex = new Mutex(
                initiallyOwned: false,
                name: @"Local\TQLTK.UserAuthentication." + Convert.ToHexString(identityHash));
            CryptographicOperations.ZeroMemory(identityHash);

            try
            {
                bool acquired = false;
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
                catch (AbandonedMutexException) { acquired = true; }

                if (!acquired)
                    throw new TimeoutException("Hết thời gian chờ khóa dữ liệu đăng nhập.");

                return new AuthenticationMutexLease(mutex);
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
        }

        private sealed class AuthenticationMutexLease : IDisposable
        {
            private readonly Mutex _mutex;
            private bool _disposed;

            public AuthenticationMutexLease(Mutex mutex) => _mutex = mutex;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { _mutex.ReleaseMutex(); }
                finally { _mutex.Dispose(); }
            }
        }

        public (bool ok, string message) CreateUser(
            string username, string password, UserRole role)
        {
            return CreateUserFull(username, password, role, username, "", "");
        }

        public (bool ok, string message) CreateInitialAdmin(
            string username, string password, string fullName, string email, string phone)
        {
            lock (Sync)
            {
                Reload();
                if (_users.Count != 0)
                    return (false, "Tài khoản đầu tiên đã được tạo. Hãy đăng ký tài khoản người dùng thông thường.");

                if (!IsValidUsername(username))
                    return (false, "Tên đăng nhập phải dài từ 3 đến 64 ký tự và không chứa ký tự điều khiển.");
                if (!IsValidPassword(password, twoFactorEnabled: false))
                    return (false, PasswordLengthMessage(twoFactorEnabled: false));
                if (IsDefaultPassword(password))
                    return (false, DefaultPasswordMessage);
                if (UserStorageIdentity.HasLegacyDataForUsername(
                    Path.GetDirectoryName(_usersFile)!, username))
                    return (false, LegacyStorageMessage);
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
                    return (false, "Tên đăng nhập phải dài từ 3 đến 64 ký tự và không chứa ký tự điều khiển.");

                if (!IsValidPassword(password, twoFactorEnabled: false))
                    return (false, PasswordLengthMessage(twoFactorEnabled: false));

                if (IsDefaultPassword(password))
                    return (false, DefaultPasswordMessage);

                if (string.IsNullOrWhiteSpace(fullName))
                    return (false, "Họ tên không được để trống.");

                if (FindByUsernameCore(username) != null)
                    return (false, "Tên đăng nhập đã tồn tại.");

                if (UserStorageIdentity.HasLegacyDataForUsername(
                    Path.GetDirectoryName(_usersFile)!, username))
                    return (false, LegacyStorageMessage);

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

        public (bool ok, string message, User? user) Authenticate(
            string username, string password)
        {
            lock (Sync)
            {
                IDisposable processLock;
                try { processLock = AcquireCrossProcessAuthenticationLock(); }
                catch (Exception ex) when (
                    ex is TimeoutException ||
                    ex is UnauthorizedAccessException ||
                    ex is IOException ||
                    ex is WaitHandleCannotBeOpenedException)
                {
                    return (false,
                        "Không thể khóa dữ liệu đăng nhập giữa các tiến trình; yêu cầu đăng nhập bị từ chối để bảo vệ tài khoản.",
                        null);
                }

                using var processLockScope = processLock;
                Reload();

                var user = FindByUsernameCore(username);
                if (user == null)
                    return (false, "Tài khoản không tồn tại.", null);

                if (!user.IsActive)
                    return (false, "Tài khoản đã bị khóa.", null);

                if (user.LockedUntil is { } until)
                {
                    if (until > DateTime.Now)
                    {
                        int mins = Math.Max(1, (int)Math.Ceiling((until - DateTime.Now).TotalMinutes));
                        return (false,
                            $"Nhập sai mật khẩu quá nhiều lần. Tài khoản bị khóa tạm thời, hãy thử lại sau khoảng {mins} phút.",
                            null);
                    }

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

                if (IsDefaultPassword(password))
                    return (false,
                        "Mật khẩu mặc định cũ đã bị vô hiệu hóa. Hãy dùng Windows Hello nếu đã bật " +
                        "hoặc nhờ một quản trị viên khác đặt lại mật khẩu.",
                        null);

                if (UserStorageIdentity.HasAmbiguousLegacyData(
                    user, _users, Path.GetDirectoryName(_usersFile)!))
                    return (false,
                        "Dữ liệu cũ của tài khoản trùng đường dẫn với tài khoản khác nên đăng nhập bị từ chối. " +
                        "Các file vẫn được giữ nguyên để có thể khôi phục an toàn.",
                        null);

                user.FailedLoginCount = 0;
                user.LockedUntil = null;

                if (PasswordHasher.NeedsRehash(user.PasswordHash))
                    user.PasswordHash = PasswordHasher.Hash(password);

                if (password.Length < MinimumPasswordLength(user.TwoFactorEnabled))
                    user.MustChangePassword = true;

                user.LastLoginAt = DateTime.Now;
                Save();
                return (true, "Đăng nhập thành công.", user);
            }
        }

        public (bool ok, string message) ChangePassword(
            string username, string oldPwd, string newPwd)
        {
            lock (Sync)
            {
                Reload();

                var user = FindByUsernameCore(username);
                if (user == null)
                    return (false, "Không tìm thấy user.");

                if (!IsCurrentActiveUser(user.Id, out string denied))
                    return (false, denied);

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
