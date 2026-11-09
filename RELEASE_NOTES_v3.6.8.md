# TQLTK v3.6.8 (BETA)

Released November 9, 2026 as a BETA pre-release. | Đã phát hành ngày 09/11/2026 dưới dạng BETA pre-release.

## English

### Backup and recovery

- Added a complete encrypted backup package containing accounts, account TOTP settings, attachments, private notes, personal files and selected settings. The backup shows which components were included.
- Added a migration guide for moving PCs or reinstalling Windows: create a passphrase-protected backup, verify it before moving, then restore and configure sign-in again on the destination.
- Added periodic backups while the app is running, with change detection and failure reporting.
- Added backup retention controls for count, age, daily/weekly/monthly copies, verified pins and storage limits. Pins and retention rules can prevent reaching a configured storage target.
- Added backup comparison and selective recovery as copies, preserving newer entries.
- Added unsaved-change protection and encrypted edit drafts for account editing.
- Vault backup version metadata now follows the running app version instead of a hard-coded version number.

### Interface and validation

- Expanded localization using stable message keys and checks for missing translations, format parameters and versioned text. Vietnamese and English remain available.
- Improved responsive layouts and added automated layout checks at 100%, 125%, 150% and 200% scaling.
- Expanded the Windows regression suite to 31 groups. Additional CI checks cover transfer between two Windows VMs, a synthetic legacy v3p fixture, rollback failure, forced termination of a restore subprocess and actual disk-full errors on a bounded virtual disk.
- Windows tests run in GitHub Actions; GitLab runs Advanced SAST, Secret Detection, Dependency Scanning/SBOM and NuGet Audit. These results cover the tested cases and do not constitute an independent security audit.

### Before updating / known limitations

- Create and verify a separate passphrase-protected backup before updating. Keep the original backup until you have checked the restored data.
- A crash or failed rollback can leave a restore partially applied. Automatic rollback across all components after a crash is not implemented. Inspect the data, preview the backup again and retry the restore; the tested retry completes remaining entries without duplication. Avoid editing data while resolving an interrupted restore.
- Cross-machine tests use separate CI VMs and synthetic data. The legacy fixture is not a collection of backups from every old release. Old unauthenticated CBC data remains rejected by design.
- Disk-full testing covers the backup destination, not every disk-full point during restore or rollback. Hardware failure and power loss are not fully simulated.
- Windows Hello and Google Drive remain BETA. Windows Hello setup and Google authorization may need to be repeated after moving Windows accounts or PCs.
- Code signing is not configured for this release preparation. Check the final asset and its release information; a SHA-256 checksum alone does not prove publisher identity.

## Tiếng Việt

### Sao lưu và khôi phục

- Thêm gói sao lưu mã hóa đầy đủ gồm tài khoản, cấu hình TOTP của từng mục, tệp đính kèm, ghi chú riêng, file cá nhân và các thiết lập được chọn. Hiển thị rõ thành phần có trong bản sao lưu.
- Thêm hướng dẫn chuyển máy hoặc cài lại Windows: tạo backup dùng cụm mật khẩu, xác minh trước khi chuyển, rồi khôi phục và thiết lập lại đăng nhập trên máy đích.
- Thêm sao lưu định kỳ khi ứng dụng đang chạy, nhận biết dữ liệu thay đổi và thông báo lỗi sao lưu.
- Thêm chính sách giữ backup theo số bản, tuổi bản sao, ngày/tuần/tháng, ghim bản đã xác minh và giới hạn dung lượng. Bản ghim và quy tắc giữ bản có thể khiến dung lượng chưa giảm về mức đã đặt.
- Thêm so sánh backup và khôi phục mục được chọn thành bản sao, giữ các mục mới hơn.
- Thêm bảo vệ thay đổi chưa lưu và bản nháp mã hóa khi chỉnh sửa tài khoản.
- Metadata phiên bản trong vault backup lấy từ phiên bản app đang chạy, thay cho số phiên bản ghi cứng.

### Giao diện và kiểm thử

- Mở rộng dịch bằng mã thông báo ổn định, kiểm tra thiếu bản dịch, sai tham số và chuỗi có phiên bản. Tiếp tục hỗ trợ tiếng Việt và tiếng Anh.
- Cải thiện bố cục thích ứng, thêm kiểm tra layout tự động ở mức phóng đại 100%, 125%, 150% và 200%.
- Mở rộng suite Windows lên 31 nhóm. Các job CI bổ sung kiểm tra chuyển backup giữa hai VM Windows, fixture v3p cũ tổng hợp, rollback thất bại, dừng cưỡng bức tiến trình con đang restore và lỗi hết dung lượng thật trên ổ đĩa ảo giới hạn.
- GitHub Actions chạy test Windows; GitLab chạy Advanced SAST, Secret Detection, Dependency Scanning/SBOM và NuGet Audit. Kết quả chỉ bao phủ các tình huống đã thử, không phải kiểm toán bảo mật độc lập.

### Trước khi cập nhật / giới hạn đã biết

- Tạo và xác minh một bản sao lưu riêng dùng cụm mật khẩu trước khi cập nhật. Giữ file backup gốc đến khi kiểm tra xong dữ liệu khôi phục.
- Crash hoặc rollback thất bại có thể để lại dữ liệu khôi phục một phần. Chưa có cơ chế tự rollback toàn bộ thành phần sau crash. Hãy kiểm tra dữ liệu, xem trước backup lại rồi chạy lại restore; trường hợp đã thử hoàn tất phần còn thiếu mà không tạo trùng. Tránh chỉnh sửa dữ liệu trong lúc xử lý lần khôi phục bị gián đoạn.
- Test chuyển máy dùng các VM CI riêng và dữ liệu giả. Fixture cũ không đại diện cho backup của mọi bản phát hành. Dữ liệu CBC cũ thiếu kiểm tra toàn vẹn vẫn bị từ chối theo chủ đích.
- Test hết dung lượng bao phủ đích ghi backup, chưa bao phủ mọi điểm hết đĩa trong restore/rollback. Chưa mô phỏng đầy đủ hỏng phần cứng hoặc mất điện.
- Windows Hello và Google Drive tiếp tục là BETA. Sau khi đổi tài khoản Windows hoặc chuyển máy có thể cần thiết lập lại Hello và cấp quyền Google.
- Chưa cấu hình ký số cho lần chuẩn bị phát hành này. Kiểm tra tệp cuối và thông tin release; riêng checksum SHA-256 không chứng minh danh tính nhà phát hành.