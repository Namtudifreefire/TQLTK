using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Ném ra khi users.json tồn tại nhưng không đọc được và cũng không có bản .bak dùng được.
    /// App không tự tạo lại admin mặc định trong trường hợp này, để không ghi đè dữ liệu cũ.
    /// </summary>
    public class UsersFileUnreadableException : Exception
    {
        public UsersFileUnreadableException(string message, Exception? inner = null)
            : base(message, inner) { }
    }

    /// <summary>
    /// Quản lý tài khoản người dùng.
    /// Dữ liệu lưu tại: %AppData%\PersonalInfoManager\users.json
    ///
    /// Mỗi lần gọi, danh sách được ĐỌC LẠI từ file (nhiều cửa sổ cùng tạo UserService riêng,
    /// nếu giữ danh sách cũ trong bộ nhớ thì lần Save sau sẽ ghi đè mất thay đổi của cửa sổ khác).
    /// Ghi file qua file tạm, có bản .bak của lần ghi trước.
    /// </summary>
    public class UserService
    {
        private static readonly object Sync = new();
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private readonly string _usersFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "users.json");

        private const string DefaultAdminUsername = "admin";
        private const string DefaultAdminPassword = "admin123";

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
                EnsureDefaultAdmin();
            }
        }

        // ================================================================
        // ================== NẠP / LƯU FILE ==================
        // ================================================================

        private void Reload()
        {
            lock (Sync)
            {
                _users = ReadUsers();
            }
        }

        /// <summary>
        /// Đọc file chính; hỏng thì thử .bak; cả hai đều hỏng thì ném lỗi (không trả danh sách rỗng).
        /// File chưa tồn tại → danh sách rỗng (lần chạy đầu).
        /// Lỗi I/O (file đang bị khóa...) được ném ra ngoài, không coi là file hỏng.
        /// </summary>
        private List<User> ReadUsers()
        {
            if (!File.Exists(_usersFile)) return new List<User>();

            var main = TryParse(_usersFile, out Exception? mainError);
            if (main != null) return main;

            var bak = TryParse(_usersFile + ".bak", out _);
            if (bak != null) return bak;

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
                "Hãy đóng ứng dụng, mở thư mục %AppData%\\PersonalInfoManager rồi sửa hoặc khôi phục users.json " +
                "từ bản users.json.bak hoặc users.json.corrupt-*.\n\n" +
                (mainError != null ? "Chi tiết: " + mainError.Message : ""),
                mainError);
        }

        private static List<User>? TryParse(string file, out Exception? error)
        {
            error = null;
            if (!File.Exists(file)) return null;

            string json = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = new InvalidDataException("File trống.");
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<List<User>>(json);
            }
            catch (JsonException ex)
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

                string json = JsonSerializer.Serialize(_users, JsonOpts);
                string tmp = _usersFile + ".tmp";
                File.WriteAllText(tmp, json);

                if (File.Exists(_usersFile))
                {
                    if (TryParse(_usersFile, out _) != null)
                    {
                        // File hiện tại đọc được → giữ làm bản dự phòng
                        File.Copy(_usersFile, _usersFile + ".bak", overwrite: true);
                    }
                    else
                    {
                        // File hiện tại hỏng → giữ riêng một bản, không đè lên .bak tốt
                        File.Copy(_usersFile,
                            _usersFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
                            overwrite: true);
                    }
                }

                File.Move(tmp, _usersFile, overwrite: true);
            }
        }

        /// <summary>
        /// Chỉ tạo admin mặc định khi chưa có người dùng nào (lần chạy đầu).
        /// Admin này phải đổi mật khẩu ngay ở lần đăng nhập đầu tiên.
        /// Gọi trong lock, sau khi đã Reload.
        /// </summary>
        private void EnsureDefaultAdmin()
        {
            if (_users.Count > 0) return;

            _users.Add(new User
            {
                Username = DefaultAdminUsername,
                PasswordHash = PasswordHasher.Hash(DefaultAdminPassword),
                Role = UserRole.Admin,
                FullName = "Quản trị viên",
                Email = "admin@local",
                Phone = "",
                MustChangePassword = true
            });
            Save();
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

        private static bool IsValidPassword(string password)
            => !string.IsNullOrWhiteSpace(password) && password.Length >= 6;

        private static bool IsDefaultPassword(string password)
            => password == DefaultAdminPassword;

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

        public (bool ok, string message) CreateUserFull(
            string username, string password, UserRole role,
            string fullName, string email, string phone)
        {
            lock (Sync)
            {
                Reload();

                if (!IsValidUsername(username))
                    return (false, "Tên đăng nhập phải từ 3 ký tự trở lên.");

                if (!IsValidPassword(password))
                    return (false, "Mật khẩu phải từ 6 ký tự trở lên.");

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
        /// Nếu mật khẩu đúng nhưng chính là mật khẩu mặc định thì đánh dấu phải đổi.
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

                // Mật khẩu mặc định (kể cả bản cài cũ chưa có cờ này) → bắt buộc đổi
                if (IsDefaultPassword(password))
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

                if (!IsValidPassword(newPwd))
                    return (false, "Mật khẩu mới phải từ 6 ký tự.");

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
                Reload();

                var user = FindByIdCore(userId);
                if (user == null)
                    return (false, "Không tìm thấy user.");

                if (!IsValidPassword(newPassword))
                    return (false, "Mật khẩu phải từ 6 ký tự trở lên.");

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

        public void SetRole(Guid userId, UserRole role)
        {
            lock (Sync)
            {
                Reload();

                var user = FindByIdCore(userId);
                if (user == null) return;

                if (user.Role == UserRole.Admin &&
                    role == UserRole.User &&
                    _users.Count(u => u.Role == UserRole.Admin) <= 1)
                    return;

                user.Role = role;
                Save();
            }
        }

        // ================================================================
        // ================== THỐNG KÊ ==================
        // ================================================================

        public int CountAdmins() { lock (Sync) { Reload(); return _users.Count(u => u.Role == UserRole.Admin); } }
        public int CountUsers() { lock (Sync) { Reload(); return _users.Count(u => u.Role == UserRole.User); } }
        public int CountActive() { lock (Sync) { Reload(); return _users.Count(u => u.IsActive); } }
        public int CountInactive() { lock (Sync) { Reload(); return _users.Count(u => !u.IsActive); } }

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
                Reload();

                var user = FindByIdCore(userId);
                if (user == null)
                    return (false, "Không tìm thấy user.");

                if (enabled && string.IsNullOrWhiteSpace(sid))
                    return (false, "Không lấy được SID tài khoản Windows.");

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