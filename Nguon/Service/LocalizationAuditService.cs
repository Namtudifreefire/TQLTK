using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace PersonalInfoManager.Services;
public sealed record LocalizationAuditIssue(string Key, string Problem);
public sealed record LocalizedMessageDefinition(string Id, string Vietnamese, string English, IReadOnlyList<string> Parameters);
public static class LocalizationAuditService
{
    private sealed record Message(string Vietnamese, string English);
    private static readonly IReadOnlyDictionary<string, Message> Messages =
        new Dictionary<string, Message>(StringComparer.Ordinal)
        {
            ["backup.status.Added"] = new("Được thêm", "Added"),
            ["backup.status.Modified"] = new("Đã sửa", "Modified"),
            ["backup.status.Removed"] = new("Đã xóa", "Deleted"),
            ["backup.status.Changed"] = new("Đã thay đổi", "Changed"),
            ["backup.status.NotComparable"] = new("Không so sánh được", "Cannot compare"),
            ["backup.status.AlwaysIncluded"] = new("Luôn có", "Always included"),
            ["backup.status.Included"] = new("Có trong gói", "Included in package"),
            ["backup.status.Excluded"] = new("Không có trong gói", "Not included in package"),
            ["backup.status.KeepExisting"] = new("Giữ bản đang có", "Keep existing account"),
            ["backup.status.AddAccount"] = new("Sẽ thêm", "Will add account"),
            ["backup.status.KeepNote"] = new("Giữ ghi chú đang có", "Keep existing note"),
            ["backup.status.AddNote"] = new("Sẽ thêm ghi chú", "Will add note"),
            ["backup.status.SkipDuplicateFile"] = new("Bỏ qua tệp trùng", "Skip duplicate file"),
            ["backup.status.AddRenamedCopy"] = new("Sẽ thêm bản sao có tên mới", "Will add a renamed copy"),
            ["backup.status.AddFile"] = new("Sẽ thêm tệp", "Will add file"),
            ["backup.status.SkipDuplicate"] = new("Bỏ qua trùng", "Skip duplicates"),
            ["backup.status.Restore"] = new("Sẽ khôi phục", "Restore"),
            ["backup.status.Import"] = new("Sẽ nhập", "Will import"),
            ["backup.source.None"] = new("Không khôi phục", "Do not restore"),
            ["backup.source.Older"] = new("Bản sao lưu cũ", "Older backup"),
            ["backup.source.Newer"] = new("Bản sao lưu mới", "Newer backup"),
            ["backup.source.Unsupported"] = new("Không hỗ trợ khôi phục riêng", "Individual restore is unavailable"),
            ["auth.user.message001"] = new("Hết thời gian chờ khóa dữ liệu đăng nhập.", "Timed out waiting for the sign-in data lock."),
            ["auth.user.message002"] = new("Tài khoản đầu tiên đã được tạo. Hãy đăng ký tài khoản người dùng thông thường.", "The first account has been created. Register a standard user account."),
            ["auth.user.message003"] = new("Tên đăng nhập phải dài từ 3 đến 64 ký tự và không chứa ký tự điều khiển.", "The username must be 3 to 64 characters long and contain no control characters."),
            ["auth.user.message004"] = new("Họ tên không được để trống.", "Full name cannot be empty."),
            ["auth.user.message005"] = new("Email không hợp lệ.", "The email address is invalid."),
            ["auth.user.message006"] = new("Đã tạo tài khoản quản trị viên đầu tiên.", "The first administrator account was created."),
            ["auth.user.message007"] = new("Tên đăng nhập đã tồn tại.", "The username already exists."),
            ["auth.user.message008"] = new("Email đã được sử dụng.", "This email address is already in use."),
            ["auth.user.message009"] = new("Tạo tài khoản thành công.", "Account created successfully."),
            ["auth.user.message010"] = new("Không thể khóa dữ liệu đăng nhập giữa các tiến trình; yêu cầu đăng nhập bị từ chối để bảo vệ tài khoản.", "Could not lock sign-in data across processes. Sign-in was denied to protect the account."),
            ["auth.user.message011"] = new("Tài khoản không tồn tại.", "The account does not exist."),
            ["auth.user.message012"] = new("Tài khoản đã bị khóa.", "The account is locked."),
            ["auth.user.message013"] = new("Mật khẩu mặc định cũ đã bị vô hiệu hóa. Hãy dùng Windows Hello nếu đã bật ", "The old default password has been disabled. Use Windows Hello if it is enabled "),
            ["auth.user.message014"] = new("Đăng nhập thành công.", "Sign-in successful."),
            ["auth.user.message015"] = new("Mật khẩu cũ không đúng.", "The old password is incorrect."),
            ["auth.user.message016"] = new("Mật khẩu mới phải khác mật khẩu cũ.", "The new password must be different from the current password."),
            ["auth.user.message017"] = new("Đổi mật khẩu thành công.", "Password changed successfully."),
            ["auth.user.message018"] = new("Đã đặt lại mật khẩu. Người dùng sẽ phải đổi mật khẩu khi đăng nhập lần tới.", "Password reset. The user will have to change it at the next sign-in."),
            ["auth.user.message019"] = new("Email đã được sử dụng bởi user khác.", "This email address is already used by another user."),
            ["auth.user.message020"] = new("Cập nhật thành công.", "Updated successfully."),
            ["auth.user.message021"] = new("Không thể xóa tài khoản đang đăng nhập.", "You cannot delete the account currently signed in."),
            ["auth.user.message022"] = new("Không thể xóa admin cuối cùng.", "The last administrator account cannot be deleted."),
            ["auth.user.message023"] = new("Không thể thu quyền admin cuối cùng.", "The last administrator account cannot be demoted."),
            ["auth.user.message024"] = new("Đã đổi vai trò.", "The role was changed."),
            ["auth.user.message025"] = new("Không lấy được SID tài khoản Windows.", "Could not retrieve the Windows account SID."),
            ["auth.user.message026"] = new("Chỉ có thể liên kết Windows Hello với tài khoản Windows hiện tại.", "Windows Hello can only be linked to the current Windows account."),
            ["auth.twofactor.message001"] = new("Không tìm thấy tài khoản để khóa dữ liệu 2FA.", "No account was found to lock the 2FA data."),
            ["auth.twofactor.message002"] = new("Hết thời gian chờ khóa dữ liệu 2FA.", "Timed out waiting for the 2FA data lock."),
            ["auth.twofactor.message003"] = new("Không đọc được dữ liệu 2FA của tài khoản này.", "Could not read this account's 2FA data."),
            ["auth.twofactor.message004"] = new("Vui lòng nhập mã.", "Enter the code."),
            ["auth.twofactor.message005"] = new("Không tìm thấy tài khoản để lưu trạng thái 2FA.", "No account was found to save the 2FA state."),
            ["auth.twofactor.message006"] = new("Không thể lưu trạng thái 2FA cũ. Đăng nhập bị từ chối để bảo vệ tài khoản.", "Could not save the previous 2FA state. Sign-in was denied to protect the account."),
            ["auth.twofactor.message007"] = new("Tài khoản đã có cấu hình 2FA hoặc cần khôi phục cấu hình hiện có.", "The account already has 2FA configured or its existing configuration must be restored."),
            ["auth.twofactor.message008"] = new("Mã phải gồm đúng 6 chữ số.", "The code must contain exactly 6 digits."),
            ["auth.twofactor.message009"] = new("Mã không đúng hoặc đã hết hạn. Kiểm tra giờ trên điện thoại và máy tính rồi thử lại.", "The code is incorrect or expired. Check the time on your phone and computer, then try again."),
            ["auth.twofactor.message010"] = new("Không tìm thấy tài khoản để bật 2FA.", "No account was found to enable 2FA."),
            ["auth.twofactor.message011"] = new("Đã bật xác thực 2 bước.", "Two-factor authentication is enabled."),
            ["auth.twofactor.message012"] = new("Không tìm thấy tài khoản để lưu trạng thái tắt 2FA.", "No account was found to save the disabled 2FA state."),
            ["auth.twofactor.message013"] = new("Không có mã khôi phục để lưu.", "There are no recovery codes to save."),
            ["auth.twofactor.message014"] = new("Ký tự Base32 không hợp lệ.", "The Base32 character is invalid."),
            ["auth.twofactor.message015"] = new("Không tìm thấy tài khoản để xác định cấu hình 2FA riêng.", "No account was found to determine the per-account 2FA configuration."),
            ["auth.twofactor.message016"] = new("Không thể xác định trạng thái file cấu hình 2FA.", "Could not determine the state of the 2FA configuration file."),
            ["storage.vault.message001"] = new("Không tìm thấy tài khoản để xác định kho dữ liệu riêng.", "Could not find the account for its isolated data vault."),
            ["storage.vault.message002"] = new("Tài khoản đã bị khóa, không thể truy cập kho dữ liệu.", "This account is locked and its vault cannot be accessed."),
            ["storage.vault.message003"] = new("Kiểm tra sau mã hóa thất bại, dữ liệu chưa được ghi.", "Post-encryption verification failed; no data was written."),
            ["storage.vault.message004"] = new("Một tài khoản có quá 50 tệp đính kèm.", "An account cannot have more than 50 attachments."),
            ["storage.vault.message005"] = new("Một tệp đính kèm không hợp lệ hoặc vượt quá 10 MB.", "An attachment is invalid or exceeds 10 MB."),
            ["storage.vault.message006"] = new("Tổng tệp đính kèm của tài khoản vượt quá 25 MB.", "The account's total attachment size exceeds 25 MB."),
            ["backup.vault.message001"] = new("Vault trống, không có gì để xuất.", "The vault is empty; there is nothing to export."),
            ["backup.vault.message002"] = new("File không tồn tại.", "The file does not exist."),
            ["backup.vault.message003"] = new("File trống.", "The file is empty."),
            ["backup.vault.message004"] = new("Bản sao lưu cần cụm mật khẩu sao lưu.", "The backup requires a backup passphrase."),
            ["backup.vault.message005"] = new("Định dạng backup không được hỗ trợ hoặc không có kiểm tra toàn vẹn.", "The backup format is unsupported or does not provide integrity protection."),
            ["backup.vault.message006"] = new("Backup không chứa danh sách tài khoản hợp lệ.", "The backup does not contain a valid account list."),
            ["backup.vault.message007"] = new("Backup có mục tài khoản không hợp lệ.", "The backup contains an invalid account entry."),
            ["backup.vault.message008"] = new("Số lượng tài khoản trong manifest không khớp dữ liệu.", "The account count in the manifest does not match the data."),
            ["backup.vault.message009"] = new("Thư mục diễn tập không nằm trong thư mục tạm.", "The restore drill folder is outside the temporary directory."),
            ["backup.vault.message010"] = new("Thư mục diễn tập không được là liên kết hệ thống.", "The restore drill folder cannot be a reparse point."),
            ["backup.vault.message011"] = new("Nội dung tài khoản, TOTP hoặc tệp đính kèm sau khôi phục thử không khớp bản sao lưu.", "The accounts, TOTP data, or attachments from the restore drill do not match the backup."),
            ["backup.vault.message012"] = new("File này được bảo vệ bằng cụm mật khẩu sao lưu. Hãy nhập cụm mật khẩu để mở.", "This file is protected by a backup passphrase. Enter the passphrase to open it."),
            ["backup.vault.message013"] = new("Không thể giải mã file. Cụm mật khẩu không đúng hoặc file bị hỏng.", "Could not decrypt the file. The passphrase is incorrect or the file is damaged."),
            ["backup.vault.message014"] = new("Bản sao lưu không dùng định dạng AES-GCM v2. CBC cũ không có kiểm tra toàn vẹn, ", "The backup does not use the AES-GCM v2 format. Legacy CBC has no integrity check, "),
            ["backup.vault.message015"] = new("Không thể giải mã file. File có thể bị hỏng hoặc không đúng định dạng.", "Could not decrypt the file. It may be damaged or in an unsupported format."),
            ["backup.vault.message016"] = new("File không chứa tài khoản nào.", "The file does not contain any accounts."),
            ["backup.vault.message017"] = new("Bản sao lưu có mục tài khoản không hợp lệ.", "The backup contains an invalid account entry."),
            ["backup.vault.message018"] = new("Đã giải mã bản sao lưu và tính các mục sẽ nhập.", "The backup was decrypted and the entries to import were counted."),
            ["backup.vault.message019"] = new("Bản xem trước đã được sử dụng hoặc không còn dữ liệu.", "This preview has already been used or no longer contains data."),
            ["backup.vault.message020"] = new("Vault đã thay đổi sau khi xem trước. Hãy tạo bản xem trước mới.", "The vault changed after the preview. Create a new preview."),
            ["backup.files.message001"] = new("File không tồn tại.", "The file does not exist."),
            ["backup.files.message002"] = new("File quá lớn so với một bản sao lưu File cá nhân của TQLTK.", "The file is too large for a TQLTK personal file backup."),
            ["backup.files.message003"] = new("File này không phải bản sao lưu File cá nhân của TQLTK.", "This is not a TQLTK personal file backup."),
            ["backup.files.message004"] = new("Chưa giải mã được bản sao lưu. Hãy kiểm tra cụm mật khẩu hoặc file.", "Could not decrypt the backup. Check the passphrase or the file."),
            ["backup.files.message005"] = new("Bản sao lưu thiếu manifest hợp lệ của TQLTK.", "The backup is missing a valid TQLTK manifest."),
            ["backup.files.message006"] = new("Bản sao lưu không chứa file hợp lệ.", "The backup does not contain any valid files."),
            ["backup.files.message007"] = new("Bản sao lưu vượt giới hạn dung lượng cho phép.", "The backup exceeds the allowed size limit."),
            ["backup.files.message008"] = new("Manifest không khớp số lượng hoặc dung lượng dữ liệu trong bản sao lưu.", "The manifest does not match the backup's file count or data size."),
            ["backup.files.message009"] = new("Đã hủy kiểm tra bản sao lưu.", "Backup verification was cancelled."),
            ["backup.files.message010"] = new("Bản sao lưu có tên file trùng nhau nên không thể diễn tập an toàn.", "The backup contains duplicate file names and cannot be safely restore-tested."),
            ["backup.files.message011"] = new("Thư mục diễn tập không nằm trong thư mục tạm.", "The restore drill folder is outside the temporary directory."),
            ["backup.files.message012"] = new("Thư mục diễn tập không được là liên kết hệ thống.", "The restore drill folder cannot be a reparse point."),
            ["backup.files.message013"] = new("Đã hủy diễn tập khôi phục; kho thật không thay đổi.", "Restore drill was cancelled; the actual store was unchanged."),
            ["backup.files.message014"] = new("Kho File cá nhân đang trống, không có gì để sao lưu.", "The personal file vault is empty; there is nothing to back up."),
            ["backup.files.message015"] = new("Kho File đã thay đổi trong lúc xem trước. Hãy thử lại.", "The personal file vault changed while being previewed. Try again."),
            ["backup.files.message016"] = new("Đã giải mã và xem trước bản sao lưu; kho chưa bị thay đổi.", "The backup was decrypted and previewed; the vault has not been changed."),
            ["backup.files.message017"] = new("Đã hủy xem trước bản sao lưu.", "Backup preview was cancelled."),
            ["backup.files.message018"] = new("File sao lưu đã thay đổi sau khi xem trước. Hãy xem trước lại.", "The backup file changed after the preview. Preview it again."),
            ["backup.files.message019"] = new("Đã hủy: chưa nhập đúng cụm mật khẩu sao lưu.", "Cancelled: the correct backup passphrase was not entered."),
            ["backup.files.message020"] = new("Bản sao lưu thiếu manifest của TQLTK.", "The backup is missing the TQLTK manifest."),
            ["backup.files.message021"] = new("Kho File đã thay đổi sau khi xem trước. Hãy xem trước lại.", "The personal file vault changed after the preview. Preview it again."),
            ["backup.files.message022"] = new("Bản sao lưu không chứa file nào để khôi phục.", "The backup contains no files to restore."),
            ["backup.files.message023"] = new("Bản sao lưu có dung lượng lớn bất thường nên bị từ chối.", "The backup was rejected because its size is unusually large."),
            ["backup.files.message024"] = new("Nội dung file nén dài hơn dung lượng đã khai báo.", "The compressed file contents exceed the declared size."),
            ["backup.files.message025"] = new("Một file trong bản sao lưu lớn hơn dung lượng đã khai báo.", "A file in the backup is larger than its declared size."),
            ["backup.files.message026"] = new("Một file trong bản sao lưu ngắn hơn dung lượng đã khai báo.", "A file in the backup is smaller than its declared size."),
            ["backup.complete.message001"] = new("Đã tạo và xác minh gói sao lưu đầy đủ.", "Full backup package created and verified."),
            ["backup.complete.message002"] = new("Đã xem trước. Khôi phục chỉ bổ sung mục còn thiếu, không ghi đè dữ liệu hiện tại.", "Preview ready. Restore adds missing items only and does not overwrite existing data."),
            ["backup.complete.message003"] = new("Gói đã thay đổi sau khi xem trước. Hãy xem trước lại.", "The package changed after the preview. Preview it again."),
            ["backup.complete.message004"] = new("Gói được tạo bởi tài khoản khác. Cần xác nhận rõ trước khi nhập.", "This package was created by another account. Explicit confirmation is required before importing it."),
            ["backup.complete.message005"] = new("Dữ liệu trên máy đã thay đổi sau khi xem trước. Hãy xem trước lại.", "Data on this computer changed after the preview. Preview it again."),
            ["backup.complete.message006"] = new("Chưa chọn mục cần khôi phục.", "No recovery items were selected."),
            ["backup.complete.message007"] = new("Một gói đã thay đổi sau khi so sánh. Hãy so sánh lại trước khi khôi phục.", "A backup package changed after comparison. Compare the backups again before recovery."),
            ["backup.complete.message008"] = new("Danh sách mục khôi phục không khớp với kết quả so sánh. Hãy mở lại phần so sánh.", "The selected recovery items do not match the comparison results. Reopen the comparison."),
            ["backup.complete.message009"] = new("Có gói được tạo bởi tài khoản khác. Cần xác nhận rõ trước khi nhập.", "A package was created by another account. Explicit confirmation is required before importing."),
            ["backup.complete.message010"] = new("Dữ liệu hiện tại đã thay đổi kể từ lúc so sánh. Hãy so sánh lại để bảo vệ các chỉnh sửa mới.", "Current data changed after comparison. Compare the backups again to protect your newer edits."),
            ["backup.complete.message011"] = new("Mã tài khoản được chọn không hợp lệ.", "The selected account ID is invalid."),
            ["backup.complete.message012"] = new("Không tìm thấy tài khoản đã chọn trong bản sao lưu.", "The selected account was not found in the backup."),
            ["backup.complete.message013"] = new("Mã ghi chú được chọn không hợp lệ.", "The selected note ID is invalid."),
            ["backup.complete.message014"] = new("Không tìm thấy ghi chú đã chọn trong bản sao lưu.", "The selected note was not found in the backup."),
            ["backup.complete.message015"] = new("Không tìm thấy tệp đã chọn trong bản sao lưu.", "The selected file was not found in the backup."),
            ["backup.complete.message016"] = new("Thiếu tệp đã chọn trong gói sao lưu.", "The selected file is missing from the backup package."),
            ["backup.complete.message017"] = new("Loại mục khôi phục không được hỗ trợ.", "This recovery item type is not supported."),
            ["storage.rotation.message001"] = new("Tài khoản không hợp lệ.", "The account is invalid."),
            ["storage.rotation.message002"] = new("Xác minh dữ liệu sau khi mã hóa lại thất bại.", "Data verification failed after re-encryption."),
            ["storage.rotation.message003"] = new("Không xoay bản sao lưu nằm trong thư mục liên kết hệ thống.", "Cannot rotate a backup in a reparse-point directory."),
            ["storage.rotation.message004"] = new("Phục hồi xoay khóa bị dừng vì key.dat đã đổi ngoài giao dịch. Dữ liệu và nhật ký được giữ nguyên.", "Key rotation recovery stopped because key.dat changed outside the transaction. Data and the journal were left unchanged."),
            ["storage.rotation.message005"] = new("Journal xoay khóa không hợp lệ.", "The key rotation journal is invalid."),
            ["ui.mainwindow.message001"] = new("Chọn thời gian tự khóa hợp lệ.", "Select a valid auto-lock duration."),
            ["ui.mainwindow.message002"] = new("Cài đặt bảo mật", "Security settings"),
            ["ui.mainwindow.message003"] = new("Đã lưu cài đặt tự khóa.", "Auto-lock settings saved."),
            ["ui.mainwindow.message004"] = new("Đổi thuật toán băm mật khẩu", "Change password hashing algorithm"),
            ["ui.mainwindow.message005"] = new("Đã lưu lựa chọn thuật toán băm cho mật khẩu mới và lần nâng cấp hash tiếp theo.", "The hashing choice for new passwords and the next hash upgrade has been saved."),
            ["ui.mainwindow.message006"] = new("Chỉ quản trị viên đang hoạt động mới được phép xoay khóa vault.", "Only an active administrator can rotate the vault key."),
            ["ui.mainwindow.message007"] = new("Không đủ quyền", "Insufficient permissions"),
            ["ui.mainwindow.message008"] = new("Trước khi xoay khóa, hãy đặt cụm mật khẩu sao lưu trong Tiện ích → Cụm mật khẩu sao lưu. Bạn cần tạo một bản phục hồi mở được độc lập với khóa Windows cũ.", "Before rotating the key, set a backup passphrase under Utilities → Backup passphrase. You need a recovery copy that does not depend on the old Windows key."),
            ["ui.mainwindow.message009"] = new("Cần chuẩn bị bản phục hồi", "Prepare a recovery copy first"),
            ["ui.mainwindow.message010"] = new("Ứng dụng sẽ tạo và xác minh bản backup có cụm mật khẩu, sau đó mã hóa lại vault, ghi chú và các backup cục bộ đã nhận diện. Khóa cũ được giữ dưới DPAPI để mở bản xuất lịch sử. Tệp cá nhân dùng khóa riêng và không bị thay đổi. Tiếp tục?", "The app will create and verify a passphrase-protected backup, then re-encrypt the vault, notes, and recognized local backups. The old key is retained under DPAPI to open historical exports. Personal files use a separate key and will not change. Continue?"),
            ["ui.mainwindow.message011"] = new("Xác nhận xoay khóa vault", "Confirm vault key rotation"),
            ["ui.mainwindow.message012"] = new("Đã xoay khóa và mã hóa lại {0} tệp cục bộ.\nBản phục hồi đã xác minh ({1} tài khoản): {2}\nCác backup có cụm mật khẩu vẫn độc lập; khóa cũ được lưu mã hóa để đọc bản xuất lịch sử.", "The key was rotated and {0} local files were re-encrypted.\nVerified recovery copy ({1} accounts): {2}\nPassphrase-protected backups remain independent; the old key is stored in protected form for historical exports."),
            ["ui.mainwindow.message013"] = new("Đã giữ nguyên {0} backup dùng cụm mật khẩu.", "Preserved {0} passphrase-protected backups."),
            ["ui.mainwindow.message014"] = new("Xoay khóa hoàn tất", "Key rotation complete"),
            ["ui.mainwindow.message015"] = new("Không thể hoàn tất xoay khóa. Dữ liệu gốc và nhật ký phục hồi được giữ lại; không xóa thư mục dữ liệu ứng dụng. Lần đọc tiếp theo sẽ thử tiếp tục giao dịch.\n\n{0}", "Key rotation could not be completed. Original data and the recovery journal were retained; do not delete the application data folder. The next read will try to resume the transaction.\n\n{0}"),
            ["ui.mainwindow.message016"] = new("Xoay khóa chưa hoàn tất", "Key rotation incomplete"),
            ["ui.mainwindow.message017"] = new("Mục này chưa có URL HTTP hoặc HTTPS hợp lệ.", "This item does not have a valid HTTP or HTTPS URL."),
            ["ui.mainwindow.message018"] = new("Không có website", "No website"),
            ["ui.mainwindow.message019"] = new("Domain {0} có dấu hiệu cần kiểm tra: {1}\n\nBạn vẫn muốn mở website này?", "Domain {0} has risk indicators: {1}\n\nDo you still want to open this website?"),
            ["ui.mainwindow.message020"] = new("Kiểm tra domain", "Check domain"),
            ["ui.mainwindow.message021"] = new("Không thể mở website: {0}", "Could not open website: {0}"),
            ["ui.mainwindow.message022"] = new("Lỗi", "Error"),
            ["ui.mainwindow.message023"] = new("Đưa {0} tài khoản đã chọn vào thùng rác trong 30 ngày?", "Move the {0} selected accounts to Trash for 30 days?"),
            ["ui.mainwindow.message024"] = new("Xác nhận xóa hàng loạt", "Confirm bulk deletion"),
            ["ui.mainwindow.message025"] = new("Không đọc được dữ liệu hoàn tác nhập: ", "Could not read import undo data: "),
            ["ui.mainwindow.message026"] = new("Không có lần nhập nào có thể hoàn tác trên máy này.", "There is no import batch available to undo on this computer."),
            ["ui.mainwindow.message027"] = new("Thông tin", "Information"),
            ["ui.mainwindow.message028"] = new("Không thể hoàn tác cả lô vì một hoặc nhiều tài khoản đã bị xóa hoặc thay đổi sau lần nhập.", "The whole batch cannot be undone because one or more accounts were deleted or changed after import."),
            ["ui.mainwindow.message029"] = new("Không thể hoàn tác", "Cannot undo"),
            ["ui.mainwindow.message030"] = new("Hoàn tác lần nhập gồm {0} tài khoản? Thay đổi sau lần nhập sẽ bị từ chối để tránh mất dữ liệu.", "Undo the import of {0} accounts? Changes made since the import will be rejected to prevent data loss."),
            ["ui.mainwindow.message031"] = new("Hoàn tác nhập", "Undo import"),
            ["ui.mainwindow.message032"] = new("Đã hoàn tác lần nhập.", "The import batch was undone."),
            ["ui.mainwindow.message033"] = new("Hoàn tất", "Done"),
            ["ui.mainwindow.message034"] = new("Lỗi nhập dữ liệu", "Import error"),
            ["ui.mainwindow.message035"] = new("Không có thay đổi nào để nhập.", "There are no changes to import."),
            ["ui.mainwindow.message036"] = new("Kết quả nhập dữ liệu", "Import result"),
            ["ui.mainwindow.message037"] = new("Đã thêm {0}, ghi đè {1}, bỏ qua {2} tài khoản. Có thể hoàn tác lô này bằng nút Hoàn tác nhập.", "Added {0}, replaced {1}, and skipped {2} accounts. Use Undo import to reverse this batch."),
            ["ui.mainwindow.message038"] = new("Lỗi nhập dữ liệu: ", "Import error: "),
            ["ui.mainwindow.message039"] = new("Thông tin hệ thống", "System information"),
            ["ui.mainwindow.message040"] = new("Đã bật tự động đổi giao diện theo giờ.\n\n• 06:00 - 18:00: Sáng\n• 18:00 - 06:00: Tối\n\nHiện tại ({0}): {1}", "Automatic theme switching is on.\n\n• 06:00–18:00: Light\n• 18:00–06:00: Dark\n\nCurrent ({0}): {1}"),
            ["ui.mainwindow.message041"] = new("Chính sách đã lưu", "Policy saved"),
            ["ui.mainwindow.message042"] = new("Đã lưu chính sách sao lưu.", "Backup policy saved."),
            ["ui.mainwindow.message043"] = new("Thành công", "Success"),
            ["ui.mainwindow.message044"] = new("Không lưu được chính sách sao lưu: {0}", "Could not save the backup policy: {0}"),
            ["ui.mainwindow.message045"] = new("Sao lưu định kỳ thất bại", "Scheduled backup failed"),
            ["ui.mainwindow.message046"] = new("Gói chuyển máy cần cụm mật khẩu để mở trên thiết bị khác. Hãy đặt cụm riêng; không dùng mật khẩu đăng nhập.", "A migration package needs a passphrase so it can be opened on another device. Set a separate passphrase; do not use your sign-in password."),
            ["ui.mainwindow.message047"] = new("Cần cụm mật khẩu sao lưu", "Backup passphrase required"),
            ["ui.mainwindow.message048"] = new("Không tạo được gói sao lưu", "Could not create backup package"),
            ["ui.mainwindow.message049"] = new("Sao lưu hoàn tất", "Backup complete"),
            ["ui.mainwindow.message050"] = new("Không tạo được gói sao lưu: {0}", "Could not create backup package: {0}"),
            ["ui.mainwindow.message051"] = new("Không thể khôi phục gói sao lưu: {0}", "Could not restore the backup package: {0}"),
            ["ui.mainwindow.message052"] = new("Không thể xác minh bản cũ", "Could not verify the older backup"),
            ["ui.mainwindow.message053"] = new("Không thể xác minh bản mới", "Could not verify the newer backup"),
            ["ui.mainwindow.message054"] = new("Không thể xác minh gói", "Could not verify package"),
            ["ui.mainwindow.message055"] = new("Không thể xem trước", "Could not prepare preview"),
            ["ui.mainwindow.message056"] = new("Không rõ", "Unknown"),
            ["ui.mainwindow.message057"] = new("Đã hủy diễn tập vì phiên đăng nhập không còn hoạt động.", "The drill was cancelled because the sign-in session is no longer active."),
            ["ui.mainwindow.message058"] = new("Đã hủy", "Cancelled"),
            ["ui.mainwindow.message059"] = new("Chọn đúng một tài khoản để tạo chia sẻ.", "Select exactly one account to create a share."),
            ["ui.mainwindow.message060"] = new("Chọn một tài khoản trước.", "Select an account first."),
            ["ui.mainwindow.message061"] = new("Gói được mã hóa sẽ chứa toàn bộ tài khoản '{0}', gồm mật khẩu, TOTP, trường tùy chỉnh và tệp đính kèm. Cấp quyền đọc Drive cho {1} trong {2} ngày?", "The encrypted package will contain the full '{0}' account, including its password, TOTP, custom fields and attachments. Grant {1} Drive read access for {2} days?"),
            ["ui.mainwindow.message062"] = new("Xác nhận chia sẻ thông tin nhạy cảm", "Confirm sharing sensitive information"),
            ["ui.mainwindow.message063"] = new("Không thể tạo liên kết Drive: ", "Could not create the Drive share: "),
            ["ui.mainwindow.message064"] = new("Lỗi chia sẻ", "Sharing error"),
            ["ui.mainwindow.message065"] = new("Không thể tạo chia sẻ: ", "Could not create the share: "),
            ["ui.mainwindow.message066"] = new("Không thể tải liên kết đang chia sẻ: ", "Could not load active shares: "),
            ["ui.mainwindow.message067"] = new("Thu hồi quyền Drive của {0} và chuyển gói chia sẻ vào thùng rác? Bản sao đã tải xuống vẫn có thể được giữ.", "Revoke {0}'s Drive access and move the shared package to the trash? Downloaded copies may still be kept."),
            ["ui.mainwindow.message068"] = new("Thu hồi chia sẻ", "Revoke share"),
            ["ui.mainwindow.message069"] = new("Không thể thu hồi chia sẻ: ", "Could not revoke the share: "),
            ["ui.mainwindow.message070"] = new("Quyền đã bị thu hồi", "Access revoked"),
            ["ui.mainwindow.message071"] = new("Lỗi quản lý chia sẻ: ", "Share management error: "),
            ["ui.mainwindow.message072"] = new("Gói chia sẻ đã hết hạn trong lúc xem trước.", "The share package expired while it was being reviewed."),
            ["ui.mainwindow.message073"] = new("Gói đã hết hạn", "Package expired"),
            ["ui.mainwindow.message074"] = new("Không thể mở gói chia sẻ: ", "Could not open the share package: "),
            ["ui.mainwindow.message075"] = new("Tài khoản trùng không còn tồn tại; không có gì được ghi đè.", "The duplicate account no longer exists; nothing was overwritten."),
            ["ui.mainwindow.message076"] = new("Không thể ghi đè", "Could not overwrite"),
            ["ui.mainwindow.message077"] = new("Đã thêm mục được chia sẻ. Có thể hoàn tác bằng nút Hoàn tác nhập CSV.", "The shared item was added. You can undo it with the Undo CSV Import button."),
            ["ui.mainwindow.message078"] = new("Đã nhập mục được chia sẻ", "Shared item imported"),
            ["ui.mainwindow.message079"] = new("Không thể kiểm tra bản trên Drive: ", "Could not check the Drive backup: "),
            ["ui.mainwindow.message080"] = new("Lỗi đồng bộ", "Sync error"),
            ["ui.mainwindow.message081"] = new("Drive đã có bản của tài khoản này nhưng máy chưa có mốc đồng bộ. Không thể xác định an toàn bản nào mới hơn.\n\nCó: tải kho trên máy này lên thành một bản riêng, giữ lại các bản Drive hiện có.\nKhông: hủy và dùng Khôi phục từ Google Drive để xem bản từ xa.\nHủy: dừng.", "Drive already has a backup for this account, but this device has no sync checkpoint, so the newer copy cannot be identified safely.\n\nYes: upload this device's vault as a separate version and keep existing Drive backups.\nNo: cancel and use Restore from Google Drive to review the remote copy.\nCancel: stop."),
            ["ui.mainwindow.message082"] = new("Chưa có mốc đồng bộ", "No sync checkpoint"),
            ["ui.mainwindow.message083"] = new("Bản Drive đã đổi kể từ lần đồng bộ trước, còn kho trên máy này chưa đổi. Không có dữ liệu nào được ghi lên Drive. Dùng Khôi phục từ Google Drive để xem và nhập bản mới.", "The Drive backup changed since the last sync, while this device's vault did not. Nothing was uploaded. Use Restore from Google Drive to review and import the newer copy."),
            ["ui.mainwindow.message084"] = new("Có thay đổi trên Drive", "Drive changes detected"),
            ["ui.mainwindow.message085"] = new("Cả kho trên máy này và bản Drive đều đã đổi kể từ lần đồng bộ trước. Đồng bộ tự động đã dừng để tránh ghi đè.\n\nCó: tải kho cục bộ lên thành bản riêng, giữ bản Drive hiện tại để so sánh.\nKhông: dừng để bạn xem và xử lý bản Drive trước.\nHủy: dừng.", "Both this device's vault and the Drive backup changed since the last sync. Automatic sync stopped to prevent overwriting either copy.\n\nYes: upload this vault as a separate version and keep the current Drive backup for comparison.\nNo: stop so you can review the Drive copy first.\nCancel: stop."),
            ["ui.mainwindow.message086"] = new("Phát hiện xung đột đồng bộ", "Sync conflict detected"),
            ["ui.mainwindow.message087"] = new("Kho trên máy này và bản Drive đã đồng bộ; không có thay đổi mới.", "This device's vault and the Drive backup are in sync; there are no new changes."),
            ["ui.mainwindow.message088"] = new("Đã đồng bộ", "Already in sync"),
            ["ui.mainwindow.message089"] = new("Lỗi xuất file", "File export error"),
            ["ui.mainwindow.message090"] = new("Bản Drive vừa thay đổi trong lúc chuẩn bị đồng bộ. Chưa tải dữ liệu nào lên; hãy chạy lại để kiểm tra xung đột mới.", "The Drive backup changed while sync was being prepared. Nothing was uploaded; run sync again to check for the new conflict."),
            ["ui.mainwindow.message091"] = new("Đã đồng bộ kho mật khẩu lên Google Drive thành công!\n\nBản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.", "The vault was uploaded to Google Drive.\n\nThe backup is in the 'TQLTK Backup' folder in your Google Drive."),
            ["ui.mainwindow.message092"] = new("Đồng bộ thất bại.\n\n", "Sync failed.\n\n"),
            ["ui.mainwindow.message093"] = new("Lỗi đồng bộ: {0}", "Sync error: {0}"),
            ["ui.mainwindow.message094"] = new("Bản Drive đã thay đổi trong lúc xem trước. Không có dữ liệu nào được nhập; hãy tải lại danh sách bản sao lưu.", "The Drive backup changed while you were reviewing it. Nothing was imported; reload the backup list."),
            ["ui.mainwindow.message095"] = new("Lỗi khôi phục: {0}", "Restore error: {0}"),
            ["ui.mainwindow.message096"] = new("Đã đánh dấu xử lý • kiểm tra {0}", "Marked fixed • checked {0}"),
            ["ui.mainwindow.message097"] = new("Nhắc lại {0} • kiểm tra {1}", "Reminder {0} • checked {1}"),
            ["ui.mainwindow.message098"] = new("Đã bỏ qua: {0} • kiểm tra {1}", "Ignored: {0} • checked {1}"),
            ["ui.mainwindow.message099"] = new("Đang mở • kiểm tra {0}", "Open • checked {0}"),
            ["ui.mainwindow.message100"] = new("Không thể đọc mục", "Could not read entry"),
            ["ui.mainwindow.message101"] = new("Không lưu được trạng thái", "Could not save status"),
            ["ui.backupcomparisonwindow.message001"] = new("Sẽ thêm {0} mục thành bản sao riêng. Dữ liệu hiện tại không bị ghi đè. Tiếp tục?", "This will add {0} separate copies. Current data will not be overwritten. Continue?"),
            ["ui.backupcenterwindow.message001"] = new("Chưa có bản sao lưu đầy đủ trong thư mục này.", "There are no full backups in this folder yet."),
            ["ui.backupcenterwindow.message002"] = new("Chọn một bản để xác minh hoặc khôi phục; chọn hai bản để xem khác biệt.", "Select one backup to verify or restore, or select two backups to compare changes."),
            ["ui.backupcenterwindow.message003"] = new("Chưa chọn bản sao lưu.", "No backup selected."),
            ["ui.backupcenterwindow.message004"] = new("Đã chọn 1 bản sao lưu.", "1 backup selected."),
            ["ui.backupcenterwindow.message005"] = new("Đã chọn 2 bản sao lưu.", "2 backups selected."),
            ["ui.backupcenterwindow.message006"] = new("Chọn tối đa hai bản sao lưu để so sánh.", "Select up to two backups to compare."),
            ["ui.backupcenterwindow.message007"] = new("Bỏ ghim", "Unpin"),
            ["ui.backupcenterwindow.message008"] = new("Ghim bản", "Pin backup"),
            ["ui.backupcenterwindow.message009"] = new("Cũ:", "Older:"),
            ["ui.backupcenterwindow.message010"] = new("Mới:", "Newer:"),
            ["ui.backupcenterwindow.message011"] = new("Chọn mục cần lấy từ một trong hai bản. Ứng dụng sẽ thêm thành bản sao để giữ nguyên dữ liệu hiện tại.", "Choose items from either backup. The app will add them as copies and preserve current data."),
            ["xaml.accountattachmentswindow.text001"] = new("Tệp đính kèm", "Attachments"),
            ["xaml.accountattachmentswindow.text002"] = new("Tệp đính kèm được mã hóa cùng vault", "Attachments are encrypted with the vault"),
            ["xaml.accountattachmentswindow.text003"] = new("Tối đa 10 MB mỗi tệp và 25 MB tổng cho mỗi tài khoản. Bản sao lưu vault cũng chứa các tệp này.", "Up to 10 MB per file and 25 MB total per account. Vault backups also include these files."),
            ["xaml.accountattachmentswindow.text004"] = new("Thêm tệp", "Add file"),
            ["xaml.accountattachmentswindow.text005"] = new("Xóa tệp đã chọn", "Remove selected file"),
            ["xaml.accountattachmentswindow.text006"] = new("Tên tệp", "File name"),
            ["xaml.accountattachmentswindow.text007"] = new("Kích thước", "Size"),
            ["xaml.accountattachmentswindow.text008"] = new("Ngày thêm", "Added"),
            ["xaml.accountattachmentswindow.text009"] = new("Xuất tệp", "Export file"),
            ["xaml.accountattachmentswindow.text010"] = new("Hủy", "Cancel"),
            ["xaml.accountattachmentswindow.text011"] = new("Lưu", "Save"),
            ["xaml.accounthistorywindow.text001"] = new("Lịch sử chỉnh sửa", "Edit history"),
            ["xaml.accounthistorywindow.text002"] = new("Lịch sử thay đổi thông tin", "Account edit history"),
            ["xaml.accounthistorywindow.text003"] = new("Có thể hoàn tác dịch vụ, tên đăng nhập, URL, thư mục, nhãn, ghi chú và trường tùy chỉnh. Mật khẩu có lịch sử riêng.", "You can restore the service, username, URL, folder, tags, notes, and custom fields. Passwords have a separate history."),
            ["xaml.accounthistorywindow.text004"] = new("Thời gian", "Time"),
            ["xaml.accounthistorywindow.text005"] = new("Các mục đã thay đổi", "Changed fields"),
            ["xaml.accounthistorywindow.text006"] = new("Xem trước", "Preview"),
            ["xaml.accounthistorywindow.text007"] = new("Đóng", "Close"),
            ["xaml.accountsharesetupwindow.text001"] = new("Chia sẻ tạm thời", "Temporary share"),
            ["xaml.accountsharesetupwindow.text002"] = new("Chia sẻ tài khoản có thời hạn", "Share account temporarily"),
            ["xaml.accountsharesetupwindow.text003"] = new("Drive chỉ cấp quyền đọc cho địa chỉ email này. Một khóa ngẫu nhiên sẽ mã hóa gói; hãy gửi khóa qua kênh riêng.", "Drive grants read access only to this email address. A random key encrypts the package; send the key through a separate channel."),
            ["xaml.accountsharesetupwindow.text004"] = new("Email người nhận", "Recipient email"),
            ["xaml.accountsharesetupwindow.text005"] = new("Tự hết hạn sau", "Expires after"),
            ["xaml.accountsharesetupwindow.text006"] = new("1 ngày", "1 day"),
            ["xaml.accountsharesetupwindow.text007"] = new("7 ngày", "7 days"),
            ["xaml.accountsharesetupwindow.text008"] = new("30 ngày", "30 days"),
            ["xaml.accountsharesetupwindow.text009"] = new("Khi hết hạn hoặc bị thu hồi, quyền tải file từ Drive sẽ bị hủy. Người nhận có thể giữ bản sao đã tải hoặc dữ liệu đã giải mã.", "When access expires or is revoked, Drive access to the file is removed. The recipient may keep a downloaded copy or data they already decrypted."),
            ["xaml.accountsharesetupwindow.text010"] = new("Hủy", "Cancel"),
            ["xaml.accountsharesetupwindow.text011"] = new("Tạo liên kết", "Create share"),
            ["xaml.adminwindow.text001"] = new("Quản lý tài khoản - TQLTK", "Account manager - TQLTK"),
            ["xaml.adminwindow.text002"] = new("➕ Tạo tài khoản mới", "➕ Create new account"),
            ["xaml.adminwindow.text003"] = new("Tên ĐN:", "Username:"),
            ["xaml.adminwindow.text004"] = new("Mật khẩu:", "Password:"),
            ["xaml.adminwindow.text005"] = new("Vai trò:", "Role:"),
            ["xaml.adminwindow.text006"] = new("Người dùng", "User"),
            ["xaml.adminwindow.text007"] = new("Quản trị viên", "Administrator"),
            ["xaml.adminwindow.text008"] = new("Họ tên:", "Full name:"),
            ["xaml.adminwindow.text009"] = new("Gmail:", "Gmail:"),
            ["xaml.adminwindow.text010"] = new("SĐT:", "Phone:"),
            ["xaml.adminwindow.text011"] = new("➕ Tạo tài khoản", "➕ Create account"),
            ["xaml.adminwindow.text012"] = new("Tên đăng nhập", "Username"),
            ["xaml.adminwindow.text013"] = new("Họ tên", "Full name"),
            ["xaml.adminwindow.text014"] = new("Gmail", "Gmail"),
            ["xaml.adminwindow.text015"] = new("SĐT", "Phone"),
            ["xaml.adminwindow.text016"] = new("Vai trò", "Role"),
            ["xaml.adminwindow.text017"] = new("HĐ", "Updated"),
            ["xaml.adminwindow.text018"] = new("Tạo lúc", "Created"),
            ["xaml.adminwindow.text019"] = new("ĐN cuối", "Last sign-in"),
            ["xaml.adminwindow.text020"] = new("Mã định danh", "ID"),
            ["xaml.adminwindow.text021"] = new("Sửa", "Edit"),
            ["xaml.adminwindow.text022"] = new("Đặt lại mật khẩu", "Reset password"),
            ["xaml.adminwindow.text023"] = new("Khóa / Mở", "Lock / unlock"),
            ["xaml.adminwindow.text024"] = new("Cấp / Thu quyền quản trị", "Grant / revoke admin"),
            ["xaml.adminwindow.text025"] = new("Xóa", "Delete"),
            ["xaml.adminwindow.text026"] = new("Làm mới", "Refresh"),
            ["xaml.adminwindow.text027"] = new("Xem nhật ký", "View log"),
            ["xaml.backupcenterwindow.text001"] = new("Quản lý bản sao lưu", "Backup manager"),
            ["xaml.backupcenterwindow.text002"] = new("Bản sao lưu đầy đủ", "Full backups"),
            ["xaml.backupcenterwindow.text003"] = new("Tệp sao lưu", "Backup file"),
            ["xaml.backupcenterwindow.text004"] = new("Ngày tạo", "Created"),
            ["xaml.backupcenterwindow.text005"] = new("Dung lượng", "Size"),
            ["xaml.backupcenterwindow.text006"] = new("Trạng thái", "Status"),
            ["xaml.backupcenterwindow.text007"] = new("Làm mới", "Refresh"),
            ["xaml.backupcenterwindow.text008"] = new("Mở thư mục", "Open folder"),
            ["xaml.backupcenterwindow.text009"] = new("Ghim bản", "Pin backup"),
            ["xaml.backupcenterwindow.text010"] = new("Xác minh", "Verify"),
            ["xaml.backupcenterwindow.text011"] = new("So sánh hai bản", "Compare two backups"),
            ["xaml.backupcenterwindow.text012"] = new("Xem trước khôi phục", "Restore preview"),
            ["xaml.backupcomparisonwindow.text001"] = new("So sánh bản sao lưu", "Compare backups"),
            ["xaml.backupcomparisonwindow.text002"] = new("Mục", "Item"),
            ["xaml.backupcomparisonwindow.text003"] = new("Thay đổi", "Change"),
            ["xaml.backupcomparisonwindow.text004"] = new("Chi tiết", "Details"),
            ["xaml.backupcomparisonwindow.text005"] = new("Khôi phục từ", "Restore from"),
            ["xaml.backupcomparisonwindow.text006"] = new("Khôi phục mục đã chọn thành bản sao", "Restore selected items as copies"),
            ["xaml.backupcomparisonwindow.text007"] = new("Đóng", "Close"),
            ["xaml.backuppassphrasewindow.text001"] = new("Cụm mật khẩu sao lưu", "Backup passphrase"),
            ["xaml.backuppassphrasewindow.text002"] = new("Cụm mật khẩu:", "Passphrase:"),
            ["xaml.backuppassphrasewindow.text003"] = new("Nhập lại cụm mật khẩu:", "Confirm passphrase:"),
            ["xaml.backuppassphrasewindow.text004"] = new("Dùng ít nhất 10 ký tự, nên là một câu gồm 4 từ trở lên mà bạn nhớ được. Hãy ghi lại cụm này ở nơi an toàn: nếu mất cả máy lẫn cụm này thì không thể mở các bản sao lưu.", "Use at least 10 characters; a memorable phrase of 4 or more words is recommended. Store it safely: if you lose both your device and this phrase, you cannot open your backups."),
            ["xaml.backuppassphrasewindow.text005"] = new("Hủy", "Cancel"),
            ["xaml.backuppassphrasewindow.text006"] = new("Lưu", "Save"),
            ["xaml.backuprestorepreviewwindow.text001"] = new("Xem trước khôi phục", "Restore preview"),
            ["xaml.backuprestorepreviewwindow.text002"] = new("Mục", "Item"),
            ["xaml.backuprestorepreviewwindow.text003"] = new("Kết quả dự kiến", "Expected result"),
            ["xaml.backuprestorepreviewwindow.text004"] = new("Chi tiết", "Details"),
            ["xaml.backuprestorepreviewwindow.text005"] = new("Áp dụng các cài đặt an toàn có trong bản sao lưu (ngôn ngữ, giao diện và thời gian khóa).", "Apply safe settings from the backup (language, theme, and lock timeout)."),
            ["xaml.backuprestorepreviewwindow.text006"] = new("Hủy", "Cancel"),
            ["xaml.backuprestorepreviewwindow.text007"] = new("Tiếp tục khôi phục", "Continue restore"),
            ["xaml.bulkaccountactionswindow.text001"] = new("Thao tác hàng loạt", "Bulk actions"),
            ["xaml.bulkaccountactionswindow.text002"] = new("Đặt thư mục cho các mục đã chọn", "Set folder for selected entries"),
            ["xaml.bulkaccountactionswindow.text003"] = new("Thay nhãn hiện có bằng danh sách nhãn sau", "Replace current tags with these tags"),
            ["xaml.bulkaccountactionswindow.text004"] = new("Đưa các mục đã chọn vào thùng rác (30 ngày)", "Move selected entries to Trash (30 days)"),
            ["xaml.bulkaccountactionswindow.text005"] = new("Tách nhãn bằng dấu phẩy. Chỉ các trường đã chọn mới bị thay đổi.", "Separate tags with commas. Only selected fields will be changed."),
            ["xaml.bulkaccountactionswindow.text006"] = new("Hủy", "Cancel"),
            ["xaml.bulkaccountactionswindow.text007"] = new("Áp dụng", "Apply"),
            ["xaml.changepasswordwindow.text001"] = new("Đổi mật khẩu - TQLTK", "Change password - TQLTK"),
            ["xaml.changepasswordwindow.text002"] = new("🔐 Đổi mật khẩu", "🔐 Change password"),
            ["xaml.changepasswordwindow.text003"] = new("Nhập mật khẩu hiện tại và mật khẩu mới", "Enter your current and new passwords"),
            ["xaml.changepasswordwindow.text004"] = new("Đổi mật khẩu này chỉ thay thông tin đăng nhập TQLTK, không đổi khóa mã hóa vault. Trước khi đổi tài khoản Windows hoặc cài lại Windows, hãy tạo bản sao lưu có cụm mật khẩu.", "Changing this password updates only your TQLTK sign-in; it does not change the vault encryption key. Before changing Windows accounts or reinstalling Windows, create a passphrase-protected backup."),
            ["xaml.changepasswordwindow.text005"] = new("Mật khẩu hiện tại:", "Current password:"),
            ["xaml.changepasswordwindow.text006"] = new("Mật khẩu mới:", "New password:"),
            ["xaml.changepasswordwindow.text007"] = new("Xác nhận mật khẩu mới:", "Confirm new password:"),
            ["xaml.changepasswordwindow.text008"] = new("Hủy", "Cancel"),
            ["xaml.changepasswordwindow.text009"] = new("✓ Xác nhận", "✓ Confirm"),
            ["xaml.csvimportwindow.text001"] = new("Xem trước nhập tài khoản", "Review account import"),
            ["xaml.csvimportwindow.text002"] = new("📥 Xem trước tài khoản nhập", "📥 Review imported accounts"),
            ["xaml.csvimportwindow.text003"] = new("Dịch vụ", "Service"),
            ["xaml.csvimportwindow.text004"] = new("Tên đăng nhập", "Username"),
            ["xaml.csvimportwindow.text005"] = new("Mật khẩu", "Password"),
            ["xaml.csvimportwindow.text006"] = new("Ghi chú", "Notes"),
            ["xaml.csvimportwindow.text007"] = new("Đối tượng trùng", "Matching entry"),
            ["xaml.csvimportwindow.text008"] = new("Hành động", "Action"),
            ["xaml.csvimportwindow.text009"] = new("Chọn tất cả mục mới", "Select all new entries"),
            ["xaml.csvimportwindow.text010"] = new("Bỏ qua tất cả bản trùng", "Skip all duplicates"),
            ["xaml.csvimportwindow.text011"] = new("Hủy", "Cancel"),
            ["xaml.csvimportwindow.text012"] = new("Tiếp tục", "Continue"),
            ["xaml.customfieldswindow.text001"] = new("Trường tùy chỉnh", "Custom fields"),
            ["xaml.customfieldswindow.text002"] = new("Trường tùy chỉnh được lưu bên trong kho mật khẩu đã mã hóa. Trường nhạy cảm được che khi nhập và không dùng trong tìm kiếm.", "Custom fields are stored in the encrypted password vault. Sensitive fields are masked while editing and excluded from search."),
            ["xaml.customfieldswindow.text003"] = new("Thêm", "Add"),
            ["xaml.customfieldswindow.text004"] = new("Xóa", "Delete"),
            ["xaml.customfieldswindow.text005"] = new("Tên trường", "Field name"),
            ["xaml.customfieldswindow.text006"] = new("Thông tin nhạy cảm", "Sensitive information"),
            ["xaml.customfieldswindow.text007"] = new("Hiện giá trị", "Show value"),
            ["xaml.customfieldswindow.text008"] = new("Hủy", "Cancel"),
            ["xaml.customfieldswindow.text009"] = new("Lưu trường", "Save field"),
            ["xaml.driveshareswindow.text001"] = new("Liên kết Drive đang chia sẻ", "Active Drive shares"),
            ["xaml.driveshareswindow.text002"] = new("Thu hồi quyền Drive và chuyển gói vào thùng rác. Không thể thu hồi dữ liệu đã tải hoặc giải mã.", "Revoke Drive access and move the package to the trash. Downloaded or decrypted data cannot be revoked."),
            ["xaml.driveshareswindow.text003"] = new("Email người nhận", "Recipient email"),
            ["xaml.driveshareswindow.text004"] = new("Hết hạn", "Expires"),
            ["xaml.driveshareswindow.text005"] = new("Tạo lúc", "Created"),
            ["xaml.driveshareswindow.text006"] = new("Đóng", "Close"),
            ["xaml.driveshareswindow.text007"] = new("Thu hồi quyền", "Revoke access"),
            ["xaml.editaccountwindow.text001"] = new("Tài khoản", "Account"),
            ["xaml.editaccountwindow.text002"] = new("🔐 Thêm tài khoản mới", "🔐 Add new account"),
            ["xaml.editaccountwindow.text003"] = new("Điền thông tin tài khoản cần lưu", "Enter the account details to save"),
            ["xaml.editaccountwindow.text004"] = new("Tên dịch vụ / website (*):", "Service / website name (*):"),
            ["xaml.editaccountwindow.text005"] = new("URL chính thức của website:", "Official website URL:"),
            ["xaml.editaccountwindow.text006"] = new("Nhập URL http:// hoặc https:// chính thức. Chỉ so khớp chính xác hostname.", "Enter the official http:// or https:// URL. Hostnames are matched exactly."),
            ["xaml.editaccountwindow.text007"] = new("Thư mục", "Folder"),
            ["xaml.editaccountwindow.text008"] = new("Nhãn (phân cách bằng dấu phẩy)", "Tags (comma-separated)"),
            ["xaml.editaccountwindow.text009"] = new("Đánh dấu yêu thích", "Mark as favorite"),
            ["xaml.editaccountwindow.text010"] = new("Tên đăng nhập / Email (*):", "Username / Email (*):"),
            ["xaml.editaccountwindow.text011"] = new("Mật khẩu (*):", "Password (*):"),
            ["xaml.editaccountwindow.text012"] = new("Đưa ghi chú vào thùng rác", "Move note to Trash"),
            ["xaml.editaccountwindow.text013"] = new("Ghi chú:", "Notes:"),
            ["xaml.editaccountwindow.text014"] = new("Mã xác thực cho website (TOTP)", "Website authenticator code (TOTP)"),
            ["xaml.editaccountwindow.text015"] = new("Nhập / đổi TOTP", "Add / change TOTP"),
            ["xaml.editaccountwindow.text016"] = new("Xóa TOTP", "Remove TOTP"),
            ["xaml.editaccountwindow.text017"] = new("Chưa có mã xác thực cho website này.", "This website does not have a TOTP code yet."),
            ["xaml.editaccountwindow.text018"] = new("Trường tùy chỉnh", "Custom fields"),
            ["xaml.editaccountwindow.text019"] = new("Quản lý trường…", "Manage fields…"),
            ["xaml.editaccountwindow.text020"] = new("Chưa có trường tùy chỉnh", "No custom fields yet"),
            ["xaml.editaccountwindow.text021"] = new("Tạo / tùy chỉnh mật khẩu", "Generate / customize password"),
            ["xaml.editaccountwindow.text022"] = new("Lưu bản nháp mã hóa để khôi phục sau sự cố", "Save an encrypted draft for crash recovery"),
            ["xaml.editaccountwindow.text023"] = new("Bản nháp chỉ được lưu khi bạn bật tùy chọn này; khóa bằng tài khoản Windows hiện tại.", "Drafts are saved only when this option is enabled and are protected by the current Windows account."),
            ["xaml.editaccountwindow.text024"] = new("Hủy", "Cancel"),
            ["xaml.editaccountwindow.text025"] = new("✓ Lưu", "✓ Save"),
            ["xaml.edituserwindow.text001"] = new("Sửa thông tin người dùng", "Edit user"),
            ["xaml.edituserwindow.text002"] = new("✏️ Sửa thông tin người dùng", "✏️ Edit user"),
            ["xaml.edituserwindow.text003"] = new("Tên đăng nhập (không đổi):", "Username (cannot be changed):"),
            ["xaml.edituserwindow.text004"] = new("Họ tên:", "Full name:"),
            ["xaml.edituserwindow.text005"] = new("Gmail:", "Gmail:"),
            ["xaml.edituserwindow.text006"] = new("Số điện thoại:", "Phone number:"),
            ["xaml.edituserwindow.text007"] = new("Hủy", "Cancel"),
            ["xaml.edituserwindow.text008"] = new("✓ Lưu", "✓ Save"),
            ["xaml.loginwindow.text001"] = new("Đăng nhập - TQLTK", "Sign in - TQLTK"),
            ["xaml.loginwindow.text002"] = new("🔐 Đăng nhập", "🔐 Sign in"),
            ["xaml.loginwindow.text003"] = new("TQLTK - Trình Quản Lý Tài Khoản", "TQLTK - Account Manager"),
            ["xaml.loginwindow.text004"] = new("Tên đăng nhập:", "Username:"),
            ["xaml.loginwindow.text005"] = new("Mật khẩu:", "Password:"),
            ["xaml.loginwindow.text006"] = new("🔓  Đăng nhập", "🔓  Sign in"),
            ["xaml.loginwindow.text007"] = new("Đăng nhập bằng Windows Hello", "Sign in with Windows Hello"),
            ["xaml.loginwindow.text008"] = new("📝  Chưa có tài khoản? Đăng ký ngay", "📝  New here? Register now"),
            ["xaml.loginwindow.text009"] = new("🔐 Bật Windows Hello trong Thông tin cá nhân", "🔐 Enable Windows Hello in Personal Information"),
            ["xaml.loginwindow.text010"] = new("Ngôn ngữ:", "Language:"),
            ["xaml.loginwindow.text011"] = new("Tiếng Việt", "Vietnamese"),
            ["xaml.loginwindow.text012"] = new("Tiếng Anh", "English"),
            ["xaml.mainwindow.text001"] = new("TQLTK • Phát triển bởi thienthuongthienha_duyngadocton_", "TQLTK • Developed by thienthuongthienha_duyngadocton_"),
            ["xaml.mainwindow.text002"] = new("Ẩn / hiện thanh bên (Ctrl+B)", "Show / hide sidebar (Ctrl+B)"),
            ["xaml.mainwindow.text003"] = new("Trình Quản Lý Tài Khoản", "Account Manager"),
            ["xaml.mainwindow.text004"] = new("Tài khoản và cài đặt", "Accounts and settings"),
            ["xaml.mainwindow.text005"] = new("⚙️  Cài đặt", "⚙️  Settings"),
            ["xaml.mainwindow.text006"] = new("Ctrl+Shift+,", "Ctrl+Shift+,"),
            ["xaml.mainwindow.text007"] = new("🚪  Đăng xuất", "🚪  Sign out"),
            ["xaml.mainwindow.text008"] = new("⏻  Thoát", "⏻  Exit"),
            ["xaml.mainwindow.text009"] = new("🔐  Tài khoản", "🔐  Accounts"),
            ["xaml.mainwindow.text010"] = new("📁  Tài liệu", "📁  Files"),
            ["xaml.mainwindow.text011"] = new("Tài khoản đã lưu", "Saved accounts"),
            ["xaml.mainwindow.text012"] = new("🔍 Tìm:", "🔍 Search:"),
            ["xaml.mainwindow.text013"] = new("Yêu thích", "Favorites"),
            ["xaml.mainwindow.text014"] = new("(tìm cả ghi chú và trường tùy chỉnh không nhạy cảm)", "(also searches notes and non-sensitive custom fields)"),
            ["xaml.mainwindow.text015"] = new("Thêm mới", "Add new"),
            ["xaml.mainwindow.text016"] = new("Làm mới", "Refresh"),
            ["xaml.mainwindow.text017"] = new("Thùng rác", "Trash"),
            ["xaml.mainwindow.text018"] = new("Tên miền", "Domain"),
            ["xaml.mainwindow.text019"] = new("Tất cả tên miền", "All domains"),
            ["xaml.mainwindow.text020"] = new("Thư mục", "Folder"),
            ["xaml.mainwindow.text021"] = new("Tất cả thư mục", "All folders"),
            ["xaml.mainwindow.text022"] = new("Nhãn", "Tag"),
            ["xaml.mainwindow.text023"] = new("Tất cả nhãn", "All tags"),
            ["xaml.mainwindow.text024"] = new("Thiếu TOTP", "Missing TOTP"),
            ["xaml.mainwindow.text025"] = new("Chỉ cảnh báo bảo mật", "Security warnings only"),
            ["xaml.mainwindow.text026"] = new("Dịch vụ", "Service"),
            ["xaml.mainwindow.text027"] = new("Tên đăng nhập", "Username"),
            ["xaml.mainwindow.text028"] = new("Mật khẩu", "Password"),
            ["xaml.mainwindow.text029"] = new("Trang web", "Website"),
            ["xaml.mainwindow.text030"] = new("Thư mục / nhãn", "Folder / tags"),
            ["xaml.mainwindow.text031"] = new("Ghi chú", "Notes"),
            ["xaml.mainwindow.text032"] = new("Ngày đổi MK", "Password changed"),
            ["xaml.mainwindow.text033"] = new("Cập nhật", "Update"),
            ["xaml.mainwindow.text034"] = new("Hiện / Ẩn MK", "Show / hide password"),
            ["xaml.mainwindow.text035"] = new("Sao chép tên đăng nhập", "Copy username"),
            ["xaml.mainwindow.text036"] = new("Sao chép mật khẩu", "Copy password"),
            ["xaml.mainwindow.text037"] = new("Sửa", "Edit"),
            ["xaml.mainwindow.text038"] = new("Thao tác khác", "More actions"),
            ["xaml.mainwindow.text039"] = new("Lịch sử mật khẩu", "Password history"),
            ["xaml.mainwindow.text040"] = new("Mã TOTP", "TOTP code"),
            ["xaml.mainwindow.text041"] = new("Mở website", "Open website"),
            ["xaml.mainwindow.text042"] = new("Mở URL đã lưu và kiểm tra một số dấu hiệu domain giả mạo phổ biến. Không theo dõi tab trình duyệt.", "Open the saved URL and check for some common signs of a spoofed domain. Browser tabs are not monitored."),
            ["xaml.mainwindow.text043"] = new("Tệp đính kèm", "Attachments"),
            ["xaml.mainwindow.text044"] = new("Lịch sử sửa", "Edit history"),
            ["xaml.mainwindow.text045"] = new("Chia sẻ tạm thời", "Temporary share"),
            ["xaml.mainwindow.text046"] = new("Thao tác hàng loạt", "Bulk actions"),
            ["xaml.mainwindow.text047"] = new("Xuất mục đã chọn", "Export selected entries"),
            ["xaml.mainwindow.text048"] = new("Hoàn tác nhập", "Undo import"),
            ["xaml.mainwindow.text049"] = new("Xóa", "Delete"),
            ["xaml.mainwindow.text050"] = new("Tài liệu cá nhân", "Personal files"),
            ["xaml.mainwindow.text051"] = new("Nhập", "Import"),
            ["xaml.mainwindow.text052"] = new("Xuất", "Export"),
            ["xaml.mainwindow.text053"] = new("Tạo text", "Create text"),
            ["xaml.mainwindow.text054"] = new("Tên tệp", "File name"),
            ["xaml.mainwindow.text055"] = new("Kích thước", "Size"),
            ["xaml.mainwindow.text056"] = new("Ngày sửa", "Modified"),
            ["xaml.mainwindow.text057"] = new("Đường dẫn", "Path"),
            ["xaml.mainwindow.text058"] = new("Cài đặt", "Settings"),
            ["xaml.mainwindow.text059"] = new("Đóng (Esc)", "Close (Esc)"),
            ["xaml.mainwindow.text060"] = new("🔍  Tìm trong cài đặt", "🔍  Search settings"),
            ["xaml.mainwindow.text061"] = new("Tài khoản", "Account"),
            ["xaml.mainwindow.text062"] = new("👤  Thông tin cá nhân", "👤  Personal information"),
            ["xaml.mainwindow.text063"] = new("🛡️  Bảo mật", "🛡️  Security"),
            ["xaml.mainwindow.text064"] = new("Tùy chỉnh", "Customize"),
            ["xaml.mainwindow.text065"] = new("🎨  Giao diện", "🎨  Appearance"),
            ["xaml.mainwindow.text066"] = new("Sao lưu", "Back up"),
            ["xaml.mainwindow.text067"] = new("💾  Xuất / nhập dữ liệu", "💾  Data export / import"),
            ["xaml.mainwindow.text068"] = new("☁️  Google Drive (BETA)", "☁️  Google Drive (BETA)"),
            ["xaml.mainwindow.text069"] = new("Hệ thống", "System"),
            ["xaml.mainwindow.text070"] = new("⚙️  Hệ thống", "⚙️  System"),
            ["xaml.mainwindow.text071"] = new("👑  Quản trị", "👑  Administration"),
            ["xaml.mainwindow.text072"] = new("Không có mục nào khớp.", "No matching items."),
            ["xaml.mainwindow.text073"] = new("Thông tin cá nhân", "Personal information"),
            ["xaml.mainwindow.text074"] = new("Cập nhật họ tên, Gmail, số điện thoại và đăng nhập bằng Windows Hello.", "Update your name, Gmail address, phone number, and Windows Hello sign-in."),
            ["xaml.mainwindow.text075"] = new("Tên đăng nhập (không đổi):", "Username (cannot be changed):"),
            ["xaml.mainwindow.text076"] = new("Họ tên:", "Full name:"),
            ["xaml.mainwindow.text077"] = new("Gmail:", "Gmail:"),
            ["xaml.mainwindow.text078"] = new("Số điện thoại:", "Phone number:"),
            ["xaml.mainwindow.text079"] = new("Vai trò:", "Role:"),
            ["xaml.mainwindow.text080"] = new("Ngày tạo tài khoản:", "Account created:"),
            ["xaml.mainwindow.text081"] = new("Bật Windows Hello", "Enable Windows Hello"),
            ["xaml.mainwindow.text082"] = new("🔐 Đăng nhập bằng Windows Hello (BETA)", "🔐 Sign in with Windows Hello (BETA)"),
            ["xaml.mainwindow.text083"] = new("Cho phép đăng nhập TQLTK bằng vân tay / khuôn mặt / PIN của Windows.", "Sign in to TQLTK with your Windows fingerprint, face, or PIN."),
            ["xaml.mainwindow.text084"] = new("Lưu thay đổi", "Save changes"),
            ["xaml.mainwindow.text085"] = new("Bảo mật", "Security"),
            ["xaml.mainwindow.text086"] = new("Bảo vệ đăng nhập và các thao tác nhạy cảm.", "Protect sign-in and sensitive actions."),
            ["xaml.mainwindow.text087"] = new("Tổng quan bảo mật", "Security overview"),
            ["xaml.mainwindow.text088"] = new("Kiểm tra mật khẩu rò rỉ", "check for breached passwords"),
            ["xaml.mainwindow.text089"] = new("Các chỉ số khác được tính cục bộ. Kiểm tra rò rỉ là tùy chọn và chỉ gửi tiền tố hash một phần qua HTTPS.", "Other checks run locally. The optional breach check sends only a partial hash prefix over HTTPS."),
            ["xaml.mainwindow.text090"] = new("Nhắc kiểm tra lại (không tự gửi yêu cầu):", "Remind me to check again (no automatic requests):"),
            ["xaml.mainwindow.text091"] = new("Không nhắc định kỳ", "No scheduled reminder"),
            ["xaml.mainwindow.text092"] = new("Sau 30 ngày", "After 30 days"),
            ["xaml.mainwindow.text093"] = new("Sau 90 ngày", "After 90 days"),
            ["xaml.mainwindow.text094"] = new("Sau 180 ngày", "After 180 days"),
            ["xaml.mainwindow.text095"] = new("Tài khoản cần xem xét", "Accounts to review"),
            ["xaml.mainwindow.text096"] = new("Trạng thái", "Status"),
            ["xaml.mainwindow.text097"] = new("Mở mục", "Open entry"),
            ["xaml.mainwindow.text098"] = new("Tự khóa ứng dụng", "Auto-lock app"),
            ["xaml.mainwindow.text099"] = new("Khóa khi không thao tác hoặc ngay khi cửa sổ bị thu nhỏ.", "Lock after inactivity or immediately when the window is minimized."),
            ["xaml.mainwindow.text100"] = new("Sau", "After"),
            ["xaml.mainwindow.text101"] = new("1 phút", "1 minute"),
            ["xaml.mainwindow.text102"] = new("5 phút", "5 minutes"),
            ["xaml.mainwindow.text103"] = new("10 phút", "10 minutes"),
            ["xaml.mainwindow.text104"] = new("15 phút", "15 minutes"),
            ["xaml.mainwindow.text105"] = new("Khóa khi thu nhỏ", "Lock when minimized"),
            ["xaml.mainwindow.text106"] = new("Lưu", "Save"),
            ["xaml.mainwindow.text107"] = new("Băm mật khẩu đăng nhập", "Sign-in password hashing"),
            ["xaml.mainwindow.text108"] = new("Mặc định dùng Argon2id. Có thể bật PBKDF2-HMAC-SHA256 cho môi trường cần tương thích FIPS; hash cũ được nâng cấp sau lần đăng nhập thành công.", "Argon2id is used by default. PBKDF2-HMAC-SHA256 is available for environments that require FIPS-compatible hashing; existing hashes are upgraded after a successful sign-in."),
            ["xaml.mainwindow.text109"] = new("Dùng PBKDF2-HMAC-SHA256 (tương thích FIPS)", "Use PBKDF2-HMAC-SHA256 (FIPS-compatible)"),
            ["xaml.mainwindow.text110"] = new("Xoay khóa", "Rotate key"),
            ["xaml.mainwindow.text111"] = new("Xoay khóa vault", "rotate the vault key"),
            ["xaml.mainwindow.text112"] = new("Mã hóa lại vault, ghi chú và bản sao lưu cục bộ đã nhận diện. Hãy đặt cụm mật khẩu sao lưu trước; khóa cũ được giữ mã hóa để mở các bản xuất lịch sử.", "Re-encrypt the vault, notes, and recognized local backups. Set a backup passphrase first; the old key is retained in protected form for historical exports."),
            ["xaml.mainwindow.text113"] = new("Ngôn ngữ", "Language"),
            ["xaml.mainwindow.text114"] = new("Chọn ngôn ngữ hiển thị của ứng dụng.", "Choose the app's display language."),
            ["xaml.mainwindow.text115"] = new("Tiếng Việt", "Vietnamese"),
            ["xaml.mainwindow.text116"] = new("Tiếng Anh", "English"),
            ["xaml.mainwindow.text117"] = new("Đổi mật khẩu", "Change password"),
            ["xaml.mainwindow.text118"] = new("Mật khẩu đăng nhập", "Sign-in password"),
            ["xaml.mainwindow.text119"] = new("Đặt mật khẩu mới cho tài khoản của bạn.", "Set a new password for your account."),
            ["xaml.mainwindow.text120"] = new("Bật / Tắt", "On / Off"),
            ["xaml.mainwindow.text121"] = new("○ Đang tắt", "○ Off"),
            ["xaml.mainwindow.text122"] = new("Bật xác thực 2 bước", "Enable two-factor authentication"),
            ["xaml.mainwindow.text123"] = new("Dùng Google / Microsoft Authenticator hoặc Authy khi đăng nhập bằng mật khẩu.", "Use Google Authenticator, Microsoft Authenticator, or Authy when signing in with a password."),
            ["xaml.mainwindow.text124"] = new("Tạo lại", "Regenerate"),
            ["xaml.mainwindow.text125"] = new("Mã khôi phục 2FA", "2FA recovery codes"),
            ["xaml.mainwindow.text126"] = new("Sinh 8 mã mới. Mã cũ chỉ bị vô hiệu khi bạn bấm hoàn tất.", "Generate 8 new codes. Existing codes remain valid until you finish."),
            ["xaml.mainwindow.text127"] = new("Mở", "Open"),
            ["xaml.mainwindow.text128"] = new("Windows Hello (BETA)", "Windows Hello (BETA)"),
            ["xaml.mainwindow.text129"] = new("Bật hoặc tắt trong trang Thông tin cá nhân.", "Enable or disable this on the Personal Information page."),
            ["xaml.mainwindow.text130"] = new("Giao diện", "Appearance"),
            ["xaml.mainwindow.text131"] = new("Màu sắc và vị trí cửa sổ.", "Theme and window position."),
            ["xaml.mainwindow.text132"] = new("Đổi", "Change"),
            ["xaml.mainwindow.text133"] = new("Sáng / Tối", "Light / dark"),
            ["xaml.mainwindow.text134"] = new("Chuyển nhanh giữa giao diện sáng và tối.", "Quickly switch between light and dark themes."),
            ["xaml.mainwindow.text135"] = new("Tự động đổi giao diện theo giờ", "Automatically change the theme by time"),
            ["xaml.mainwindow.text136"] = new("06:00 - 18:00 giao diện sáng, còn lại giao diện tối.", "Light theme from 06:00 to 18:00; dark theme otherwise."),
            ["xaml.mainwindow.text137"] = new("Đưa về giữa", "Center window"),
            ["xaml.mainwindow.text138"] = new("Vị trí cửa sổ", "Window position"),
            ["xaml.mainwindow.text139"] = new("Dùng khi cửa sổ bị lệch ra ngoài màn hình.", "Use this if the window is off-screen."),
            ["xaml.mainwindow.text140"] = new("Xuất / nhập dữ liệu", "Data export / import"),
            ["xaml.mainwindow.text141"] = new("Sao lưu tài khoản ra tệp trên máy, hoặc nhập từ tệp.", "Back up accounts to a file on this computer, or import them from a file."),
            ["xaml.mainwindow.text142"] = new("Xuất kho mật khẩu (Sao lưu)", "Export password vault (Backup)"),
            ["xaml.mainwindow.text143"] = new("Lưu toàn bộ tài khoản vào tệp .vault đã mã hóa.", "Save all accounts to an encrypted .vault file."),
            ["xaml.mainwindow.text144"] = new("Nhập kho mật khẩu (Khôi phục)", "Import password vault (Restore)"),
            ["xaml.mainwindow.text145"] = new("Nạp lại từ tệp .vault, tài khoản trùng sẽ được bỏ qua.", "Load from a .vault file; duplicate accounts are skipped."),
            ["xaml.mainwindow.text146"] = new("Sao lưu đầy đủ và chuyển máy", "Full backup and computer migration"),
            ["xaml.mainwindow.text147"] = new("Một gói mã hóa gồm vault, TOTP, tệp đính kèm, ghi chú, tệp cá nhân và tùy chọn cài đặt đã chọn. Khôi phục chỉ bổ sung, không ghi đè mục đang có.", "One encrypted package contains the vault, TOTP, attachments, notes, personal files, and selected settings. Restore adds missing items and does not overwrite existing entries."),
            ["xaml.mainwindow.text148"] = new("Tạo gói đầy đủ", "Create full package"),
            ["xaml.mainwindow.text149"] = new("Khôi phục / chuyển máy", "Restore / migrate"),
            ["xaml.mainwindow.text150"] = new("Duyệt / so sánh", "Browse / compare"),
            ["xaml.mainwindow.text151"] = new("Hướng dẫn chuyển máy", "Computer migration guide"),
            ["xaml.mainwindow.text152"] = new("Ghi chú riêng", "Private notes"),
            ["xaml.mainwindow.text153"] = new("Tệp cá nhân", "Personal files"),
            ["xaml.mainwindow.text154"] = new("Cài đặt an toàn", "Safe settings"),
            ["xaml.mainwindow.text155"] = new("Sao lưu định kỳ và giữ bản đã kiểm tra", "Scheduled backups and verified retention"),
            ["xaml.mainwindow.text156"] = new("Tự chạy khi ứng dụng mở và vault đã mở khóa; chỉ tạo bản mới khi dữ liệu thay đổi. Nếu sao lưu lỗi, ứng dụng sẽ thử lại sau 6 giờ.", "Runs while the app is open and the vault is unlocked; a new copy is created only after data changes. If a backup fails, the app retries after 6 hours."),
            ["xaml.mainwindow.text157"] = new("Sao lưu định kỳ đang tắt.", "Scheduled backups are off."),
            ["xaml.mainwindow.text158"] = new("Bật sao lưu định kỳ", "Enable scheduled backups"),
            ["xaml.mainwindow.text159"] = new("Chu kỳ", "Interval"),
            ["xaml.mainwindow.text160"] = new("Hằng ngày", "Daily"),
            ["xaml.mainwindow.text161"] = new("Hằng tuần", "Weekly"),
            ["xaml.mainwindow.text162"] = new("Hằng tháng", "Monthly"),
            ["xaml.mainwindow.text163"] = new("Số bản tối đa", "Maximum copies"),
            ["xaml.mainwindow.text164"] = new("3 bản", "3 copies"),
            ["xaml.mainwindow.text165"] = new("7 bản", "7 copies"),
            ["xaml.mainwindow.text166"] = new("14 bản", "14 copies"),
            ["xaml.mainwindow.text167"] = new("30 bản", "30 copies"),
            ["xaml.mainwindow.text168"] = new("Thời gian giữ", "Retention period"),
            ["xaml.mainwindow.text169"] = new("30 ngày", "30 days"),
            ["xaml.mainwindow.text170"] = new("90 ngày", "90 days"),
            ["xaml.mainwindow.text171"] = new("180 ngày", "180 days"),
            ["xaml.mainwindow.text172"] = new("365 ngày", "365 days"),
            ["xaml.mainwindow.text173"] = new("Giới hạn dung lượng", "Storage limit"),
            ["xaml.mainwindow.text174"] = new("512 MB", "512 MB"),
            ["xaml.mainwindow.text175"] = new("1 GB", "1 GB"),
            ["xaml.mainwindow.text176"] = new("2 GB", "2 GB"),
            ["xaml.mainwindow.text177"] = new("4 GB", "4 GB"),
            ["xaml.mainwindow.text178"] = new("Giữ một bản mỗi ngày", "Keep one copy per day"),
            ["xaml.mainwindow.text179"] = new("Tắt", "Off"),
            ["xaml.mainwindow.text180"] = new("7 ngày", "7 days"),
            ["xaml.mainwindow.text181"] = new("14 ngày", "14 days"),
            ["xaml.mainwindow.text182"] = new("Giữ một bản mỗi tuần", "Keep one copy per week"),
            ["xaml.mainwindow.text183"] = new("4 tuần", "4 weeks"),
            ["xaml.mainwindow.text184"] = new("8 tuần", "8 weeks"),
            ["xaml.mainwindow.text185"] = new("12 tuần", "12 weeks"),
            ["xaml.mainwindow.text186"] = new("Giữ một bản mỗi tháng", "Keep one copy per month"),
            ["xaml.mainwindow.text187"] = new("3 tháng", "3 months"),
            ["xaml.mainwindow.text188"] = new("6 tháng", "6 months"),
            ["xaml.mainwindow.text189"] = new("12 tháng", "12 months"),
            ["xaml.mainwindow.text190"] = new("Giữ thêm bản mới nhất của mỗi ngày, tuần hoặc tháng đã chọn, ngoài số bản và thời gian giữ thông thường. Khi vượt dung lượng, bản cũ chưa ghim được dọn trước, kể cả bản theo lịch. Bản ghim và bản mới nhất luôn được giữ.", "Keeps the newest copy for each selected day, week, or month in addition to the normal copy count and retention period. If storage exceeds its limit, older unpinned copies are removed first, including calendar copies. Pinned copies and the newest copy are always kept."),
            ["xaml.mainwindow.text191"] = new("Bản đã ghim không bị tự xóa. Hệ thống giữ bản mới nhất đã xác minh và báo nếu còn vượt giới hạn dung lượng.", "Pinned copies are not deleted automatically. The newest verified copy is retained, and the app reports when backups still exceed the storage limit."),
            ["xaml.mainwindow.text192"] = new("Chính sách dọn tự động chỉ áp dụng cho các bản sao lưu định kỳ. Gói xuất thủ công được giữ nguyên cho đến khi bạn tự xóa.", "Automatic cleanup applies only to scheduled backup copies. Manually exported packages are kept until you delete them."),
            ["xaml.mainwindow.text193"] = new("Lưu chính sách", "Save policy"),
            ["xaml.mainwindow.text194"] = new("Kiểm tra", "Check"),
            ["xaml.mainwindow.text195"] = new("Diễn tập", "Drill"),
            ["xaml.mainwindow.text196"] = new("Kiểm tra bản sao lưu", "verify backup"),
            ["xaml.mainwindow.text197"] = new("Xác minh tệp .vault hoặc .tqf và xem số lượng dữ liệu trước khi khôi phục; không sửa kho mật khẩu.", "Verify a .vault or .tqf file and view its contents before restoring; the password vault is not modified."),
            ["xaml.mainwindow.text198"] = new("Xuất CSV", "Export CSV"),
            ["xaml.mainwindow.text199"] = new("Cần xác thực Windows Hello. Tệp chứa mật khẩu dạng chữ thường.", "Windows Hello authentication is required. The file contains passwords in plaintext."),
            ["xaml.mainwindow.text200"] = new("Nhập dữ liệu tài khoản", "Import account data"),
            ["xaml.mainwindow.text201"] = new("Hỗ trợ CSV, JSON Bitwarden, KeePass XML; có xem trước và hoàn tác. Dùng Khôi phục sao lưu cho file TQLTK.", "Supports CSV, Bitwarden JSON, and KeePass XML with preview and undo. Use Restore backup for TQLTK backup files."),
            ["xaml.mainwindow.text202"] = new("Mở chia sẻ", "Open shared item"),
            ["xaml.mainwindow.text203"] = new("Mở mục được chia sẻ", "Open shared item"),
            ["xaml.mainwindow.text204"] = new("Mở gói mã hóa có thời hạn bằng khóa người gửi đã chia sẻ riêng.", "Open an encrypted, expiring package with the key provided separately by the sender."),
            ["xaml.mainwindow.text205"] = new("Google Drive (BETA)", "Google Drive (BETA)"),
            ["xaml.mainwindow.text206"] = new("Bản sao lưu nằm trong thư mục 'TQLTK Backup' trên Google Drive của bạn.", "Backups are stored in the 'TQLTK Backup' folder in your Google Drive."),
            ["xaml.mainwindow.text207"] = new("Kiểm tra tài khoản", "Check account"),
            ["xaml.mainwindow.text208"] = new("Ngắt kết nối", "Disconnect"),
            ["xaml.mainwindow.text209"] = new("Tài khoản Drive đã liên kết", "Linked Drive account"),
            ["xaml.mainwindow.text210"] = new("Chưa kiểm tra kết nối Drive. Nhấn Kiểm tra tài khoản để xác minh.", "Drive connection has not been checked. Select Check account to verify it."),
            ["xaml.mainwindow.text211"] = new("Ngắt kết nối chỉ xóa token được mã hóa trên máy này; quyền Google có thể thu hồi riêng trong tài khoản Google.", "Disconnecting removes only the encrypted token on this device; Google access can be revoked separately in your Google account."),
            ["xaml.mainwindow.text212"] = new("Đồng bộ", "Sync"),
            ["xaml.mainwindow.text213"] = new("Đồng bộ kho mật khẩu", "Sync password vault"),
            ["xaml.mainwindow.text214"] = new("Tải danh sách tài khoản lên Drive (giữ 5 bản gần nhất).", "Upload the account list to Drive (keep the 5 most recent backups)."),
            ["xaml.mainwindow.text215"] = new("Quản lý liên kết", "Manage shares"),
            ["xaml.mainwindow.text216"] = new("Xem và thu hồi quyền đọc Drive đã cấp cho người nhận.", "View and revoke Drive read access granted to recipients."),
            ["xaml.mainwindow.text217"] = new("Khôi phục", "Restore"),
            ["xaml.mainwindow.text218"] = new("Khôi phục kho mật khẩu", "Restore password vault"),
            ["xaml.mainwindow.text219"] = new("Chọn một bản trên Drive để nhập lại vào app.", "Select a Drive backup to restore to the app."),
            ["xaml.mainwindow.text220"] = new("Sao lưu tệp cá nhân", "Back up personal files"),
            ["xaml.mainwindow.text221"] = new("Nén và mã hóa kho tệp (tối đa 100 MB) rồi đưa lên Drive.", "Compress and encrypt the file storage (up to 100 MB), then upload it to Drive."),
            ["xaml.mainwindow.text222"] = new("Khôi phục tệp cá nhân", "Restore personal files"),
            ["xaml.mainwindow.text223"] = new("Tải bản sao lưu về, không ghi đè tệp nào.", "Download the backup without overwriting any files."),
            ["xaml.mainwindow.text224"] = new("Đặt / Đổi", "Set / change"),
            ["xaml.mainwindow.text225"] = new("Cụm mật khẩu sao lưu", "Backup passphrase"),
            ["xaml.mainwindow.text226"] = new("Giúp mở bản sao lưu trên máy khác hoặc sau khi cài lại Windows.", "Use it to open backups on another computer or after reinstalling Windows."),
            ["xaml.mainwindow.text227"] = new("Thông tin máy và đặt lại cài đặt.", "Device information and reset settings."),
            ["xaml.mainwindow.text228"] = new("Xem", "View"),
            ["xaml.mainwindow.text229"] = new("Thông tin hệ thống", "System information"),
            ["xaml.mainwindow.text230"] = new("Hệ điều hành, .NET, tiến trình và quyền hiện tại.", "Operating system, .NET, process, and current permissions."),
            ["xaml.mainwindow.text231"] = new("Cấu hình", "Configure"),
            ["xaml.mainwindow.text232"] = new("Phím tắt", "Keyboard shortcuts"),
            ["xaml.mainwindow.text233"] = new("Tùy chỉnh hoặc tắt phím khóa vault, tìm kiếm, sao chép tên đăng nhập và mở TOTP.", "Customize or disable shortcuts for locking the vault, searching, copying a username, and opening TOTP."),
            ["xaml.mainwindow.text234"] = new("Đặt lại", "Reset"),
            ["xaml.mainwindow.text235"] = new("Đặt lại cài đặt", "Reset settings"),
            ["xaml.mainwindow.text236"] = new("Xóa giao diện, kích thước, vị trí cửa sổ và tài khoản đăng nhập gần nhất.", "Clear the theme, window size and position, and the last signed-in account."),
            ["xaml.mainwindow.text237"] = new("Quản trị", "Administration"),
            ["xaml.mainwindow.text238"] = new("Chỉ quản trị viên mới thấy mục này.", "Only administrators can see this section."),
            ["xaml.mainwindow.text239"] = new("Quản lý tài khoản", "Account manager"),
            ["xaml.mainwindow.text240"] = new("Tạo, sửa, khóa, phân quyền, xóa người dùng và xem nhật ký.", "Create, edit, lock, manage roles, delete users, and view logs."),
            ["xaml.migrationguidewindow.text001"] = new("Hướng dẫn chuyển máy", "Computer migration guide"),
            ["xaml.migrationguidewindow.text002"] = new("Đóng", "Close"),
            ["xaml.opensharedaccountwindow.text001"] = new("Mở mục được chia sẻ", "Open shared item"),
            ["xaml.opensharedaccountwindow.text002"] = new("Mở gói chia sẻ được mã hóa", "Open encrypted share package"),
            ["xaml.opensharedaccountwindow.text003"] = new("Chọn tệp .tqltkshare đã tải từ Drive, nhập khóa do người chia sẻ gửi riêng, rồi xem trước trước khi thêm vào vault.", "Select the .tqltkshare file downloaded from Drive, enter the key sent separately by the sender, then preview it before adding it to the vault."),
            ["xaml.opensharedaccountwindow.text004"] = new("Tệp chia sẻ", "Share file"),
            ["xaml.opensharedaccountwindow.text005"] = new("Chọn tệp", "Browse"),
            ["xaml.opensharedaccountwindow.text006"] = new("Khóa chia sẻ", "Share key"),
            ["xaml.opensharedaccountwindow.text007"] = new("Gói hết hạn sẽ không được mở trong ứng dụng. Việc hết hạn hoặc thu hồi Drive không xóa bản sao đã tải xuống.", "The app will not open an expired package. Expiring or revoking Drive access does not delete downloaded copies."),
            ["xaml.opensharedaccountwindow.text008"] = new("Hủy", "Cancel"),
            ["xaml.opensharedaccountwindow.text009"] = new("Mở và xem trước", "Open and preview"),
            ["xaml.passwordgeneratorwindow.text001"] = new("Tạo mật khẩu", "Generate password"),
            ["xaml.passwordgeneratorwindow.text002"] = new("Tùy chỉnh bộ tạo mật khẩu", "Customize password generator"),
            ["xaml.passwordgeneratorwindow.text003"] = new("Kiểu mật khẩu", "Password type"),
            ["xaml.passwordgeneratorwindow.text004"] = new("Ngẫu nhiên", "Random"),
            ["xaml.passwordgeneratorwindow.text005"] = new("Cụm từ dễ nhớ", "Memorable passphrase"),
            ["xaml.passwordgeneratorwindow.text006"] = new("Độ dài (8–64)", "Length (8–64)"),
            ["xaml.passwordgeneratorwindow.text007"] = new("Chữ hoa", "Uppercase"),
            ["xaml.passwordgeneratorwindow.text008"] = new("Chữ thường", "Lowercase"),
            ["xaml.passwordgeneratorwindow.text009"] = new("Chữ số", "Digits"),
            ["xaml.passwordgeneratorwindow.text010"] = new("Ký hiệu", "Symbols"),
            ["xaml.passwordgeneratorwindow.text011"] = new("Loại ký tự dễ nhầm lẫn (O/0, I/l/1)", "Exclude ambiguous characters (O/0, I/l/1)"),
            ["xaml.passwordgeneratorwindow.text012"] = new("Số từ (6–12)", "Word count (6–12)"),
            ["xaml.passwordgeneratorwindow.text013"] = new("Dấu phân cách", "Separator"),
            ["xaml.passwordgeneratorwindow.text014"] = new("khoảng trắng", "spaces"),
            ["xaml.passwordgeneratorwindow.text015"] = new("8 từ được chọn ngẫu nhiên bằng bộ sinh mật mã học.", "Eight words are selected using a cryptographic random generator."),
            ["xaml.passwordgeneratorwindow.text016"] = new("Áp dụng quy tắc đã lưu cho website này", "Apply saved rules for this website"),
            ["xaml.passwordgeneratorwindow.text017"] = new("Độ dài tối thiểu", "Minimum length"),
            ["xaml.passwordgeneratorwindow.text018"] = new("Độ dài tối đa", "Maximum length"),
            ["xaml.passwordgeneratorwindow.text019"] = new("Bắt buộc chữ hoa", "Require uppercase letters"),
            ["xaml.passwordgeneratorwindow.text020"] = new("Bắt buộc chữ thường", "Require lowercase letters"),
            ["xaml.passwordgeneratorwindow.text021"] = new("Bắt buộc chữ số", "Require digits"),
            ["xaml.passwordgeneratorwindow.text022"] = new("Bắt buộc ký hiệu", "Require symbols"),
            ["xaml.passwordgeneratorwindow.text023"] = new("Ký tự bị cấm", "Forbidden characters"),
            ["xaml.passwordgeneratorwindow.text024"] = new("Lưu quy tắc website", "Save website rules"),
            ["xaml.passwordgeneratorwindow.text025"] = new("Xóa quy tắc đã lưu", "Remove saved rules"),
            ["xaml.passwordgeneratorwindow.text026"] = new("Hủy", "Cancel"),
            ["xaml.passwordgeneratorwindow.text027"] = new("Dùng mật khẩu này", "Use this password"),
            ["xaml.passwordhistorywindow.text001"] = new("Lịch sử mật khẩu", "Password history"),
            ["xaml.passwordhistorywindow.text002"] = new("Các mật khẩu trước được giữ mã hóa trong kho mật khẩu. Chỉ lưu 5 mật khẩu gần nhất.", "Previous passwords are kept encrypted in the password vault. Only the 5 most recent are retained."),
            ["xaml.passwordhistorywindow.text003"] = new("Ngày đổi", "Changed"),
            ["xaml.passwordhistorywindow.text004"] = new("Mật khẩu trước đây", "Previous passwords"),
            ["xaml.passwordhistorywindow.text005"] = new("Đóng", "Close"),
            ["xaml.passwordhistorywindow.text006"] = new("Hiện mật khẩu", "Show password"),
            ["xaml.passwordhistorywindow.text007"] = new("Sao chép", "Copy"),
            ["xaml.passwordhistorywindow.text008"] = new("Dùng mật khẩu này", "Use this password"),
            ["xaml.recoverycodeswindow.text001"] = new("Mã khôi phục mới", "New recovery codes"),
            ["xaml.recoverycodeswindow.text002"] = new("🔑 8 mã khôi phục mới", "🔑 8 new recovery codes"),
            ["xaml.recoverycodeswindow.text003"] = new("Hãy lưu các mã này ở nơi an toàn NGOÀI máy tính (giấy, két, trình quản lý khác). Mỗi mã chỉ dùng được một lần và sẽ không hiển thị lại. Các mã cũ vẫn dùng được cho đến khi bạn bấm \"Tôi đã lưu, hoàn tất\".", "Store these codes somewhere safe OFF your computer (paper, a safe, or another password manager). Each code works once and will not be shown again. Old codes remain valid until you select \"I have saved them, finish\"."),
            ["xaml.recoverycodeswindow.text004"] = new("📋 Sao chép", "📋 Copy"),
            ["xaml.recoverycodeswindow.text005"] = new("✅ Tôi đã lưu, hoàn tất", "✅ I have saved them, finish"),
            ["xaml.registerwindow.text001"] = new("Đăng ký tài khoản - TQLTK", "Register account - TQLTK"),
            ["xaml.registerwindow.text002"] = new("📝 Đăng ký tài khoản", "📝 Register account"),
            ["xaml.registerwindow.text003"] = new("Tạo tài khoản mới để sử dụng TQLTK", "Create an account to use TQLTK"),
            ["xaml.registerwindow.text004"] = new("Tên đăng nhập (*):", "Username (*):"),
            ["xaml.registerwindow.text005"] = new("Từ 3 ký tự trở lên, không dấu, không khoảng trắng", "At least 3 characters; no accents or spaces"),
            ["xaml.registerwindow.text006"] = new("Mật khẩu (*):", "Password (*):"),
            ["xaml.registerwindow.text007"] = new("Xác nhận mật khẩu (*):", "Confirm password (*):"),
            ["xaml.registerwindow.text008"] = new("Họ tên (*):", "Full name (*):"),
            ["xaml.registerwindow.text009"] = new("Gmail:", "Gmail:"),
            ["xaml.registerwindow.text010"] = new("Ví dụ: tenban@gmail.com", "Example: name@example.com"),
            ["xaml.registerwindow.text011"] = new("Số điện thoại:", "Phone number:"),
            ["xaml.registerwindow.text012"] = new("Hủy", "Cancel"),
            ["xaml.registerwindow.text013"] = new("✓ Đăng ký", "✓ Register"),
            ["xaml.restorebackupwindow.text001"] = new("Khôi phục từ Google Drive", "Restore from Google Drive"),
            ["xaml.restorebackupwindow.text002"] = new("☁️ Khôi phục từ Google Drive", "☁️ Restore from Google Drive"),
            ["xaml.restorebackupwindow.text003"] = new("Ngày tạo", "Created"),
            ["xaml.restorebackupwindow.text004"] = new("Dung lượng", "Size"),
            ["xaml.restorebackupwindow.text005"] = new("Thuộc về", "Owner"),
            ["xaml.restorebackupwindow.text006"] = new("Tên tệp", "File name"),
            ["xaml.restorebackupwindow.text007"] = new("Hiện cả bản sao lưu của người dùng khác (khi dùng chung một tài khoản Google)", "Show backups from other users (when sharing a Google account)"),
            ["xaml.restorebackupwindow.text008"] = new("Dữ liệu của bản sao lưu được nhập thêm vào tài khoản hiện tại. Tài khoản trùng sẽ được bỏ qua, dữ liệu hiện có không bị xóa.", "Backup data is added to the current account. Duplicate accounts are skipped; existing data is not deleted."),
            ["xaml.restorebackupwindow.text009"] = new("Hủy", "Cancel"),
            ["xaml.restorebackupwindow.text010"] = new("⬇ Khôi phục", "⬇ Restore"),
            ["xaml.sharecreatedwindow.text001"] = new("Liên kết chia sẻ đã tạo", "Share link created"),
            ["xaml.sharecreatedwindow.text002"] = new("Đã tạo liên kết chia sẻ", "Share link created"),
            ["xaml.sharecreatedwindow.text003"] = new("Liên kết Drive", "Drive link"),
            ["xaml.sharecreatedwindow.text004"] = new("Sao chép liên kết", "Copy link"),
            ["xaml.sharecreatedwindow.text005"] = new("Khóa giải mã (chỉ hiển thị lần này)", "Decryption key (shown only once)"),
            ["xaml.sharecreatedwindow.text006"] = new("Sao chép khóa", "Copy key"),
            ["xaml.sharecreatedwindow.text007"] = new("Gửi khóa này qua một kênh khác với email Drive. Thu hồi Drive chỉ chặn việc tải lại gói; không xóa bản sao mà người nhận đã tải hoặc giải mã.", "Send this key through a channel separate from the Drive email. Revoking Drive access only prevents downloading the package again; it cannot delete copies the recipient downloaded or decrypted."),
            ["xaml.sharecreatedwindow.text008"] = new("Đóng", "Close"),
            ["xaml.totpcodewindow.text001"] = new("Mã xác thực TOTP", "TOTP authenticator code"),
            ["xaml.totpcodewindow.text002"] = new("Trang web", "Website"),
            ["xaml.totpcodewindow.text003"] = new("Sao chép mã (bộ nhớ tạm tự xóa)", "Copy code (clipboard clears automatically)"),
            ["xaml.totpsetupwindow.text001"] = new("Nhập mã TOTP", "Enter TOTP code"),
            ["xaml.totpsetupwindow.text002"] = new("Thêm mã xác thực của website", "Add website authenticator"),
            ["xaml.totpsetupwindow.text003"] = new("Dán URI otpauth:// hoặc khóa bí mật Base32; quét mã QR chuyển tài khoản Google Authenticator; nhập tệp JSON chưa mã hóa từ Aegis/2FAS hoặc tệp URI otpauth.", "Paste an otpauth:// URI or Base32 secret; scan a Google Authenticator account-transfer QR code; or import an unencrypted Aegis/2FAS JSON export or otpauth URI file."),
            ["xaml.totpsetupwindow.text004"] = new("URI otpauth://totp/... hoặc khóa bí mật Base32", "otpauth://totp/... URI or Base32 secret"),
            ["xaml.totpsetupwindow.text005"] = new("Đọc QR từ ảnh…", "Read QR code from image…"),
            ["xaml.totpsetupwindow.text006"] = new("Nhập tệp Authenticator…", "Import authenticator file…"),
            ["xaml.totpsetupwindow.text007"] = new("Đọc URI / khóa bí mật", "Read URI / secret"),
            ["xaml.totpsetupwindow.text008"] = new("Mục được nhập", "Imported items"),
            ["xaml.totpsetupwindow.text009"] = new("Chưa đọc dữ liệu.", "No data loaded."),
            ["xaml.totpsetupwindow.text010"] = new("Mã xác thực được lưu cùng kho mật khẩu đã mã hóa. Mã QR và khóa bí mật chứa thông tin nhạy cảm; chỉ nhập từ nguồn bạn tin cậy.", "Authenticator data is stored in the encrypted password vault. QR codes and secrets contain sensitive information; import them only from trusted sources."),
            ["xaml.totpsetupwindow.text011"] = new("Hủy", "Cancel"),
            ["xaml.totpsetupwindow.text012"] = new("Lưu TOTP", "Save TOTP"),
            ["xaml.trashwindow.text001"] = new("Thùng rác", "Trash"),
            ["xaml.trashwindow.text002"] = new("Tài khoản và ghi chú giữ lại 30 ngày. Xóa hẳn cần xác thực lại.", "Accounts and notes are kept for 30 days. Permanent deletion requires reauthentication."),
            ["xaml.trashwindow.text003"] = new("Loại", "Type"),
            ["xaml.trashwindow.text004"] = new("Tên", "Name"),
            ["xaml.trashwindow.text005"] = new("Thông tin", "Information"),
            ["xaml.trashwindow.text006"] = new("Đã xóa", "Deleted"),
            ["xaml.trashwindow.text007"] = new("Còn lại", "Remaining"),
            ["xaml.trashwindow.text008"] = new("Thùng rác đang trống.", "Trash is empty."),
            ["xaml.trashwindow.text009"] = new("Khôi phục", "Restore"),
            ["xaml.trashwindow.text010"] = new("Xóa hẳn", "Delete permanently"),
            ["xaml.twofactorpromptwindow.text001"] = new("Xác thực 2 bước", "Two-factor authentication"),
            ["xaml.twofactorpromptwindow.text002"] = new("🔑 Xác thực 2 bước", "🔑 Two-factor authentication"),
            ["xaml.twofactorpromptwindow.text003"] = new("Mất điện thoại? Nhập mã khôi phục (dạng XXXXX-XXXXX) vào ô trên.", "Lost your phone? Enter a recovery code (XXXXX-XXXXX) above."),
            ["xaml.twofactorpromptwindow.text004"] = new("Hủy", "Cancel"),
            ["xaml.twofactorpromptwindow.text005"] = new("Xác nhận", "Confirm"),
            ["xaml.twofactorsetupwindow.text001"] = new("Bật xác thực 2 bước (Authenticator)", "Enable two-factor authentication (Authenticator)"),
            ["xaml.twofactorsetupwindow.text002"] = new("🔑 Bật xác thực 2 bước", "🔑 Enable two-factor authentication"),
            ["xaml.twofactorsetupwindow.text003"] = new("1. Cài Google Authenticator, Microsoft Authenticator hoặc Authy trên điện thoại.", "1. Install Google Authenticator, Microsoft Authenticator, or Authy on your phone."),
            ["xaml.twofactorsetupwindow.text004"] = new("2. Chọn Thêm tài khoản → Quét mã QR, rồi quét mã bên dưới.", "2. Tap Add account → Scan a QR code, then scan the code below."),
            ["xaml.twofactorsetupwindow.text005"] = new("3. Nhập mã 6 số đang hiển thị trong ứng dụng để xác nhận.", "3. Enter the 6-digit code shown in the app to confirm."),
            ["xaml.twofactorsetupwindow.text006"] = new("Không quét được? Nhập khóa này thủ công vào ứng dụng:", "Can't scan it? Enter this key manually in the app:"),
            ["xaml.twofactorsetupwindow.text007"] = new("📋 Sao chép", "📋 Copy"),
            ["xaml.twofactorsetupwindow.text008"] = new("Mã 6 số trong ứng dụng:", "6-digit code from the app:"),
            ["xaml.twofactorsetupwindow.text009"] = new("Hủy", "Cancel"),
            ["xaml.twofactorsetupwindow.text010"] = new("Xác nhận và bật", "Confirm and enable"),
            ["xaml.twofactorsetupwindow.text011"] = new("✅ Đã bật xác thực 2 bước", "✅ Two-factor authentication is enabled"),
            ["xaml.twofactorsetupwindow.text012"] = new("Đây là 8 mã khôi phục, mỗi mã dùng được 1 lần khi bạn mất điện thoại.", "These are 8 recovery codes. Each can be used once if you lose your phone."),
            ["xaml.twofactorsetupwindow.text013"] = new("Hãy lưu ở nơi an toàn (giấy, két sắt...). Mã chỉ hiển thị MỘT LẦN duy nhất.", "Store this somewhere safe (on paper or in a safe). The code is shown only ONCE."),
            ["xaml.twofactorsetupwindow.text014"] = new("📋 Sao chép mã", "📋 Copy code"),
            ["xaml.twofactorsetupwindow.text015"] = new("Tôi đã lưu, hoàn tất", "I have saved them, finish"),
            ["app.footer"] = new("Được tạo bởi 天地と月の造化 - TQLTK v{0} (BETA) | © 2026 Namtudifreefire | Giấy phép MIT", "Made by 天地と月の造化 - TQLTK v{0} (BETA) | © 2026 Namtudifreefire | MIT License"),
            ["backup.periodic.disabled"] = new("Sao lưu định kỳ đang tắt.", "Scheduled backups are off."),
            ["backup.periodic.failure"] = new("Sao lưu gần nhất thất bại: {0}", "Most recent backup failed: {0}"),
            ["backup.periodic.last_success"] = new("Sao lưu đầy đủ gần nhất: {0}.", "Most recent full backup: {0}."),
            ["backup.periodic.enabled_empty"] = new("Sao lưu định kỳ đã bật; chưa có bản sao lưu đầy đủ nào.", "Scheduled backups are on; no full backup has been created yet."),
            ["backup.retention.warning"] = new("Cảnh báo giữ bản sao lưu: còn {0} bản ({1}); giới hạn là {2} bản, {3} ngày và {4}.", "Backup retention warning: {0} copies remain ({1}); limits are {2} copies, {3} days, and {4}."),
            ["backup.retention.failed"] = new("Không thể áp dụng chính sách giữ bản sao lưu; hãy kiểm tra quyền truy cập thư mục sao lưu.", "Could not apply the backup retention policy; check access to the backup folder."),
            ["backup.restore.summary"] = new("Đã khôi phục: thêm {0} tài khoản, {1} ghi chú và {2} tệp.", "Restored: added {0} accounts, {1} notes, and {2} files."),
            ["backup.migration.next_steps"] = new("Đã xác minh gói trước khi lưu. Chuyển tệp {0} và cụm mật khẩu sang máy mới qua kênh an toàn, nhưng cất chúng riêng nhau. Trên máy mới, đăng nhập hoặc tạo tài khoản TQLTK, chọn Khôi phục / chuyển máy và kiểm tra dữ liệu. Giữ nguyên dữ liệu máy cũ cho đến khi xác minh xong; sau đó thiết lập lại Windows Hello hoặc 2FA.", "The package was verified before saving. Transfer {0} and its passphrase to the new computer through a secure channel, but store them separately. On the new computer, sign in to or create a TQLTK account, choose Restore / migrate, and verify the restored data. Keep the old computer's data until verification is complete; then set up Windows Hello or two-factor sign-in again."),
            ["backup.migration.guide"] = new("1. Đặt cụm mật khẩu sao lưu riêng, khác mật khẩu đăng nhập; cất cụm này an toàn.\n2. Chọn ghi chú riêng, tệp cá nhân và cài đặt an toàn cần đưa vào gói. Tài khoản luôn gồm mật khẩu, TOTP, tệp đính kèm, trường tùy chỉnh và lịch sử.\n3. Tạo gói .tqltk. Ứng dụng giải mã và xác minh gói ngay sau khi tạo; kiểm tra danh sách thành phần trong thông báo.\n4. Chuyển gói và cụm mật khẩu sang máy mới bằng kênh an toàn, lưu riêng nhau. Không gửi cả hai trong cùng một tin nhắn hoặc thư mục chia sẻ.\n5. Trên máy mới, cài và mở TQLTK, đăng nhập hoặc tạo tài khoản ứng dụng rồi chọn Khôi phục / chuyển máy.\n6. Chọn gói, nhập cụm mật khẩu, xem trước các thành phần và xác nhận khôi phục. Mục trùng được giữ nguyên; mục còn thiếu được thêm.\n7. Kiểm tra tài khoản, TOTP, ghi chú, tệp đính kèm và tệp cá nhân; thiết lập lại Windows Hello hoặc 2FA trên máy mới.\n8. Chỉ xóa dữ liệu cũ và bản trung gian sau khi đã xác minh đầy đủ bản khôi phục.", "1. Set a dedicated backup passphrase that differs from your sign-in password, and store it safely.\n2. Choose whether to include private notes, personal files, and safe settings. Accounts always include passwords, TOTP, attachments, custom fields, and history.\n3. Create the .tqltk package. The app decrypts and verifies it immediately; review the component summary.\n4. Transfer the package and passphrase to the new computer through a secure channel and store them separately. Do not send both in one message or shared folder.\n5. On the new computer, install and open TQLTK, sign in to or create an app account, then choose Restore / migrate.\n6. Select the package, enter its passphrase, review the preview, and confirm the restore. Existing matching items are kept; missing items are added.\n7. Check accounts, TOTP, notes, attachments, and personal files; set up Windows Hello or two-factor sign-in again on the new computer.\n8. Delete old data and temporary copies only after you have fully verified the restore."),
            ["backup.recovered_copy_suffix"] = new("(bản phục hồi {0})", "(recovered copy {0})"),
            ["backup.selective.success"] = new("Đã khôi phục thành bản sao: {0} tài khoản, {1} ghi chú và {2} tệp. Dữ liệu hiện tại không bị ghi đè.", "Recovered as separate copies — accounts: {0}, notes: {1}, files: {2}. Current data was not overwritten."),
            ["backup.created.contents_summary"] = new("Đã tạo và xác minh gói mã hóa: {0}\nTài khoản gồm mật khẩu, TOTP, tệp đính kèm và trường tùy chỉnh: {1}. Ghi chú riêng: {2} ({3}). Tệp cá nhân: {4} ({5}). Cài đặt an toàn: {6}.", "Created and verified encrypted package: {0}\nAccounts include passwords, TOTP, attachments, and custom fields: {1}. Private notes: {2} ({3}). Personal files: {4} ({5}). Safe settings: {6}."),
            ["import.preview.count"] = new("Đọc được {count} mục. Chọn một mục để xem thông tin.", "Found {count} entries. Select one to view its details.")
        };
    private static readonly Regex Placeholder = new(@"(?<!\{)\{(?<name>[A-Za-z_][A-Za-z0-9_]*|\d+)(?<suffix>(?:,[^{}]*)?(?::[^{}]*)?)\}(?!\})", RegexOptions.CultureInvariant);
    private static readonly Lazy<IReadOnlyDictionary<string, LocalizedMessageDefinition>> Definitions = new(BuildDefinitions);
    private static readonly Lazy<IReadOnlyDictionary<string, LocalizedMessageDefinition>> SourceDefinitions = new(() =>
        Definitions.Value.Values.GroupBy(item => item.Vietnamese, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal));
    public static IReadOnlyCollection<LocalizedMessageDefinition> Catalog => Definitions.Value.Values.ToArray();
    public static string GetMessageId(string source)
    {
        return SourceDefinitions.Value.TryGetValue(source, out LocalizedMessageDefinition? definition)
            ? definition.Id
            : throw new KeyNotFoundException("The UI message has no registered translation: " + source);
    }
    internal static bool TryTranslateKnown(string sourceOrId, string languageCode, out string translation)
    {
        LocalizedMessageDefinition? definition = Definitions.Value.GetValueOrDefault(sourceOrId)
            ?? SourceDefinitions.Value.GetValueOrDefault(sourceOrId);
        if (definition == null)
        {
            translation = "";
            return false;
        }
        translation = IsEnglish(languageCode) ? definition.English : definition.Vietnamese;
        return true;
    }
    private static IReadOnlyDictionary<string, LocalizedMessageDefinition> BuildDefinitions()
    {
        var definitions = new Dictionary<string, LocalizedMessageDefinition>(StringComparer.Ordinal);
        var registeredSources = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string id, Message message) in Messages)
        {
            definitions.Add(id, new(id, message.Vietnamese, message.English, GetPlaceholders(message.Vietnamese)));
            registeredSources.Add(message.Vietnamese);
        }
        foreach ((string source, string english) in EnglishCatalog.Entries.Concat(EnglishCatalog.Fragments))
        {
            if (!registeredSources.Add(source)) continue;
            string id = "ui." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
            definitions.Add(id, new(id, source, english, GetPlaceholders(source)));
        }
        return definitions;
    }
    public static string Get(string key) => Get(key, LanguageService.IsEnglish);
    public static string Get(string key, string? languageCode) => Get(key, IsEnglish(languageCode));
    private static string Get(string key, bool isEnglish)
    {
        if (!Definitions.Value.TryGetValue(key, out LocalizedMessageDefinition? message))
            throw new KeyNotFoundException($"Localization key '{key}' is not registered.");
        return isEnglish ? message.English : message.Vietnamese;
    }
    public static string FormatForLanguage(string key, string languageCode, params object?[] arguments) =>
        FormatTemplate(key, Get(key, languageCode), arguments, GetCulture(languageCode));
    public static string Format(string key, params object?[] arguments)
    {
        string template = Get(key);
        return FormatTemplate(key, template, arguments, GetCulture(null));
    }
    public static string FormatSource(string source, string languageCode, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(arguments);
        string template = LanguageService.Translate(source, languageCode);
        CultureInfo culture = GetCulture(languageCode);
        string[] placeholders = GetPlaceholders(template);
        if (placeholders.Any(placeholder => !int.TryParse(placeholder, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            if (arguments.Length != 1 || arguments[0] is not IReadOnlyDictionary<string, object?> namedArguments)
                throw new FormatException("This message uses named placeholders and requires a named-argument dictionary.");
            return FormatNamedTemplate(source, template, namedArguments, culture);
        }
        return FormatTemplate(source, template, arguments, culture);
    }
    private static string FormatTemplate(string key, string template, object?[] arguments, CultureInfo culture)
    {
        string[] placeholders = GetPlaceholders(template);
        if (placeholders.Any(placeholder => !int.TryParse(placeholder, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            if (arguments.Length != 1 || arguments[0] is not IReadOnlyDictionary<string, object?> namedArguments)
                throw new FormatException($"Localization key '{key}' uses named placeholders and requires one named-argument dictionary.");
            return FormatNamedTemplate(key, template, namedArguments, culture);
        }
        ValidateArguments(key, template, arguments.Length);
        return string.Format(culture, template, arguments);
    }
    public static string FormatNamed(string key, IReadOnlyDictionary<string, object?> arguments, string? languageCode = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool isEnglish = IsEnglish(languageCode);
        return FormatNamedTemplate(key, Get(key, isEnglish), arguments, GetCulture(languageCode));
    }
    public static string FormatDate(DateTime value, string? languageCode = null)
    {
        CultureInfo culture = GetCulture(languageCode);
        return NormalizeForDisplay(value).ToString("d", culture);
    }
    public static string FormatDateTime(DateTime value, string? languageCode = null, bool includeSeconds = false)
    {
        CultureInfo culture = GetCulture(languageCode);
        return NormalizeForDisplay(value).ToString(includeSeconds ? "G" : "g", culture);
    }
    public static string FormatDateTime(DateTimeOffset value, string? languageCode = null, bool includeSeconds = false) =>
        value.ToLocalTime().ToString(includeSeconds ? "G" : "g", GetCulture(languageCode));
    public static string FormatDate(DateTimeOffset value, string? languageCode = null) =>
        value.ToLocalTime().ToString("d", GetCulture(languageCode));
    private static DateTime NormalizeForDisplay(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value.ToLocalTime(),
        DateTimeKind.Local => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local)
    };
    public static string FormatCount(int count, string singularEnglish, string pluralEnglish, string unitVietnamese,
        string? languageCode = null)
    {
        CultureInfo culture = GetCulture(languageCode);
        bool isEnglish = IsEnglish(languageCode);
        if (!isEnglish) return $"{count.ToString(culture)} {unitVietnamese}";
        return count.ToString(CultureInfo.GetCultureInfo("en-US")) + " " + (count == 1 ? singularEnglish : pluralEnglish);
    }
    private static bool IsEnglish(string? languageCode) => languageCode == null
        ? LanguageService.IsEnglish
        : string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase);
    private static CultureInfo GetCulture(string? languageCode) => IsEnglish(languageCode)
        ? CultureInfo.GetCultureInfo("en-US")
        : CultureInfo.GetCultureInfo("vi-VN");
    public static IReadOnlyList<LocalizationAuditIssue> Audit()
    {
        var issues = new List<LocalizationAuditIssue>();
        foreach ((string key, Message message) in Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Vietnamese)) issues.Add(new(key, "Thiếu nội dung tiếng Việt."));
            if (string.IsNullOrWhiteSpace(message.English)) issues.Add(new(key, "Thiếu nội dung tiếng Anh."));
            ValidateTemplate(issues, key + ".vi", message.Vietnamese);
            ValidateTemplate(issues, key + ".en", message.English);
            if (!GetPlaceholders(message.Vietnamese).SequenceEqual(GetPlaceholders(message.English)))
                issues.Add(new(key, "Hai bản dịch không có cùng tập tham số."));
        }
        foreach (Message message in Messages.Values)
        {
            foreach (string template in new[] { message.Vietnamese, message.English })
            {
                int[] indexes = GetPlaceholders(template)
                    .Where(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
                    .OrderBy(index => index)
                    .ToArray();
                if (indexes.Length > 0 && !indexes.SequenceEqual(Enumerable.Range(0, indexes.Length)))
                    issues.Add(new("LocalizationKeys", "Tham số phải liên tục từ {0} và không được bỏ số."));
            }
        }
        foreach ((string source, string translation) in EnglishCatalog.Entries)
        {
            if (string.IsNullOrWhiteSpace(source)) issues.Add(new("EnglishCatalog", "Có khóa tiếng Việt rỗng."));
            if (string.IsNullOrWhiteSpace(translation)) issues.Add(new(source, "Thiếu bản dịch tiếng Anh."));
            ValidateTemplate(issues, source, source);
            ValidateTemplate(issues, source, translation);
            if (!GetPlaceholders(source).SequenceEqual(GetPlaceholders(translation)))
                issues.Add(new(source, "Bản dịch tiếng Anh không giữ nguyên các tham số."));
        }
        foreach (string source in EnglishCatalog.ConflictingDuplicateSources)
            issues.Add(new(source, "Khóa bản dịch nguồn được khai báo nhiều lần với các bản dịch tiếng Anh khác nhau."));
        foreach (LocalizedMessageDefinition definition in Catalog)
            if (definition.Parameters.Count != GetPlaceholders(definition.English).Length)
                issues.Add(new(definition.Id, "Số tham số mô tả không khớp bản dịch."));
        return issues;
    }
    public static IReadOnlyList<LocalizationAuditIssue> AuditSources(IEnumerable<string> sources) =>
        sources.Distinct(StringComparer.Ordinal)
            .Where(source => !TryTranslateKnown(source, "en", out _))
            .Select(source => new LocalizationAuditIssue(source, "Thiếu mã thông báo hoặc bản dịch đã đăng ký."))
            .ToArray();
    private static void ValidateArguments(string key, string template, int argumentCount)
    {
        int[] indexes = GetPlaceholders(template)
            .Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ? index : -1)
            .Where(index => index >= 0)
            .ToArray();
        if (indexes.Length == 0 && argumentCount == 0) return;
        if (indexes.Any(index => index >= argumentCount) || indexes.Distinct().Count() != argumentCount)
            throw new FormatException($"Localization key '{key}' expects placeholders [{string.Join(",", indexes)}], but received {argumentCount} arguments.");
    }
    private static void ValidateTemplate(List<LocalizationAuditIssue> issues, string key, string template)
    {
        try { _ = string.Format(CultureInfo.InvariantCulture, NormalizeNamedPlaceholders(template), new object?[32]); }
        catch (FormatException ex) { issues.Add(new(key, "Định dạng tham số không hợp lệ: " + ex.Message)); }
    }
    private static string[] GetPlaceholders(string value) => Placeholder.Matches(value)
        .Select(match => match.Groups["name"].Value)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    private static string NormalizeNamedPlaceholders(string template)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        return Placeholder.Replace(template, match =>
        {
            string name = match.Groups["name"].Value;
            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return match.Value;
            if (!indexes.TryGetValue(name, out int index)) indexes[name] = index = indexes.Count;
            return "{" + index.ToString(CultureInfo.InvariantCulture) + match.Groups["suffix"].Value + "}";
        });
    }
    private static string FormatNamedTemplate(string key, string template,
        IReadOnlyDictionary<string, object?> arguments, CultureInfo culture)
    {
        string[] placeholders = GetPlaceholders(template);
        if (placeholders.Any(name => int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            throw new FormatException($"Localization key '{key}' mixes named and positional placeholders.");
        string? missing = placeholders.FirstOrDefault(name => !arguments.ContainsKey(name));
        if (missing != null)
            throw new FormatException($"Localization key '{key}' is missing named argument '{missing}'.");
        if (arguments.Keys.Any(name => !placeholders.Contains(name, StringComparer.Ordinal)))
            throw new FormatException($"Localization key '{key}' received an unregistered named argument.");
        return Placeholder.Replace(template, match =>
        {
            string name = match.Groups["name"].Value;
            object? value = arguments[name];
            string suffix = match.Groups["suffix"].Value;
            return string.Format(culture, "{0" + suffix + "}", value);
        });
    }
}
