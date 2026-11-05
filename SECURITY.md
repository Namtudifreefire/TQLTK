# Security Policy | Chính sách bảo mật

TQLTK handles account credentials and other sensitive information. This document explains which versions receive security attention, how to report a vulnerability privately, what data the application sends to third parties, and the current code-signing status.

TQLTK lưu thông tin đăng nhập và dữ liệu nhạy cảm. Tài liệu này nêu các phiên bản được xem xét bảo mật, cách báo cáo lỗ hổng riêng tư, dữ liệu ứng dụng gửi cho bên thứ ba và trạng thái ký số hiện tại.

## Supported versions | Phiên bản được hỗ trợ

| Version | Security updates |
| --- | --- |
| Latest published release | Reviewed for security reports; fixes are provided at the maintainer's discretion. |
| Current `main` branch | Reviewed while it is actively maintained. |
| Older releases | No security fixes are promised. Upgrade to the latest release when practical. |

| Phiên bản | Cập nhật bảo mật |
| --- | --- |
| Bản phát hành mới nhất | Được xem xét khi có báo cáo bảo mật; việc phát hành bản sửa do người duy trì quyết định. |
| Nhánh `main` hiện tại | Được xem xét trong thời gian còn được duy trì. |
| Bản cũ hơn | Không cam kết có bản sửa bảo mật. Khi có thể, hãy cập nhật lên bản mới nhất. |

## Reporting a vulnerability | Báo cáo lỗ hổng

Please do not publish exploitable details, credentials, recovery codes, vault files, or personal data in a public issue. Use [GitHub's private vulnerability reporting page](https://github.com/Namtudifreefire/TQLTK/security/advisories/new) when it is available. If private reporting is unavailable, contact the repository owner through the [Namtudifreefire GitHub profile](https://github.com/Namtudifreefire) and ask for a private reporting channel.

Include the affected version, Windows version, a concise description of the impact, and reproducible steps that do not expose real account data. Do not attach real vaults, credentials, recovery codes, or OAuth tokens.

Vui lòng không đăng công khai cách khai thác, thông tin đăng nhập, mã khôi phục, file vault hoặc dữ liệu cá nhân. Hãy dùng [trang báo cáo lỗ hổng riêng tư của GitHub](https://github.com/Namtudifreefire/TQLTK/security/advisories/new) nếu tính năng này khả dụng. Nếu không, liên hệ chủ repo qua [hồ sơ GitHub Namtudifreefire](https://github.com/Namtudifreefire) để xin kênh báo cáo riêng.

Báo cáo nên ghi phiên bản bị ảnh hưởng, phiên bản Windows, tác động ngắn gọn và các bước tái hiện không làm lộ dữ liệu thật. Không đính kèm vault, mật khẩu, mã khôi phục hoặc OAuth token thật.

## Data and network behavior | Dữ liệu và kết nối mạng

- TQLTK has no developer telemetry or analytics. Vault data is stored locally in the current Windows user's application-data folder.
- Google Drive is contacted when the user invokes a Drive backup, restore, or sharing action. Google processes data under its [Privacy Policy](https://policies.google.com/privacy).
- The optional breached-password check contacts Have I Been Pwned and sends the first five hexadecimal characters of the password's SHA-1 hash, not the password. See [Have I Been Pwned's privacy information](https://haveibeenpwned.com/Privacy).
- Windows DPAPI protects local encryption keys for the current Windows account. It does not protect against malware running as that user. TQLTK has not received an independent security audit.

- TQLTK không gửi telemetry hoặc analytics cho nhà phát triển. Dữ liệu vault được lưu cục bộ trong thư mục dữ liệu ứng dụng của tài khoản Windows hiện tại.
- Google Drive chỉ được kết nối khi người dùng chủ động dùng chức năng sao lưu, khôi phục hoặc chia sẻ qua Drive. Google xử lý dữ liệu theo [Chính sách quyền riêng tư](https://policies.google.com/privacy).
- Tính năng tùy chọn kiểm tra mật khẩu bị lộ kết nối Have I Been Pwned và chỉ gửi năm ký tự thập lục phân đầu tiên của SHA-1 hash mật khẩu, không gửi mật khẩu. Xem [thông tin quyền riêng tư của Have I Been Pwned](https://haveibeenpwned.com/Privacy).
- Windows DPAPI bảo vệ khóa mã hóa cục bộ theo tài khoản Windows hiện tại. Cơ chế này không bảo vệ trước malware chạy bằng tài khoản đó. TQLTK chưa được kiểm toán bảo mật độc lập.

For the detailed storage and backup limitations, see the [README security notes](README.md#security-notes).

Đọc thêm giới hạn lưu trữ và sao lưu trong [lưu ý bảo mật của README](README.md#lưu-ý-bảo-mật).

## Code-signing policy | Chính sách ký số

**Status: TQLTK has not been accepted into the SignPath Foundation program. No release is represented as signed by SignPath.** This policy is preparatory and does not imply approval, a certificate, or a signed release.

**Trạng thái: TQLTK chưa được chương trình SignPath Foundation chấp thuận. Không có bản phát hành nào được xác nhận là do SignPath ký.** Chính sách này chỉ chuẩn bị hồ sơ, không có nghĩa dự án đã được duyệt, cấp chứng chỉ hoặc có bản phát hành đã ký.

If accepted and signing is enabled, the project will publish the required attribution on its homepage and download pages: **“Free code signing provided by SignPath.io, certificate by SignPath Foundation.”** Until then, check each release's own asset information and do not assume that a download has a publisher signature.

Nếu được chấp thuận và bật ký số, dự án sẽ đăng dòng ghi công bắt buộc trên trang chủ và trang tải xuống: **“Free code signing provided by SignPath.io, certificate by SignPath Foundation.”** Trước thời điểm đó, hãy kiểm tra thông tin của từng tệp phát hành; không mặc định tệp tải xuống có chữ ký nhà phát hành.

The repository owner and project maintainer is [Namtudifreefire](https://github.com/Namtudifreefire). The maintainer authors and approves releases. Contributions from others must be reviewed by the maintainer before merge. No SignPath signing workflow or signing approver has been established yet.

Chủ repo và người duy trì dự án là [Namtudifreefire](https://github.com/Namtudifreefire). Người duy trì viết và duyệt bản phát hành. Đóng góp từ người khác phải được người duy trì xem xét trước khi gộp. Dự án chưa thiết lập quy trình ký SignPath hoặc người duyệt ký số.
