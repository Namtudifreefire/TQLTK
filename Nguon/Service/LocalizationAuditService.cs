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
