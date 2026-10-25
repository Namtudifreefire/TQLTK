using System;

namespace PersonalInfoManager.Models
{
    /// <summary>
    /// Một tài khoản được lưu local (dùng cho trình quản lý mật khẩu mini).
    /// Password được mã hóa AES khi lưu xuống file.
    /// </summary>
    public class SavedAccount
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Tên dịch vụ / website (vd: "Facebook", "Gmail").</summary>
        public string ServiceName { get; set; } = "";

        /// <summary>Tên đăng nhập của dịch vụ đó.</summary>
        public string Username { get; set; } = "";

        /// <summary>Mật khẩu (sẽ được mã hóa AES khi lưu).</summary>
        public string Password { get; set; } = "";

        /// <summary>Ghi chú thêm (URL, câu hỏi bảo mật...).</summary>
        public string Note { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public SavedAccount Clone() => (SavedAccount)MemberwiseClone();
    }
}