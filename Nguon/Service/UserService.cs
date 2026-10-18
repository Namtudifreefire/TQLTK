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
    /// Quản lý tài khoản người dùng.
    /// Dữ liệu lưu tại: %AppData%\PersonalInfoManager\users.json
    /// </summary>
    public class UserService
    {
        private readonly string _usersFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "users.json");

        private const string DefaultAdminUsername = "admin";
        private const string DefaultAdminPassword = "admin123";

        private List<User> _users = new();

        public UserService()
        {
            Load();
            EnsureDefaultAdmin();
        }

        // ================================================================
        // ================== NẠP / LƯU FILE ==================
        // ================================================================

        private void Load()
        {
            if (!File.Exists(_usersFile))
            {
                _users = new List<User>();
                return;
            }

            try
            {
                string json = File.ReadAllText(_usersFile);
                _users = JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
            }
            catch
            {
                _users = new List<User>();
            }
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_usersFile)!);
            string json = JsonSerializer.Serialize(_users,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_usersFile, json);
        }

        private void EnsureDefaultAdmin()
        {
            if (_users.Count == 0)
            {
                _users.Add(new User
                {
                    Username = DefaultAdminUsername,
                    PasswordHash = PasswordHasher.Hash(DefaultAdminPassword),
                    Role = UserRole.Admin,
                    FullName = "Quản trị viên",
                    Email = "admin@local",
                    Phone = ""
                });
                Save();
            }
        }

        // ================================================================
        // ================== TRUY VẤN ==================
        // ================================================================

        public IReadOnlyList<User> GetAll() => _users.AsReadOnly();

        public User? FindByUsername(string username)
            => _users.FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        public User? FindById(Guid id) => _users.FirstOrDefault(u => u.Id == id);

        public bool UsernameExists(string username)
            => FindByUsername(username) != null;

        public bool EmailExists(string email, Guid? excludeUserId = null)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            return _users.Any(u =>
                u.Id != excludeUserId &&
                !string.IsNullOrEmpty(u.Email) &&
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
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
            if (!IsValidUsername(username))
                return (false, "Tên đăng nhập phải từ 3 ký tự trở lên.");

            if (!IsValidPassword(password))
                return (false, "Mật khẩu phải từ 6 ký tự trở lên.");

            if (string.IsNullOrWhiteSpace(fullName))
                return (false, "Họ tên không được để trống.");

            if (UsernameExists(username))
                return (false, "Tên đăng nhập đã tồn tại.");

            if (!IsValidEmail(email))
                return (false, "Email không hợp lệ.");

            if (EmailExists(email))
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

        // ================================================================
        // ================== ĐĂNG NHẬP ==================
        // ================================================================

        public (bool ok, string message, User? user) Authenticate(
            string username, string password)
        {
            var user = FindByUsername(username);
            if (user == null)
                return (false, "Tài khoản không tồn tại.", null);

            if (!user.IsActive)
                return (false, "Tài khoản đã bị khóa.", null);

            if (!PasswordHasher.Verify(password, user.PasswordHash))
                return (false, "Sai mật khẩu.", null);

            user.LastLoginAt = DateTime.Now;
            Save();
            return (true, "Đăng nhập thành công.", user);
        }

        // ================================================================
        // ================== ĐỔI MẬT KHẨU ==================
        // ================================================================

        public (bool ok, string message) ChangePassword(
            string username, string oldPwd, string newPwd)
        {
            var user = FindByUsername(username);
            if (user == null)
                return (false, "Không tìm thấy user.");

            if (!PasswordHasher.Verify(oldPwd, user.PasswordHash))
                return (false, "Mật khẩu cũ không đúng.");

            if (!IsValidPassword(newPwd))
                return (false, "Mật khẩu mới phải từ 6 ký tự.");

            if (oldPwd == newPwd)
                return (false, "Mật khẩu mới phải khác mật khẩu cũ.");

            user.PasswordHash = PasswordHasher.Hash(newPwd);
            Save();
            return (true, "Đổi mật khẩu thành công.");
        }

        public (bool ok, string message) ResetPassword(Guid userId, string newPassword)
        {
            var user = FindById(userId);
            if (user == null)
                return (false, "Không tìm thấy user.");

            if (!IsValidPassword(newPassword))
                return (false, "Mật khẩu phải từ 6 ký tự trở lên.");

            user.PasswordHash = PasswordHasher.Hash(newPassword);
            Save();
            return (true, "Đã reset mật khẩu.");
        }

        // ================================================================
        // ================== CẬP NHẬT PROFILE ==================
        // ================================================================

        public (bool ok, string message) UpdateProfile(
            Guid userId, string fullName, string email, string phone)
        {
            var user = FindById(userId);
            if (user == null)
                return (false, "Không tìm thấy user.");

            if (string.IsNullOrWhiteSpace(fullName))
                return (false, "Họ tên không được để trống.");

            if (!IsValidEmail(email))
                return (false, "Email không hợp lệ.");

            if (EmailExists(email, excludeUserId: userId))
                return (false, "Email đã được sử dụng bởi user khác.");

            user.FullName = fullName.Trim();
            user.Email = email.Trim();
            user.Phone = phone.Trim();
            Save();
            return (true, "Cập nhật thành công.");
        }

        public (bool ok, string message) AdminUpdateUser(
            Guid userId, string fullName, string email, string phone,
            UserRole role, bool isActive)
        {
            var user = FindById(userId);
            if (user == null)
                return (false, "Không tìm thấy user.");

            if (string.IsNullOrWhiteSpace(fullName))
                return (false, "Họ tên không được để trống.");

            if (!IsValidEmail(email))
                return (false, "Email không hợp lệ.");

            if (EmailExists(email, excludeUserId: userId))
                return (false, "Email đã được sử dụng bởi user khác.");

            user.FullName = fullName.Trim();
            user.Email = email.Trim();
            user.Phone = phone.Trim();
            user.Role = role;
            user.IsActive = isActive;
            Save();
            return (true, "Cập nhật thành công.");
        }

        // ================================================================
        // ================== KHÓA / MỞ ==================
        // ================================================================

        public void SetActive(Guid userId, bool active)
        {
            var user = FindById(userId);
            if (user == null) return;
            user.IsActive = active;
            Save();
        }

        // ================================================================
        // ================== XÓA ==================
        // ================================================================

        public (bool ok, string message) DeleteUser(Guid userId, Guid currentUserId)
        {
            if (userId == currentUserId)
                return (false, "Không thể xóa tài khoản đang đăng nhập.");

            var user = FindById(userId);
            if (user == null)
                return (false, "Không tìm thấy user.");

            if (user.Role == UserRole.Admin &&
                _users.Count(u => u.Role == UserRole.Admin) <= 1)
                return (false, "Không thể xóa admin cuối cùng.");

            _users.Remove(user);
            Save();
            return (true, "Đã xóa tài khoản.");
        }

        // ================================================================
        // ================== PHÂN QUYỀN ==================
        // ================================================================

        public void SetRole(Guid userId, UserRole role)
        {
            var user = FindById(userId);
            if (user == null) return;

            if (user.Role == UserRole.Admin &&
                role == UserRole.User &&
                _users.Count(u => u.Role == UserRole.Admin) <= 1)
                return;

            user.Role = role;
            Save();
        }

        // ================================================================
        // ================== THỐNG KÊ ==================
        // ================================================================

        public int CountAdmins() => _users.Count(u => u.Role == UserRole.Admin);
        public int CountUsers() => _users.Count(u => u.Role == UserRole.User);
        public int CountActive() => _users.Count(u => u.IsActive);
        public int CountInactive() => _users.Count(u => !u.IsActive);

        // ================================================================
        // ================== WINDOWS HELLO ==================
        // ================================================================

        /// <summary>
        /// Bật/tắt tính năng đăng nhập bằng Windows Hello cho user.
        /// </summary>
        public (bool ok, string message) SetWindowsHello(
            Guid userId, bool enabled, string sid)
        {
            var user = FindById(userId);
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

        /// <summary>
        /// Tìm user đã bật Windows Hello và khớp SID tài khoản Windows hiện tại.
        /// </summary>
        public User? FindByWindowsHelloSid(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid)) return null;

            return _users.FirstOrDefault(u =>
                u.WindowsHelloEnabled &&
                u.IsActive &&
                !string.IsNullOrEmpty(u.WindowsHelloSid) &&
                string.Equals(u.WindowsHelloSid, sid,
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Lấy danh sách user đã bật Windows Hello.
        /// </summary>
        public List<User> GetWindowsHelloUsers()
        {
            return _users.Where(u => u.WindowsHelloEnabled && u.IsActive).ToList();
        }
    }
}