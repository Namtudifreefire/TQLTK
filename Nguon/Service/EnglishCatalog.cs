namespace PersonalInfoManager.Services;

internal static class EnglishCatalog
{
    internal static readonly IReadOnlyDictionary<string, string> Entries = BuildEntries();
    internal static readonly IReadOnlyList<KeyValuePair<string, string>> Fragments = BuildFragments();

    private static Dictionary<string, string> BuildEntries()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string source, string translation) => values[source] = translation;

        Add("Mật khẩu mới và hash được nâng cấp sau đăng nhập sẽ dùng PBKDF2-HMAC-SHA256 600.000 vòng. Hash Argon2id hiện tại vẫn được xác minh bình thường.", "New passwords and hashes upgraded after sign-in will use PBKDF2-HMAC-SHA256 with 600,000 iterations. Existing Argon2id hashes will still be verified normally.");
        Add("Mật khẩu mới và hash được nâng cấp sau đăng nhập sẽ dùng Argon2id. Hash PBKDF2 hiện tại vẫn được xác minh bình thường.", "New passwords and hashes upgraded after sign-in will use Argon2id. Existing PBKDF2 hashes will still be verified normally.");
        Add("Đã lưu lựa chọn thuật toán băm cho mật khẩu mới và lần nâng cấp hash tiếp theo.", "The hashing choice for new passwords and the next hash upgrade has been saved.");
        Add("Ứng dụng sẽ tạo và xác minh bản backup có cụm mật khẩu, sau đó mã hóa lại vault, ghi chú và các backup cục bộ đã nhận diện. Khóa cũ được giữ dưới DPAPI để mở bản xuất lịch sử. Tệp cá nhân dùng khóa riêng và không bị thay đổi. Tiếp tục?", "The app will create and verify a passphrase-protected backup, then re-encrypt the vault, notes, and recognized local backups. The old key is retained under DPAPI for historical exports. Personal files use a separate key and will not change. Continue?");
        Add("Không thể đọc trạng thái sao lưu.", "Could not read backup health.");
        Add("Trạng thái sao lưu: bản gần nhất bị thiếu, thay đổi hoặc chưa xác minh.", "Backup health: the latest backup is missing, has changed, or has not been verified.");
        Add("Trạng thái sao lưu: bản gần nhất đã cũ ({0}).", "Backup health: the most recent backup is old ({0}).");
        Add("Diễn tập thành công: đã khôi phục thử {0} file ({1}) vào kho tạm được mã hóa, xác minh từng file trong {2} giây rồi xóa kho tạm. Kho File cá nhân thật không thay đổi.", "Restore drill succeeded: restored {0} files ({1}) to an encrypted temporary store, verified each file in {2} seconds, and removed the temporary store. The actual personal-file store was unchanged.");
        Add("Diễn tập khôi phục thành công: đã đọc, giải mã, ghi thử an toàn vào kho tạm, đọc lại và đối chiếu {0} tài khoản trong {1} giây. Vault thật không thay đổi.", "Restore drill succeeded: read, decrypted, wrote to a safe temporary store, read back, and compared {0} accounts in {1} seconds. The actual vault was unchanged.");
        Add("Xoay khóa hoàn tất", "Key rotation complete");
        Add("Xoay khóa chưa hoàn tất", "Key rotation incomplete");
        Add("Đã xoay khóa và mã hóa lại {0} tệp cục bộ.\nBản phục hồi đã xác minh ({1} tài khoản): {2}\nCác backup có cụm mật khẩu vẫn độc lập; khóa cũ được lưu mã hóa để đọc bản xuất lịch sử.", "The key was rotated and {0} local files were re-encrypted.\nVerified recovery copy ({1} accounts): {2}\nPassphrase-protected backups remain independent; the old key is stored in protected form for historical exports.");
        Add("Không thể hoàn tất xoay khóa. Dữ liệu gốc và nhật ký phục hồi được giữ lại; không xóa thư mục dữ liệu ứng dụng. Lần đọc tiếp theo sẽ thử tiếp tục giao dịch.\n\n{0}", "Key rotation could not be completed. Original data and the recovery journal were retained; do not delete the application data folder. The next read will try to resume the transaction.\n\n{0}");
        Add("Trước khi xoay khóa, hãy đặt cụm mật khẩu sao lưu trong Tiện ích → Cụm mật khẩu sao lưu. Bạn cần tạo một bản phục hồi mở được độc lập với khóa Windows cũ.", "Before rotating the key, set a backup passphrase under Utilities → Backup passphrase. Create a recovery copy that can be opened independently of the old Windows key.");
        Add("Không tạo được bản phục hồi: {0}", "Could not create the recovery copy: {0}");
        Add("Bản phục hồi không qua xác minh: {0}", "The recovery copy did not pass verification: {0}");
        Add("Đã giữ nguyên {0} backup dùng cụm mật khẩu.", "Preserved {0} passphrase-protected backups.");
        Add("Trạng thái sao lưu: có bản sao lưu nhưng chưa xác minh bản gần nhất.", "Backup health: a backup exists, but the latest one has not been verified.");
        Add("Băm mật khẩu đăng nhập", "Sign-in password hashing");
        Add("Mặc định dùng Argon2id. Có thể bật PBKDF2-HMAC-SHA256 cho môi trường cần tương thích FIPS; hash cũ được nâng cấp sau lần đăng nhập thành công.", "Argon2id is used by default. PBKDF2-HMAC-SHA256 is available for environments that require FIPS-compatible hashing; existing hashes are upgraded after a successful sign-in.");
        Add("Dùng PBKDF2-HMAC-SHA256 (tương thích FIPS)", "Use PBKDF2-HMAC-SHA256 (FIPS-compatible)");
        Add("Xoay khóa vault", "Rotate vault key");
        Add("Chỉ quản trị viên đang hoạt động mới được phép xoay khóa vault.", "Only an active administrator can rotate the vault key.");
        Add("Không đủ quyền", "Insufficient permissions");
        Add("Mã hóa lại vault, ghi chú và bản sao lưu cục bộ đã nhận diện. Hãy đặt cụm mật khẩu sao lưu trước; khóa cũ được giữ mã hóa để mở các bản xuất lịch sử.", "Re-encrypt the vault, notes, and recognized local backups. Set a backup passphrase first; the old key is retained in protected form for historical exports.");
        Add("Trạng thái", "Status");
        Add("Đang mở", "Open");
        Add("Đã xử lý", "Fixed");
        Add("Nhắc lại sau 1 ngày", "Remind me in 1 day");
        Add("Nhắc lại sau 7 ngày", "Remind me in 7 days");
        Add("Nhắc lại sau 30 ngày", "Remind me in 30 days");
        Add("Bỏ qua có lý do", "Ignore with a reason");
        Add("Lý do (bắt buộc khi bỏ qua)", "Reason (required when ignoring)");
        Add("Lưu trạng thái", "Save status");
        Add("Hãy ghi lý do khi bỏ qua cảnh báo.", "Enter a reason before ignoring this finding.");
        Add("Thiếu lý do", "Reason required");
        Add("Trạng thái cảnh báo bảo mật", "Security finding status");
        Add("Lần kiểm tra gần nhất: {0}", "Last checked: {0}");
        Add("Đã đánh dấu xử lý • kiểm tra {0}", "Marked fixed • checked {0}");
        Add("Nhắc lại {0} • kiểm tra {1}", "Reminder {0} • checked {1}");
        Add("Đã bỏ qua: {0} • kiểm tra {1}", "Ignored: {0} • checked {1}");
        Add("Đang mở • kiểm tra {0}", "Open • checked {0}");
        Add("Trạng thái sao lưu: chưa từng tạo bản sao lưu.", "Backup health: no backup has been created.");
        Add("Sao lưu gần nhất thất bại: {0}", "Most recent backup failed: {0}");
        Add("Trạng thái sao lưu: vault có thay đổi sau lần sao lưu {0}.", "Backup health: the vault changed after the backup on {0}.");
        Add("Trạng thái sao lưu: bản gần nhất đã cũ ({0}).", "Backup health: the most recent backup is old ({0}).");
        Add("Trạng thái sao lưu: có bản sao lưu nhưng chưa xác minh bản gần nhất.", "Backup health: a backup exists, but the latest one has not been verified.");
        Add("Trạng thái sao lưu: chưa có diễn tập khôi phục thành công.", "Backup health: no successful restore drill has been recorded.");
        Add("Trạng thái sao lưu: cần diễn tập khôi phục lại; lần gần nhất {0}.", "Backup health: run another restore drill; the last one was on {0}.");
        Add("Trạng thái sao lưu tốt: đã xác minh và diễn tập gần đây ({0}).", "Backup health: recently verified and restore-tested ({0}).");
        Add("Đổi thuật toán băm mật khẩu", "Change password hashing algorithm");
        Add("Đã hủy diễn tập khôi phục; kho thật không thay đổi.", "Restore drill was cancelled; the actual store was unchanged.");
        Add("Không thể hoàn tất xoay khóa. Dữ liệu gốc và nhật ký phục hồi được giữ lại; không xóa thư mục dữ liệu ứng dụng. Lần đọc tiếp theo sẽ thử tiếp tục giao dịch.\n\n{0}", "Key rotation could not be completed. Original data and the recovery journal were retained; do not delete the application data folder. The next read will try to resume the transaction.\n\n{0}");
        Add("Bản phục hồi không qua xác minh: {0}", "The recovery copy did not pass verification: {0}");
        Add("Không tạo được bản phục hồi: {0}", "Could not create the recovery copy: {0}");
        Add("Trạng thái sao lưu: bản gần nhất bị thiếu, thay đổi hoặc chưa xác minh.", "Backup health: the latest backup is missing, has changed, or has not been verified.");
        Add("Không thể đọc mục", "Could not read entry");
        Add("Không lưu được trạng thái", "Could not save status");
        Add("Cần chuẩn bị bản phục hồi", "Prepare a recovery copy first");
        Add("Trước khi xoay khóa, hãy đặt cụm mật khẩu sao lưu trong Tiện ích → Cụm mật khẩu sao lưu. Bạn cần tạo một bản phục hồi mở được độc lập với khóa Windows cũ.", "Before rotating the key, set a backup passphrase under Utilities → Backup passphrase. You need a recovery copy that does not depend on the old Windows key.");
        Add("Lưu bản phục hồi trước khi xoay khóa", "Save a recovery copy before key rotation");
        Add("Xác nhận xoay khóa vault", "Confirm vault key rotation");
        Add("Ứng dụng sẽ tạo và xác minh bản backup có cụm mật khẩu, sau đó mã hóa lại vault, ghi chú và các backup cục bộ đã nhận diện. Khóa cũ được giữ dưới DPAPI để mở bản xuất lịch sử. Tệp cá nhân dùng khóa riêng và không bị thay đổi. Tiếp tục?", "The app will create and verify a passphrase-protected backup, then re-encrypt the vault, notes, and recognized local backups. The old key is retained under DPAPI to open historical exports. Personal files use a separate key and will not change. Continue?");
        Add("Không tạo được bản phục hồi", "Could not create recovery copy");
        Add("Xoay khóa hoàn tất", "Key rotation complete");
        Add("Xoay khóa chưa hoàn tất", "Key rotation incomplete");
        Add("Đã xoay khóa và mã hóa lại {0} tệp cục bộ.\nBản phục hồi đã xác minh: {1}\nCác backup có cụm mật khẩu vẫn độc lập; khóa cũ được lưu mã hóa để đọc bản xuất lịch sử.", "The key was rotated and {0} local files were re-encrypted.\nVerified recovery copy: {1}\nPassphrase-protected backups remain independent; the old key is stored in protected form for historical exports.");
        Add("Đã giữ nguyên {0} backup dùng cụm mật khẩu.", "Preserved {0} passphrase-protected backups.");
        Add("Diễn tập", "Drill");
        Add("Chọn bản sao lưu để diễn tập khôi phục", "Select a backup for a restore drill");
        Add("Diễn tập hoàn tất", "Restore drill complete");
        Add("Diễn tập thất bại", "Restore drill failed");
        Add("Đã hủy diễn tập vì phiên đăng nhập không còn hoạt động.", "The drill was cancelled because the sign-in session is no longer active.");
        Add("Không thể diễn tập khôi phục: ", "Could not run the restore drill: ");
        Add("Khóa vault", "Vault key");
        Add("Xoay khóa", "Rotate key");
        Add("(tìm theo tên, URL, nhãn, thư mục)", "(search by name, URL, tag, or folder)");
        Add("⏻  Thoát", "⏻  Exit");
        Add("○ Đang tắt", "○ Off");
        Add("☁️  Google Drive (BETA)", "☁️  Google Drive (BETA)");
        Add("☁️ Khôi phục từ Google Drive", "☁️ Restore from Google Drive");
        Add("Tài khoản Drive đã liên kết", "Linked Drive account");
        Add("Kiểm tra tài khoản", "Check account");
        Add("Ngắt kết nối", "Disconnect");
        Add("Chưa kiểm tra kết nối Drive. Nhấn Kiểm tra tài khoản để xác minh.", "Drive connection has not been checked. Select Check account to verify it.");
        Add("Đã tìm thấy thông tin đăng nhập Drive trên máy này. Nhấn Kiểm tra tài khoản để xem tài khoản Google.", "Drive sign-in data was found on this device. Select Check account to see the Google account.");
        Add("Chưa lưu tài khoản Google Drive trên máy này.", "No Google Drive account is saved on this device.");
        Add("Không thể đọc trạng thái token Google Drive trên máy này.", "Could not read Google Drive token status on this device.");
        Add("Đang kiểm tra tài khoản Google Drive...", "Checking the Google Drive account...");
        Add("Đã hủy kiểm tra tài khoản Google Drive.", "Google Drive account check was cancelled.");
        Add("Không tìm thấy thông tin đăng nhập Google Drive còn dùng được.", "No usable Google Drive sign-in data was found.");
        Add("Đã xác minh tài khoản Drive bằng token đã lưu trên máy này.", "The Drive account was verified using the token saved on this device.");
        Add("Có token Drive đã lưu nhưng chưa xác minh được tài khoản: ", "A Drive token is saved, but the account could not be verified: ");
        Add("Đường dẫn token Google Drive không khớp tài khoản hiện tại.", "The Google Drive token path does not match the current account.");
        Add("Thư mục token Google Drive là liên kết hệ thống; không thể xóa an toàn.", "The Google Drive token directory is a system link and cannot be safely deleted.");
        Add("Tài khoản Google Drive đã liên kết: {0}.", "Linked Google Drive account: {0}.");
        Add("Không thể kiểm tra tài khoản Google Drive: ", "Could not check the Google Drive account: ");
        Add("Đã ngắt kết nối Drive trên máy này. Bản sao lưu trên Drive không bị xóa; quyền Google vẫn có thể được thu hồi riêng.", "Drive was disconnected on this device. Drive backups were not deleted; Google access can be revoked separately.");
        Add("Xóa token Google Drive đã mã hóa đang lưu trên máy này? Bản sao lưu trên Drive không bị xóa. Quyền ứng dụng trong tài khoản Google vẫn còn và cần thu hồi riêng nếu bạn muốn.", "Remove the encrypted Google Drive token saved on this device? Drive backups will not be deleted. The app's permission in your Google account will remain and must be revoked separately if desired.");
        Add("Ngắt kết nối Google Drive", "Disconnect Google Drive");
        Add("Ngắt kết nối chỉ xóa token được mã hóa trên máy này; quyền Google có thể thu hồi riêng trong tài khoản Google.", "Disconnecting removes only the encrypted token on this device; Google access can be revoked separately in your Google account.");
        Add("Sai mật khẩu {0} lần liên tiếp. Tài khoản bị khóa tạm thời {1} phút.", "The password was incorrect {0} times in a row. The account is temporarily locked for {1} minutes.");
        Add("Mã khôi phục 2FA còn: {0} • Cụm mật khẩu sao lưu: {1}. Khóa tệp cá nhân gắn với Windows hiện tại; để đổi máy hoặc cài lại Windows, hãy tạo backup có cụm mật khẩu và giữ mã khôi phục riêng.", "2FA recovery codes remaining: {0} • Backup passphrase: {1}. Personal-file encryption is tied to the current Windows account; before changing computers or reinstalling Windows, create a passphrase-protected backup and keep recovery codes separately.");
        Add("đã đặt", "set");
        Add("chưa đặt", "not set");
        Add("Không thể đọc trạng thái khôi phục.", "Could not read recovery status.");
        Add("Tài khoản cần xem xét", "Accounts to review");
        Add("Mở mục", "Open entry");
        Add("Chưa kiểm tra mật khẩu rò rỉ trong phiên này; dùng nút kiểm tra để đối chiếu tùy chọn.", "Breached passwords have not been checked in this session. Use the optional check to compare them.");
        Add("Vault đã thay đổi sau lần kiểm tra rò rỉ; hãy chạy lại để cập nhật kết quả.", "The vault changed after the breached-password check. Run it again to refresh the results.");
        Add("Chưa phát hiện vấn đề trong các kiểm tra hiện có.", "No issues were found in the checks performed.");
        Add("Chưa có vấn đề cục bộ; kiểm tra mật khẩu rò rỉ chưa chạy hoặc kết quả đã cũ.", "No local issues were found. The breached-password check has not run or its results are out of date.");
        Add("Tìm thấy {0} cảnh báo; đang hiển thị {1} mục ưu tiên cao nhất.", "Found {0} warnings; showing the {1} highest-priority entries.");
        Add("Tìm thấy {0} cảnh báo mật khẩu cần xem xét.", "Found {0} password warnings to review.");
        Add("Mật khẩu xuất hiện trong dữ liệu rò rỉ", "Password found in breach data");
        Add("Mật khẩu được dùng lại", "Reused password");
        Add("Mật khẩu yếu", "Weak password");
        Add("Mật khẩu chưa đổi hơn 365 ngày", "Password unchanged for over 365 days");
        Add("Chưa rõ ngày đổi mật khẩu", "Password change date is unknown");
        Add("Đổi mật khẩu này chỉ thay thông tin đăng nhập TQLTK, không đổi khóa mã hóa vault. Trước khi đổi tài khoản Windows hoặc cài lại Windows, hãy tạo bản sao lưu có cụm mật khẩu.", "Changing this password updates only your TQLTK sign-in; it does not change the vault encryption key. Before changing Windows accounts or reinstalling Windows, create a passphrase-protected backup.");
        Add("⚙️  Cài đặt", "⚙️  Settings");
        Add("⚙️  Hệ thống", "⚙️  System");
        Add("✅ Đã bật xác thực 2 bước", "✅ Two-factor authentication is enabled");
        Add("✅ Tôi đã lưu, hoàn tất", "✅ I have saved them, finish");
        Add("✏️ Sửa thông tin user", "✏️ Edit user");
        Add("✓ Đăng ký", "✓ Register");
        Add("✓ Lưu", "✓ Save");
        Add("✓ Xác nhận", "✓ Confirm");
        Add("➕ Tạo tài khoản", "➕ Create account");
        Add("➕ Tạo tài khoản mới", "➕ Create new account");
        Add("⬇ Khôi phục", "⬇ Restore");
        Add("🎨  Giao diện", "🎨  Appearance");
        Add("👑  Quản trị", "👑  Administration");
        Add("👤  Thông tin cá nhân", "👤  Personal information");
        Add("💾  Xuất / nhập dữ liệu", "💾  Data export / import");
        Add("📁  Tài liệu", "📁  Files");
        Add("📋 Copy mã", "📋 Copy code");
        Add("📝  Chưa có tài khoản? Đăng ký ngay", "📝  New here? Register now");
        Add("📝 Đăng ký tài khoản", "📝 Register account");
        Add("📥 Import", "📥 Import");
        Add("📥 Import tài khoản từ CSV", "📥 Import accounts from CSV");
        Add("🔍  Tìm trong cài đặt", "🔍  Search settings");
        Add("🔍 Tìm:", "🔍 Search:");
        Add("🔐  Tài khoản", "🔐  Accounts");
        Add("🔐 Bật Windows Hello trong Thông tin cá nhân", "🔐 Enable Windows Hello in Personal Information");
        Add("🔐 Đăng nhập", "🔐 Sign in");
        Add("🔐 Đăng nhập bằng Windows Hello (BETA)", "🔐 Sign in with Windows Hello (BETA)");
        Add("🔐 Đổi mật khẩu", "🔐 Change password");
        Add("🔐 Thêm tài khoản mới", "🔐 Add new account");
        Add("🔑 8 mã khôi phục mới", "🔑 8 new recovery codes");
        Add("🔑 Bật xác thực 2 bước", "🔑 Enable two-factor authentication");
        Add("🔑 Tắt xác thực 2 bước", "🔑 Disable two-factor authentication");
        Add("🔑 Tạo lại mã khôi phục", "🔑 Regenerate recovery codes");
        Add("🔑 Xác thực 2 bước", "🔑 Two-factor authentication");
        Add("🔓  Đăng nhập", "🔓  Sign in");
        Add("🚪  Đăng xuất", "🚪  Sign out");
        Add("🛡️  Bảo mật", "🛡️  Security");
        Add("06:00 - 18:00 giao diện sáng, còn lại giao diện tối.", "Light theme from 06:00 to 18:00; dark theme otherwise.");
        Add("1 phút", "1 minute");
        Add("1. Cài Google Authenticator, Microsoft Authenticator hoặc Authy trên điện thoại.", "1. Install Google Authenticator, Microsoft Authenticator, or Authy on your phone.");
        Add("10 phút", "10 minutes");
        Add("15 phút", "15 minutes");
        Add("2. Chọn Thêm tài khoản → Quét mã QR, rồi quét mã bên dưới.", "2. Tap Add account → Scan a QR code, then scan the code below.");
        Add("3. Nhập mã 6 số đang hiển thị trong ứng dụng để xác nhận.", "3. Enter the 6-digit code shown in the app to confirm.");
        Add("5 phút", "5 minutes");
        Add("8 từ được chọn ngẫu nhiên bằng bộ sinh mật mã học.", "Eight words are selected using a cryptographic random generator.");
        Add("Admin", "Admin");
        Add("Ẩn / hiện thanh bên (Ctrl+B)", "Show / hide sidebar (Ctrl+B)");
        Add("Bản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.", "Backups are stored in the 'TQLTK Backup' folder in your Google Drive.");
        Add("Bảo mật", "Security");
        Add("Bảo vệ đăng nhập và các thao tác nhạy cảm.", "Protect sign-in and sensitive actions.");
        Add("Bật / Tắt", "On / Off");
        Add("Bật hoặc tắt trong trang Thông tin cá nhân.", "Enable or disable this on the Personal Information page.");
        Add("Bật Windows Hello", "Enable Windows Hello");
        Add("Bật xác thực 2 bước", "Enable two-factor authentication");
        Add("Bật xác thực 2 bước (Authenticator)", "Enable two-factor authentication (Authenticator)");
        Add("Bỏ chọn tất cả", "Clear all");
        Add("Các chỉ số khác được tính cục bộ. Kiểm tra rò rỉ là tùy chọn và chỉ gửi tiền tố hash một phần qua HTTPS.", "Other checks run locally. The optional breach check sends only a partial hash prefix over HTTPS.");
        Add("Các mật khẩu trước được giữ mã hóa trong vault. Chỉ 5 mật khẩu gần nhất được lưu.", "Previous passwords are kept encrypted in the vault. Only the 5 most recent passwords are retained.");
        Add("Cài đặt", "Settings");
        Add("Cần xác thực Windows Hello. File chứa mật khẩu dạng chữ thường.", "Windows Hello authentication is required. The file contains passwords in plaintext.");
        Add("Cấp / Thu Admin", "Grant / revoke admin");
        Add("Cập nhật", "Update");
        Add("Cập nhật họ tên, Gmail, số điện thoại và đăng nhập bằng Windows Hello.", "Update your name, Gmail address, phone number, and Windows Hello sign-in.");
        Add("Chỉ quản trị viên mới thấy mục này.", "Only administrators can see this section.");
        Add("Chi tiết", "Details");
        Add("Cho phép đăng nhập TQLTK bằng vân tay / khuôn mặt / PIN của Windows.", "Sign in to TQLTK with your Windows fingerprint, face, or PIN.");
        Add("Chọn một bản trên Drive để nhập lại vào app.", "Select a Drive backup to restore to the app.");
        Add("Chọn tất cả", "Select all");
        Add("Chữ hoa", "Uppercase");
        Add("Chữ số", "Digits");
        Add("Chữ thường", "Lowercase");
        Add("Chưa có mã xác thực cho website này.", "This website does not have a TOTP code yet.");
        Add("Chưa có trường tùy chỉnh", "No custom fields yet");
        Add("Chưa đọc dữ liệu.", "No data loaded.");
        Add("Chuyển nhanh giữa giao diện sáng và tối.", "Quickly switch between light and dark themes.");
        Add("Còn lại", "Remaining");
        Add("Trang web", "Website");
        Add("📋 Sao chép mã", "📋 Copy code");
        Add("Sao chép mã (bộ nhớ tạm tự xóa)", "Copy code (clipboard clears automatically)");
        Add("Trường tùy chỉnh được lưu bên trong kho mật khẩu đã mã hóa. Trường nhạy cảm được che khi nhập và không dùng trong tìm kiếm.", "Custom fields are stored in the encrypted password vault. Sensitive fields are masked while editing and excluded from search.");
        Add("Tự động đổi giao diện theo giờ", "Automatically change the theme by time");
        Add("Xuất kho mật khẩu (Sao lưu)", "Export password vault (Backup)");
        Add("Nhập kho mật khẩu (Khôi phục)", "Import password vault (Restore)");
        Add("Đồng bộ kho mật khẩu", "Sync password vault");
        Add("Khôi phục kho mật khẩu", "Restore password vault");
        Add("Xóa giao diện, kích thước, vị trí cửa sổ và tài khoản đăng nhập gần nhất.", "Clear the theme, window size and position, and the last signed-in account.");
        Add("Các mật khẩu trước được giữ mã hóa trong kho mật khẩu. Chỉ lưu 5 mật khẩu gần nhất.", "Previous passwords are kept encrypted in the password vault. Only the 5 most recent are retained.");
        Add("Dán URI otpauth:// hoặc khóa bí mật Base32; quét mã QR chuyển tài khoản Google Authenticator; nhập tệp JSON chưa mã hóa từ Aegis/2FAS hoặc tệp URI otpauth.", "Paste an otpauth:// URI or Base32 secret; scan a Google Authenticator account-transfer QR code; or import an unencrypted Aegis/2FAS JSON export or otpauth URI file.");
        Add("Mã xác thực được lưu cùng kho mật khẩu đã mã hóa. Mã QR và khóa bí mật chứa thông tin nhạy cảm; chỉ nhập từ nguồn bạn tin cậy.", "Authenticator data is stored in the encrypted password vault. QR codes and secrets contain sensitive information; import them only from trusted sources.");
        Add("Đọc URI / khóa bí mật", "Read URI / secret");
        Add("Tài liệu cá nhân", "Personal files");
        Add("Lưu toàn bộ tài khoản vào tệp .vault đã mã hóa.", "Save all accounts to an encrypted .vault file.");
        Add("Nạp lại từ tệp .vault, tài khoản trùng sẽ được bỏ qua.", "Load from a .vault file; duplicate accounts are skipped.");
        Add("Xác minh tệp .vault hoặc .tqf và xem số lượng dữ liệu trước khi khôi phục; không sửa kho mật khẩu.", "Verify a .vault or .tqf file and view its contents before restoring; the password vault is not modified.");
        Add("Sao lưu tệp cá nhân", "Back up personal files");
        Add("Nén và mã hóa kho tệp (tối đa 100 MB) rồi đưa lên Drive.", "Compress and encrypt the file storage (up to 100 MB), then upload it to Drive.");
        Add("Khôi phục tệp cá nhân", "Restore personal files");
        Add("Khôi phục tệp cá nhân từ Google Drive", "Restore personal files from Google Drive");
        Add("📁 Khôi phục tệp cá nhân từ Google Drive", "📁 Restore personal files from Google Drive");
        Add("Không có tệp nào bị ghi đè: tệp trùng nội dung được bỏ qua, tệp trùng tên nhưng khác nội dung được lưu thêm với tên mới.", "No files are overwritten: files with duplicate content are skipped, while files with the same name but different content are saved under a new name.");
        Add("Sao lưu tài khoản ra tệp trên máy, hoặc nhập từ tệp.", "Back up accounts to a file on this computer, or import them from a file.");
        Add("Tệp chứa mật khẩu dạng chữ thường.", "The file contains passwords in plaintext.");
        Add("Cần xác thực Windows Hello. Tệp chứa mật khẩu dạng chữ thường.", "Windows Hello authentication is required. The file contains passwords in plaintext.");
        Add("Tải bản sao lưu về, không ghi đè tệp nào.", "Download the backup without overwriting any files.");
        Add("Tên tệp", "File name");
        Add("URI otpauth://totp/... hoặc khóa bí mật Base32", "otpauth://totp/... URI or Base32 secret");
        Add("Xem trước khôi phục kho mật khẩu", "Preview password vault restore");
        Add("Xem trước khôi phục tệp cá nhân", "Preview personal file restore");
        Add("Nhập tệp Authenticator…", "Import authenticator file…");
        Add("Chọn tệp cần nhập", "Select files to import");
        Add("Xuất tệp", "Export file");
        Add("Tạo tệp văn bản", "Create text file");
        Add("Xuất kho mật khẩu", "Export password vault");
        Add("Nhập kho mật khẩu", "Import password vault");
        Add("Chọn tệp cần kiểm tra", "Select a file to verify");
        Add("Chọn bản sao lưu cần kiểm tra", "Select a backup to verify");
        Add("Chọn ảnh mã QR từ ứng dụng xác thực", "Select a QR code image from your authenticator app");
        Add("Chọn tệp xuất từ ứng dụng xác thực", "Select an export file from your authenticator app");
        Add("Tất cả tệp (*.*)|*.*", "All files (*.*)|*.*");
        Add("Tệp văn bản (*.txt)|*.txt", "Text files (*.txt)|*.txt");
        Add("Tệp CSV (*.csv)|*.csv|Tất cả tệp (*.*)|*.*", "CSV files (*.csv)|*.csv|All files (*.*)|*.*");
        Add("Bản sao lưu kho mật khẩu (*.vault)|*.vault|Tất cả tệp (*.*)|*.*", "Password vault backup (*.vault)|*.vault|All files (*.*)|*.*");
        Add("Bản sao lưu TQLTK (*.vault;*.tqf)|*.vault;*.tqf|Tất cả tệp (*.*)|*.*", "TQLTK backup (*.vault;*.tqf)|*.vault;*.tqf|All files (*.*)|*.*");
        Add("Ảnh mã QR (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif|Tất cả tệp (*.*)|*.*", "QR code images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif|All files (*.*)|*.*");
        Add("Tệp xuất từ ứng dụng xác thực (*.2fas;*.json;*.txt)|*.2fas;*.json;*.txt|Tất cả tệp (*.*)|*.*", "Authenticator app export (*.2fas;*.json;*.txt)|*.2fas;*.json;*.txt|All files (*.*)|*.*");
        Add("Tài khoản không còn trong kho mật khẩu.", "This account is no longer in the password vault.");
        Add("Không thể sao chép mật khẩu vào bộ nhớ tạm an toàn. Hãy thử lại.", "Could not safely copy the password to the clipboard. Try again.");
        Add("Không thể sao chép mã vào bộ nhớ tạm an toàn. Hãy thử lại.", "Could not safely copy the code to the clipboard. Try again.");
        Add("Đang kiểm tra các mật khẩu duy nhất…", "Checking unique passwords…");
        Add("Kiểm tra xong: không tìm thấy mật khẩu nào trong dữ liệu Pwned Passwords ({0} mật khẩu duy nhất).", "Check complete: no passwords were found in the Pwned Passwords dataset ({0} unique passwords).");
        Add("Cần đổi {0} mật khẩu trong kho mật khẩu ({1} giá trị duy nhất xuất hiện trong dữ liệu rò rỉ).{2}{3}", "Change {0} passwords in the password vault ({1} unique values appeared in breach data).{2}{3}");
        Add("Đã hủy kiểm tra; không có kết luận về mật khẩu.", "Check cancelled; no conclusion about the passwords was reached.");
        Add("Không thể hoàn tất kiểm tra; chưa thể kết luận an toàn. {0}", "The check could not be completed; password safety cannot be determined. {0}");
        Add("Kho mật khẩu: {0} tài khoản • {1} mật khẩu yếu • {2} mục dùng mật khẩu trùng • mật khẩu cũ nhất đã biết: {3} ngày • {4} tài khoản chưa rõ ngày đổi MK", "Password vault: {0} accounts • {1} weak passwords • {2} entries with reused passwords • oldest known password: {3} days • password change date unknown for {4} accounts");
        Add("2FA: đã bật cho đăng nhập bằng mật khẩu.", "2FA: enabled for password sign-in.");
        Add("2FA: đang tắt; nên bật để tăng bảo vệ khi đăng nhập bằng mật khẩu.", "2FA: disabled; enable it to better protect password sign-in.");
        Add("Sao lưu kho mật khẩu: chưa ghi nhận bản sao lưu nào.", "Password vault backup: no backup has been recorded.");
        Add("Sao lưu kho mật khẩu gần nhất: {0} • dữ liệu đã thay đổi sau lần sao lưu.", "Latest password vault backup: {0} • data has changed since the backup.");
        Add("Sao lưu kho mật khẩu gần nhất: {0} • chưa phát hiện thay đổi mới.", "Latest password vault backup: {0} • no new changes detected.");
        Add("Kho tệp: {0} tệp mã hóa không đọc được; giữ nguyên để xử lý riêng.", "File storage: {0} encrypted files could not be read; they were left unchanged for separate handling.");
        Add("Kho tệp: {0}/{1} tệp đã mã hóa; tệp cũ sẽ được chuyển đổi khi mở kho.", "File storage: {0}/{1} files are encrypted; legacy files will be converted when storage is opened.");
        Add("Kho tệp: {0} tệp được mã hóa AES-GCM, khóa gắn với tài khoản Windows hiện tại.", "File storage: {0} files are encrypted with AES-GCM using a key bound to the current Windows account.");
        Add("Không thể đọc trạng thái mã hóa tệp: {0}", "Could not read file encryption status: {0}");
        Add("Đặt lại mật khẩu '{0}'", "Reset password '{0}'");
        Add("Đã đồng bộ kho mật khẩu lên Google Drive thành công!\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.", "Password vault synced to Google Drive successfully!\n\nThe backup is in the 'TQLTK Backup' folder in your Google Drive.");
        Add("Chưa có mật khẩu trong kho mật khẩu để kiểm tra.", "There are no passwords in the password vault to check.");
        Add("Sao lưu kho mật khẩu: chưa ghi nhận bản sao lưu nào.", "Password vault backup: no backup has been recorded.");
        Add("Không thể đọc đầy đủ trạng thái kho mật khẩu.", "Could not read the full password vault status.");
        Add("xem lịch sử mật khẩu", "view password history");
        Add("sửa tài khoản (cửa sổ Sửa hiển thị được mật khẩu)", "edit an account (the Edit window displays its password)");
        Add("xem mã TOTP", "view the TOTP code");
        Add("xóa hẳn mục khỏi thùng rác", "permanently delete an item from Trash");
        Add("xuất kho mật khẩu", "export the password vault");
        Add("nhập kho mật khẩu", "import the password vault");
        Add("kiểm tra bản sao lưu", "verify a backup");
        Add("đồng bộ kho mật khẩu lên Google Drive", "sync the password vault to Google Drive");
        Add("khôi phục kho mật khẩu từ Google Drive", "restore the password vault from Google Drive");
        Add("đặt hoặc đổi cụm mật khẩu sao lưu", "set or change the backup passphrase");
        Add("lưu cụm mật khẩu sao lưu", "save the backup passphrase");
        Add("nhận cụm mật khẩu sao lưu cũ", "adopt the existing backup passphrase");
        Add("khôi phục tệp cá nhân từ Google Drive", "restore personal files from Google Drive");
        Add("kiểm tra mật khẩu rò rỉ", "check for breached passwords");
        Add("Đã bật tự động đổi giao diện theo giờ.\n\n• 06:00 - 18:00: Sáng\n• 18:00 - 06:00: Tối\n\nHiện tại ({0}): {1}", "Automatic theme switching is on.\n\n• 06:00–18:00: Light\n• 18:00–06:00: Dark\n\nCurrent ({0}): {1}");
        Add("Đã tắt tự động đổi giao diện theo giờ.", "Automatic theme switching is off.");
        Add("Giao diện tự động", "Automatic theme");
        Add("Đặt lại mật khẩu '{0}'", "Reset password '{0}'");
        Add("Nhập", "Import");
        Add("Xem nhật ký", "View log");
        Add("TQLTK • Phát triển bởi thienthuongthienha_duyngadocton_", "TQLTK • Developed by thienthuongthienha_duyngadocton_");
        Add("Được tạo bởi 天地と月の造化 - TQLTK v3.6.7 (BETA) | © 2026 Namtudifreefire | Giấy phép MIT", "Made by 天地と月の造化 - TQLTK v3.6.7 (BETA) | © 2026 Namtudifreefire | MIT License");
        Add("Sửa thông tin người dùng", "Edit user");
        Add("✏️ Sửa thông tin người dùng", "✏️ Edit user");
        Add("Cấp / Thu quyền quản trị", "Grant / revoke admin");
        Add("Tạo, sửa, khóa, phân quyền, xóa người dùng và xem nhật ký.", "Create, edit, lock, manage roles, delete users, and view logs.");
        Add("👑 Quản trị viên", "👑 Administrator");
        Add("👤 Người dùng", "👤 User");
        Add("Đặt lại cài đặt", "Reset settings");
        Add("Xác nhận đặt lại", "Confirm reset");
        Add("Đã đặt lại cài đặt.\n\nKhởi động lại ứng dụng để áp dụng toàn bộ.", "Settings were reset.\n\nRestart the app to apply all changes.");
        Add("Xóa toàn bộ cài đặt (giao diện, kích thước, vị trí, tài khoản gần nhất)?\n\nKhởi động lại ứng dụng để áp dụng toàn bộ.", "Clear all settings (theme, size, position, and last signed-in account)?\n\nRestart the app to apply all changes.");
        Add("Nhật ký hôm nay", "Today's log");
        Add("(Chưa có nhật ký hôm nay)", "(No log entries today)");
        Add("Nhập tài khoản từ CSV", "Import accounts from CSV");
        Add("📥 Nhập tài khoản từ CSV", "📥 Import accounts from CSV");
        Add("📥 Nhập", "📥 Import");
        Add("✅ Sẽ nhập: ", "✅ To import: ");
        Add("Chọn các tài khoản bạn muốn nhập. Các tài khoản trùng lặp sẽ được bỏ chọn sẵn.", "Select the accounts to import. Duplicate accounts are unchecked by default.");
        Add("Bạn chưa chọn tài khoản nào để nhập.", "You have not selected any accounts to import.");
        Add("Vui lòng nhập tên đăng nhập.", "Enter a username.");
        Add("Vui lòng nhập mật khẩu.", "Enter a password.");
        Add("Đã sao chép tên đăng nhập!", "Username copied!");
        Add("Xác nhận để sao chép mật khẩu", "Confirm to copy password");
        Add("Xác thực Windows Hello không thành công và bạn chưa bật xác thực 2 bước nên không thể sao chép mật khẩu.\n\nVào Bảo mật → Bật xác thực 2 bước (Authenticator) để có cách xác thực dự phòng.", "Windows Hello authentication failed and two-factor authentication is not enabled, so the password cannot be copied.\n\nGo to Security → Enable two-factor authentication (Authenticator) to add a backup verification method.");
        Add("Xác thực thất bại. Đã hủy sao chép mật khẩu.", "Authentication failed. Password copying was cancelled.");
        Add("Đã sao chép mật khẩu! Clipboard sẽ tự xóa sau ", "Password copied! The clipboard will clear in ");
        Add("Chọn tệp cần xuất.", "Select a file to export.");
        Add("Xuất thành công!", "Export completed successfully!");
        Add("Đã đặt lại mật khẩu. Người dùng sẽ phải đổi mật khẩu khi đăng nhập lần tới.", "Password reset. The user will have to change it at the next sign-in.");
        Add("Tên đăng nhập / Email (*):", "Username / Email (*):");
        Add("Chưa có bản sao lưu File cá nhân nào của bạn trên Google Drive của tài khoản này. Nếu bạn dùng tên đăng nhập khác trên máy cũ, hãy tick ô bên dưới để xem thêm.", "There are no personal file backups for your account on this Google Drive. If you used a different username on the old computer, select the box below to see more.");
        Add("Chưa có bản sao lưu nào trên Google Drive của tài khoản này. Lưu ý: bản sao lưu tạo bằng phiên bản cũ hơn 3.6.1 sẽ không hiện ở đây.", "There are no backups on this account's Google Drive. Note: backups created with versions earlier than 3.6.1 are not shown here.");
        Add("Chưa có bản sao lưu nào của bạn trên Google Drive của tài khoản này. Nếu bạn dùng tên đăng nhập khác trên máy cũ, hãy tick ô bên dưới để xem thêm. Bản sao lưu tạo bằng phiên bản cũ hơn 3.6.1 sẽ không hiện ở đây.", "There are no backups for your account on this Google Drive. If you used a different username on the old computer, select the box below to see more. Backups created with versions earlier than 3.6.1 are not shown here.");
        Add("Tìm thấy {0} bản sao lưu File cá nhân, mới nhất ở trên cùng. Chọn một bản rồi bấm Khôi phục.", "Found {0} personal file backups, newest first. Select one and choose Restore.");
        Add("Tìm thấy {0} bản sao lưu, mới nhất ở trên cùng. Cột Thuộc về: Của bạn là bản tạo từ tài khoản này, Bản cũ là bản tạo trước khi tách theo người dùng. Chọn một bản rồi bấm Khôi phục.", "Found {0} backups, newest first. In the Owner column, Yours means the backup was created from this account, and Legacy means it predates per-user separation. Select a backup and choose Restore.");
        Add("Nguồn: {0}\nNgười tạo: {1}\nSẽ nhập {2} tài khoản; bỏ qua {3} mục trùng.\nMật khẩu không hiển thị trong phần xem trước.", "Source: {0}\nCreated by: {1}\nWill import {2} accounts; skip {3} duplicates.\nPasswords are hidden in the preview.");
        Add("Bản sao lưu Google Drive: {0} ({1})\nNgười tạo: {2}\nSẽ nhập {3} tài khoản; bỏ qua {4} mục trùng. Mật khẩu không hiển thị.", "Google Drive backup: {0} ({1})\nCreated by: {2}\nWill import {3} accounts; skip {4} duplicates. Passwords are hidden.");
        Add("Bản sao lưu Google Drive: {0} ({1})\nNgười tạo: {2}\nSẽ khôi phục {3} file; bỏ qua {4} file trùng. Dung lượng: {5}.", "Google Drive backup: {0} ({1})\nCreated by: {2}\nWill restore {3} files; skip {4} duplicate files. Size: {5}.");
        Add("Bản sao lưu Google Drive: {0} ({1})\nNgười tạo: {2}\nSẽ khôi phục {3} file; bỏ qua {4} file trùng. Dung lượng: {5}. Bỏ qua {6} mục không hợp lệ.", "Google Drive backup: {0} ({1})\nCreated by: {2}\nWill restore {3} files; skip {4} duplicate files. Size: {5}. Skip {6} invalid entries.");
        Add("Làm mới", "Refresh");
        Add("Sao chép", "Copy");
        Add("📋 Sao chép", "📋 Copy");
        Add("Sao chép tên đăng nhập", "Copy username");
        Add("Sao chép mật khẩu", "Copy password");
        Add("Thao tác khác", "More actions");
        Add("Đặt lại", "Reset");
        Add("Đặt lại mật khẩu", "Reset password");
        Add("Chưa đăng nhập", "Not signed in");
        Add("Sẽ nhập", "Will import");
        Add("Copy mã (clipboard tự xóa)", "Copy code (clipboard clears automatically)");
        Add("Ctrl+Shift+,", "Ctrl+Shift+,");
        Add("Cụm mật khẩu sao lưu", "Backup passphrase");
        Add("Cụm mật khẩu:", "Passphrase:");
        Add("Cụm từ dễ nhớ", "Memorable passphrase");
        Add("Đã có tài khoản? Đóng cửa sổ này để quay lại đăng nhập.", "Already have an account? Close this window to return to sign in.");
        Add("Đã xóa", "Deleted");
        Add("Dán URI otpauth:// hoặc secret Base32; đọc QR chuyển tài khoản Google Authenticator; nhập export JSON không mã hóa từ Aegis/2FAS hoặc file URI otpauth.", "Paste an otpauth:// URI or Base32 secret; scan a Google Authenticator transfer QR code; or import an unencrypted Aegis/2FAS JSON export or otpauth URI file.");
        Add("Đăng ký tài khoản - TQLTK", "Register account - TQLTK");
        Add("Đăng nhập - TQLTK", "Sign in - TQLTK");
        Add("Đăng nhập bằng Windows Hello", "Sign in with Windows Hello");
        Add("Role=Admin", "Role=Administrator");
        Add("Role=User", "Role=User");
        Add("Đánh dấu yêu thích", "Mark as favorite");
        Add("Đặt / Đổi", "Set / change");
        Add("Đặt mật khẩu mới cho tài khoản của bạn.", "Set a new password for your account.");
        Add("Dấu phân cách", "Separator");
        Add("Đây là 8 mã khôi phục, mỗi mã dùng được 1 lần khi bạn mất điện thoại.", "These are 8 recovery codes. Each can be used once if you lose your phone.");
        Add("Dịch vụ", "Service");
        Add("Điền thông tin tài khoản cần lưu", "Enter the account details to save");
        Add("ĐN cuối", "Last sign-in");
        Add("Độ dài (8–64)", "Length (8–64)");
        Add("Đọc QR từ ảnh…", "Read QR code from image…");
        Add("Đọc URI / secret", "Read URI / secret");
        Add("Đổi", "Change");
        Add("Đổi mật khẩu", "Change password");
        Add("Đổi mật khẩu - TQLTK", "Change password - TQLTK");
        Add("Đóng", "Close");
        Add("Đóng (Esc)", "Close (Esc)");
        Add("Đồng bộ", "Sync");
        Add("Đồng bộ vault", "Sync vault");
        Add("Dữ liệu của bản sao lưu được nhập thêm vào tài khoản hiện tại. Tài khoản trùng sẽ được bỏ qua, dữ liệu hiện có không bị xóa.", "Backup data is added to the current account. Duplicate accounts are skipped; existing data is not deleted.");
        Add("Đưa ghi chú vào thùng rác", "Move note to Trash");
        Add("Đưa về giữa", "Center window");
        Add("Dùng Google / Microsoft Authenticator hoặc Authy khi đăng nhập bằng mật khẩu.", "Use Google Authenticator, Microsoft Authenticator, or Authy when signing in with a password.");
        Add("Dùng ít nhất 10 ký tự, nên là một câu gồm 4 từ trở lên mà bạn nhớ được. Hãy ghi lại cụm này ở nơi an toàn: nếu mất cả máy lẫn cụm này thì không thể mở các bản sao lưu.", "Use at least 10 characters; a memorable phrase of 4 or more words is recommended. Store it safely: if you lose both your device and this phrase, you cannot open your backups.");
        Add("Dùng khi cửa sổ bị lệch ra ngoài màn hình.", "Use this if the window is off-screen.");
        Add("Dung lượng", "Size");
        Add("Dùng mật khẩu này", "Use this password");
        Add("Đường dẫn", "Path");
        Add("Ghi chú", "Notes");
        Add("Ghi chú:", "Notes:");
        Add("Giao diện", "Appearance");
        Add("Giúp mở bản sao lưu trên máy khác hoặc sau khi cài lại Windows.", "Use it to open backups on another computer or after reinstalling Windows.");
        Add("Gmail", "Gmail");
        Add("Gmail:", "Gmail:");
        Add("Google Drive (BETA)", "Google Drive (BETA)");
        Add("Hãy lưu các mã này ở nơi an toàn NGOÀI máy tính (giấy, két, trình quản lý khác). Mỗi mã chỉ dùng được một lần và sẽ không hiển thị lại. Các mã cũ vẫn dùng được cho đến khi bạn bấm \"Tôi đã lưu, hoàn tất\".", "Store these codes somewhere safe OFF your computer (paper, a safe, or another password manager). Each code works once and will not be shown again. Old codes remain valid until you select \"I have saved them, finish\".");
        Add("Hãy lưu ở nơi an toàn (giấy, két sắt...). Mã chỉ hiển thị MỘT LẦN duy nhất.", "Store this somewhere safe (on paper or in a safe). The code is shown only ONCE.");
        Add("HĐ", "Updated");
        Add("Hệ điều hành, .NET, tiến trình và quyền hiện tại.", "Operating system, .NET, process, and current permissions.");
        Add("Hệ thống", "System");
        Add("Hiện / Ẩn MK", "Show / hide password");
        Add("Hiện cả bản sao lưu của người dùng khác (khi dùng chung một tài khoản Google)", "Show backups from other users (when sharing a Google account)");
        Add("Hiện giá trị", "Show value");
        Add("Hiện mật khẩu", "Show password");
        Add("Họ tên", "Full name");
        Add("Họ tên (*):", "Full name (*):");
        Add("Họ tên:", "Full name:");
        Add("Hỗ trợ Chrome, Bitwarden, LastPass, TQLTK. Có xem trước trước khi nhập.", "Supports Chrome, Bitwarden, LastPass, and TQLTK. Preview entries before importing.");
        Add("Hủy", "Cancel");
        Add("Id", "ID");
        Add("Import", "Import");
        Add("Import tài khoản từ CSV", "Import accounts from CSV");
        Add("Kết quả dự kiến", "Expected result");
        Add("Khóa / Mở", "Lock / unlock");
        Add("Khóa khi không thao tác hoặc ngay khi cửa sổ bị thu nhỏ.", "Lock after inactivity or immediately when the window is minimized.");
        Add("Khóa khi thu nhỏ", "Lock when minimized");
        Add("khoảng trắng", "spaces");
        Add("Khôi phục", "Restore");
        Add("Khôi phục File cá nhân", "Restore personal files");
        Add("Khôi phục từ Google Drive", "Restore from Google Drive");
        Add("Khôi phục vault", "Restore vault");
        Add("Không có mục nào khớp.", "No matching items.");
        Add("Không quét được? Nhập khóa này thủ công vào ứng dụng:", "Can't scan it? Enter this key manually in the app:");
        Add("Kích thước", "Size");
        Add("Kiểm tra", "Check");
        Add("Kiểm tra bản sao lưu", "Verify backup");
        Add("Kiểm tra mật khẩu rò rỉ", "Check for breached passwords");
        Add("Kiểu mật khẩu", "Password type");
        Add("Ký hiệu", "Symbols");
        Add("Lịch sử mật khẩu", "Password history");
        Add("Lịch sử MK", "Password history");
        Add("Loại", "Type");
        Add("Loại ký tự dễ nhầm lẫn (O/0, I/l/1)", "Exclude ambiguous characters (O/0, I/l/1)");
        Add("Lưu", "Save");
        Add("Lưu thay đổi", "Save changes");
        Add("Lưu toàn bộ tài khoản ra file .vault đã mã hóa.", "Save all accounts to an encrypted .vault file.");
        Add("Lưu TOTP", "Save TOTP");
        Add("Lưu trường", "Save field");
        Add("Mã 6 số trong ứng dụng:", "6-digit code from the app:");
        Add("Mã khôi phục 2FA", "2FA recovery codes");
        Add("Mã khôi phục mới", "New recovery codes");
        Add("Mã định danh", "ID");
        Add("Mã TOTP", "TOTP code");
        Add("Mã xác thực cho website (TOTP)", "Website authenticator code (TOTP)");
        Add("Mã xác thực được lưu cùng vault đã mã hóa. QR/secret chứa khóa bí mật; nhập chỉ từ nguồn bạn tin cậy.", "Authenticator data is stored in the encrypted vault. QR codes and secrets contain private keys; import them only from trusted sources.");
        Add("Mã xác thực TOTP", "TOTP authenticator code");
        Add("Made By 天地と月の造化 - TQLTK v3.6.7 (BETA) | © 2026 Namtudifreefire | MIT License", "Made by 天地と月の造化 - TQLTK v3.6.7 (BETA) | © 2026 Namtudifreefire | MIT License");
        Add("Mất điện thoại? Nhập mã khôi phục (dạng XXXXX-XXXXX) vào ô trên.", "Lost your phone? Enter a recovery code (XXXXX-XXXXX) above.");
        Add("Mật khẩu (*):", "Password (*):");
        Add("Mật khẩu đăng nhập", "Sign-in password");
        Add("Mật khẩu hiện tại:", "Current password:");
        Add("Mật khẩu mới:", "New password:");
        Add("Mật khẩu mới: tối thiểu 8 ký tự nếu bật 2FA, nếu không tối thiểu 15 ký tự", "New password: at least 8 characters with 2FA enabled; otherwise at least 15 characters");
        Add("Mật khẩu trước đây", "Previous passwords");
        Add("Mật khẩu", "Password");
        Add("Mật khẩu:", "Password:");
        Add("Màu sắc và vị trí cửa sổ.", "Theme and window position.");
        Add("Mở", "Open");
        Add("Mục", "Item");
        Add("Mục được nhập", "Imported items");
        Add("Nạp lại từ file .vault, tài khoản trùng sẽ được bỏ qua.", "Load from a .vault file; duplicate accounts are skipped.");
        Add("Nén và mã hóa kho file (tối đa 100 MB) rồi đưa lên Drive.", "Compress and encrypt the file archive (up to 100 MB), then upload it to Drive.");
        Add("Ngẫu nhiên", "Random");
        Add("Ngày đổi", "Changed");
        Add("Ngày đổi MK", "Password changed");
        Add("Ngày sửa", "Modified");
        Add("Ngày tạo", "Created");
        Add("Ngày tạo tài khoản:", "Account created:");
        Add("Nhãn", "Tags");
        Add("Nhãn (phân cách bằng dấu phẩy)", "Tags (comma-separated)");
        Add("Nhập", "Import");
        Add("Nhập / đổi TOTP", "Add / change TOTP");
        Add("Nhập CSV", "Import CSV");
        Add("Nhập file Authenticator…", "Import authenticator file…");
        Add("Nhập lại cụm mật khẩu:", "Confirm passphrase:");
        Add("Nhập mã TOTP", "Enter TOTP code");
        Add("Nhập mật khẩu hiện tại và mật khẩu mới", "Enter your current and new passwords");
        Add("Nhập URL http:// hoặc https:// chính thức. Chỉ so khớp chính xác hostname.", "Enter the official http:// or https:// URL. Hostnames are matched exactly.");
        Add("Nhập vault (Restore)", "Import vault (restore)");
        Add("Ngôn ngữ", "Language");
        Add("Ngôn ngữ:", "Language:");
        Add("Tiếng Việt", "Vietnamese");
        Add("Tiếng Anh", "English");
        Add("Chọn ngôn ngữ hiển thị của ứng dụng.", "Choose the app's display language.");
        Add("Sau", "After");
        Add("Quản lý tài khoản", "Account manager");
        Add("Quản lý tài khoản - TQLTK", "Account manager - TQLTK");
        Add("Quản lý trường…", "Manage fields…");
        Add("Quản trị", "Administration");
        Add("Reset cài đặt", "Reset settings");
        Add("Sáng / Tối", "Light / dark");
        Add("Sao lưu", "Back up");
        Add("Sao lưu File cá nhân", "Back up personal files");
        Add("Sao lưu tài khoản ra file trên máy, hoặc nhập từ file.", "Back up accounts to a local file, or import them from a file.");
        Add("SĐT", "Phone");
        Add("SĐT:", "Phone:");
        Add("Sinh 8 mã mới. Mã cũ chỉ bị vô hiệu khi bạn bấm hoàn tất.", "Generate 8 new codes. Existing codes remain valid until you finish.");
        Add("Số điện thoại:", "Phone number:");
        Add("Số từ (6–12)", "Word count (6–12)");
        Add("Sửa", "Edit");
        Add("Sửa thông tin user", "Edit user");
        Add("Tải bản sao lưu về, không ghi đè file nào.", "Download a backup without overwriting any files.");
        Add("Tải danh sách tài khoản lên Drive (giữ 5 bản gần nhất).", "Upload the account list to Drive (keep the 5 most recent backups).");
        Add("Tài khoản", "Account");
        Add("Tài khoản đã lưu", "Saved accounts");
        Add("Tài khoản và cài đặt", "Accounts and settings");
        Add("Tài khoản và ghi chú giữ lại 30 ngày. Xóa hẳn cần xác thực lại.", "Accounts and notes are kept for 30 days. Permanent deletion requires reauthentication.");
        Add("Tài liệu (File cá nhân)", "Files (personal files)");
        Add("Tạo / tùy chỉnh mật khẩu", "Generate / customize password");
        Add("Tạo lại", "Regenerate");
        Add("Tạo lúc", "Created at");
        Add("Tạo mật khẩu", "Generate password");
        Add("Tạo tài khoản mới để sử dụng TQLTK", "Create an account to use TQLTK");
        Add("Tạo text", "Create text");
        Add("Tạo, sửa, khóa, phân quyền, xóa người dùng và xem log.", "Create, edit, lock, manage roles, delete users, and view logs.");
        Add("Tên", "Name");
        Add("Tên đăng nhập", "Username");
        Add("Tên đăng nhập (*):", "Username (*):");
        Add("Tên đăng nhập (không đổi):", "Username (cannot be changed):");
        Add("Tên đăng nhập:", "Username:");
        Add("Tên dịch vụ / website (*):", "Service / website name (*):");
        Add("Tên ĐN:", "Username:");
        Add("Tên file", "File name");
        Add("Tên trường", "Field name");
        Add("Thêm", "Add");
        Add("Thêm mã xác thực của website", "Add website authenticator");
        Add("Thêm mới", "Add new");
        Add("Thông tin", "Information");
        Add("Thông tin cá nhân", "Personal information");
        Add("Thông tin hệ thống", "System information");
        Add("Thông tin máy và đặt lại cài đặt.", "Device information and reset settings.");
        Add("Thông tin nhạy cảm", "Sensitive information");
        Add("Thư mục", "Folder");
        Add("Thư mục / nhãn", "Folder / tags");
        Add("Thùng rác", "Trash");
        Add("Thùng rác đang trống.", "Trash is empty.");
        Add("Thuộc về", "Owner");
        Add("Tiếp tục khôi phục", "Continue restore");
        Add("Tôi đã lưu, hoàn tất", "I have saved them, finish");
        Add("Tổng quan bảo mật", "Security overview");
        Add("TQLTK - Trình Quản Lý Tài Khoản", "TQLTK - Account Manager");
        Add("TQLTK Made By thienthuongthienha_duyngadocton_", "TQLTK made by thienthuongthienha_duyngadocton_");
        Add("Trạng thái", "Status");
        Add("Trình Quản Lý Tài Khoản", "Account Manager");
        Add("Trường tùy chỉnh", "Custom fields");
        Add("Trường tùy chỉnh được lưu bên trong vault mã hóa. Trường nhạy cảm được che khi nhập và không dùng trong tìm kiếm.", "Custom fields are stored inside the encrypted vault. Sensitive fields are masked while editing and excluded from search.");
        Add("Từ 3 ký tự trở lên, không dấu, không khoảng trắng", "At least 3 characters; no accents or spaces");
        Add("Tự động đổi theme theo giờ", "Automatically change theme by time");
        Add("Tự khóa ứng dụng", "Auto-lock app");
        Add("Tùy chỉnh", "Customize");
        Add("Tùy chỉnh bộ tạo mật khẩu", "Customize password generator");
        Add("URI otpauth://totp/... hoặc secret Base32", "otpauth://totp/... URI or Base32 secret");
        Add("URL chính thức của website:", "Official website URL:");
        Add("Username / Email (*):", "Username / Email (*):");
        Add("Vai trò", "Role");
        Add("Vai trò:", "Role:");
        Add("Ví dụ: tenban@gmail.com", "Example: name@example.com");
        Add("Vị trí cửa sổ", "Window position");
        Add("Website", "Website");
        Add("Windows Hello (BETA)", "Windows Hello (BETA)");
        Add("Xác minh file .vault hoặc .tqf và xem số lượng dữ liệu trước khi khôi phục; không sửa vault.", "Verify a .vault or .tqf file and preview its contents before restoring; the vault is not modified.");
        Add("Xác nhận", "Confirm");
        Add("Xác nhận mật khẩu (*):", "Confirm password (*):");
        Add("Xác nhận mật khẩu mới:", "Confirm new password:");
        Add("Xác nhận và bật", "Confirm and enable");
        Add("Xác thực 2 bước", "Two-factor authentication");
        Add("Xem trước khôi phục", "Restore preview");
        Add("Xóa", "Delete");
        Add("Xóa hẳn", "Delete permanently");
        Add("Xóa theme, kích thước, vị trí cửa sổ và user đăng nhập cuối.", "Clear theme, window size and position, and last signed-in user.");
        Add("Xóa TOTP", "Remove TOTP");
        Add("Xuất", "Export");
        Add("Xuất / nhập dữ liệu", "Data export / import");
        Add("Xuất CSV", "Export CSV");
        Add("Xuất vault (Backup)", "Export vault (backup)");
        Add("Yêu thích", "Favorites");
        Add("Bạn chưa bấm hoàn tất.\nNếu đóng bây giờ, các mã mới sẽ bị hủy và mã khôi phục cũ vẫn dùng như trước.\n\nĐóng cửa sổ?", "You have not finished yet.\nIf you close now, the new codes will be discarded and the old recovery codes will remain valid.\n\nClose the window?");
        Add("Bạn chưa bật xác thực 2 bước nên chưa có mã khôi phục.", "Two-factor authentication is not enabled, so there are no recovery codes yet.");
        Add("Bạn không có quyền truy cập chức năng quản trị.", "You do not have permission to access administration features.");
        Add("Bạn đã lưu mã khôi phục chưa?\nMã này chỉ hiển thị một lần và sẽ không xem lại được.", "Have you saved the recovery codes?\nThey are shown only once and cannot be viewed again.");
        Add("Bạn đã nhập sai quá nhiều lần.", "Too many incorrect attempts.");
        Add("Bản sao lưu của người dùng khác", "Backup from another user");
        Add("Bản sao lưu hợp lệ", "Backup verified");
        Add("Bản sao lưu trên Google Drive (nếu có) không bị ảnh hưởng.", "Google Drive backups, if any, are not affected.");
        Add("Bị từ chối", "Denied");
        Add("Chưa bật Windows Hello", "Windows Hello is not enabled");
        Add("Chưa thể khôi phục", "Unable to restore");
        Add("Chỉ tiếp tục nếu đây là dữ liệu của chính bạn (ví dụ bạn dùng tên đăng nhập khác trên máy mới).", "Continue only if this is your own data (for example, if you used a different username on your new computer).");
        Add("Cài đặt bảo mật", "Security settings");
        Add("Cảnh báo bảo mật", "Security warning");
        Add("Dịch vụ vẫn có thể thấy địa chỉ IP và tiền tố hash. Bạn chỉ tiếp tục nếu đồng ý gửi yêu cầu này.", "The service can still see your IP address and hash prefix. Continue only if you agree to send this request.");
        Add("Hãy cài đặt vân tay / khuôn mặt / PIN trong Cài đặt Windows rồi thử lại.", "Set up your fingerprint, face, or PIN in Windows Settings, then try again.");
        Add("Hãy xóa bớt hoặc xuất bớt file ra ngoài rồi thử lại.", "Remove some files or export them elsewhere, then try again.");
        Add("Kho File cá nhân", "Personal file vault");
        Add("Kho File cá nhân đang trống, không có gì để sao lưu.", "The personal file vault is empty; there is nothing to back up.");
        Add("Không khả dụng", "Unavailable");
        Add("Không: giữ nguyên để đúng người đã đặt cụm đó nhận sau.", "No: keep it unchanged so the person who set the passphrase can receive it later.");
        Add("Kết quả nhập CSV", "CSV import result");
        Add("Lỗi", "Error");
        Add("Máy tính không hỗ trợ Windows Hello.", "This computer does not support Windows Hello.");
        Add("Mỗi trường tùy chỉnh cần có tên.", "Each custom field must have a name.");
        Add("Người dùng", "User");
        Add("Quá dung lượng", "Too large");
        Add("Quản trị viên", "Administrator");
        Add("Thiếu tên trường", "Field name required");
        Add("Thông báo", "Notice");
        Add("Tiếp tục?", "Continue?");
        Add("Tên trường tùy chỉnh không được trùng nhau.", "Custom field names must be unique.");
        Add("Tải bản sao lưu từ Google Drive thất bại.", "Failed to download the backup from Google Drive.");
        Add("Từ chối", "Denied");
        Add("Từ chối truy cập", "Access denied");
        Add("Từ giờ các bản sao lưu mới từ máy này sẽ mở được bằng cùng cụm đó.", "New backups from this computer can now be opened with the same passphrase.");
        Add("URL không hợp lệ", "Invalid URL");
        Add("URL phải là địa chỉ HTTP hoặc HTTPS hợp lệ và không chứa thông tin đăng nhập trong URL.", "The URL must be a valid HTTP or HTTPS address and must not contain credentials.");
        Add("Vào Bảo mật → Bật xác thực 2 bước (Authenticator) để có cách xác thực dự phòng.", "Go to Security → Enable two-factor authentication (Authenticator) to set up a backup sign-in method.");
        Add("Xuất CSV cần xác thực bằng Windows Hello nhưng máy này chưa hỗ trợ hoặc chưa thiết lập.", "CSV export requires Windows Hello, but it is unavailable or not set up on this computer.");
        Add("Xác nhận Reset", "Confirm reset");
        Add("Xác nhận sao lưu", "Confirm backup");
        Add("Xác nhận thay đổi domain", "Confirm domain change");
        Add("Xóa dữ liệu của người dùng", "Delete user data");
        Add("Xóa ghi chú", "Delete note");
        Add("Xóa toàn bộ cài đặt (theme, kích thước, vị trí, user cuối)?\n\nKhởi động lại app để áp dụng toàn bộ.", "Clear all settings (theme, size, position, last user)?\n\nRestart the app to apply all changes.");
        Add("Xóa vĩnh viễn luôn các dữ liệu này?\n\n", "Permanently delete this data?\n\n");
        Add("Đồng bộ thất bại.", "Sync failed.");
        Add("Đã copy username!", "Username copied!");
        Add("Đã hủy", "Cancelled");
        Add("Đã hủy kiểm tra vì phiên đăng nhập không còn hoạt động.", "The check was cancelled because the sign-in session is no longer active.");
        Add("Đã reset cài đặt.\n\nKhởi động lại app để áp dụng toàn bộ.", "Settings were reset.\n\nRestart the app to apply all changes.");
        Add("Đã sao chép", "Copied");
        Add("Đã tạo mã khôi phục mới. Các mã cũ không còn dùng được.", "New recovery codes were generated. The old codes are no longer valid.");
        Add("Đã tắt tự động đổi theme theo giờ.", "Automatic theme switching is off.");
        Add("Đã tắt xác thực 2 bước.", "Two-factor authentication is disabled.");
        Add("Đã đưa cửa sổ về giữa màn hình.", "The window was centered on the screen.");
        Add("Đưa ghi chú hiện tại vào thùng rác 30 ngày?", "Move the current note to Trash for 30 days?");
        Add("• Có: xóa hết, không thể khôi phục.", "• Yes: delete everything permanently.");
        Add("• Hủy: không xóa gì cả.\n\n", "• Cancel: do not delete anything.\n\n");
        Add("Mã cũ vẫn còn hiệu lực. Hãy đóng cửa sổ và thử lại.", "The old codes are still valid. Close the window and try again.");
        Add("Bản sao lưu tạo bằng phiên bản cũ hơn 3.6.1 sẽ không hiện ở đây.", "Backups created with versions earlier than 3.6.1 are not shown here.");
        Add("Lưu ý: bản sao lưu tạo bằng phiên bản cũ hơn 3.6.1 sẽ không hiện ở đây.", "Note: backups created with versions earlier than 3.6.1 are not shown here.");
        Add("Chưa bật đăng nhập bằng Windows Hello.", "Windows Hello sign-in is not enabled.");
        Add("Không lấy được danh sách sao lưu.", "Could not retrieve the backup list.");
        Add("Mã không đúng.", "Incorrect code.");
        Add("Nếu bạn dùng tên đăng nhập khác trên máy cũ, hãy tick ô bên dưới để xem thêm.", "If you used a different username on the old computer, select the box below to see more.");
        Add("Phiên đăng nhập không còn hợp lệ.", "The sign-in session is no longer valid.");
        Add("Thông tin TOTP không hợp lệ.", "The TOTP information is invalid.");
        Add("Tắt Windows Hello", "Disable Windows Hello");
        Add("Tắt xác thực 2 bước", "Disable two-factor authentication");
        Add("Đang kết nối Google Drive... (lần đầu có thể cần đăng nhập Google trong trình duyệt)", "Connecting to Google Drive... (you may need to sign in to Google in your browser the first time)");
        Add("Đọc secret hoặc QR và chọn một mục trước.", "Read a secret or QR code and select an entry first.");
        Add("Đọc được {count} mục. Chọn một mục để xem thông tin.", "Found {count} entries. Select one to view its details.");
        Add("✓ Tạo quản trị viên", "✓ Create administrator");
        Add("🛡 Tạo quản trị viên đầu tiên", "🛡 Create the first administrator");
        Add("Tạo quản trị viên đầu tiên - TQLTK", "Create the first administrator - TQLTK");
        Add("✓ Đang bật đăng nhập bằng Windows Hello.", "✓ Windows Hello sign-in is enabled.");
        Add("👑 Quản trị viên (Admin)", "👑 Administrator (Admin)");
        Add("👤 Người dùng (User)", "👤 User");
        Add("● Đang bật", "● On");
        Add("2FA: đang tắt; nên bật để tăng bảo vệ khi đăng nhập bằng mật khẩu.", "2FA: off; enable it to improve password sign-in security.");
        Add("2FA: đã bật cho đăng nhập bằng mật khẩu.", "2FA: enabled for password sign-in.");
        Add("Bản sao lưu này được bảo vệ bằng cụm mật khẩu. Hãy nhập cụm bạn đã đặt khi sao lưu.", "This backup is protected by a passphrase. Enter the passphrase you set when creating the backup.");
        Add("Chưa có bản sao lưu File cá nhân nào của bạn trên Google Drive của tài khoản này.", "There are no personal file backups for your account on this Google Drive.");
        Add("Chưa có bản sao lưu File cá nhân nào trên Google Drive của tài khoản này.", "There are no personal file backups on this account's Google Drive.");
        Add("Chưa có bản sao lưu nào của bạn trên Google Drive của tài khoản này.", "There are no backups for your account on this Google Drive.");
        Add("Chưa có bản sao lưu nào trên Google Drive của tài khoản này.", "There are no backups on this account's Google Drive.");
        Add("Chưa có lịch sử. Mật khẩu trước được thêm khi bạn đổi mật khẩu của mục này.", "No history yet. The previous password is added when you change this item's password.");
        Add("Chưa có mật khẩu trong vault để kiểm tra.", "There are no passwords in the vault to check.");
        Add("Cột \"Thuộc về\": \"Của bạn\" là bản tạo từ tài khoản này, \"Bản cũ\" là bản tạo trước khi tách theo người dùng.", "The \"Owner\" column: \"Yours\" means the backup was created from this account; \"Legacy\" means it was created before backups were separated by user.");
        Add("Hãy chọn mật khẩu riêng, mạnh để bảo vệ tài khoản quản trị.", "Choose a unique, strong password to protect the administrator account.");
        Add("Hãy tạo mật khẩu trước.", "Generate a password first.");
        Add("Hãy đặt mật khẩu mới để tiếp tục.", "Set a new password to continue.");
        Add("Đang kiểm tra các mật khẩu duy nhất…", "Checking unique passwords…");
        Add("Ẩn mật khẩu", "Hide password");
        Add("✏️ Sửa tài khoản", "✏️ Edit account");
        Add("✓ Mật khẩu khớp", "✓ Passwords match");
        Add("✗ Mật khẩu không khớp", "✗ Passwords do not match");
        Add("🔑 Nhập cụm mật khẩu sao lưu", "🔑 Enter backup passphrase");
        Add("🔑 Đặt cụm mật khẩu sao lưu", "🔑 Set backup passphrase");
        Add("Bạn chưa chọn tài khoản nào để import.", "You have not selected any accounts to import.");
        Add("Cho phép kiểm tra mật khẩu rò rỉ", "Allow checking for breached passwords");
        Add("Các file được nén và mã hóa bằng cụm mật khẩu sao lưu trước khi tải lên.", "Files are compressed and encrypted with the backup passphrase before upload.");
        Add("Cần cụm mật khẩu sao lưu", "Backup passphrase required");
        Add("Cụm mật khẩu không đúng.", "Incorrect passphrase.");
        Add("Cụm mật khẩu sao lưu cũ", "Old backup passphrase");
        Add("Dùng lại mật khẩu cũ", "Reuse old password");
        Add("Kho vault, 2FA và file cục bộ được lưu theo ID tài khoản.", "The vault, 2FA settings, and local files are stored by account ID.");
        Add("Không lấy được SID tài khoản Windows.", "Could not retrieve the Windows account SID.");
        Add("Không đọc được cụm cũ trên máy này. Hãy đặt cụm mới (Google Drive → Cụm mật khẩu sao lưu).", "Could not read the old passphrase on this computer. Set a new one (Google Drive → Backup Passphrase).");
        Add("Lưu cụm mật khẩu", "Save passphrase");
        Add("Máy này đã có cụm mật khẩu sao lưu.", "This computer already has a backup passphrase.");
        Add("Nên đặt cụm mật khẩu sao lưu", "Set a backup passphrase");
        Add("Nếu mất máy hoặc cài lại Windows, bạn sẽ không khôi phục được.\n\nĐặt cụm mật khẩu ngay bây giờ?", "If you lose this computer or reinstall Windows, you will not be able to restore your data.\n\nSet a passphrase now?");
        Add("Tài khoản không còn trong vault.", "The account is no longer in the vault.");
        Add("Tài khoản này chưa bật đăng nhập bằng Windows Hello.", "Windows Hello sign-in is not enabled for this account.");
        Add("Tài khoản này chưa có mã TOTP.", "This account does not have a TOTP code.");
        Add("Đặt cụm mật khẩu ngay bây giờ?", "Set a passphrase now?");
        Add("Ứng dụng sẽ kiểm tra các mật khẩu hiện tại trong vault qua Have I Been Pwned.", "The app will check current vault passwords using Have I Been Pwned.");
        Add("Bản sao lưu lên Google Drive cần cụm mật khẩu sao lưu để có thể mở trên máy khác.", "Google Drive backups need a backup passphrase so they can be opened on another computer.");
        Add("Bản sao lưu lên Google Drive chưa có cụm mật khẩu khôi phục, nên chỉ mở được trên máy này.", "This Google Drive backup has no recovery passphrase, so it can only be opened on this computer.");
        Add("Ứng dụng chỉ gửi 5 ký tự đầu của SHA-1 hash qua HTTPS; mật khẩu đầy đủ, hash đầy đủ, tên dịch vụ và username không được gửi.", "The app sends only the first 5 characters of the SHA-1 hash over HTTPS; the full password, full hash, service name, and username are not sent.");
        Add("Có: dùng cụm đó cho tài khoản này (người dùng khác sẽ không nhận được nữa).", "Yes: use this passphrase for this account (other users will no longer receive it).");
        Add("Đổi cụm mật khẩu sao lưu", "Change backup passphrase");
        Add("• Không: giữ lại. Cụm sao lưu và phiên Google được lưu theo tên đăng nhập có thể dùng lại nếu tạo tài khoản trùng tên.", "• No: keep it. The backup passphrase and Google session stored under this username can be reused if an account with the same name is created.");
        Add("Aegis export đang được mã hóa. Hãy mở khóa trong Aegis rồi xuất bản JSON không mã hóa; ứng dụng không nhận mật khẩu export.", "The Aegis export is encrypted. Unlock it in Aegis and export an unencrypted JSON file; the app cannot accept the export password.");
        Add("Backup có mục tài khoản không hợp lệ.", "The backup contains an invalid account entry.");
        Add("Backup không chứa danh sách tài khoản hợp lệ.", "The backup does not contain a valid account list.");
        Add("Bạn chưa từng sao lưu dữ liệu, và tính năng tự sao lưu khi thoát đang tắt.", "You have not backed up your data yet, and automatic backup on exit is off.");
        Add("Bạn cần đổi mật khẩu mới để tiếp tục đăng nhập.", "You must change your password before you can sign in.");
        Add("Bản sao lưu có dung lượng lớn bất thường nên bị từ chối.", "The backup was rejected because its size is unusually large.");
        Add("Bản sao lưu có mục tài khoản không hợp lệ.", "The backup contains an invalid account entry.");
        Add("Bản sao lưu cần cụm mật khẩu sao lưu.", "The backup requires a backup passphrase.");
        Add("Bản sao lưu không chứa file hợp lệ.", "The backup does not contain any valid files.");
        Add("Bản sao lưu không chứa file nào để khôi phục.", "The backup contains no files to restore.");
        Add("Bản sao lưu thiếu manifest của TQLTK.", "The backup is missing the TQLTK manifest.");
        Add("Bản sao lưu thiếu manifest hợp lệ của TQLTK.", "The backup is missing a valid TQLTK manifest.");
        Add("Manifest không khớp số lượng hoặc dung lượng dữ liệu trong bản sao lưu.", "The manifest does not match the backup's file count or data size.");
        Add("Bản sao lưu vượt giới hạn dung lượng cho phép.", "The backup exceeds the allowed size limit.");
        Add("Bản xem trước đã được sử dụng hoặc không còn dữ liệu.", "This preview has already been used or no longer contains data.");
        Add("Bắt buộc đổi mật khẩu - TQLTK", "Password change required - TQLTK");
        Add("Chưa chọn thư mục.", "No folder selected.");
        Add("Chưa có file dữ liệu để sao lưu.", "There are no data files to back up.");
        Add("Chưa giải mã được bản sao lưu. Hãy kiểm tra cụm mật khẩu hoặc file.", "Could not decrypt the backup. Check the passphrase or the file.");
        Add("Chưa rõ", "Unknown");
        Add("Chỉ có thể liên kết Windows Hello với tài khoản Windows hiện tại.", "Windows Hello can only be linked to the current Windows account.");
        Add("Chỉ hỗ trợ URI otpauth://totp.", "Only otpauth://totp URIs are supported.");
        Add("CÓ (Administrator)", "YES (Administrator)");
        Add("Các file vẫn được giữ nguyên để có thể khôi phục an toàn.", "The files remain unchanged so they can be restored safely.");
        Add("Cấu hình xác thực 2 bước (2FA)", "Two-factor authentication (2FA) configuration");
        Add("Cụm mật khẩu không đúng hoặc file bị hỏng. Hãy thử lại.", "The passphrase is incorrect or the file is damaged. Try again.");
        Add("Cụm mật khẩu sao lưu đã lưu trên máy", "Backup passphrase saved on this computer");
        Add("Cụm mật khẩu đang lưu trên máy này không mở được bản sao lưu này.", "The passphrase saved on this computer cannot open this backup.");
        Add("Của bạn", "Yours");
        Add("Cửa sổ được thu nhỏ theo tùy chọn bảo mật.", "The window was minimized according to the security setting.");
        Add("Dữ liệu Protobuf bị cắt.", "The Protobuf data is truncated.");
        Add("Dữ liệu chuyển mã không phải Base64 hợp lệ.", "The encoded data is not valid Base64.");
        Add("Dữ liệu cũ được giữ nguyên; hãy dùng tên khác hoặc khôi phục dữ liệu sau khi xác định đúng chủ sở hữu.", "The old data was left unchanged. Use a different name or restore it after confirming its owner.");
        Add("Dữ liệu ghi chú AES-GCM không hợp lệ; file được giữ nguyên.", "The AES-GCM note data is invalid; the file was left unchanged.");
        Add("Dữ liệu mã hóa không hợp lệ.", "The encrypted data is invalid.");
        Add("Email không hợp lệ.", "The email address is invalid.");
        Add("Email đã được sử dụng bởi user khác.", "This email address is already used by another user.");
        Add("Email đã được sử dụng.", "This email address is already in use.");
        Add("File gốc được giữ nguyên để phục hồi riêng.", "The original file was left unchanged for separate recovery.");
        Add("File không chứa tài khoản nào.", "The file does not contain any accounts.");
        Add("File không thuộc định dạng mã hóa của TQLTK.", "The file is not in TQLTK's encrypted format.");
        Add("File không tồn tại trong kho.", "The file is not in the vault.");
        Add("File không tồn tại.", "The file does not exist.");
        Add("File mã hóa bị thiếu dữ liệu hoặc có phần dư.", "The encrypted file is incomplete or contains trailing data.");
        Add("File mã hóa không hỗ trợ đọc hoặc tìm kích thước.", "The encrypted file does not support reading or seeking.");
        Add("File mã hóa vượt giới hạn hỗ trợ.", "The encrypted file exceeds the supported size limit.");
        Add("File nguồn không tồn tại.", "The source file does not exist.");
        Add("File nguồn đã thay đổi trong lúc mã hóa.", "The source file changed while it was being encrypted.");
        Add("File này không phải bản sao lưu File cá nhân của TQLTK.", "This is not a TQLTK personal file backup.");
        Add("File này được bảo vệ bằng cụm mật khẩu sao lưu. Hãy nhập cụm mật khẩu để mở.", "This file is protected by a backup passphrase. Enter the passphrase to open it.");
        Add("File quá lớn để mã hóa an toàn.", "The file is too large to encrypt safely.");
        Add("File sao lưu đã thay đổi sau khi xem trước. Hãy xem trước lại.", "The backup file changed after the preview. Preview it again.");
        Add("File trống.", "The file is empty.");
        Add("File tải về từ Google Drive bị rỗng.", "The file downloaded from Google Drive is empty.");
        Add("Google Drive báo lỗi.\n\n", "Google Drive returned an error.\n\n");
        Add("Hai lần nhập không giống nhau.", "The two entries do not match.");
        Add("Header file mã hóa không hợp lệ.", "The encrypted file header is invalid.");
        Add("Hết thời gian chờ khóa di chuyển dữ liệu người dùng.", "Timed out waiting for the user-data migration lock.");
        Add("Hết thời gian chờ khóa dữ liệu 2FA.", "Timed out waiting for the 2FA data lock.");
        Add("Hết thời gian chờ khóa dữ liệu đăng nhập.", "Timed out waiting for the sign-in data lock.");
        Add("Họ tên không được để trống.", "Full name cannot be empty.");
        Add("JSON export không thuộc định dạng Aegis, 2FAS hoặc danh sách Authy không mã hóa được hỗ trợ.", "The JSON export is not a supported unencrypted Aegis, 2FAS, or Authy list.");
        Add("KHÔNG (User thường)", "NO (Standard user)");
        Add("Kho File đã thay đổi sau khi xem trước. Hãy xem trước lại.", "The personal file vault changed after the preview. Preview it again.");
        Add("Kho File đã thay đổi trong lúc xem trước. Hãy thử lại.", "The personal file vault changed while being previewed. Try again.");
        Add("Khóa mã hóa file không hợp lệ.", "The file encryption key is invalid.");
        Add("Không còn ký tự hợp lệ trong một nhóm đã chọn.", "A selected character group contains no valid characters.");
        Add("Không lấy được thông tin tài khoản Windows.", "Could not retrieve the Windows account information.");
        Add("Không phải định dạng sao lưu có cụm mật khẩu.", "This is not a passphrase-protected backup format.");
        Add("Không phải định dạng sao lưu nhị phân của TQLTK.", "This is not a TQLTK binary backup format.");
        Add("Không rõ", "Unknown");
        Add("Không được dùng mật khẩu mặc định. Hãy chọn mật khẩu khác.", "The default password cannot be used. Choose a different password.");
        Add("Không đọc được dữ liệu 2FA của tài khoản này.", "Could not read this account's 2FA data.");
        Add("Không đọc được mã QR trong ảnh.", "Could not read a QR code from the image.");
        Add("Không đổi mật khẩu bắt buộc", "Required password change was not completed");
        Add("Kiểm tra sau mã hóa thất bại, dữ liệu chưa được ghi.", "Post-encryption verification failed; no data was written.");
        Add("Kích thước dữ liệu chuyển mã không hợp lệ.", "The encoded data size is invalid.");
        Add("Kích thước file mã hóa không hợp lệ.", "The encrypted file size is invalid.");
        Add("Ký tự Base32 không hợp lệ.", "The Base32 character is invalid.");
        Add("Loại trường Protobuf không được hỗ trợ.", "This Protobuf field type is not supported.");
        Add("Luồng dữ liệu không thể đọc.", "The data stream is not readable.");
        Add("Luồng dữ liệu phải hỗ trợ đọc và tìm độ dài.", "The data stream must support reading and seeking to its length.");
        Add("Luồng dữ liệu phải hỗ trợ đọc độ dài.", "The data stream must support reading its length.");
        Add("Mã khôi phục không đúng hoặc đã được dùng.", "The recovery code is incorrect or has already been used.");
        Add("Mã không đúng hoặc đã hết hạn. Kiểm tra giờ trên điện thoại và máy tính rồi thử lại.", "The code is incorrect or expired. Check the time on your phone and computer, then try again.");
        Add("Mã không đúng hoặc đã được dùng.", "The code is incorrect or has already been used.");
        Add("Mã phải gồm đúng 6 chữ số.", "The code must contain exactly 6 digits.");
        Add("Mật khẩu cũ không đúng.", "The old password is incorrect.");
        Add("Bản cũ", "Legacy");
        Add("Bỏ qua trùng", "Skip duplicates");
        Add("CSV files (*.csv)|*.csv|Tất cả file (*.*)|*.*", "CSV files (*.csv)|*.csv|All files (*.*)|*.*");
        Add("Export Authenticator (*.2fas;*.json;*.txt)|*.2fas;*.json;*.txt|Tất cả file (*.*)|*.*", "Authenticator exports (*.2fas;*.json;*.txt)|*.2fas;*.json;*.txt|All files (*.*)|*.*");
        Add("Bản sao lưu TQLTK (*.vault;*.tqf)|*.vault;*.tqf|Tất cả file (*.*)|*.*", "TQLTK backups (*.vault;*.tqf)|*.vault;*.tqf|All files (*.*)|*.*");
        Add("Vault backup (*.vault)|*.vault|Tất cả file (*.*)|*.*", "Vault backups (*.vault)|*.vault|All files (*.*)|*.*");
        Add("File cá nhân", "Personal file");
        Add("Ghi chú đã xóa", "Deleted note");
        Add("Khi thoát", "On exit");
        Add("Kho tài khoản đã lưu (vault) và bản dự phòng", "Saved account vault and backup");
        Add("Mật khẩu không hiển thị trong phần xem trước.", "Passwords are hidden in the preview.");
        Add("Một file trong bản sao lưu lớn hơn dung lượng đã khai báo.", "A file in the backup is larger than its declared size.");
        Add("Một file trong bản sao lưu ngắn hơn dung lượng đã khai báo.", "A file in the backup is smaller than its declared size.");
        Add("Nhập khóa Base32 hoặc URI otpauth trước.", "Enter a Base32 key or otpauth URI first.");
        Add("Nhập mã 6 số hiện tại từ Authenticator (không dùng mã khôi phục).", "Enter the current 6-digit code from your authenticator (do not use a recovery code).");
        Add("Nhập mã 6 số hiện tại từ Authenticator để tạo 8 mã khôi phục mới (không dùng mã khôi phục cũ).", "Enter the current 6-digit code from your authenticator to generate 8 new recovery codes (do not use an old recovery code).");
        Add("Nhập mã 6 số từ Authenticator (hoặc mã khôi phục) để xác nhận tắt.", "Enter a 6-digit authenticator code (or a recovery code) to confirm disabling it.");
        Add("Nhập mã 6 số từ Authenticator hoặc mã khôi phục (10 ký tự).", "Enter a 6-digit authenticator code or a 10-character recovery code.");
        Add("Nhập mã 6 số từ ứng dụng Authenticator trên điện thoại.", "Enter the 6-digit code from the authenticator app on your phone.");
        Add("Nhập vault", "Import vault");
        Add("Nhớ xác thực", "Remember authentication");
        Add("Nội dung file nén dài hơn dung lượng đã khai báo.", "The compressed file contents exceed the declared size.");
        Add("Nội dung ghi chú không hợp lệ; file được giữ nguyên.", "The note contents are invalid; the file was left unchanged.");
        Add("Nội dung ghi chú không đúng định dạng danh sách.", "The note contents are not in the expected list format.");
        Add("Nội dung mẫu", "Sample content");
        Add("Nội dung đã có", "Existing content");
        Add("Phản hồi Pwned Passwords vượt giới hạn an toàn.", "The Pwned Passwords response exceeded the safety limit.");
        Add("QR không chứa mục TOTP được hỗ trợ. Mã HOTP hoặc MD5 không được nhập.", "The QR code contains no supported TOTP entries. HOTP or MD5 codes cannot be imported.");
        Add("Reset mật khẩu", "Reset password");
        Add("Rất mạnh", "Very strong");
        Add("Độ mạnh: Yếu", "Strength: Weak");
        Add("Độ mạnh: Trung bình", "Strength: Medium");
        Add("Độ mạnh: Mạnh", "Strength: Strong");
        Add("Độ mạnh: Rất mạnh", "Strength: Very strong");
        Add("Sai mã hoặc hủy xác thực 2 bước", "Incorrect code or cancelled two-factor authentication");
        Add("Số từ", "Word count");
        Add("Số từ phải từ 6 đến 12.", "The word count must be between 6 and 12.");
        Add("TOTP chỉ hỗ trợ SHA1, SHA256 hoặc SHA512.", "TOTP supports only SHA1, SHA256, or SHA512.");
        Add("TOTP chỉ hỗ trợ mã 6 hoặc 8 chữ số.", "TOTP supports only 6- or 8-digit codes.");
        Add("Tham số mã hóa không hợp lệ.", "The encryption parameter is invalid.");
        Add("Thiếu tên người dùng.", "Username is required.");
        Add("Thuật toán TOTP không được hỗ trợ.", "The TOTP algorithm is not supported.");
        Add("Thủ công", "Manual");
        Add("Trung bình", "Medium");
        Add("Trường Protobuf quá lớn.", "The Protobuf field is too large.");
        Add("Trường Protobuf vượt quá dữ liệu.", "The Protobuf field exceeds the available data.");
        Add("Trường mới", "New field");
        Add("Tài khoản không tồn tại.", "The account does not exist.");
        Add("Tài khoản đã có cấu hình 2FA hoặc cần khôi phục cấu hình hiện có.", "The account already has 2FA configured or its existing configuration must be restored.");
        Add("Tài khoản đầu tiên đã được tạo. Hãy đăng ký tài khoản người dùng thông thường.", "The first account has been created. Register a standard user account.");
        Add("Tên file không hợp lệ.", "The file name is invalid.");
        Add("Tên đăng nhập chỉ được chứa chữ, số và dấu gạch dưới.", "The username may contain only letters, numbers, and underscores.");
        Add("Tên đăng nhập phải dài từ 3 đến 64 ký tự và không chứa ký tự điều khiển.", "The username must be 3 to 64 characters long and contain no control characters.");
        Add("Tên đăng nhập đã tồn tại.", "The username already exists.");
        Add("Tệp Authenticator không tồn tại, rỗng hoặc vượt quá 10 MB.", "The authenticator file is missing, empty, or larger than 10 MB.");
        Add("Tệp không phải URI TOTP hoặc JSON export được hỗ trợ.", "The file is not a supported TOTP URI or JSON export.");
        Add("Từ chối thao tác: chỉ người dùng đang hoạt động mới được thay đổi thiết lập đăng nhập của chính mình.", "Operation denied: only an active user may change their own sign-in settings.");
        Add("Từ chối thao tác: chỉ quản trị viên đang hoạt động mới được phép thực hiện.", "Operation denied: only an active administrator may perform this action.");
        Add("Từ chối thao tác: chỉ được sửa hồ sơ của chính mình hoặc hồ sơ khác khi là quản trị viên đang hoạt động.", "Operation denied: users may edit only their own profile; only an active administrator may edit another profile.");
        Add("URI chuyển mã Authenticator không hợp lệ.", "The authenticator transfer URI is invalid.");
        Add("Varint Protobuf quá dài.", "The Protobuf varint is too long.");
        Add("Vault cũ có đường dẫn trùng với tài khoản khác nên đăng nhập bị từ chối.", "Sign-in was denied because the legacy vault path conflicts with another account.");
        Add("Vault đã thay đổi sau khi xem trước. Hãy tạo bản xem trước mới.", "The vault changed after the preview. Create a new preview.");
        Add("Windows Hello thất bại, chưa bật 2FA", "Windows Hello failed; 2FA is not enabled");
        Add("Windows Hello và 2FA đều thất bại", "Both Windows Hello and 2FA failed");
        Add("Windows bị khóa (Win+L)", "Windows locked (Win+L)");
        Add("Xem trước khôi phục File cá nhân", "Preview personal file restore");
        Add("Xem trước khôi phục vault", "Preview vault restore");
        Add("Xem trước nhập vault", "Preview vault import");
        Add("Xem", "View");
        Add("Xuất vault", "Export vault");
        Add("Xác nhận bật Windows Hello", "Confirm enabling Windows Hello");
        Add("Xác nhận để copy mật khẩu", "Confirm to copy password");
        Add("Xác nhận để hiện mật khẩu", "Confirm to reveal password");
        Add("Xác nhận để xuất CSV. File sẽ chứa mật khẩu, khóa TOTP, trường tùy chỉnh và URL đầy đủ dạng chữ thường.", "Confirm CSV export. The file will contain passwords, TOTP keys, custom fields, and full URLs in plain text.");
        Add("Xác thực 2 bước thất bại hoặc đã bị hủy.", "Two-factor authentication failed or was cancelled.");
        Add("Xác thực Windows Hello thất bại", "Windows Hello authentication failed");
        Add("Xác thực Windows Hello thất bại.", "Windows Hello authentication failed.");
        Add("[TOTP không nhập được: kiểm tra secret trong file CSV.]", "[TOTP could not be imported: check the secret in the CSV file.]");
        Add("[URL không hợp lệ đã được bỏ qua khi nhập.]", "[An invalid URL was skipped during import.]");
        Add("key.dat không hợp lệ.", "key.dat is invalid.");
        Add("Không hoạt động", "Inactive");
        Add("Không rõ người tạo", "Unknown creator");
        Add("Không thể tự chọn chủ sở hữu an toàn.", "Could not safely determine the owner.");
        Add("Không xóa được cấu hình 2FA: ", "Could not delete the 2FA configuration: ");
        Add("Đã bật xác thực 2 bước.", "Two-factor authentication is enabled.");
        Add("Đã bật đăng nhập bằng Windows Hello.", "Windows Hello sign-in is enabled.");
        Add("Đã giải mã bản sao lưu và tính các mục sẽ nhập.", "The backup was decrypted and the entries to import were counted.");
        Add("Đã giải mã và kiểm tra cấu trúc; chưa thay đổi vault hiện tại.", "The data was decrypted and its structure verified; the current vault has not been changed.");
        Add("Đã giải mã và xem trước bản sao lưu; kho chưa bị thay đổi.", "The backup was decrypted and previewed; the vault has not been changed.");
        Add("Đã hủy hoặc hết thời gian chờ đăng nhập Google.", "Google sign-in was cancelled or timed out.");
        Add("Đã hủy khôi phục.", "Restore was cancelled.");
        Add("Đã hủy kiểm tra bản sao lưu.", "Backup verification was cancelled.");
        Add("Đã hủy xem trước bản sao lưu.", "Backup preview was cancelled.");
        Add("Đã hủy: chưa nhập đúng cụm mật khẩu sao lưu.", "Cancelled: the correct backup passphrase was not entered.");
        Add("Đã kiểm tra giải mã và nội dung nén; chưa thay đổi dữ liệu trong kho.", "Decryption and compressed contents were verified; no vault data was changed.");
        Add("Đã reset mật khẩu. Người dùng sẽ phải đổi mật khẩu khi đăng nhập lần tới.", "Password reset. The user will have to change the password at the next sign-in.");
        Add("Đã tạo tài khoản quản trị viên đầu tiên.", "The first administrator account was created.");
        Add("Đã tắt đăng nhập bằng Windows Hello.", "Windows Hello sign-in is disabled.");
        Add("Đăng nhập Google (token đồng bộ Drive)", "Google sign-in (Drive sync token)");
        Add("Độ dài file không hợp lệ.", "The file length is invalid.");
        Add("Độ dài luồng nguồn đã thay đổi.", "The source stream length changed.");
        Add("Độ dài phải đủ để có ít nhất một ký tự trong mỗi nhóm đã chọn.", "The length must allow at least one character from each selected group.");
        Add("Định dạng CBC cũ hoặc định dạng không được hỗ trợ không thể được mở an toàn. ", "The legacy CBC or unsupported format cannot be opened safely. ");
        Add("đừng xóa thư mục %AppData%\\PersonalInfoManager. ", "do not delete the %AppData%\\PersonalInfoManager folder. ");
        Add("File 2FA cũ có đường dẫn trùng với tài khoản khác nên được giữ nguyên.", "The legacy 2FA file conflicts with another account's path and was left unchanged.");
        Add("File quá lớn so với một bản sao lưu File cá nhân của TQLTK.", "The file is too large for a TQLTK personal file backup.");
        Add("Mật khẩu mặc định cũ đã bị vô hiệu hóa. Hãy dùng Windows Hello nếu đã bật ", "The old default password has been disabled. Use Windows Hello if it is enabled ");
        Add("Secret TOTP phải giải mã thành 10–64 byte.", "The decrypted TOTP secret must be 10–64 bytes.");
        Add("Số lượng tài khoản trong manifest không khớp dữ liệu.", "The account count in the manifest does not match the data.");
        Add("Thư mục file cũ có đường dẫn trùng với tài khoản khác nên được giữ nguyên.", "The legacy file folder conflicts with another account's path and was left unchanged.");
        Add("Thư mục file cũ trùng đường dẫn với tài khoản khác. Thư mục vẫn được giữ nguyên; ", "The legacy file folder conflicts with another account's path. The folder was left unchanged; ");
        Add("Ứng dụng KHÔNG tự tạo lại tài khoản admin mặc định để tránh ghi đè dữ liệu cũ.\n\n", "The app will NOT recreate the default admin account, to avoid overwriting existing data.\n\n");
        Add("Ứng dụng không tự tạo key mới để tránh mất dữ liệu.", "The app will not create a new key automatically, to avoid data loss.");
        Add("Ảnh QR không tồn tại, rỗng hoặc vượt quá 20 MB.", "The QR image is missing, empty, or larger than 20 MB.");
        Add("Đã đổi vai trò.", "The role was changed.");
        Add("Người dùng khác", "Another user");
        Add("☀️ Sáng", "☀️ Light");
        Add("🌙 Tối", "🌙 Dark");
        Add("⚠ Trùng lặp", "⚠ Duplicate");
        Add("✓ Mới", "✓ New");
        Add("💾 Chưa từng sao lưu", "💾 Never backed up");
        Add("💾 Sao lưu: chưa có dữ liệu", "💾 Backup: no data yet");
        Add("Đã đồng bộ lên Google Drive thành công!\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.", "Successfully synced to Google Drive!\n\nThe backup is in the 'TQLTK Backup' folder in your Google Drive.");
        Add("Đã đồng bộ vault lên Google Drive thành công!\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.", "Successfully synced the vault to Google Drive!\n\nThe backup is in the 'TQLTK Backup' folder in your Google Drive.");
        Add("Thư mục file cũ trùng đường dẫn với tài khoản khác. Không thể xem trước khôi phục an toàn.", "The legacy file folder conflicts with another account's path. A safe restore preview is not possible.");
        Add("Đã hủy kiểm tra; không có kết luận về mật khẩu.", "The check was cancelled; no conclusion about passwords was reached.");
        Add("✓ Đăng ký thành công! Vui lòng đăng nhập.", "✓ Registration successful! Please sign in.");
        Add("📁 Khôi phục File cá nhân từ Google Drive", "📁 Restore personal files from Google Drive");
        Add("Chưa đăng nhập. Vui lòng đăng nhập lại.", "You are signed out. Please sign in again.");
        Add("Cập nhật thành công!", "Updated successfully!");
        Add("Cập nhật thành công.", "Updated successfully.");
        Add("Export thành công!", "Export completed successfully!");
        Add("Tài khoản đã bị khóa hoặc không thể xác minh trạng thái.", "The account is locked or its status could not be verified.");
        Add("Tài khoản đã bị khóa, không thể truy cập kho dữ liệu.", "The account is locked and cannot access the vault.");
        Add("Tài khoản đã bị khóa.", "The account is locked.");
        Add("Tạo tài khoản thành công.", "Account created successfully.");
        Add("URI chuyển mã không có dữ liệu.", "The transfer URI contains no data.");
        Add("URI otpauth không có secret.", "The otpauth URI contains no secret.");
        Add("Vault trống, không có gì để xuất.", "The vault is empty; there is nothing to export.");
        Add("Đã bật thành công!", "Enabled successfully!");
        Add("Đã tắt thành công!", "Disabled successfully!");
        Add("Đăng nhập Google không thành công.", "Google sign-in failed.");
        Add("Đăng nhập thành công.", "Sign-in successful.");
        Add("Đổi mật khẩu thành công.", "Password changed successfully.");
        Add("Đăng ký thành công!", "Registration successful!");
        Add("Định dạng backup không được hỗ trợ hoặc không có kiểm tra toàn vẹn.", "The backup format is unsupported or does not provide integrity protection.");
        Add("File CSV rỗng hoặc không có dữ liệu.", "The CSV file is empty or contains no data.");
        Add("Ghi chú không dùng định dạng AES-GCM v2. CBC cũ không có kiểm tra toàn vẹn, nên ứng dụng từ chối mở file này và không thay đổi bản gốc.", "The note does not use the AES-GCM v2 format. Legacy CBC has no integrity check, so the app refused to open the file and left the original unchanged.");
        Add("Bản sao lưu không dùng định dạng AES-GCM v2. CBC cũ không có kiểm tra toàn vẹn, nên ứng dụng từ chối mở và giữ nguyên file để phục hồi riêng.", "The backup does not use the AES-GCM v2 format. Legacy CBC has no integrity check, so the app refused to open it and left the file unchanged for separate recovery.");
        Add("Bản build này không có credentials.json nhúng sẵn.", "This build does not include an embedded credentials.json file.");
        Add("Không đọc được danh sách tài khoản (users.json) và cũng không có bản dự phòng dùng được.", "Could not read the account list (users.json), and no usable backup is available.");
        Add("Bản sao lưu không dùng định dạng AES-GCM v2. CBC cũ không có kiểm tra toàn vẹn, ", "The backup does not use the AES-GCM v2 format. Legacy CBC has no integrity check, ");
        Add("Ghi chú không dùng định dạng AES-GCM v2. CBC cũ không có kiểm tra toàn vẹn, ", "The note does not use the AES-GCM v2 format. Legacy CBC has no integrity check, ");
        Add("Xác thực Windows Hello không thành công và bạn chưa bật xác thực 2 bước nên không thể copy mật khẩu.\n\n", "Windows Hello authentication failed and two-factor authentication is not enabled, so the password cannot be copied.\n\n");
        Add("👉 Vui lòng đăng nhập bằng mật khẩu, sau đó vào:", "👉 Please sign in with your password, then go to:");
        Add("Đăng nhập Google không thành công.\n\n", "Google sign-in failed.\n\n");
        Add("Vault cũ có đường dẫn trùng với tài khoản khác nên được giữ nguyên.", "The legacy vault path conflicts with another account, so the file was left unchanged.");
        Add("Hãy nhập cụm mật khẩu.", "Enter the passphrase.");
        Add("Tạo lại mã khôi phục", "Regenerate recovery codes");
        Add("không chỉnh sửa thủ công nội dung file đã mã hóa.", "do not edit the contents of an encrypted file manually.");
        Add("Ứng dụng KHÔNG tự tạo lại tài khoản admin mặc định để tránh ghi đè dữ liệu cũ.", "The app will NOT recreate the default admin account, to avoid overwriting existing data.");
        Add("Nếu bạn tự build từ mã nguồn, hãy tạo credentials.json của riêng bạn ", "If you build from source, create your own credentials.json ");
        Add("Không ghi được vào thư mục này:", "Could not write to this folder:");
        Add("Nguồn:", "Source:");
        Add("EnableAsync bắt đầu", "EnableAsync started");
        Add("Encrypt hoàn tất", "Encrypt completed");
        Add("HWND = IntPtr.Zero, không thể xác thực", "HWND = IntPtr.Zero; authentication is unavailable");
        Add("IsAvailableAsync không kiểm tra được (", "IsAvailableAsync check failed (");
        Add("OnExit đã chạy, mã thoát ", "OnExit ran; exit code ");
        Add("Phiên Windows ngắt kết nối (", "Windows session disconnected (");
        Add("PromptToDecrypt hoàn tất", "PromptToDecrypt completed");
        Add("VerifyAsync bắt đầu", "VerifyAsync started");
        Add("Đã tạo WinHelloProvider", "WinHelloProvider created");
        Add("Made by 天地と月の造化 - TQLTK v3.6.7 (BETA) | © 2026 Namtudifreefire | MIT License", "Made by 天地と月の造化 - TQLTK v3.6.7 (BETA) | © 2026 Namtudifreefire | MIT License");
        Add("Độ dài", "Length");
        Add("Log hôm nay", "Today's log");
        Add("Xem Log", "View Log");
        Add("Ảnh QR (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif|Tất cả file (*.*)|*.*", "QR images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif|All files (*.*)|*.*");
        Add("Không chỉnh sửa thủ công nội dung file đã mã hóa.\n\n", "Do not edit the contents of an encrypted file manually.\n\n");
        Add("không rõ người tạo", "unknown creator");
        Add("không thể tự chọn chủ sở hữu an toàn.", "could not safely determine the owner.");
        Add("kiểm tra bản sao lưu", "verify backup");
        Add("kiểm tra mật khẩu rò rỉ", "check for breached passwords");
        Add("sửa tài khoản (cửa sổ Sửa hiển thị được mật khẩu)", "edit accounts (the Edit window reveals passwords)");
        Add("xem lịch sử mật khẩu", "view password history");
        Add("xem mã TOTP", "view TOTP code");
        Add("xuất vault", "export vault");
        Add("xóa hẳn mục khỏi thùng rác", "permanently delete an item from Trash");

        Add(" mục từ QR chuyển tài khoản. Chọn mục dành cho website đang sửa.", " entries from the account transfer QR code. Select the entry for the website being edited.");
        Add("Bạn vừa dùng 1 mã khôi phục. Còn lại ", "You just used a recovery code. Remaining codes: ");
        Add("Chu kỳ TOTP phải từ 5 đến 120 giây.", "The TOTP period must be between 5 and 120 seconds.");
        Add("Chọn bản sao lưu cần kiểm tra", "Select a backup to verify");
        Add("Chọn các tài khoản bạn muốn import. Các tài khoản trùng lặp sẽ được bỏ chọn sẵn.", "Select the accounts to import. Duplicate accounts are unchecked by default.");
        Add("Chọn export từ Authenticator", "Select an Authenticator export");
        Add("Chọn file cần export.", "Select a file to export.");
        Add("Chọn một bản rồi bấm Khôi phục.", "Select a backup and choose Restore.");
        Add("Chọn một mục để khôi phục.", "Select an item to restore.");
        Add("Chọn một mục để xóa hẳn.", "Select an item to permanently delete.");
        Add("Chọn một tài khoản để sửa.", "Select an account to edit.");
        Add("Chọn một tài khoản để xem lịch sử mật khẩu.", "Select an account to view its password history.");
        Add("Chọn một tài khoản để xóa.", "Select an account to delete.");
        Add("Chọn một tài khoản.", "Select an account.");
        Add("Chọn thời gian tự khóa hợp lệ.", "Select a valid auto-lock duration.");
        Add("Chọn ít nhất một nhóm ký tự.", "Select at least one character group.");
        Add("Chọn ảnh QR từ Authenticator", "Select a QR code image from Authenticator");
        Add("Có", "Yes");
        Add("Khôi phục File cá nhân từ Drive: ", "Restore personal files from Drive: ");
        Add("Khôi phục File cá nhân từ Google Drive", "Restore personal files from Google Drive");
        Add("Khôi phục ghi chú", "Restore note");
        Add("Khôi phục tài khoản trước khi khôi phục ghi chú của tài khoản đó.", "Restore the account before restoring its note.");
        Add("Không có file nào bị ghi đè: file trùng nội dung được bỏ qua, ", "No files were overwritten. Files with duplicate content were skipped: ");
        Add("Không có mã khôi phục để lưu.", "There are no recovery codes to save.");
        Add("Không có tài khoản nào để xuất.", "There are no accounts to export.");
        Add("Không thể cập nhật thùng rác. Dữ liệu hiện có vẫn được giữ nguyên.\n\n", "Could not update Trash. Existing data was preserved.\n\n");
        Add("Không thể dọn thùng rác", "Could not empty Trash");
        Add("Không thể dọn thùng rác đã hết hạn. Dữ liệu hiện có vẫn được giữ nguyên.\n\n", "Could not empty expired Trash items. Existing data was preserved.\n\n");
        Add("Không thể giải mã file. Cụm mật khẩu không đúng hoặc file bị hỏng.", "Could not decrypt the file. The passphrase is incorrect or the file is damaged.");
        Add("Không thể giải mã file. File có thể bị hỏng hoặc không đúng định dạng.", "Could not decrypt the file. It may be damaged or in an unsupported format.");
        Add("Không thể hoàn tất kiểm tra; chưa thể kết luận an toàn. ", "Could not complete the check; safety could not be confirmed. ");
        Add("Không thể khóa chính mình.", "You cannot lock your own account.");
        Add("Không thể khóa dữ liệu đăng nhập giữa các tiến trình; yêu cầu đăng nhập bị từ chối để bảo vệ tài khoản.", "Could not lock sign-in data across processes. Sign-in was denied to protect the account.");
        Add("Không thể khôi phục mật khẩu từ lịch sử. Vault chưa được cập nhật.\n\n", "Could not restore the password from history. The vault was not changed.\n\n");
        Add("Không thể khởi động ứng dụng:\n\n", "Could not start the application:\n\n");
        Add("Không thể kiểm tra bản sao lưu: ", "Could not verify the backup: ");
        Add("Không thể lưu", "Could not save");
        Add("Không thể lưu trạng thái 2FA cũ. Đăng nhập bị từ chối để bảo vệ tài khoản.", "Could not save the previous 2FA state. Sign-in was denied to protect the account.");
        Add("Không thể lưu trạng thái 2FA; hãy thử lại. Chi tiết: ", "Could not save the 2FA state. Try again. Details: ");
        Add("Không thể lưu trạng thái giới hạn thử 2FA; lần xác thực bị từ chối. Chi tiết: ", "Could not save the 2FA attempt limit. Authentication was denied. Details: ");
        Add("Không thể mở khóa mã hóa file trên tài khoản Windows này.", "Could not unlock file encryption for this Windows account.");
        Add("Không thể mở thùng rác", "Could not open Trash");
        Add("Không thể sao chép", "Could not copy");
        Add("Không thể sao chép khóa 2FA vào clipboard an toàn. Hãy thử lại.", "Could not securely copy the 2FA key to the clipboard. Try again.");
        Add("Không thể sao chép mã khôi phục vào clipboard an toàn. Hãy thử lại.", "Could not securely copy the recovery code to the clipboard. Try again.");
        Add("Không thể sao chép mã vào clipboard an toàn. Hãy thử lại.", "Could not securely copy the code to the clipboard. Try again.");
        Add("Không thể sao chép mật khẩu vào clipboard an toàn.", "Could not securely copy the password to the clipboard.");
        Add("Không thể sao chép mật khẩu vào clipboard an toàn. Hãy thử lại.", "Could not securely copy the password to the clipboard. Try again.");
        Add("Không thể sao lưu", "Could not create the backup");
        Add("Không thể thu quyền admin cuối cùng.", "The last administrator account cannot be demoted.");
        Add("Không thể xem trước", "Could not preview");
        Add("Không thể xem trước bản sao lưu: ", "Could not preview the backup: ");
        Add("Không thể xuất file đè lên bản mã hóa trong kho.", "Cannot export a file over its encrypted copy in the vault.");
        Add("Không thể xác minh", "Could not verify");
        Add("Không thể xác minh bản sao lưu: ", "Could not verify the backup: ");
        Add("Không thể xác minh trạng thái 2FA cũ. Đăng nhập các tài khoản bị ảnh hưởng sẽ bị từ chối cho đến khi lưu được trạng thái bảo mật. ", "Could not verify the previous 2FA state. Sign-in for affected accounts will be denied until the security state can be saved. ");
        Add("Không thể xác thực", "Could not authenticate");
        Add("Không thể xác thực 2 bước", "Could not verify two-factor authentication");
        Add("Không thể xác định trạng thái file cấu hình 2FA.", "Could not determine the state of the 2FA configuration file.");
        Add("Không thể xác định trạng thái xác thực 2 bước. Đăng nhập đã bị từ chối để bảo vệ tài khoản.\n\nChi tiết: ", "Could not determine the two-factor authentication state. Sign-in was denied to protect the account.\n\nDetails: ");
        Add("Không thể xóa admin cuối cùng.", "The last administrator account cannot be deleted.");
        Add("Không thể xóa tài khoản đang đăng nhập.", "You cannot delete the account currently signed in.");
        Add("Không thể đọc hoặc mã hóa kho File cá nhân. File chưa chuyển đổi được vẫn được giữ nguyên.\n\n", "Could not read or encrypt the personal file vault. Files that could not be converted were left unchanged.\n\n");
        Add("Không thể đọc trạng thái 2FA.", "Could not read the 2FA state.");
        Add("Không thể đọc trạng thái mã hóa File: ", "Could not read the file encryption state: ");
        Add("Không thể đọc trạng thái sao lưu.", "Could not read the backup state.");
        Add("Không thể đọc đầy đủ trạng thái vault.", "Could not read the complete vault state.");
        Add("Không tìm thấy bản sao lưu này trên Google Drive (có thể đã bị xóa hoặc chuyển vào Thùng rác).\n\n", "This backup was not found on Google Drive. It may have been deleted or moved to Trash.\n\n");
        Add("Không tìm thấy danh sách mục trong Aegis export.", "No entries were found in the Aegis export.");
        Add("Không tìm thấy key.dat nhưng vẫn còn dữ liệu vault_*.dat. ", "key.dat was not found, but vault_*.dat data still exists. ");
        Add("Không tìm thấy mục TOTP hợp lệ trong tệp Authenticator.", "No valid TOTP entries were found in the Authenticator file.");
        Add("Không tìm thấy tài khoản hợp lệ trong file.", "No valid accounts were found in the file.");
        Add("Không tìm thấy tài khoản để bật 2FA.", "No account was found to enable 2FA.");
        Add("Không tìm thấy tài khoản để khóa dữ liệu 2FA.", "No account was found to lock the 2FA data.");
        Add("Không tìm thấy tài khoản để lưu trạng thái 2FA.", "No account was found to save the 2FA state.");
        Add("Không tìm thấy tài khoản để lưu trạng thái tắt 2FA.", "No account was found to save the disabled 2FA state.");
        Add("Không tìm thấy tài khoản để xác định cấu hình 2FA riêng.", "No account was found to determine the per-account 2FA configuration.");
        Add("Không tìm thấy tài khoản để xác định kho dữ liệu riêng.", "No account was found to determine the per-account data vault.");
        Add("Không tìm thấy tài khoản để xác định kho file riêng.", "No account was found to determine the per-account file vault.");
        Add("Không tìm thấy ảnh trong file.", "No image was found in the file.");
        Add("Lỗi hệ thống", "System error");
        Add("Lỗi khôi phục File cá nhân: ", "Personal file restore error: ");
        Add("Lỗi khôi phục: ", "Restore error: ");
        Add("Lỗi khởi động", "Startup error");
        Add("Lỗi kết nối mạng. Hãy kiểm tra Internet rồi thử lại.\n\n", "Network error. Check your Internet connection and try again.\n\n");
        Add("Lỗi lưu dữ liệu 2FA: ", "Error saving 2FA data: ");
        Add("Lỗi nhập CSV", "CSV import error");
        Add("Lỗi nhập CSV: ", "CSV import error: ");
        Add("Lỗi xuất CSV", "CSV export error");
        Add("Lỗi xuất CSV: ", "CSV export error: ");
        Add("Lỗi xuất file", "File export error");
        Add("Lỗi đọc CSV: ", "CSV read error: ");
        Add("Lỗi đọc log: ", "Log read error: ");
        Add("Lỗi đồng bộ File cá nhân: ", "Personal file sync error: ");
        Add("Lỗi đồng bộ: ", "Sync error: ");
        Add("Máy này đã có cụm mật khẩu sao lưu.\n\nĐổi sang cụm mới? Các bản sao lưu đã tạo trước đó ", "This computer already has a backup passphrase.\n\nChange to a new passphrase? Backups created earlier ");
        Add("Mã không đúng hoặc đã được dùng. Hãy đợi mã mới (tối đa 30 giây) rồi nhập lại.", "The code is incorrect or has already been used. Wait for a new code (up to 30 seconds) and try again.");
        Add("Mạnh", "Strong");
        Add("Mật khẩu hiện tại là mật khẩu mặc định hoặc do quản trị viên cấp. ", "The current password is a default password or was assigned by an administrator. ");
        Add("Mật khẩu mới phải khác mật khẩu cũ.", "The new password must be different from the current password.");
        Add("Sai mật khẩu. Còn ", "Incorrect password. Remaining attempts: ");
        Add("Sao lưu vault gần nhất: ", "Most recent vault backup: ");
        Add("Sao lưu vault: chưa ghi nhận bản sao lưu nào.", "Vault backup: no backup has been recorded.");
        Add("Secret phải là chuỗi Base32 hợp lệ dài 16–128 ký tự.", "The secret must be a valid Base32 string between 16 and 128 characters long.");
        Add("Tài khoản mới chưa bật 2FA nên mật khẩu phải từ 15 ký tự trở lên.", "New accounts without 2FA must use a password of at least 15 characters.");
        Add("Tên bị trùng", "Duplicate name");
        Add("Tên đăng nhập này còn dữ liệu cũ chưa thể gắn an toàn với tài khoản. ", "This username still has legacy data that cannot be safely linked to the account. ");
        Add("Tên đăng nhập phải từ 3 ký tự trở lên.", "The username must be at least 3 characters long.");
        Add("Vui lòng kiểm tra kết nối mạng và quyền truy cập Google Drive.", "Check your Internet connection and Google Drive access.");
        Add("Vui lòng kiểm tra kết nối mạng, quyền truy cập Google Drive và cửa sổ trình duyệt (nếu có).", "Check your Internet connection, Google Drive access, and the browser window if one is open.");
        Add("Vui lòng nhập họ tên.", "Enter your full name.");
        Add("Vui lòng nhập mã.", "Enter the code.");
        Add("Vui lòng nhập mật khẩu.", "Enter your password.");
        Add("Vui lòng nhập password.", "Enter your password.");
        Add("Vui lòng nhập tên dịch vụ.", "Enter a service name.");
        Add("Vui lòng nhập tên đăng nhập.", "Enter a username.");
        Add("Vui lòng nhập username.", "Enter a username.");
        Add("Vui lòng nhập đầy đủ thông tin.", "Complete all required fields.");
        Add("Vui lòng thiết lập vân tay / khuôn mặt / PIN trong Windows Settings.", "Set up your fingerprint, face, or PIN in Windows Settings.");
        Add("Vui lòng đăng nhập để sử dụng.", "Sign in to continue.");
        Add("Xác thực thất bại. Mật khẩu vẫn được ẩn.", "Authentication failed. The password remains hidden.");
        Add("Xác thực thất bại. Đã hủy copy mật khẩu.", "Authentication failed. Password copying was cancelled.");
        Add("Xác thực thất bại. Đã hủy xuất CSV.", "Authentication failed. CSV export was cancelled.");
        Add("Xóa mã TOTP khỏi tài khoản này?", "Remove the TOTP code from this account?");
        Add("Xóa tài khoản", "Delete account");
        Add("Xóa tài khoản '", "Delete account '");
        Add("Xóa vĩnh viễn '", "Permanently delete '");
        Add("Yếu", "Weak");
        Add("không có ngày xuất", "not exported");
        Add("Đã lưu cài đặt tự khóa.", "Auto-lock settings saved.");
        Add("Đã lưu thông tin cá nhân!", "Personal information saved!");
        Add("Đổi mật khẩu bắt buộc thành công", "The required password change succeeded");
        Add("Đổi mật khẩu thành công", "Password changed successfully");
        Add("Độ dài phải từ 8 đến 64 ký tự.", "The length must be between 8 and 64 characters.");
        Add("Máy này đã có cụm mật khẩu sao lưu.\n\nĐổi sang cụm mới? Các bản sao lưu đã tạo trước đó vẫn cần cụm cũ để mở, nên hãy giữ lại cụm cũ.", "This computer already has a backup passphrase.\n\nChange to a new passphrase? Existing backups still require the old passphrase, so keep it.");
        Add("Mật khẩu hiện tại là mật khẩu mặc định hoặc do quản trị viên cấp. Hãy đặt mật khẩu mới để tiếp tục.", "The current password is a default password or was assigned by an administrator. Set a new password to continue.");
        Add("Đăng ký thành công!", "Registration successful!");
        Add("Bản sao lưu lên Google Drive chưa có cụm mật khẩu khôi phục, nên chỉ mở được trên máy này. Nếu mất máy hoặc cài lại Windows, bạn sẽ không khôi phục được.\n\nĐặt cụm mật khẩu ngay bây giờ?", "This Google Drive backup has no recovery passphrase and can only be opened on this computer. If you lose this computer or reinstall Windows, you will not be able to restore it.\n\nSet a passphrase now?");
        Add("Bản sao lưu lên Google Drive cần cụm mật khẩu sao lưu để có thể mở trên máy khác.\n\nĐặt cụm mật khẩu ngay bây giờ?", "Google Drive backups need a backup passphrase so they can be opened on another computer.\n\nSet a passphrase now?");
        Add("Dùng cụm mật khẩu vừa nhập làm cụm sao lưu cho máy này?\n\nTừ giờ các bản sao lưu mới từ máy này sẽ mở được bằng cùng cụm đó.", "Use the passphrase you just entered as this computer's backup passphrase?\n\nNew backups from this computer will use the same passphrase.");
        Add("Hiện mật khẩu cần xác thực bằng Windows Hello nhưng máy này chưa hỗ trợ hoặc chưa thiết lập.\n\nHãy cài đặt vân tay / khuôn mặt / PIN trong Cài đặt Windows rồi thử lại.", "Revealing a password requires Windows Hello, but it is unavailable or not set up on this computer.\n\nSet up your fingerprint, face, or PIN in Windows Settings and try again.");
        Add("Xuất CSV cần xác thực bằng Windows Hello nhưng máy này chưa hỗ trợ hoặc chưa thiết lập.\n\nHãy cài đặt vân tay / khuôn mặt / PIN trong Cài đặt Windows rồi thử lại.", "CSV export requires Windows Hello, but it is unavailable or not set up on this computer.\n\nSet up your fingerprint, face, or PIN in Windows Settings and try again.");
        Add("Xác thực Windows Hello không thành công và bạn chưa bật xác thực 2 bước nên không thể copy mật khẩu.\n\nVào Bảo mật → Bật xác thực 2 bước (Authenticator) để có cách xác thực dự phòng.", "Windows Hello authentication failed and two-factor authentication is not enabled, so the password cannot be copied.\n\nGo to Security → Enable two-factor authentication (Authenticator) to add a backup verification method.");
        Add("Tài khoản này chưa bật đăng nhập bằng Windows Hello.\n\n👉 Vui lòng đăng nhập bằng mật khẩu, sau đó vào:\n   Tab '👤 Thông tin cá nhân' → Bật 'Đăng nhập bằng Windows Hello'.", "Windows Hello sign-in is not enabled for this account.\n\n👉 Sign in with your password, then go to:\n   '👤 Personal Information' → Enable 'Sign in with Windows Hello'.");
        Add("Tab '👤 Thông tin cá nhân' → Bật 'Đăng nhập bằng Windows Hello'.", "On the '👤 Personal Information' tab, enable 'Sign in with Windows Hello'.");
        Add("Ứng dụng sẽ kiểm tra các mật khẩu hiện tại trong vault qua Have I Been Pwned. Ứng dụng chỉ gửi 5 ký tự đầu của SHA-1 hash qua HTTPS; mật khẩu đầy đủ, hash đầy đủ, tên dịch vụ và username không được gửi. Dịch vụ vẫn có thể thấy địa chỉ IP và tiền tố hash. Bạn chỉ tiếp tục nếu đồng ý gửi yêu cầu này.", "The app will check current passwords in the vault using Have I Been Pwned. It sends only the first 5 characters of the SHA-1 hash over HTTPS; the full password, full hash, service name, and username are not sent. The service may still see your IP address and hash prefix. Continue only if you agree to send this request.");

        Add("Mở website", "Open website");
        Add("Tệp đính kèm", "Attachments");
        Add("Lịch sử sửa", "Edit history");
        Add("Thao tác hàng loạt", "Bulk actions");
        Add("Xuất mục đã chọn", "Export selected entries");
        Add("Hoàn tác nhập CSV", "Undo CSV import");
        Add("Tệp đính kèm được mã hóa cùng vault", "Attachments are encrypted with the vault");
        Add("Tối đa 10 MB mỗi tệp và 25 MB tổng cho mỗi tài khoản. Bản sao lưu vault cũng chứa các tệp này.", "Up to 10 MB per file and 25 MB total per account. Vault backups also include these files.");
        Add("Thêm tệp", "Add file");
        Add("Xóa tệp đã chọn", "Remove selected file");
        Add("Xuất tệp", "Export file");
        Add("Tên tệp", "File name");
        Add("Kích thước", "Size");
        Add("Ngày thêm", "Added");
        Add("Lưu", "Save");
        Add("Chọn tệp đính kèm", "Select attachments");
        Add("Xuất tệp đính kèm", "Export attachment");
        Add("Tệp '{0}' vượt quá giới hạn 10 MB.", "File '{0}' exceeds the 10 MB limit.");
        Add("Tổng dung lượng tệp đính kèm vượt quá giới hạn 25 MB.", "Total attachment size exceeds the 25 MB limit.");
        Add("Tệp '{0}' đã thay đổi trong lúc đọc.", "File '{0}' changed while it was being read.");
        Add("Đã xuất tệp đính kèm.", "Attachment exported.");
        Add("Không thể xuất tệp: ", "Could not export file: ");
        Add("Lịch sử thay đổi thông tin", "Account edit history");
        Add("Lịch sử chỉnh sửa", "Edit history");
        Add("Có thể hoàn tác dịch vụ, tên đăng nhập, URL, thư mục, nhãn, ghi chú và trường tùy chỉnh. Mật khẩu có lịch sử riêng.", "You can restore the service, username, URL, folder, tags, notes, and custom fields. Passwords have a separate history.");
        Add("Thời gian", "Time");
        Add("Các mục đã thay đổi", "Changed fields");
        Add("Xem trước", "Preview");
        Add("Đóng", "Close");
        Add("Hoàn tác sẽ khôi phục các trường sau về trạng thái trước thay đổi này:", "Undo will restore these fields to their previous values:");
        Add("Bạn muốn tiếp tục?", "Do you want to continue?");
        Add("Xác nhận hoàn tác", "Confirm undo");
        Add("URL website", "Website URL");
        Add("Mục yêu thích", "Favorite");
        Add("Trường tùy chỉnh", "Custom fields");
        Add("Thao tác hàng loạt", "Bulk actions");
        Add("Đã chọn {0} tài khoản.", "{0} accounts selected.");
        Add("Đặt thư mục cho các mục đã chọn", "Set folder for selected entries");
        Add("Thay nhãn hiện có bằng danh sách nhãn sau", "Replace current tags with these tags");
        Add("Đưa các mục đã chọn vào thùng rác (30 ngày)", "Move selected entries to Trash (30 days)");
        Add("Tách nhãn bằng dấu phẩy. Chỉ các trường đã chọn mới bị thay đổi.", "Separate tags with commas. Only selected fields will be changed.");
        Add("Áp dụng", "Apply");
        Add("Chọn tất cả mục mới", "Select all new entries");
        Add("Bỏ qua tất cả bản trùng", "Skip all duplicates");
        Add("Tiếp tục", "Continue");
        Add("Tổng:", "Total:");
        Add("Bản trùng:", "Duplicates:");
        Add("Sẽ thêm:", "Will add:");
        Add("Sẽ ghi đè:", "Will overwrite:");
        Add("Đối tượng trùng", "Matching entry");
        Add("Hành động", "Action");
        Add("Bỏ qua", "Skip");
        Add("Nhập mới", "Import as new");
        Add("Ghi đè", "Overwrite");
        Add("Tạo bản sao", "Import as copy");
        Add("Xem lại từng dòng. Bản trùng mặc định được bỏ qua; có thể chọn ghi đè hoặc tạo bản sao.", "Review each row. Duplicates are skipped by default; you can overwrite or import a copy.");
        Add("Đã thêm {0}, ghi đè {1}, bỏ qua {2} tài khoản. Có thể hoàn tác lô này bằng nút Hoàn tác nhập CSV.", "Added {0}, overwrote {1}, and skipped {2} accounts. Use Undo CSV import to undo this batch.");
        Add("Không có thay đổi nào để nhập.", "There are no changes to import.");
        Add("Không có lần nhập CSV nào có thể hoàn tác trên máy này.", "There is no CSV import to undo on this device.");
        Add("Không đọc được dữ liệu hoàn tác nhập CSV: ", "Could not read CSV undo data: ");
        Add("Không thể hoàn tác cả lô vì một hoặc nhiều tài khoản đã bị xóa hoặc thay đổi sau lần nhập.", "The whole batch cannot be undone because one or more accounts were deleted or changed after import.");
        Add("Không thể hoàn tác", "Cannot undo");
        Add("Hoàn tác lần nhập CSV gồm {0} tài khoản? Thay đổi sau lần nhập sẽ bị từ chối để tránh mất dữ liệu.", "Undo the CSV import of {0} accounts? Entries changed after import will be left untouched to prevent data loss.");
        Add("Đã hoàn tác lần nhập CSV.", "CSV import undone.");
        Add("Hoàn tất", "Done");
        Add("Chọn một tài khoản trước.", "Select an account first.");
        Add("Chọn một hoặc nhiều tài khoản trước.", "Select one or more accounts first.");
        Add("Mục này chưa có URL HTTP hoặc HTTPS hợp lệ.", "This entry does not have a valid HTTP or HTTPS URL.");
        Add("Không có website", "No website");
        Add("Domain {0} có dấu hiệu cần kiểm tra: {1}\n\nBạn vẫn muốn mở website này?", "Domain {0} has risk indicators: {1}\n\nDo you still want to open this website?");
        Add("Kiểm tra domain", "Check domain");
        Add("Không thể mở website: {0}", "Could not open website: {0}");
        Add("Cảnh báo domain", "Domain warning");
        Add("Website URL có dấu hiệu cần kiểm tra:", "The website URL has risk indicators:");
        Add("Chỉ tiếp tục nếu bạn đã xác minh domain chính thức.", "Continue only if you have verified the official domain.");
        Add("Tên miền chứa ký hiệu Punycode.", "The domain contains Punycode notation.");
        Add("Tên miền trộn nhiều bảng chữ cái.", "The domain mixes multiple writing systems.");
        Add("Website dùng địa chỉ IP trực tiếp.", "The website uses a direct IP address.");
        Add("Website không dùng HTTPS.", "The website does not use HTTPS.");
        Add("Tên miền gần giống ", "The domain is similar to ");
        Add("Đã thêm {0}, ghi đè {1}, bỏ qua {2} tài khoản.", "Added {0}, overwrote {1}, and skipped {2} accounts.");
        Add("Tài khoản này chưa có URL HTTP hoặc HTTPS hợp lệ.", "This account does not have a valid HTTP or HTTPS URL.");

        Add("Mở chia sẻ", "Open shared item");
        Add("Mở mục được chia sẻ", "Open shared item");
        Add("Mở gói mã hóa có thời hạn bằng khóa người gửi đã chia sẻ riêng.", "Open an encrypted, expiring package with the key provided separately by the sender.");
        Add("Quản lý liên kết", "Manage shares");
        Add("Xem và thu hồi quyền đọc Drive đã cấp cho người nhận.", "View and revoke Drive read access granted to recipients.");
        Add("Chia sẻ tạm thời", "Temporary share");
        Add("Chia sẻ tài khoản có thời hạn", "Share account temporarily");
        Add("Drive chỉ cấp quyền đọc cho địa chỉ email này. Một khóa ngẫu nhiên sẽ mã hóa gói; hãy gửi khóa qua kênh riêng.", "Drive grants read access only to this email address. A random key encrypts the package; send the key through a separate channel.");
        Add("Email người nhận", "Recipient email");
        Add("Tự hết hạn sau", "Expires after");
        Add("1 ngày", "1 day");
        Add("7 ngày", "7 days");
        Add("30 ngày", "30 days");
        Add("Khi hết hạn hoặc bị thu hồi, quyền tải file từ Drive sẽ bị hủy. Người nhận có thể giữ bản sao đã tải hoặc dữ liệu đã giải mã.", "When access expires or is revoked, Drive access to the file is removed. The recipient may keep a downloaded copy or data they already decrypted.");
        Add("Tạo liên kết", "Create share");
        Add("Mở gói chia sẻ được mã hóa", "Open encrypted share package");
        Add("Chọn tệp .tqltkshare đã tải từ Drive, nhập khóa do người chia sẻ gửi riêng, rồi xem trước trước khi thêm vào vault.", "Select the .tqltkshare file downloaded from Drive, enter the key sent separately by the sender, then preview it before adding it to the vault.");
        Add("Tệp chia sẻ", "Share file");
        Add("Chọn tệp", "Browse");
        Add("Khóa chia sẻ", "Share key");
        Add("Gói hết hạn sẽ không được mở trong ứng dụng. Việc hết hạn hoặc thu hồi Drive không xóa bản sao đã tải xuống.", "The app will not open an expired package. Expiring or revoking Drive access does not delete downloaded copies.");
        Add("Mở và xem trước", "Open and preview");
        Add("Liên kết chia sẻ đã tạo", "Share link created");
        Add("Đã tạo liên kết chia sẻ", "Share link created");
        Add("Liên kết Drive", "Drive link");
        Add("Sao chép liên kết", "Copy link");
        Add("Khóa giải mã (chỉ hiển thị lần này)", "Decryption key (shown only once)");
        Add("Sao chép khóa", "Copy key");
        Add("Gửi khóa này qua một kênh khác với email Drive. Thu hồi Drive chỉ chặn việc tải lại gói; không xóa bản sao mà người nhận đã tải hoặc giải mã.", "Send this key through a channel separate from the Drive email. Revoking Drive access only prevents downloading the package again; it cannot delete copies the recipient downloaded or decrypted.");
        Add("Liên kết Drive đang chia sẻ", "Active Drive shares");
        Add("Thu hồi quyền Drive và chuyển gói vào thùng rác. Không thể thu hồi dữ liệu đã tải hoặc giải mã.", "Revoke Drive access and move the package to the trash. Downloaded or decrypted data cannot be revoked.");
        Add("Hết hạn", "Expires");
        Add("Tạo lúc", "Created");
        Add("Thu hồi quyền", "Revoke access");
        Add("Không đặt", "Not set");
        Add("Nhập địa chỉ email người nhận hợp lệ.", "Enter a valid recipient email address.");
        Add("Email không hợp lệ", "Invalid email");
        Add("Chọn gói chia sẻ", "Select a shared package");
        Add("Gói chia sẻ TQLTK (*.tqltkshare)|*.tqltkshare|Tất cả tệp (*.*)|*.*", "TQLTK share package (*.tqltkshare)|*.tqltkshare|All files (*.*)|*.*");
        Add("Chọn tệp chia sẻ và nhập khóa.", "Select a share file and enter its key.");
        Add("Thiếu thông tin", "Missing information");
        Add("Người nhận: {0}\nCó hiệu lực đến: {1:dd/MM/yyyy HH:mm} (giờ địa phương).", "Recipient: {0}\nValid until: {1:dd/MM/yyyy HH:mm} (local time).");
        Add("Đã sao chép liên kết.", "The link was copied.");
        Add("Đã sao chép khóa. Clipboard sẽ tự xóa sau 30 giây.", "The key was copied. The clipboard will be cleared after 30 seconds.");
        Add("Không thể sao chép an toàn.", "Could not copy securely.");
        Add("Lỗi clipboard", "Clipboard error");
        Add("Chọn ít nhất một thay đổi hoặc thao tác.", "Select at least one change or action.");
        Add("Tổng: {0}", "Total: {0}");
        Add("Bản trùng: {0}", "Duplicates: {0}");
        Add("Sẽ thêm: {0}", "To add: {0}");
        Add("Sẽ ghi đè: {0}", "To overwrite: {0}");
        Add("Domain gần giống {0}.", "The domain resembles {0}.");
        Add("Mở URL đã lưu và kiểm tra một số dấu hiệu domain giả mạo phổ biến. Không theo dõi tab trình duyệt.", "Open the saved URL and check for some common signs of a spoofed domain. Browser tabs are not monitored.");
        Add("Gói có trường tài khoản vượt quá giới hạn kích thước.", "The package contains an account field that exceeds the size limit.");
        Add("Gói có nhãn không hợp lệ.", "The package contains an invalid tag.");
        Add("Gói có trường tùy chỉnh không hợp lệ hoặc quá dài.", "The package contains an invalid or oversized custom field.");
        Add("Domain {0} có dấu hiệu cần kiểm tra: {1}\n\nBạn vẫn muốn mở website này?", "Domain {0} has risk indicators: {1}\n\nDo you still want to open this website?");
        Add("Phát hiện xung đột đồng bộ", "Sync conflict detected");
        Add("Không thể kiểm tra bản trên Drive: ", "Could not check the Drive backup: ");
        Add("Chưa có mốc đồng bộ", "No sync checkpoint");
        Add("Drive đã có bản của tài khoản này nhưng máy chưa có mốc đồng bộ. Không thể xác định an toàn bản nào mới hơn.\n\nCó: tải kho trên máy này lên thành một bản riêng, giữ lại các bản Drive hiện có.\nKhông: hủy và dùng Khôi phục từ Google Drive để xem bản từ xa.\nHủy: dừng.", "Drive already has a backup for this account, but this device has no sync checkpoint, so the newer copy cannot be identified safely.\n\nYes: upload this device's vault as a separate version and keep existing Drive backups.\nNo: cancel and use Restore from Google Drive to review the remote copy.\nCancel: stop.");
        Add("Bản Drive đã đổi kể từ lần đồng bộ trước, còn kho trên máy này chưa đổi. Không có dữ liệu nào được ghi lên Drive. Dùng Khôi phục từ Google Drive để xem và nhập bản mới.", "The Drive backup changed since the last sync, while this device's vault did not. Nothing was uploaded. Use Restore from Google Drive to review and import the newer copy.");
        Add("Có thay đổi trên Drive", "Drive changes detected");
        Add("Cả kho trên máy này và bản Drive đều đã đổi kể từ lần đồng bộ trước. Đồng bộ tự động đã dừng để tránh ghi đè.\n\nCó: tải kho cục bộ lên thành bản riêng, giữ bản Drive hiện tại để so sánh.\nKhông: dừng để bạn xem và xử lý bản Drive trước.\nHủy: dừng.", "Both this device's vault and the Drive backup changed since the last sync. Automatic sync stopped to prevent overwriting either copy.\n\nYes: upload this vault as a separate version and keep the current Drive backup for comparison.\nNo: stop so you can review the Drive copy first.\nCancel: stop.");
        Add("Kho trên máy này và bản Drive đã đồng bộ; không có thay đổi mới.", "This device's vault and the Drive backup are in sync; there are no new changes.");
        Add("Đã đồng bộ", "Already in sync");
        Add("Bản Drive vừa thay đổi trong lúc chuẩn bị đồng bộ. Chưa tải dữ liệu nào lên; hãy chạy lại để kiểm tra xung đột mới.", "The Drive backup changed while sync was being prepared. Nothing was uploaded; run sync again to check for the new conflict.");
        Add("Bản Drive đã thay đổi trong lúc xem trước. Không có dữ liệu nào được nhập; hãy tải lại danh sách bản sao lưu.", "The Drive backup changed while you were reviewing it. Nothing was imported; reload the backup list.");
        Add("Gói được mã hóa sẽ chứa toàn bộ tài khoản '{0}', gồm mật khẩu, TOTP, trường tùy chỉnh và tệp đính kèm. Cấp quyền đọc Drive cho {1} trong {2} ngày?", "The encrypted package will contain the full '{0}' account, including its password, TOTP, custom fields and attachments. Grant {1} Drive read access for {2} days?");
        Add("Xác nhận chia sẻ thông tin nhạy cảm", "Confirm sharing sensitive information");
        Add("Không thể tạo liên kết Drive: ", "Could not create the Drive share: ");
        Add("Không thể tạo chia sẻ: ", "Could not create the share: ");
        Add("Không thể tải liên kết đang chia sẻ: ", "Could not load active shares: ");
        Add("Thu hồi quyền Drive của {0} và chuyển gói chia sẻ vào thùng rác? Bản sao đã tải xuống vẫn có thể được giữ.", "Revoke {0}'s Drive access and move the shared package to the trash? Downloaded copies may still be kept.");
        Add("Thu hồi chia sẻ", "Revoke share");
        Add("Không thể thu hồi chia sẻ: ", "Could not revoke the share: ");
        Add("Lỗi quản lý chia sẻ: ", "Share management error: ");
        Add("Gói chia sẻ đã hết hạn trong lúc xem trước.", "The share package expired while it was being reviewed.");
        Add("Gói đã hết hạn", "Package expired");
        Add("Không thể mở gói chia sẻ: ", "Could not open the share package: ");
        Add("Tài khoản trùng không còn tồn tại; không có gì được ghi đè.", "The duplicate account no longer exists; nothing was overwritten.");
        Add("Không thể ghi đè", "Could not overwrite");
        Add("Đã thêm mục được chia sẻ. Có thể hoàn tác bằng nút Hoàn tác nhập CSV.", "The shared item was added. You can undo it with the Undo CSV Import button.");
        Add("Đã nhập mục được chia sẻ", "Shared item imported");
        Add("Chọn đúng một tài khoản để tạo chia sẻ.", "Select exactly one account to create a share.");
        Add("Quyền đã bị thu hồi", "Access revoked");
        Add("Tệp '{0}' vượt quá giới hạn 10 MB.", "File '{0}' exceeds the 10 MB limit.");
        Add("Tệp vượt quá giới hạn 10 MB.", "File exceeds the 10 MB limit.");
        Add("Không thể đọc tệp '{0}': {1}", "Could not read file '{0}': {1}");
        Add("Tệp '{0}' đã thay đổi trong lúc đọc.", "File '{0}' changed while it was being read.");
        Add("Tải gói chia sẻ lên Drive thất bại: ", "Could not upload the share package to Drive: ");
        Add("Google Drive không trả về file chia sẻ vừa tạo.", "Google Drive did not return the uploaded share file.");
        Add("Google Drive không xác nhận được quyền chỉ đọc có thời hạn cho đúng người nhận.", "Google Drive did not confirm expiring read access for the intended recipient.");
        Add("Địa chỉ email người nhận không hợp lệ.", "The recipient email address is invalid.");
        Add("Thời hạn chia sẻ phải từ 5 phút đến 30 ngày.", "The share duration must be between 5 minutes and 30 days.");
        Add("Không tìm thấy thư mục chia sẻ Drive của ứng dụng.", "The app's Drive share folder was not found.");
        Add("File này không phải gói chia sẻ do tài khoản ứng dụng hiện tại tạo.", "This is not a share package created by the current app account.");
        Add("Không còn quyền Drive tương ứng với người nhận.", "There is no matching Drive permission for this recipient.");
        Add("Google Drive vẫn báo quyền truy cập cho người nhận sau khi thu hồi.", "Google Drive still reports access for the recipient after revocation.");
        Add("Quyền Drive đã bị thu hồi, nhưng gói mã hóa chưa chuyển được vào thùng rác: {0}", "Drive access was revoked, but the encrypted package could not be moved to the trash: {0}");
        Add("Cụm mật khẩu chia sẻ quá ngắn.", "The share passphrase is too short.");
        Add("Thời hạn gói chia sẻ phải trong vòng 30 ngày.", "The share package must expire within 30 days.");
        Add("Gói chia sẻ vượt quá giới hạn 40 MB.", "The share package exceeds the 40 MB limit.");
        Add("Không thể xác minh gói chia sẻ vừa tạo.", "The newly created share package could not be verified.");
        Add("Không tìm thấy gói chia sẻ.", "The share package was not found.");
        Add("Kích thước gói chia sẻ không hợp lệ hoặc vượt quá 40 MB.", "The share package has an invalid size or exceeds 40 MB.");
        Add("Kích thước gói chia sẻ vượt quá 40 MB.", "The share package exceeds the 40 MB limit.");
        Add("Gói này không dùng định dạng AES-GCM được hỗ trợ.", "This package does not use a supported AES-GCM format.");
        Add("Gói chia sẻ trống hoặc sai cấu trúc.", "The share package is empty or malformed.");
        Add("Gói chia sẻ đã hết hạn hoặc có thông tin thời hạn không hợp lệ.", "The share package has expired or contains invalid expiry information.");
        Add("Gói có quá nhiều tệp đính kèm.", "The package contains too many attachments.");
        Add("Gói có tệp đính kèm không hợp lệ.", "The package contains an invalid attachment.");
        Add("Tổng dung lượng tệp đính kèm vượt giới hạn 25 MB.", "The combined attachment size exceeds 25 MB.");
        Add("Gói có quá nhiều trường tùy chỉnh.", "The package contains too many custom fields.");
        Add("Bản sao lưu lên Google Drive chưa có cụm mật khẩu khôi phục, nên chỉ mở được trên máy này. Nếu mất máy hoặc cài lại Windows, bạn sẽ không khôi phục được.\n\nĐặt cụm mật khẩu ngay bây giờ?", "The Google Drive backup has no recovery passphrase, so it can only be opened on this device. If the device is lost or Windows is reinstalled, the backup cannot be restored.\n\nSet a passphrase now?");
        Add("Lỗi đồng bộ", "Sync error");
        Add("Đã đồng bộ kho mật khẩu lên Google Drive thành công!\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.", "The vault was uploaded to Google Drive.\n\nThe backup is in the 'TQLTK Backup' folder in your Google Drive.");
        Add("Đồng bộ thất bại.\n\n", "Sync failed.\n\n");
        Add("Lỗi đồng bộ: {0}", "Sync error: {0}");
        Add("Bản sao lưu Google Drive: {0} ({1})\nNgười tạo: {2}\nSẽ nhập {3} tài khoản; bỏ qua {4} mục trùng. Mật khẩu không hiển thị.", "Google Drive backup: {0} ({1})\nCreated by: {2}\n{3} accounts will be imported; {4} duplicates will be skipped. Passwords are hidden.");
        Add("Xem trước khôi phục kho mật khẩu", "Preview vault restore");
        Add("Mục này chưa có URL HTTP hoặc HTTPS hợp lệ.", "This item does not have a valid HTTP or HTTPS URL.");

        Add("Tên miền", "Domain");
        Add("Tất cả tên miền", "All domains");
        Add("Thư mục", "Folder");
        Add("Tất cả thư mục", "All folders");
        Add("Nhãn", "Tag");
        Add("Tất cả nhãn", "All tags");
        Add("Thiếu TOTP", "Missing TOTP");
        Add("Cảnh báo bảo mật", "Security warnings");
        Add("Hỗ trợ CSV, JSON Bitwarden, KeePass XML; có xem trước và hoàn tác. Dùng Khôi phục sao lưu cho file TQLTK.", "Supports CSV, Bitwarden JSON, and KeePass XML with preview and undo. Use Restore backup for TQLTK backup files.");
        Add("Phím tắt", "Keyboard shortcuts");
        Add("Cấu hình", "Configure");
        Add("Tùy chỉnh hoặc tắt phím khóa vault, tìm kiếm, sao chép tên đăng nhập và mở TOTP.", "Customize or disable shortcuts for locking the vault, searching, copying a username, and opening TOTP.");
        Add("Chọn phím cho từng thao tác. Thao tác bị tắt sẽ không phản hồi với phím tắt.", "Choose a shortcut for each action. Disabled actions will not respond to keyboard shortcuts.");
        Add("Khóa vault", "Lock the vault");
        Add("Tìm kiếm tài khoản", "Search accounts");
        Add("Sao chép tên đăng nhập đã chọn", "Copy selected username");
        Add("Hiện mã TOTP của tài khoản đã chọn", "Show TOTP for the selected account");
        Add("Không thể mở khóa cài đặt phím tắt.", "Could not open the keyboard shortcut settings key.");
        Add("Cấu hình phím tắt không đầy đủ hoặc có mục lặp.", "The keyboard shortcut configuration is incomplete or contains duplicates.");
        Add("Phím tắt không nằm trong danh sách tổ hợp được hỗ trợ.", "The shortcut is not one of the supported key combinations.");
        Add("Không thể gán cùng một phím tắt cho nhiều thao tác đang bật.", "The same shortcut cannot be assigned to multiple enabled actions.");
        Add("Xem trước nhập tài khoản", "Review account import");
        Add("Xem trước nhập {0}", "Review {0} import");
        Add("📥 Xem trước tài khoản nhập", "📥 Review imported accounts");
        Add("Xem lại từng mục trước khi nhập. Mục trùng mặc định được bỏ qua; có thể chọn ghi đè hoặc tạo bản sao cho từng mục.", "Review each item before importing. Duplicates are skipped by default; choose overwrite or copy for each item.");
        Add("Xem lại từng dòng. Bản trùng mặc định được bỏ qua; có thể chọn ghi đè hoặc tạo bản sao.", "Review each row. Duplicates are skipped by default; you can overwrite or import a copy.");
        Add("Tên đăng nhập", "Username");
        Add("Độ dài tối thiểu", "Minimum length");
        Add("Độ dài tối đa", "Maximum length");
        Add("Bắt buộc chữ hoa", "Require uppercase letters");
        Add("Bắt buộc chữ thường", "Require lowercase letters");
        Add("Bắt buộc chữ số", "Require digits");
        Add("Bắt buộc ký hiệu", "Require symbols");
        Add("Ký tự bị cấm", "Forbidden characters");
        Add("Áp dụng quy tắc đã lưu cho website này", "Apply saved rules for this website");
        Add("Lưu quy tắc website", "Save website rules");
        Add("Xóa quy tắc đã lưu", "Remove saved rules");
        Add("Quy tắc được lưu riêng cho {0}.", "Rules are saved separately for {0}.");
        Add("Giới hạn độ dài mật khẩu của website không hợp lệ.", "Website password length limits are invalid.");
        Add("Hãy chọn độ dài từ {0} đến {1} ký tự cho website này.", "Choose a password length from {0} to {1} for this website.");
        Add("Độ dài đã chọn không đủ để đáp ứng các nhóm ký tự bắt buộc của website.", "The selected length is too short to satisfy this website's required character groups.");
        Add("Hãy nhập URL website HTTP hoặc HTTPS hợp lệ trước.", "Enter a valid HTTP or HTTPS website URL first.");
        Add("Độ dài tối đa không đủ cho tất cả nhóm ký tự bắt buộc.", "The maximum password length cannot satisfy all required character groups.");
        Add("Đã đạt số lượng tối đa các quy tắc mật khẩu website được lưu.", "The maximum number of saved website password policies was reached.");
        Add("Dữ liệu quy tắc mật khẩu website vượt giới hạn kích thước.", "Saved website password policies exceed the size limit.");
        Add("Không thể mở khóa cài đặt của ứng dụng.", "Could not open the app settings registry key.");
        Add("Không thể bảo đảm quy tắc ký tự của website bằng cụm mật khẩu. Hãy chọn chế độ mật khẩu ngẫu nhiên.", "Website character rules cannot be guaranteed by a passphrase. Choose random-password mode.");
        Add("Độ dài website phải trong khoảng 8–64 và độ dài tối thiểu không được lớn hơn tối đa.", "Website password length limits must be 8–64, with the minimum no greater than the maximum.");
        Add("Độ dài tối đa không đủ cho các nhóm ký tự bắt buộc.", "The maximum length is too short for the required character groups.");
        Add("{0} phải là số nguyên.", "{0} must be an integer.");
        Add("Tệp đã chọn trống hoặc vượt giới hạn nhập 64 MB.", "The selected file is empty or exceeds the 64 MB import limit.");
        Add("Không tìm thấy tệp đã chọn.", "The selected file does not exist.");
        Add("Chưa hỗ trợ mở trực tiếp file .kdbx đã mã hóa. Hãy xuất dữ liệu thành KeePass XML trong KeePass rồi nhập file XML.", "Encrypted .kdbx files are not supported directly. Export the database as KeePass XML from KeePass, then import the XML file.");
        Add("Đây là bản sao lưu kho mật khẩu TQLTK. Hãy dùng Tiện ích → Nhập kho mật khẩu để xem trước và nhập dữ liệu.", "This is a TQLTK vault backup. Use Utilities → Import vault to preview and import its data.");
        Add("Đây là bản sao lưu File cá nhân TQLTK, không phải tệp tài khoản. Hãy dùng Tiện ích → Kiểm tra bản sao lưu hoặc Diễn tập khôi phục.", "This is a TQLTK personal-file backup, not an account export. Use Utilities → Verify backup or Restore drill.");
        Add("Định dạng tài khoản được hỗ trợ là CSV, JSON và KeePass XML. File .vault có luồng nhập riêng; file .tqf là bản sao lưu File cá nhân.", "Supported account formats are CSV, JSON, and KeePass XML. .vault files have a separate import flow; .tqf files are personal-file backups.");
        Add("Không thể đọc tệp nhập. Hãy kiểm tra định dạng, quyền truy cập và giới hạn 64 MB.", "Could not read the import file. Check its format, access permissions, and the 64 MB limit.");
        Add("Tệp JSON trống hoặc vượt giới hạn nhập 64 MB.", "The JSON file is empty or exceeds the 64 MB import limit.");
        Add("Tệp nhập có hơn {0} mục được hỗ trợ.", "The import contains more than {0} supported entries.");
        Add("Tài khoản đã nhập", "Imported account");
        Add("TOTP chưa phân tích được", "Unparsed TOTP");
        Add("Không tìm thấy mục đăng nhập được hỗ trợ trong tệp JSON.", "No supported login entries were found in this JSON file.");
        Add("Đã đọc {0} mục đăng nhập từ JSON.", "Read {0} login entries from JSON.");
        Add("Tệp không phải JSON hợp lệ hoặc dữ liệu lồng quá sâu so với giới hạn an toàn.", "The file is not valid JSON or its nesting exceeds the safe import limit.");
        Add("Không thể phân tích dữ liệu đăng nhập JSON.", "Could not parse the JSON login export.");
        Add("Tệp XML trống hoặc vượt giới hạn nhập 64 MB.", "The XML file is empty or exceeds the 64 MB import limit.");
        Add("Đây không phải file xuất KeePass XML.", "This is not a KeePass XML export.");
        Add("File xuất KeePass XML không có nhóm gốc.", "The KeePass XML export has no root group.");
        Add("Không tìm thấy mục đăng nhập trong file KeePass XML.", "No login entries were found in the KeePass XML export.");
        Add("Đã đọc {0} mục đăng nhập từ KeePass XML.", "Read {0} login entries from KeePass XML.");
        Add("Tệp không phải KeePass XML hợp lệ. DTD và thực thể bên ngoài bị từ chối.", "The file is not valid KeePass XML. DTDs and external entities are not allowed.");
        Add("Nhóm KeePass lồng nhau vượt giới hạn 64 cấp.", "KeePass groups exceed the maximum nesting depth of 64.");
        Add("KeePass XML có trường Protected không thể giải mã an toàn. Hãy xuất bản XML dạng chữ thường bằng KeePass.", "KeePass XML contains protected values that cannot be safely decoded. Export a plaintext XML copy using KeePass.");
        Add("Tệp trống hoặc vượt giới hạn nhập 64 MB.", "The file is empty or exceeds the 64 MB import limit.");
        Add("Tệp vượt giới hạn nhập 64 MB.", "The file exceeds the 64 MB import limit.");
        Add("Chưa chạy kiểm tra rò rỉ. Kiểm tra là tùy chọn và không bao giờ tự gửi yêu cầu.", "No breach check has been run. Checking is optional and is never sent automatically.");
        Add("Không thể đọc kết quả kiểm tra rò rỉ đã lưu trong tài khoản Windows này. Hãy chạy lại kiểm tra nếu muốn.", "A saved breach-check result could not be read in this Windows account. Run a new optional check.");
        Add("Vault đã thay đổi sau lần kiểm tra này; kết quả đã cũ.", "The vault changed after this check; results are outdated.");
        Add("Đã đến hạn nhắc kiểm tra lại theo lựa chọn của bạn.", "Your chosen recheck reminder is due.");
        Add("Kết quả khớp với vault hiện tại.", "Results match the current vault.");
        Add(" Không thể lưu kết quả; dữ liệu này chỉ dùng được đến khi đóng ứng dụng.", " The result is only available until this app session ends because it could not be saved.");
        Add("Lần kiểm tra HIBP gần nhất: {0} • đã kiểm tra {1} mật khẩu duy nhất • {2} giá trị bị lộ • {3}", "Last HIBP check: {0} • {1} unique passwords checked • {2} breached values • {3}");
        Add("Không nhắc định kỳ", "No scheduled reminder");
        Add("Sau 30 ngày", "After 30 days");
        Add("Sau 90 ngày", "After 90 days");
        Add("Sau 180 ngày", "After 180 days");
        Add("Nhắc kiểm tra lại (không tự gửi yêu cầu):", "Remind me to check again (no automatic requests):");
        Add("Lần kiểm tra HIBP gần nhất", "Last HIBP check");
        Add("Tệp tài khoản (*.csv;*.json;*.xml;*.kdbx)|*.csv;*.json;*.xml;*.kdbx|CSV (*.csv)|*.csv|JSON (*.json)|*.json|KeePass XML (*.xml)|*.xml|Tất cả tệp (*.*)|*.*", "Account files (*.csv;*.json;*.xml;*.kdbx)|*.csv;*.json;*.xml;*.kdbx|CSV (*.csv)|*.csv|JSON (*.json)|*.json|KeePass XML (*.xml)|*.xml|All files (*.*)|*.*");
        Add("Không thể đọc tệp nhập. Hãy kiểm tra định dạng, quyền truy cập và giới hạn 64 MB.", "Could not read the import file. Check its format, access permissions, and the 64 MB limit.");

        return values;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildFragments() => new[]
    {
        new KeyValuePair<string, string>("Diễn tập khôi phục thất bại; kho thật không thay đổi. ", "Restore drill failed; the actual store was unchanged. "),
        new KeyValuePair<string, string>("Diễn tập thất bại; kho thật không thay đổi. ", "Restore drill failed; the actual store was unchanged. "),
        new KeyValuePair<string, string>("Không thể diễn tập khôi phục: ", "Could not run the restore drill: "),
        new KeyValuePair<string, string>("Diễn tập thành công: đã khôi phục thử ", "Restore drill succeeded: restored "),
        new KeyValuePair<string, string>("Diễn tập khôi phục thành công: đã đọc, giải mã, ghi thử an toàn vào kho tạm, đọc lại và đối chiếu ", "Restore drill succeeded: read, decrypted, safely wrote to a temporary store, read back, and compared "),
        new KeyValuePair<string, string>(" tài khoản trong ", " accounts in "),
        new KeyValuePair<string, string>(" vào kho tạm được mã hóa, xác minh từng file trong ", " to an encrypted temporary store, verified each file in "),
        new KeyValuePair<string, string>(" rồi xóa kho tạm. Kho File cá nhân thật không thay đổi.", " and removed the temporary store. The actual personal-file store was unchanged."),
        new KeyValuePair<string, string>(" giây. Vault thật không thay đổi.", " seconds. The actual vault was unchanged."),
        new KeyValuePair<string, string>("Sao lưu gần nhất thất bại: ", "Most recent backup failed: "),
        new KeyValuePair<string, string>("Trạng thái sao lưu: bản gần nhất bị thiếu, thay đổi hoặc chưa xác minh.", "Backup health: the latest backup is missing, has changed, or has not been verified."),
        new KeyValuePair<string, string>("Bản sao lưu tạo mới không qua kiểm tra: ", "The newly created backup did not pass verification: "),
        new KeyValuePair<string, string>("Bản phục hồi không qua xác minh: ", "The recovery copy did not pass verification: "),
        new KeyValuePair<string, string>("Không tạo được bản phục hồi: ", "Could not create the recovery copy: "),
        new KeyValuePair<string, string>(" phút", " minutes"),
        new KeyValuePair<string, string>(" Bỏ qua ", " Skipped "),
        new KeyValuePair<string, string>("Bỏ qua ", "Skipped "),
        new KeyValuePair<string, string>(" Còn ", " Remaining "),
        new KeyValuePair<string, string>("Hiện còn ", "Remaining "),
        new KeyValuePair<string, string>(" lần sai hoặc hủy mã xác thực 2 bước\n\n", " incorrect attempts or cancelled two-factor prompts\n\n"),
        new KeyValuePair<string, string>(" lần sai mật khẩu\n\n", " incorrect password attempts\n\n"),
        new KeyValuePair<string, string>(" mã khôi phục chưa dùng.\n\n", " unused recovery codes.\n\n"),
        new KeyValuePair<string, string>(" mục trùng.\n\n", " duplicate entries.\n\n"),
        new KeyValuePair<string, string>(" tài khoản ra:\n\n", " accounts to:\n\n"),
        new KeyValuePair<string, string>("' (>= 6 ký tự):\n\n", "' (at least 6 characters):\n\n"),
        new KeyValuePair<string, string>("OnExit đã chạy, mã thoát ", "OnExit ran; exit code "),
        new KeyValuePair<string, string>("Tự động đổi giao diện sang ", "Automatically switched theme to "),
        new KeyValuePair<string, string>(" sang Sáng lúc ", " to Light at "),
        new KeyValuePair<string, string>(" sang Tối lúc ", " to Dark at "),
        new KeyValuePair<string, string>("Đã bật tự động đổi giao diện theo giờ.\n\n", "Automatic theme switching is on.\n\n"),
        new KeyValuePair<string, string>("Đã tắt tự động đổi giao diện theo giờ.", "Automatic theme switching is off."),
        new KeyValuePair<string, string>("• 06:00 - 18:00: Sáng", "• 06:00–18:00: Light"),
        new KeyValuePair<string, string>("• 18:00 - 06:00: Tối", "• 18:00–06:00: Dark"),
        new KeyValuePair<string, string>(": Sáng", ": Light"),
        new KeyValuePair<string, string>(": Tối", ": Dark"),
        new KeyValuePair<string, string>("Đã sao chép mã. Bộ nhớ tạm sẽ tự xóa sau ", "Code copied. The clipboard will clear after "),
        new KeyValuePair<string, string>("Đã sao chép. Bộ nhớ tạm sẽ tự xóa sau ", "Copied. The clipboard will clear after "),
        new KeyValuePair<string, string>("Đã sao chép mật khẩu! Bộ nhớ tạm sẽ tự xóa sau ", "Password copied! The clipboard will clear after "),
        new KeyValuePair<string, string>("\n\nTừ giờ các bản sao lưu và tệp xuất kho mật khẩu mới sẽ mở được trên máy khác ", "\n\nNew backups and password vault exports can now be opened on another computer "),
        new KeyValuePair<string, string>("Nhập vào sẽ thêm các tài khoản của '", "Importing will add accounts from '"),
        new KeyValuePair<string, string>("Kho mật khẩu: ", "Password vault: "),
        new KeyValuePair<string, string>(" tài khoản • ", " accounts • "),
        new KeyValuePair<string, string>(" mật khẩu yếu • ", " weak passwords • "),
        new KeyValuePair<string, string>(" mục dùng mật khẩu trùng • ", " entries with reused passwords • "),
        new KeyValuePair<string, string>(" mật khẩu cũ nhất đã biết: ", " oldest known password: "),
        new KeyValuePair<string, string>(" ngày • ", " days • "),
        new KeyValuePair<string, string>(" tài khoản chưa rõ ngày đổi MK", " accounts with an unknown password change date"),
        new KeyValuePair<string, string>("Sao lưu kho mật khẩu gần nhất: ", "Latest password vault backup: "),
        new KeyValuePair<string, string>(" • dữ liệu đã thay đổi sau lần sao lưu.", " • data has changed since the backup."),
        new KeyValuePair<string, string>(" • chưa phát hiện thay đổi mới.", " • no new changes detected."),
        new KeyValuePair<string, string>("Tải lên ", "Uploaded "),
        new KeyValuePair<string, string>(" tệp cá nhân (", " personal files ("),
        new KeyValuePair<string, string>("Nhập kho mật khẩu từ Drive: ", "Import password vault from Drive: "),
        new KeyValuePair<string, string>("Đã tải kho mật khẩu lên thành công", "Password vault uploaded successfully"),
        new KeyValuePair<string, string>("Không thể khôi phục mật khẩu từ lịch sử. Kho mật khẩu chưa được cập nhật.\n\n", "Could not restore the password from history. The password vault was not updated.\n\n"),
        new KeyValuePair<string, string>("Cần xác thực để ", "Authentication is required to "),
        new KeyValuePair<string, string>("nhưng Windows Hello không thành công và bạn chưa bật xác thực 2 bước.\n\n", "but Windows Hello failed and two-factor authentication is not enabled.\n\n"),
        new KeyValuePair<string, string>("Người tạo: ", "Created by: "),
        new KeyValuePair<string, string>("Nhập mật khẩu mới cho '", "Enter a new password for '"),
        new KeyValuePair<string, string>("Nhập sai mã 2FA quá nhiều lần. Hãy thử lại sau khoảng ", "Too many incorrect 2FA codes. Try again in about "),
        new KeyValuePair<string, string>("Nhập sai mật khẩu quá nhiều lần. Tài khoản bị khóa tạm thời, hãy thử lại sau khoảng ", "Too many incorrect passwords. The account is temporarily locked; try again in about "),
        new KeyValuePair<string, string>("Nhập vault từ Drive: ", "Import vault from Drive: "),
        new KeyValuePair<string, string>("Reset mật khẩu '", "Reset password for '"),
        new KeyValuePair<string, string>("Đặt lại mật khẩu '", "Reset password '"),
        new KeyValuePair<string, string>("Sai mật khẩu ", "Incorrect password "),
        new KeyValuePair<string, string>("Sai mật khẩu. Còn ", "Incorrect password. Remaining attempts: "),
        new KeyValuePair<string, string>("Sửa '", "Edit '"),
        new KeyValuePair<string, string>("Tạo '", "Create '"),
        new KeyValuePair<string, string>("Tải lên Google Drive thất bại: ", "Google Drive upload failed: "),
        new KeyValuePair<string, string>("Tải xuống từ Google Drive thất bại: ", "Google Drive download failed: "),
        new KeyValuePair<string, string>("Tự động đổi theme sang ", "Automatically switched theme to "),
        new KeyValuePair<string, string>("Xác nhận để ", "Confirm to "),
        new KeyValuePair<string, string>("Sẽ khôi phục", "Will restore"),
        new KeyValuePair<string, string>("Sẽ nhập", "Will import"),
        new KeyValuePair<string, string>("Đã hủy khôi phục: bản sao lưu này thuộc về người dùng '", "Restore cancelled: this backup belongs to user '"),
        new KeyValuePair<string, string>("Đã hủy nhập: bản sao lưu này thuộc về người dùng '", "Import cancelled: this backup belongs to user '"),
        new KeyValuePair<string, string>("Đã hủy khôi phục.", "Restore was cancelled."),
        new KeyValuePair<string, string>("Đã khoảng ", "About "),
        new KeyValuePair<string, string>("Đã khôi phục ", "Restored "),
        new KeyValuePair<string, string>("Đã kiểm tra ", "Checked "),
        new KeyValuePair<string, string>("Đã xuất ", "Exported "),
        new KeyValuePair<string, string>("Đã đóng gói ", "Packaged "),
        new KeyValuePair<string, string>("Đã đọc ", "Read "),
        new KeyValuePair<string, string>("Đăng nhập TQLTK với tài khoản '", "Sign in to TQLTK with account '"),
        new KeyValuePair<string, string>("Xác nhận để copy mật khẩu", "Confirm to copy password"),
        new KeyValuePair<string, string>("Xác nhận để hiện mật khẩu", "Confirm to reveal password"),
        new KeyValuePair<string, string>("đặt hoặc đổi cụm mật khẩu sao lưu", "set or change the backup passphrase"),
        new KeyValuePair<string, string>("đồng bộ vault lên Google Drive", "sync the vault to Google Drive"),
        new KeyValuePair<string, string>("và tài khoản của bạn nằm trong danh sách Test users nếu app đang ở chế độ Testing.\n\n", "and your account is listed as a Test user if the app is in Testing mode.\n\n"),
        new KeyValuePair<string, string>("từ users.json.bak nếu có. File mới được mã hóa theo tài khoản Windows hiện tại; ", "from users.json.bak if available. The new file is encrypted for the current Windows account; "),
        new KeyValuePair<string, string>("trong nhật ký gần đây (tối đa 30 ngày)", "in recent logs (up to 30 days)"),
        new KeyValuePair<string, string>("khôi phục File cá nhân từ Google Drive", "restore personal files from Google Drive"),
        new KeyValuePair<string, string>("khôi phục vault từ Google Drive", "restore the vault from Google Drive"),
        new KeyValuePair<string, string>("nên ứng dụng từ chối mở file này và không thay đổi bản gốc.", "so the app refused to open this file and left the original unchanged."),
        new KeyValuePair<string, string>("nên ứng dụng từ chối mở và giữ nguyên file để phục hồi riêng.", "so the app refused to open it and left the file unchanged for separate recovery."),
        new KeyValuePair<string, string>("lưu cụm mật khẩu sao lưu", "save the backup passphrase"),
        new KeyValuePair<string, string>("nhận cụm mật khẩu sao lưu cũ", "accept the old backup passphrase"),
        new KeyValuePair<string, string>("nhập vault", "import vault"),
        new KeyValuePair<string, string>("xuất vault", "export vault"),
        new KeyValuePair<string, string>("xem lịch sử mật khẩu", "view password history"),
        new KeyValuePair<string, string>("xem mã TOTP", "view TOTP code"),
        new KeyValuePair<string, string>("xóa hẳn mục khỏi thùng rác", "permanently delete an item from Trash"),
        new KeyValuePair<string, string>("ngày xuất: ", "export date: "),
        new KeyValuePair<string, string>("Nguồn: ", "Source: "),
        new KeyValuePair<string, string>("Bản sao lưu Google Drive: ", "Google Drive backup: "),
        new KeyValuePair<string, string>("người tạo: ", "created by: "),
        new KeyValuePair<string, string>("nhớ xác thực ≤", "remember authentication ≤"),
        new KeyValuePair<string, string>("kể từ lần sao lưu gần nhất (", "since the last backup ("),
        new KeyValuePair<string, string>("kể từ lần đăng nhập trước (", "since the previous sign-in ("),
        new KeyValuePair<string, string>(" kể từ lần đăng nhập trước (", " since the previous sign-in ("),
        new KeyValuePair<string, string>(" trong nhật ký gần đây (tối đa 30 ngày):\n\n", " in recent logs (up to 30 days):\n\n"),
        new KeyValuePair<string, string>("cũ không đúng", "is incorrect"),
        new KeyValuePair<string, string>("hoặc nhờ một quản trị viên khác đặt lại mật khẩu.", "or ask another administrator to reset the password."),
        new KeyValuePair<string, string>("lần gần nhất: ", "last seen: "),
        new KeyValuePair<string, string>("Tham số ", "Parameter "),
        new KeyValuePair<string, string>("nguồn: ", "source: "),
        new KeyValuePair<string, string>("Tên         : ", "Name        : "),
        new KeyValuePair<string, string>("Tên gốc: ", "Original name: "),
        new KeyValuePair<string, string>("Thư mục app : ", "App folder  : "),
        new KeyValuePair<string, string>("RAM dùng    : ", "RAM used    : "),
        new KeyValuePair<string, string>("RAM ảo      : ", "Virtual RAM : "),
        new KeyValuePair<string, string>("Số CPU      : ", "CPU count   : "),
        new KeyValuePair<string, string>("Thời gian   : ", "Uptime      : "),
        new KeyValuePair<string, string>("độ dài", "length"),
        new KeyValuePair<string, string>("\n\nKhông xóa được:\n\n", "\n\nCould not delete:\n\n"),
        new KeyValuePair<string, string>("\n\nLưu ý: bạn chưa đặt cụm mật khẩu sao lưu nên file này chỉ mở được trên máy này ", "\n\nNote: you have not set a backup passphrase, so this file can only be opened on this computer "),
        new KeyValuePair<string, string>("\nBỏ qua ", "\nSkipped "),
        new KeyValuePair<string, string>("\nLần gần nhất: ", "\nLast seen: "),
        new KeyValuePair<string, string>("\nNgười tạo: ", "\nCreated by: "),
        new KeyValuePair<string, string>("Nếu không phải bạn, hãy đổi mật khẩu ngay (Hệ thống → Đổi mật khẩu) và xem lại nhật ký hoạt động.", "If this was not you, change your password now (System → Change Password) and review the activity log."),
        new KeyValuePair<string, string>("Sẽ khôi phục ", "Will restore "),
        new KeyValuePair<string, string>("Sẽ nhập ", "Will import "),
        new KeyValuePair<string, string>("\nSẽ nhập ", "\nWill import "),
        new KeyValuePair<string, string>(" tài khoản; bỏ qua ", " accounts; skip "),
        new KeyValuePair<string, string>("\nMật khẩu không hiển thị trong phần xem trước.", "\nPasswords are hidden in the preview."),
        new KeyValuePair<string, string>("Trước khi hủy, ", "Before cancellation, "),
        new KeyValuePair<string, string>("\nTrước khi hủy, ", "\nBefore cancellation, "),
        new KeyValuePair<string, string>(" Cột \"Thuộc về\": \"Của bạn\" là bản tạo từ tài khoản này, \"Bản cũ\" là bản tạo trước khi tách theo người dùng. ", " The \"Owner\" column: \"Yours\" means the backup was created from this account; \"Legacy\" means it predates per-user separation. "),
        new KeyValuePair<string, string>("; bỏ qua ", "; skip "),
        new KeyValuePair<string, string>(" Mật khẩu không hiển thị.", " Passwords are hidden."),
        new KeyValuePair<string, string>("Trước khi lỗi xảy ra, ", "Before the error occurred, "),
        new KeyValuePair<string, string>(" (bản sao lưu của người dùng '", " (backup from user '"),
        new KeyValuePair<string, string>(" (có thay đổi mới)", " (new changes)"),
        new KeyValuePair<string, string>(" (giữ dữ liệu)", " (keep data)"),
        new KeyValuePair<string, string>(" (kèm dữ liệu)", " (include data)"),
        new KeyValuePair<string, string>(" (xác thực: ", " (authentication: "),
        new KeyValuePair<string, string>("file trùng (cùng tên và cùng nội dung).", "duplicate files (same name and contents)."),
        new KeyValuePair<string, string>(" file trùng. ", " duplicate files. "),
        new KeyValuePair<string, string>(" file đã được khôi phục vào kho File cá nhân.", " files were restored to the personal file vault."),
        new KeyValuePair<string, string>(" file, bỏ qua ", " files, skipped "),
        new KeyValuePair<string, string>(" file; bỏ qua ", " files; skipped "),
        new KeyValuePair<string, string>(" giá trị; ảnh hưởng ", " values; affects "),
        new KeyValuePair<string, string>(" ký tự.", " characters."),
        new KeyValuePair<string, string>(" lúc ", " at "),
        new KeyValuePair<string, string>(" lần liên tiếp. Tài khoản bị khóa tạm thời ", " consecutive times. The account is temporarily locked for "),
        new KeyValuePair<string, string>(" lần thử trước khi bị khóa tạm thời.", " attempts before the account is temporarily locked."),
        new KeyValuePair<string, string>(" mã.\nHãy tắt rồi bật lại 2FA nếu bạn đã mất điện thoại.", " codes.\nDisable and re-enable 2FA if you have lost your phone."),
        new KeyValuePair<string, string>(" mục có tên không hợp lệ nếu khôi phục.", " entries have names that would be invalid after restoration."),
        new KeyValuePair<string, string>(" mục có tên không hợp lệ.", " entries have invalid names."),
        new KeyValuePair<string, string>(" mục khác.", " other entries."),
        new KeyValuePair<string, string>(" mục không hợp lệ.", " invalid entries."),
        new KeyValuePair<string, string>(" mục trùng. Mật khẩu không hiển thị.", " duplicate entries. Passwords are hidden."),
        new KeyValuePair<string, string>(" mục, lỗi ", " entries, errors "),
        new KeyValuePair<string, string>(" ngày chưa sao lưu, và dữ liệu đã thay đổi ", " days since the last backup, and data has changed "),
        new KeyValuePair<string, string>(" phút.", " minutes."),
        new KeyValuePair<string, string>(" phải là số nguyên.", " must be a whole number."),
        new KeyValuePair<string, string>(" trong URI không hợp lệ.", " in the URI is invalid."),
        new KeyValuePair<string, string>(" tài khoản (bỏ qua ", " accounts (skipped "),
        new KeyValuePair<string, string>(" tài khoản (format: ", " accounts (format: "),
        new KeyValuePair<string, string>(" tài khoản ra ", " accounts to "),
        new KeyValuePair<string, string>(" tài khoản ra CSV", " accounts to CSV"),
        new KeyValuePair<string, string>(" tài khoản trùng.", " duplicate accounts."),
        new KeyValuePair<string, string>(" tài khoản từ CSV (", " accounts from CSV ("),
        new KeyValuePair<string, string>(" tài khoản, ", " accounts, "),
        new KeyValuePair<string, string>(" tài khoản; ", " accounts; "),
        new KeyValuePair<string, string>(" tài khoản; bỏ qua ", " accounts; skipped "),
        new KeyValuePair<string, string>("Đã tạm khóa xác thực 2FA trong ", "2FA verification has been temporarily locked for "),
        new KeyValuePair<string, string>(" • tự sao lưu: tắt", " • automatic backup: off"),
        new KeyValuePair<string, string>("(Chưa có log hôm nay)", "(No log entries today)"),
        new KeyValuePair<string, string>("(Google Cloud Console → OAuth client kiểu Desktop) và đặt cạnh file .csproj.", "(Google Cloud Console → Desktop OAuth client) and place it next to the .csproj file."),
        new KeyValuePair<string, string>("(Người dùng sẽ phải đổi lại mật khẩu này khi đăng nhập lần tới.)", "(The user will have to change this password at the next sign-in.)"),
        new KeyValuePair<string, string>("(Registry trống)", "(Registry is empty)"),
        new KeyValuePair<string, string>("(Tiện ích → Cụm mật khẩu sao lưu).", "(Utilities → Backup Passphrase)."),
        new KeyValuePair<string, string>(") → coi như khả dụng", ") → treated as available"),
        new KeyValuePair<string, string>(": xóa ", ": deleted "),
        new KeyValuePair<string, string>("=== QUYỀN ===", "=== PERMISSIONS ==="),
        new KeyValuePair<string, string>("=== THÔNG TIN HỆ THỐNG ===", "=== SYSTEM INFORMATION ==="),
        new KeyValuePair<string, string>("=== TIẾN TRÌNH HIỆN TẠI ===", "=== CURRENT PROCESS ==="),
        new KeyValuePair<string, string>("Bạn vừa dùng 1 mã khôi phục. Còn lại ", "You just used a recovery code. Remaining codes: "),
        new KeyValuePair<string, string>("Bản sao lưu Google Drive: ", "Google Drive backup: "),
        new KeyValuePair<string, string>("Bản sao lưu hợp lệ: ", "Backup verified: "),
        new KeyValuePair<string, string>("Bắt đầu     : ", "Started      : "),
        new KeyValuePair<string, string>("Chi tiết: ", "Details: "),
        new KeyValuePair<string, string>("Có đăng nhập thất bại vào tài khoản '", "There was a failed sign-in to account '"),
        new KeyValuePair<string, string>("Cụm mật khẩu cần ít nhất ", "The passphrase must contain at least "),
        new KeyValuePair<string, string>("Dung lượng: ", "Size: "),
        new KeyValuePair<string, string>("Dịch vụ: ", "Service: "),
        new KeyValuePair<string, string>("Dữ liệu cũ của tài khoản trùng đường dẫn với tài khoản khác nên đăng nhập bị từ chối. ", "Sign-in was denied because this account's legacy data path conflicts with another account. "),
        new KeyValuePair<string, string>("File vault cũ trùng đường dẫn với tài khoản khác. File cũ được giữ nguyên; ", "The legacy vault file conflicts with another account's path. The old file was left unchanged; "),
        new KeyValuePair<string, string>("File xác thực 2 bước cũ trùng đường dẫn với tài khoản khác. File cũ được giữ nguyên; ", "The legacy two-factor file conflicts with another account's path. The old file was left unchanged; "),
        new KeyValuePair<string, string>("Google từ chối quyền truy cập (403). Kiểm tra Google Drive API đã được bật, ", "Google denied access (403). Verify that the Google Drive API is enabled, "),
        new KeyValuePair<string, string>("Hãy khôi phục key.dat từ bản sao lưu. ", "Restore key.dat from a backup. "),
        new KeyValuePair<string, string>("Hãy đóng ứng dụng, mở thư mục %AppData%\\PersonalInfoManager rồi khôi phục users.json ", "Close the app, open %AppData%\\PersonalInfoManager, and restore users.json "),
        new KeyValuePair<string, string>("Không ghi được vào thư mục này:\n\n", "Could not write to this folder:\n\n"),
        new KeyValuePair<string, string>("Không lưu được cấu hình 2FA: ", "Could not save the 2FA configuration: "),
        new KeyValuePair<string, string>("Không lưu được mã khôi phục mới: ", "Could not save the new recovery code: "),
        new KeyValuePair<string, string>("Không lưu được trạng thái tắt 2FA; đăng nhập vẫn bị khóa để bảo vệ tài khoản: ", "Could not save the disabled 2FA state; sign-in remains blocked to protect the account: "),
        new KeyValuePair<string, string>("Không lưu được tài khoản quản trị viên: ", "Could not save the administrator account: "),
        new KeyValuePair<string, string>("Không sao lưu được: ", "Backup failed: "),
        new KeyValuePair<string, string>("Không xóa được cấu hình 2FA: ", "Could not delete the 2FA configuration: "),
        new KeyValuePair<string, string>("Không đọc được dữ liệu đã lưu. Dữ liệu cũ vẫn được giữ nguyên, ", "Could not read the saved data. The old data remains unchanged, "),
        new KeyValuePair<string, string>("Máy tính    : ", "Computer    : "),
        new KeyValuePair<string, string>("Máy tính không hỗ trợ Windows Hello hoặc chưa thiết lập.\n\n", "This computer does not support Windows Hello or it has not been set up.\n\n"),
        new KeyValuePair<string, string>(" mật khẩu duy nhất)", " unique passwords)") ,
        new KeyValuePair<string, string>(" mật khẩu yếu • ", " weak passwords • "),
        new KeyValuePair<string, string>(" mục dùng mật khẩu trùng • ", " entries use duplicate passwords • "),
        new KeyValuePair<string, string>(" tài khoản | 💾 ", " accounts | 💾 "),
        new KeyValuePair<string, string>(" tài khoản • ", " accounts • "),
        new KeyValuePair<string, string>(" mật khẩu trong vault ", " passwords in the vault "),
        new KeyValuePair<string, string>("Kho File: ", "File vault: "),
        new KeyValuePair<string, string>(" file được mã hóa AES-GCM, khóa gắn với tài khoản Windows hiện tại.", " files are encrypted with AES-GCM using a key bound to the current Windows account."),
        new KeyValuePair<string, string>(" file mã hóa không đọc được; giữ nguyên để xử lý riêng.", " unreadable encrypted files; kept unchanged for separate handling."),
        new KeyValuePair<string, string>(" file đã mã hóa; file cũ sẽ được chuyển đổi khi mở kho.", " encrypted files; legacy files will be converted when the vault is opened."),
        new KeyValuePair<string, string>("mật khẩu cũ nhất đã biết: ", "oldest known password change: "),
        new KeyValuePair<string, string>("Kiểm tra xong: không tìm thấy mật khẩu nào trong dữ liệu Pwned Passwords ", "Check complete: no passwords were found in Pwned Passwords data "),
        new KeyValuePair<string, string>("Chưa có bản sao lưu File cá nhân nào của bạn trên Google Drive của tài khoản này. ", "There are no personal file backups for your account on this Google Drive. "),
        new KeyValuePair<string, string>("Chưa có bản sao lưu File cá nhân nào trên Google Drive của tài khoản này.", "There are no personal file backups on this account's Google Drive."),
        new KeyValuePair<string, string>("Chưa có bản sao lưu nào của bạn trên Google Drive của tài khoản này. ", "There are no backups for your account on this Google Drive. "),
        new KeyValuePair<string, string>("Chưa có bản sao lưu nào trên Google Drive của tài khoản này. ", "There are no backups on this account's Google Drive. "),
        new KeyValuePair<string, string>("Đã đặt cụm mật khẩu sao lưu.\n\nTừ giờ các bản sao lưu và file xuất vault mới sẽ mở được trên máy khác ", "Backup passphrase set.\n\nFrom now on, new backups and exported vault files can be opened on another computer "),
        new KeyValuePair<string, string>("Dùng cụm mật khẩu vừa nhập làm cụm sao lưu cho máy này?\n\n", "Use the passphrase you just entered as this computer's backup passphrase?\n\n"),
        new KeyValuePair<string, string>("Hiện mật khẩu cần xác thực bằng Windows Hello nhưng máy này chưa hỗ trợ hoặc chưa thiết lập.\n\n", "Revealing a password requires Windows Hello, but it is unavailable or not set up on this computer.\n\n"),
        new KeyValuePair<string, string>("Máy này có một cụm mật khẩu sao lưu được đặt từ phiên bản trước, khi app chưa tách theo người dùng.\n\n", "This computer has a backup passphrase from an earlier version, before the app separated data by user.\n\n"),
        new KeyValuePair<string, string>("Đổi sang cụm mới? Các bản sao lưu đã tạo trước đó ", "Change to a new passphrase? Backups created earlier "),
        new KeyValuePair<string, string>("Nhập vào sẽ thêm các tài khoản của '", "Importing will add the accounts for '") ,
        new KeyValuePair<string, string>("Nếu mất máy hoặc cài lại Windows, bạn sẽ không khôi phục được.\n\n", "If you lose this computer or reinstall Windows, you will not be able to restore your data.\n\n"),
        new KeyValuePair<string, string>("Tài khoản đang bật xác thực 2 bước nhưng file cấu hình bị thiếu hoặc không đọc được. Đăng nhập đã bị từ chối. Hãy khôi phục cấu hình 2FA từ bản sao lưu hoặc liên hệ quản trị viên.", "Two-factor authentication is enabled for this account, but its configuration file is missing or unreadable. Sign-in was denied. Restore the 2FA configuration from a backup or contact an administrator."),
        new KeyValuePair<string, string>("Đặt lại mật khẩu của '", "Reset the password for '") ,
        new KeyValuePair<string, string>("Ứng dụng chỉ gửi 5 ký tự đầu của SHA-1 hash qua HTTPS; mật khẩu đầy đủ, hash đầy đủ, tên dịch vụ và username không được gửi. ", "The app sends only the first 5 characters of the SHA-1 hash over HTTPS; the full password, full hash, service name, and username are not sent. "),
        new KeyValuePair<string, string>("• Không: giữ lại. Cụm sao lưu và phiên Google được lưu theo tên đăng nhập có thể dùng lại nếu tạo tài khoản trùng tên.\n\n", "• No: keep it. The backup passphrase and Google session stored under this username can be reused if an account with the same name is created.\n\n"),
        new KeyValuePair<string, string>("  tài khoản.", " accounts."),
        new KeyValuePair<string, string>("' về mật khẩu đã dùng ngày ", "' for a password used on "),
        new KeyValuePair<string, string>(" tài khoản.", " accounts."),
        new KeyValuePair<string, string>("Mật khẩu xác nhận không khớp.", "Password confirmation does not match."),
        new KeyValuePair<string, string>("Tài khoản này chưa bật đăng nhập bằng Windows Hello.\n\n", "Windows Hello sign-in is not enabled for this account.\n\n"),
        new KeyValuePair<string, string>("Tài khoản này chưa có mã TOTP.\n\n", "This account does not have a TOTP code.\n\n"),
        new KeyValuePair<string, string>("Bản sao lưu lên Google Drive cần cụm mật khẩu sao lưu để có thể mở trên máy khác. ", "Google Drive backups need a backup passphrase so they can be opened on another computer. "),
        new KeyValuePair<string, string>("Bản sao lưu lên Google Drive chưa có cụm mật khẩu khôi phục, nên chỉ mở được trên máy này. ", "This Google Drive backup has no recovery passphrase, so it can only be opened on this computer. "),
        new KeyValuePair<string, string>("Cho phép kiểm tra mật khẩu rò rỉ", "Allow checking for breached passwords"),
        new KeyValuePair<string, string>("Cụm mật khẩu không đúng.", "Incorrect passphrase."),
        new KeyValuePair<string, string>("Cụm mật khẩu sao lưu cũ", "Old backup passphrase"),
        new KeyValuePair<string, string>("Cần cụm mật khẩu sao lưu", "Backup passphrase required"),
        new KeyValuePair<string, string>("Không lấy được SID tài khoản Windows.", "Could not retrieve the Windows account SID."),
        new KeyValuePair<string, string>("Không đọc được cụm cũ trên máy này.", "Could not read the old passphrase on this computer."),
        new KeyValuePair<string, string>("Lưu cụm mật khẩu", "Save passphrase"),
        new KeyValuePair<string, string>("Tài khoản không còn trong vault.", "The account is no longer in the vault."),
        new KeyValuePair<string, string>("Tài khoản đang bật xác thực 2 bước", "Two-factor authentication is enabled for this account"),
        new KeyValuePair<string, string>("Tài khoản này chưa bật đăng nhập bằng Windows Hello.", "Windows Hello sign-in is not enabled for this account."),
        new KeyValuePair<string, string>("Tài khoản này chưa có mã TOTP.", "This account does not have a TOTP code."),
        new KeyValuePair<string, string>("Đặt cụm mật khẩu ngay bây giờ?", "Set a passphrase now?"),
        new KeyValuePair<string, string>("🔑 Đặt cụm mật khẩu sao lưu", "🔑 Set backup passphrase"),
        new KeyValuePair<string, string>("🔑 Nhập cụm mật khẩu sao lưu", "🔑 Enter backup passphrase"),
        new KeyValuePair<string, string>("Lịch sử mật khẩu — ", "Password history — "),
        new KeyValuePair<string, string>(" lần sai hoặc hủy mã xác thực 2 bước", " incorrect attempts or cancelled two-factor prompts"),
        new KeyValuePair<string, string>(" lần sai mật khẩu", " incorrect password attempts"),
        new KeyValuePair<string, string>(" mã khôi phục chưa dùng.", " unused recovery codes."),
        new KeyValuePair<string, string>("trùng)", "duplicates)"),
        new KeyValuePair<string, string>(" tài khoản ra:", " accounts to:"),
        new KeyValuePair<string, string>("' (>= 6 ký tự):", "' (at least 6 characters):"),
        new KeyValuePair<string, string>("Mật khẩu phải từ ", "The password must be at least "),
        new KeyValuePair<string, string>("Không ghi được vào thư mục này:", "Could not write to this folder:"),
        new KeyValuePair<string, string>("\n\nKhông xóa được:\n", "\n\nCould not delete:\n"),
        new KeyValuePair<string, string>("\n\nĐã xóa dữ liệu:\n", "\n\nDeleted data:\n"),
        new KeyValuePair<string, string>("Bản build này không có credentials.json nhúng sẵn. ", "This build does not include an embedded credentials.json file. "),
        new KeyValuePair<string, string>(", nhưng Windows Hello không thành công và bạn chưa bật xác thực 2 bước.\n\n", ", but Windows Hello failed and you have not enabled two-factor authentication.\n\n"),
        new KeyValuePair<string, string>("'?\nKhông thể hoàn tác!", "'?\nThis action cannot be undone!"),
        new KeyValuePair<string, string>("'? Không thể hoàn tác.", "'? This action cannot be undone."),
        new KeyValuePair<string, string>("💾 Sao lưu gần nhất: ", "💾 Last backup: "),
        new KeyValuePair<string, string>("Không thể hoàn tác!", "This action cannot be undone!"),
        new KeyValuePair<string, string>("bỏ qua ", "skipped "),
        new KeyValuePair<string, string>("… và ", "… and "),
        new KeyValuePair<string, string>("Website/tài khoản: ", "Website/account: "),
        new KeyValuePair<string, string>("Nhà phát hành: ", "Issuer: "),
        new KeyValuePair<string, string>("Cụm mật khẩu này dùng để mã hóa các bản sao lưu, giúp bạn khôi phục được trên máy khác ", "This passphrase encrypts backups so you can restore them on another computer "),
        new KeyValuePair<string, string>(" bản sao lưu, mới nhất ở trên cùng. ", " backups, newest first. "),
        new KeyValuePair<string, string>(" bản sao lưu File cá nhân, mới nhất ở trên cùng. Chọn một bản rồi bấm Khôi phục.", " personal file backups, newest first. Select one and choose Restore."),
        new KeyValuePair<string, string>(" chữ số • mỗi ", " digits • every "),
        new KeyValuePair<string, string>(" chữ số • ", " digits • "),
        new KeyValuePair<string, string>(" file mã hóa không đọc được; giữ nguyên để xử lý riêng.", " encrypted files could not be read; they were left untouched for separate handling."),
        new KeyValuePair<string, string>(" file đã mã hóa; file cũ sẽ được chuyển đổi khi mở kho.", " files are encrypted; older files will be converted when the vault is opened."),
        new KeyValuePair<string, string>(" giá trị duy nhất xuất hiện trong dữ liệu rò rỉ).", " unique values appeared in breached data)."),
        new KeyValuePair<string, string>(" ký tự trở lên.", " characters or more."),
        new KeyValuePair<string, string>(" ngày • ", " days • "),
        new KeyValuePair<string, string>(" tài khoản chưa rõ ngày đổi MK", " accounts with an unknown password change date"),
        new KeyValuePair<string, string>(" trường nhạy cảm", " sensitive fields"),
        new KeyValuePair<string, string>(" trường • ", " fields • "),
        new KeyValuePair<string, string>(" • chưa phát hiện thay đổi mới.", " • no new changes detected."),
        new KeyValuePair<string, string>(" • dữ liệu đã thay đổi sau lần sao lưu.", " • data changed since the last backup."),
        new KeyValuePair<string, string>(" • mỗi ", " • every "),
        new KeyValuePair<string, string>("(ví dụ khi đổi máy hoặc cài lại Windows). Đồng bộ Drive sẽ không hỏi lại cụm này mỗi lần.", "(for example, after changing computers or reinstalling Windows). Drive sync will not ask for this passphrase each time."),
        new KeyValuePair<string, string>("Mã mới sau ", "Next code in "),
        new KeyValuePair<string, string>("Tìm thấy ", "Found "),
        new KeyValuePair<string, string>("Cần đổi ", "Change "),
        new KeyValuePair<string, string>("Đã thiết lập • ", "Configured • "),
        new KeyValuePair<string, string>("Đọc được ", "Found "),
        new KeyValuePair<string, string>("Độ mạnh: ", "Strength: "),
        new KeyValuePair<string, string>("⚠ Trùng: ", "⚠ Duplicates: "),
        new KeyValuePair<string, string>("✅ Sẽ nhập: ", "✅ To import: "),
        new KeyValuePair<string, string>("📊 Tổng: ", "📊 Total: "),
        new KeyValuePair<string, string>("Đang thử lại sau ", "Retrying in "),
        new KeyValuePair<string, string>("Đã thiết lập ", "Configured "),
        new KeyValuePair<string, string>(" giây", " seconds"),
        new KeyValuePair<string, string>(" mục từ QR chuyển tài khoản. Chọn mục dành cho website đang sửa.", " entries from the account transfer QR code. Select the entry for the website being edited."),
        new KeyValuePair<string, string>(" mục. Chọn một mục để xem thông tin.", " entries. Select an entry to view details."),
        new KeyValuePair<string, string>(" mục trùng.", " duplicate entries."),
        new KeyValuePair<string, string>(" mục", " entries"),
        new KeyValuePair<string, string>("file trùng tên nhưng khác nội dung được lưu thêm với tên mới.", "Files with a duplicate name but different contents are saved under a new name."),
        new KeyValuePair<string, string>("không có", "none"),
        new KeyValuePair<string, string>("Mật khẩu mới phải từ ", "The new password must be at least "),
        new KeyValuePair<string, string>("Tab '👤 Thông tin cá nhân' → Bật 'Đăng nhập bằng Windows Hello'.", "On the '👤 Personal Information' tab, enable 'Sign in with Windows Hello'."),
        new KeyValuePair<string, string>("File cá nhân (", "Personal files ("),
        new KeyValuePair<string, string>(" giây.", " seconds."),
        new KeyValuePair<string, string>("' sẽ được đưa vào thùng rác và thay bằng ghi chú được khôi phục. Tiếp tục?", "' will be moved to Trash and replaced with the restored note. Continue?"),
        new KeyValuePair<string, string>("' trên máy này:", "' on this computer:"),
        new KeyValuePair<string, string>("' vào thùng rác 30 ngày?", "' to Trash for 30 days?"),
        new KeyValuePair<string, string>("' vào vault của '", "' to the vault for '"),
        new KeyValuePair<string, string>("', không phải '", "', not '"),
        new KeyValuePair<string, string>("'. Hãy xác minh đây là domain chính thức trước khi lưu. Tiếp tục?", "'. Verify this is the official domain before saving. Continue?"),
        new KeyValuePair<string, string>(") lên Google Drive?", ") to Google Drive?"),
        new KeyValuePair<string, string>(", vượt giới hạn sao lưu ", ", exceeds the backup limit "),
        new KeyValuePair<string, string>("Bản sao lưu này được tạo bởi người dùng '", "This backup was created by user '"),
        new KeyValuePair<string, string>("Bật đăng nhập bằng Windows Hello cho '", "Enable Windows Hello sign-in for '"),
        new KeyValuePair<string, string>("Cần xác thực để ", "Authentication is required to "),
        new KeyValuePair<string, string>("Dữ liệu còn lại của '", "Remaining data for '"),
        new KeyValuePair<string, string>("Ghi chú hiện tại của '", "The current note for '"),
        new KeyValuePair<string, string>("Hostname thay đổi từ '", "The hostname changed from '"),
        new KeyValuePair<string, string>("Họ tên: ", "Full name: "),
        new KeyValuePair<string, string>("Kho File cá nhân hiện là ", "The personal file vault is now "),
        new KeyValuePair<string, string>("Sao lưu ", "Back up "),
        new KeyValuePair<string, string>("Tên đăng nhập: ", "Username: "),
        new KeyValuePair<string, string>("Tắt đăng nhập bằng Windows Hello cho '", "Disable Windows Hello sign-in for '"),
        new KeyValuePair<string, string>("Vai trò: ", "Role: "),
        new KeyValuePair<string, string>("Xóa ", "Delete "),
        new KeyValuePair<string, string>("Xóa vĩnh viễn '", "Permanently delete '"),
        new KeyValuePair<string, string>("bằng cụm này. Hãy ghi lại cụm ở nơi an toàn.", " with this passphrase. Write it down and keep it somewhere safe."),
        new KeyValuePair<string, string>("vẫn cần cụm cũ để mở, nên hãy giữ lại cụm cũ.", " still require the old passphrase, so keep it."),
        new KeyValuePair<string, string>("Đây có phải cụm của '", "Is this the passphrase for '"),
        new KeyValuePair<string, string>("Đã bật tự động đổi theme theo giờ.\n\n• 06:00 - 18:00: Sáng\n• 18:00 - 06:00: Tối\n\nHiện tại (", "Automatic theme switching is on.\n\n• 06:00 - 18:00: Light\n• 18:00 - 06:00: Dark\n\nCurrent theme at ("),
        new KeyValuePair<string, string>("Đã copy mã. Clipboard sẽ tự xóa sau ", "Code copied. The clipboard will clear in "),
        new KeyValuePair<string, string>("Đã copy password! Clipboard sẽ tự xóa sau ", "Password copied! The clipboard will clear in "),
        new KeyValuePair<string, string>("Đã copy. Clipboard sẽ tự xóa sau ", "Copied. The clipboard will clear in "),
        new KeyValuePair<string, string>("Đã nhập ", "Imported "),
        new KeyValuePair<string, string>("Đưa '", "Move '"),
        new KeyValuePair<string, string>("Không thể ", "Unable to "),
        new KeyValuePair<string, string>("Đã xảy ra lỗi:", "An error occurred:"),
        new KeyValuePair<string, string>("Lỗi:", "Error:"),
        new KeyValuePair<string, string>("Lỗi ", "Error "),
        new KeyValuePair<string, string>("Vui lòng ", "Please "),
        new KeyValuePair<string, string>("Xác thực thất bại", "Authentication failed"),
        new KeyValuePair<string, string>("Mật khẩu hiện tại", "Current password"),
        new KeyValuePair<string, string>("Mật khẩu mới", "New password"),
        new KeyValuePair<string, string>("Không tìm thấy", "Not found"),
        new KeyValuePair<string, string>("đã bị khóa", "is locked"),
        new KeyValuePair<string, string>("Đã lưu", "Saved"),
        new KeyValuePair<string, string>("Đã xóa", "Deleted"),
        new KeyValuePair<string, string>("Khôi phục", "Restore"),
        new KeyValuePair<string, string>("thành công", "successfully"),
        new KeyValuePair<string, string>("Thành công", "Success"),
        new KeyValuePair<string, string>("bị trùng", "duplicate"),
        new KeyValuePair<string, string>("đã được nhập", "was imported"),
        new KeyValuePair<string, string>("đang được sử dụng", "is already in use"),
        new KeyValuePair<string, string>("Không có ", "No "),
        new KeyValuePair<string, string>("Chọn ", "Select "),
        new KeyValuePair<string, string>("File ", "File "),
        new KeyValuePair<string, string>("TOTP", "TOTP"),
        new KeyValuePair<string, string>("CSV", "CSV"),
        new KeyValuePair<string, string>("vault", "vault"),
        new KeyValuePair<string, string>("Windows Hello", "Windows Hello"),
        new KeyValuePair<string, string>("Google Drive", "Google Drive"),
        new KeyValuePair<string, string>("Xóa tài khoản '", "Delete account '"),
        new KeyValuePair<string, string>("Xóa vĩnh viễn '", "Permanently delete '"),
        new KeyValuePair<string, string>("Sao lưu vault gần nhất: ", "Most recent vault backup: "),
        new KeyValuePair<string, string>("Không thể kiểm tra bản sao lưu: ", "Could not verify the backup: "),
        new KeyValuePair<string, string>("Không thể xem trước bản sao lưu: ", "Could not preview the backup: "),
        new KeyValuePair<string, string>("Không thể xác minh bản sao lưu: ", "Could not verify the backup: "),
        new KeyValuePair<string, string>("Không thể đọc trạng thái mã hóa File: ", "Could not read the file encryption state: "),
        new KeyValuePair<string, string>("Lỗi khôi phục File cá nhân: ", "Personal file restore error: "),
        new KeyValuePair<string, string>("Lỗi khôi phục: ", "Restore error: "),
        new KeyValuePair<string, string>("Lỗi nhập CSV: ", "CSV import error: "),
        new KeyValuePair<string, string>("Lỗi xuất CSV: ", "CSV export error: "),
        new KeyValuePair<string, string>("Lỗi đọc CSV: ", "CSV read error: "),
        new KeyValuePair<string, string>("Lỗi đọc log: ", "Log read error: "),
        new KeyValuePair<string, string>("Lỗi đồng bộ File cá nhân: ", "Personal file sync error: "),
        new KeyValuePair<string, string>("Lỗi đồng bộ: ", "Sync error: "),
        new KeyValuePair<string, string>("Lỗi lưu dữ liệu 2FA: ", "Error saving 2FA data: "),
        new KeyValuePair<string, string>("Sai mật khẩu. Còn ", "Incorrect password. Remaining attempts: "),
        new KeyValuePair<string, string>("Bạn vừa dùng 1 mã khôi phục. Còn lại ", "You just used a recovery code. Remaining codes: "),
        new KeyValuePair<string, string>("Không có file nào bị ghi đè: file trùng nội dung được bỏ qua, ", "No files were overwritten; files with identical contents were skipped, "),
        new KeyValuePair<string, string>(" file trùng tên nhưng khác nội dung được lưu thêm với tên mới.", " files with the same name but different contents were saved under a new name."),
        new KeyValuePair<string, string>("Xóa vĩnh viễn luôn các dữ liệu này?", "Permanently delete all of this data?"),
        new KeyValuePair<string, string>("\n• Có: xóa hết, không thể khôi phục.", "\n• Yes: delete everything permanently."),
        new KeyValuePair<string, string>("\n• Không: giữ lại. Cụm sao lưu và phiên Google được lưu theo tên đăng nhập có thể dùng lại nếu tạo tài khoản trùng tên.\n", "\n• No: keep it. The backup passphrase and Google session are stored by username and can be reused if an account with the same name is created.\n"),
        new KeyValuePair<string, string>("\nKho vault, 2FA và file cục bộ được lưu theo ID tài khoản.", "\nThe vault, 2FA data, and local files are stored by account ID."),
        new KeyValuePair<string, string>("\n• Hủy: không xóa gì cả.", "\n• Cancel: do not delete anything."),
        new KeyValuePair<string, string>("\nBản sao lưu trên Google Drive (nếu có) không bị ảnh hưởng.", "\nGoogle Drive backups, if any, are not affected."),
        new KeyValuePair<string, string>("\n\nNhập vào sẽ thêm các tài khoản của '", "\n\nImporting will add the accounts for '"),
        new KeyValuePair<string, string>("\nChỉ tiếp tục nếu đây là dữ liệu của chính bạn (ví dụ bạn dùng tên đăng nhập khác trên máy mới).\n\n", "\nContinue only if this is your data, for example, if you use a different username on the new computer.\n\n"),
        new KeyValuePair<string, string>("\n\nTiếp tục?", "\n\nContinue?"),
        new KeyValuePair<string, string>("\n\nNếu mất máy hoặc cài lại Windows, bạn sẽ không khôi phục được.\n\n", "\n\nIf you lose this computer or reinstall Windows, you will not be able to restore it.\n\n"),
        new KeyValuePair<string, string>("\n\nXóa vĩnh viễn luôn các dữ liệu này?", "\n\nPermanently delete all of this data?"),
        new KeyValuePair<string, string>("\n\nHãy xóa bớt hoặc xuất bớt file ra ngoài rồi thử lại.", "\n\nDelete or export some files and try again."),
        new KeyValuePair<string, string>("\nĐã đồng bộ lên Google Drive thành công!\n\n", "\nSuccessfully synced to Google Drive!\n\n"),
        new KeyValuePair<string, string>("\nMã cũ vẫn còn hiệu lực. Hãy đóng cửa sổ và thử lại.", "\nThe previous code is still valid. Close the window and try again."),
        new KeyValuePair<string, string>("Đăng ký thành công!\n\n", "Registration successful!\n\n"),
        new KeyValuePair<string, string>("\n\nĐặt cụm mật khẩu ngay bây giờ?", "\n\nSet a passphrase now?"),
        new KeyValuePair<string, string>("\n\nTừ giờ các bản sao lưu mới từ máy này sẽ mở được bằng cùng cụm đó.", "\n\nNew backups from this computer will use the same passphrase."),
        new KeyValuePair<string, string>("\n\nHãy cài đặt vân tay / khuôn mặt / PIN trong Cài đặt Windows rồi thử lại.", "\n\nSet up your fingerprint, face, or PIN in Windows Settings and try again."),
        new KeyValuePair<string, string>("\n\nVào Bảo mật → Bật xác thực 2 bước (Authenticator) để có cách xác thực dự phòng.", "\n\nGo to Security → Enable two-factor authentication (Authenticator) to add a backup verification method."),
        new KeyValuePair<string, string>("\n\n👉 Vui lòng đăng nhập bằng mật khẩu, sau đó vào:\n   ", "\n\n👉 Sign in with your password, then go to:\n   "),
        new KeyValuePair<string, string>("\nĐây có phải cụm của '", "\nIs this the passphrase for '"),
        new KeyValuePair<string, string>("\nCó: dùng cụm đó cho tài khoản này (người dùng khác sẽ không nhận được nữa).", "\nYes: use this passphrase for this account. Other users will no longer be offered it."),
        new KeyValuePair<string, string>("\nKhông: giữ nguyên để đúng người đã đặt cụm đó nhận sau.", "\nNo: leave it for the user who originally set it."),
        new KeyValuePair<string, string>(" File cá nhân (", " personal files ("),
        new KeyValuePair<string, string>("\n\nCác file được nén và mã hóa bằng cụm mật khẩu sao lưu trước khi tải lên.", "\n\nFiles are compressed and encrypted with the backup passphrase before upload."),
        new KeyValuePair<string, string>(" mật khẩu cũ nhất đã biết: ", " oldest known password change: "),
        new KeyValuePair<string, string>("\nNhà phát hành: ", "\nIssuer: "),
        new KeyValuePair<string, string>("\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.", "\n\nThe backup is in the 'TQLTK Backup' folder in your Google Drive."),
        new KeyValuePair<string, string>("\nTên đăng nhập: ", "\nUsername: "),
        new KeyValuePair<string, string>("\nHọ tên: ", "\nFull name: "),
        new KeyValuePair<string, string>("\n\nVai trò: ", "\n\nRole: "),
        new KeyValuePair<string, string>("\n\nVui lòng đăng nhập để sử dụng.", "\n\nSign in to continue."),
        new KeyValuePair<string, string>("Không thể cập nhật thùng rác. Dữ liệu hiện có vẫn được giữ nguyên.\n\n", "Could not update Trash. Existing data was preserved.\n\n"),
        new KeyValuePair<string, string>("Không thể dọn thùng rác đã hết hạn. Dữ liệu hiện có vẫn được giữ nguyên.\n\n", "Could not empty expired Trash items. Existing data was preserved.\n\n"),
        new KeyValuePair<string, string>("Không thể hoàn tất kiểm tra; chưa thể kết luận an toàn. ", "Could not complete the check; safety could not be confirmed. "),
        new KeyValuePair<string, string>("Không thể khôi phục mật khẩu từ lịch sử. Vault chưa được cập nhật.\n\n", "Could not restore the password from history. The vault was not changed.\n\n"),
        new KeyValuePair<string, string>("Không thể khởi động ứng dụng:\n\n", "Could not start the application:\n\n"),
        new KeyValuePair<string, string>("Không thể đọc hoặc mã hóa kho File cá nhân. File chưa chuyển đổi được vẫn được giữ nguyên.\n\n", "Could not read or encrypt the personal file vault. Files that could not be converted were left unchanged.\n\n"),
        new KeyValuePair<string, string>("Không thể xác định trạng thái xác thực 2 bước. Đăng nhập đã bị từ chối để bảo vệ tài khoản.\n\nChi tiết: ", "Could not determine the two-factor authentication state. Sign-in was denied to protect the account.\n\nDetails: "),
        new KeyValuePair<string, string>("Không thể xác định trạng thái xác thực 2 bước. Đăng nhập đã bị từ chối để bảo vệ tài khoản.\n\n", "Could not determine the two-factor authentication state. Sign-in was denied to protect the account.\n\n"),
        new KeyValuePair<string, string>("Đồng bộ thất bại.\n\n", "Sync failed.\n\n"),
    };
}
