# TQLTK — Personal Account Manager for Windows

[![Latest release](https://img.shields.io/github/v/release/Namtudifreefire/TQLTK?include_prereleases&label=release&logo=github)](https://github.com/Namtudifreefire/TQLTK/releases)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2B%20x64-0078D4)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

TQLTK is a local account and password manager for Windows. It stores logins in an encrypted vault on your PC, supports per-account TOTP and optional Windows Hello authentication, and can back up to your own Google Drive. The interface is available in Vietnamese and English. TQLTK is written in C# with WPF and .NET and is distributed as a portable, self-contained executable.

> The latest published release is **v3.6.6 (BETA)**. See its [release notes](https://github.com/Namtudifreefire/TQLTK/releases/tag/v3.6.6). This release currently has no installable binary attached. The latest ready-to-run ZIP is [PersonalInfoManager-3.6.5-win-x64.zip](https://github.com/Namtudifreefire/TQLTK/releases/download/v3.6.5/PersonalInfoManager-3.6.5-win-x64.zip).

> English interface support was introduced in v3.6.6 (BETA); earlier versions are Vietnamese-only.

TQLTK is a personal project maintained by one developer. It has not been independently security-audited. Read the [Security notes](#security-notes) before storing important credentials.

## Features

- **Encrypted vault:** save and search service names, usernames, passwords, website URLs and notes; organize entries with folders, tags and favorites.
- **Custom fields:** store additional account details; sensitive fields are encrypted with the vault.
- **Encryption:** AES-256-GCM vault encryption, with the key protected by Windows DPAPI and tied to your Windows account.
- **Per-account TOTP:** add authenticator codes to saved accounts; set them up by scanning a QR code or entering a secret, and import supported authenticator data including Google Authenticator migration data.
- **Two-factor sign-in:** TOTP codes and eight one-time recovery codes for signing in to TQLTK.
- **Windows Hello** *(beta):* optional sign-in and confirmation for sensitive actions. When configured, Windows Hello can be used as an alternative to TOTP.
- **Password tools:** configurable password length and character sets, ambiguous-character exclusion, passphrases, password history and password-change dates.
- **Password health:** check for breached passwords through Have I Been Pwned. The check sends only the first five characters of the password's SHA-1 hash, never the password itself.
- **Import and export:** CSV support for Chrome, Firefox, KeePass, 1Password, Bitwarden, LastPass and generic CSV; encrypted vault backups; TQLTK CSV preserves account metadata, TOTP settings and custom fields.
- **Trash:** deleted accounts and notes can be restored for 30 days or permanently removed.
- **Automatic protection:** choose a 1, 5, 10 or 15 minute auto-lock interval; a revealed password is hidden after 30 seconds.
- **Backup and restore previews:** review vault backups or personal files before restoring them.
- **Google Drive backup and restore** *(beta):* encrypted backups in a TQLTK Backup folder on your own Drive. A backup passphrase can be used to restore on another PC.
- **Multiple users:** separate accounts on one PC, an admin role and an activity log.
- **Personal files** tab and light, dark or automatic themes.
- **Interface languages:** switch between Vietnamese and English from the sign-in window or Settings; the preference is saved locally.

## Requirements

- Windows 10 version 1809 (build 17763) or later, 64-bit.
- No separate .NET installation is needed for the published self-contained app. The extracted executable is about 190 MB; the ZIP download is smaller.
- Windows Hello needs a PIN, fingerprint or face sign-in configured in Windows.

## Install

**Manual download:** v3.6.6 (BETA) is published without an installable binary asset. For a ready-to-run app, download [PersonalInfoManager-3.6.5-win-x64.zip](https://github.com/Namtudifreefire/TQLTK/releases/download/v3.6.5/PersonalInfoManager-3.6.5-win-x64.zip) from the [Releases page](https://github.com/Namtudifreefire/TQLTK/releases), extract it, then run PersonalInfoManager.exe. To remove the app, delete the extracted folder. Your data remains in %AppData%\PersonalInfoManager unless you remove it separately.

**WinGet:** the initial package submission [PR #463698](https://github.com/microsoft/winget-pkgs/pull/463698) is still open and targets v3.6.4, so it does not provide the v3.6.5 or v3.6.6 builds. After the package is accepted and updated, install it with:

    winget install --id Namtudifreefire.TQLTK --exact

Until then, use the ZIP download above.

## Security notes

- **Local data:** vault, users, 2FA settings and activity logs are stored in %AppData%\PersonalInfoManager; window and theme settings are stored in HKCU\Software\TQLTK.
- **Network:** there is no telemetry or analytics. Google Drive requests run when you use Drive sync. The optional breached-password check contacts Have I Been Pwned and sends only the first five characters of the password's SHA-1 hash, not the password itself. The app requests the drive.file scope for Google Drive.
- **Windows account:** DPAPI ties the vault key to your Windows account. This does not protect data from malware running as your Windows user.
- **Personal files:** files are encrypted with AES-GCM and their key is protected by Windows DPAPI for the current Windows account. Existing plaintext files are migrated atomically when the file store is opened. Before moving to another Windows account or reinstalling Windows, create a file backup protected by your backup passphrase; the local DPAPI key cannot be transferred by copying the storage folder.
- **Legacy CBC data:** v3.6.5 rejects old CBC-encrypted vault, note and backup data because that format does not verify integrity. Make and verify a separate backup before upgrading if you may have data in that format.
- **Backup passphrase:** backups use a key derived with PBKDF2-SHA256 (600,000 iterations) and AES-256-GCM. A forgotten passphrase cannot be recovered for restoring on another PC.
- **No independent audit:** do not use TQLTK as the only copy of important credentials.

## Build from source

Requires the .NET 10 SDK on Windows.

    cd Nguon
    dotnet build

`Nguon\publish.bat` builds the single-file EXE, packages the EXE and MIT `LICENSE` at the ZIP root, verifies the ZIP contents, and writes `SHA256SUMS.txt`. To sign the EXE, install a trusted code-signing certificate in the current Windows user's Personal certificate store and set `TQLTK_SIGNING_CERT_THUMBPRINT` to its thumbprint, or pass `-CertificateThumbprint` to `release-artifacts.ps1`. Pass `-RequireSignature` to fail a release build if no certificate is configured. Builds without a certificate are unsigned; checksums do not prove publisher identity.

Google Drive features need your own OAuth client. In Google Cloud Console, create an OAuth client ID of type **Desktop app**, save it as credentials.json next to PersonalInfoManager.csproj, then build. The file is embedded at build time and is git-ignored, so it is not part of this repository. Without it, the rest of the app works but Drive sync is unavailable.

## Release notes

See the [Releases page](https://github.com/Namtudifreefire/TQLTK/releases) for downloadable builds and the full release history.

### v3.6.7 (in development)

- Verify backup manifests, entry counts and total sizes before a restore preview succeeds; new encrypted backups are verified before replacing the destination.
- Use durable temporary-file writes for vaults, notes, recovery settings, CSV exports and backups so an interrupted write keeps the prior file intact.
- Increase temporary sign-in lockouts from 5 to 10, 20, 40 and then 60 minutes after repeated failed-attempt groups; a successful sign-in or password reset clears the escalation. Password hashes already use PBKDF2-HMAC-SHA256 with 600,000 iterations and rehash older entries after sign-in.
- Show the linked Google Drive account and allow removing this device's locally stored token without deleting Drive backups. Removing the local token does not revoke Google account access.
- Expand the security overview with account-level findings for weak, reused, old, unknown-age and breached passwords, with an action to open the affected account. Breach results are marked stale after the vault changes.
- Show recovery-code count, backup-passphrase status and the Windows-account dependency of personal-file encryption. Clarify that changing the TQLTK sign-in password does not rotate the vault encryption key, and guide users to make a passphrase-protected backup before moving Windows accounts or reinstalling Windows.
- Add a repeatable release script that creates a root-level EXE + LICENSE ZIP and SHA-256 file, and signs/verifies the EXE when a trusted code-signing certificate is configured.
- Store account attachments inside the encrypted vault and include them in vault backups. Each file is limited to 10 MB and each account to 25 MB total.
- Add a bounded metadata edit history with preview and undo for service name, username, website URL, folder, tags, favorites, notes and custom fields. Passwords remain in the separate password history.
- Add CSV duplicate handling to skip, overwrite or create a copy, plus undo for the latest import batch. Undo is refused if any affected account changed afterward.
- Add bulk account actions for folders, tags, Trash and exporting selected rows.
- Warn when a saved website URL uses HTTP, a direct IP, Punycode, mixed scripts or resembles a short list of popular domains. This is a heuristic and does not inspect browser tabs or cover every domain.
- Add Google Drive backup conflict checks. Concurrent changes stop automatic upload; users can review the remote backup or explicitly upload a separate version. This is versioned backup with conflict alerts, not live sync or automatic record merging.
- Add expiring, encrypted account-share packages for a named Google account. Drive grants read access for 1, 7 or 30 days; the decryption key must be sent separately. Drive access can be revoked, but downloaded packages and data already decrypted cannot be recalled. The local expiry check uses the recipient device's clock and is not tamper-proof.
- Keep the already available reused-password finding and its action to open the affected account in the security overview.

### v3.6.6 (BETA)

- Add per-account TOTP, with imports from QR codes, otpauth:// links and Google Authenticator migration data.
- Add website URLs, folders, tags, favorites and custom fields; sensitive custom fields are encrypted with the vault.
- Expand CSV support to Chrome, Firefox, KeePass, 1Password, Bitwarden, LastPass and generic CSV; TQLTK CSV preserves account metadata, TOTP settings and custom fields.
- Add a 30-day Trash with restore and permanent-delete options.
- Add password history and password-change dates.
- Add configurable auto-lock intervals: 1, 5, 10 or 15 minutes.
- Upgrade the password generator with configurable length and character sets, ambiguous-character exclusion and passphrases.
- Add a Have I Been Pwned compromised-password check that sends only the first five characters of the password's SHA-1 hash.
- Add previews before restoring vault backups or personal files.
- Add Vietnamese and English interface support, selectable from the sign-in window or Settings.
- Reauthenticate sensitive actions, block vault access for inactive accounts, protect CSV exports against spreadsheet formula injection and sanitize activity-log values.

### v3.6.5 (BETA)

- Strengthen account and session checks around sensitive vault, backup and Google Drive operations.
- Protect CSV exports against spreadsheet formula injection.
- Sanitize activity-log values to prevent forged log entries.
- Reject legacy CBC-encrypted vault, note and backup data that cannot be integrity-checked; see the compatibility note above.
- Add the MIT license to the project and published app files.

### v3.6.4

- Restore the vault from Google Drive and use a backup passphrase to restore it on another PC.
- Separate backup data and Google sign-in for each app user.
- Improve account-data isolation and protection against reuse of 2FA and recovery codes across app instances.

## License

TQLTK is distributed under the [MIT License](LICENSE). Copyright © 2026 Namtudifreefire.

---

## Tiếng Việt

**TQLTK** là ứng dụng quản lý tài khoản và mật khẩu chạy trên Windows. Dữ liệu tài khoản được lưu trong vault mã hóa trên máy; ứng dụng hỗ trợ TOTP cho từng tài khoản, tùy chọn Windows Hello và sao lưu lên Google Drive của chính bạn. Giao diện có tiếng Việt và tiếng Anh. Ứng dụng viết bằng C# (WPF, .NET), phát hành dạng portable và không cần cài .NET riêng.

> Bản phát hành mới nhất là **v3.6.6 (BETA)**. Xem [ghi chú phát hành](https://github.com/Namtudifreefire/TQLTK/releases/tag/v3.6.6). Hiện bản này chưa có file cài đặt đính kèm. ZIP mới nhất có thể chạy ngay là [PersonalInfoManager-3.6.5-win-x64.zip](https://github.com/Namtudifreefire/TQLTK/releases/download/v3.6.5/PersonalInfoManager-3.6.5-win-x64.zip).

> Giao diện tiếng Anh được giới thiệu từ v3.6.6 (BETA); các phiên bản trước chỉ có tiếng Việt.

TQLTK là dự án cá nhân do một nhà phát triển duy trì và chưa được kiểm toán bảo mật độc lập. Hãy đọc [các lưu ý bảo mật](#security-notes) trước khi lưu thông tin đăng nhập quan trọng.

## Tính năng

- **Vault mã hóa:** lưu và tìm kiếm dịch vụ, tên đăng nhập, mật khẩu, URL website và ghi chú; sắp xếp bằng thư mục, nhãn và mục yêu thích.
- **Trường tùy chỉnh:** lưu thêm thông tin cho từng tài khoản; trường nhạy cảm được mã hóa cùng vault.
- **Mã hóa:** vault dùng AES-256-GCM; khóa được bảo vệ bằng Windows DPAPI và gắn với tài khoản Windows.
- **TOTP cho từng tài khoản:** thêm mã xác thực vào mục đã lưu; thiết lập bằng cách quét QR hoặc nhập secret, đồng thời nhập dữ liệu từ Google Authenticator và các định dạng được hỗ trợ.
- **Đăng nhập hai bước:** dùng TOTP và tám mã khôi phục dùng một lần để đăng nhập TQLTK.
- **Windows Hello** *(BETA):* tùy chọn đăng nhập và xác nhận thao tác nhạy cảm; khi được cấu hình, Windows Hello có thể dùng thay TOTP.
- **Công cụ mật khẩu:** tùy chỉnh độ dài, nhóm ký tự, loại trừ ký tự dễ nhầm, tạo cụm mật khẩu, xem lịch sử và ngày đổi mật khẩu.
- **Kiểm tra an toàn mật khẩu:** kiểm tra mật khẩu bị lộ qua Have I Been Pwned. Ứng dụng chỉ gửi năm ký tự đầu của SHA-1 hash mật khẩu, không gửi mật khẩu nguyên văn.
- **Nhập và xuất:** hỗ trợ CSV từ Chrome, Firefox, KeePass, 1Password, Bitwarden, LastPass và CSV chung; bản sao lưu vault được mã hóa. CSV TQLTK giữ metadata tài khoản, TOTP và trường tùy chỉnh.
- **Thùng rác:** khôi phục tài khoản hoặc ghi chú đã xóa trong 30 ngày, hoặc xóa vĩnh viễn.
- **Tự động bảo vệ:** chọn thời gian tự khóa 1, 5, 10 hoặc 15 phút; mật khẩu đang hiển thị sẽ tự ẩn sau 30 giây.
- **Xem trước bản khôi phục:** xem nội dung vault backup hoặc file cá nhân trước khi khôi phục.
- **Sao lưu và khôi phục Google Drive** *(BETA):* lưu bản sao lưu đã mã hóa vào thư mục TQLTK Backup trên Drive của bạn. Có thể dùng cụm mật khẩu sao lưu để khôi phục trên máy khác.
- **Nhiều người dùng:** tách tài khoản trên cùng máy, có quyền admin và nhật ký hoạt động.
- Tab **File cá nhân** và giao diện sáng, tối hoặc tự đổi theo thời gian.
- **Ngôn ngữ giao diện:** đổi giữa tiếng Việt và tiếng Anh ở màn hình đăng nhập hoặc Cài đặt; lựa chọn được lưu trên máy.

## Cài đặt

**Tải thủ công:** v3.6.6 (BETA) đã được đăng nhưng hiện chưa có file cài đặt đính kèm. Để dùng ứng dụng ngay, tải [PersonalInfoManager-3.6.5-win-x64.zip](https://github.com/Namtudifreefire/TQLTK/releases/download/v3.6.5/PersonalInfoManager-3.6.5-win-x64.zip) ở [trang Releases](https://github.com/Namtudifreefire/TQLTK/releases), giải nén rồi chạy PersonalInfoManager.exe. Xóa thư mục đã giải nén để gỡ ứng dụng; dữ liệu trong %AppData%\PersonalInfoManager vẫn được giữ lại.

**WinGet:** PR tạo gói ban đầu [#463698](https://github.com/microsoft/winget-pkgs/pull/463698) vẫn đang mở và nhắm tới v3.6.4, nên chưa cung cấp v3.6.5 hoặc v3.6.6. Sau khi gói được chấp nhận và cập nhật, cài bằng lệnh:

    winget install --id Namtudifreefire.TQLTK --exact

Trong thời gian chờ, hãy dùng file ZIP ở trên.

## Lưu ý bảo mật

- **Dữ liệu cục bộ:** vault, tài khoản người dùng, cài đặt 2FA và nhật ký hoạt động nằm trong %AppData%\PersonalInfoManager; cài đặt giao diện nằm trong HKCU\Software\TQLTK.
- **Kết nối mạng:** ứng dụng không có telemetry hoặc analytics. Google Drive chỉ kết nối khi bạn dùng đồng bộ Drive. Kiểm tra mật khẩu bị lộ là tùy chọn và chỉ gửi năm ký tự đầu của SHA-1 hash tới Have I Been Pwned, không gửi mật khẩu. Ứng dụng yêu cầu quyền drive.file cho Google Drive.
- **Tài khoản Windows:** DPAPI gắn khóa vault với tài khoản Windows. Cơ chế này không bảo vệ dữ liệu trước malware chạy bằng chính tài khoản Windows của bạn.
- **File cá nhân:** file được mã hóa AES-GCM; khóa được bảo vệ bằng Windows DPAPI theo tài khoản Windows hiện tại. File cũ chưa mã hóa sẽ được chuyển đổi an toàn qua file tạm khi mở kho file. Trước khi đổi tài khoản Windows hoặc cài lại Windows, hãy tạo bản sao lưu có bảo vệ bằng cụm mật khẩu sao lưu; khóa DPAPI cục bộ không thể chuyển bằng cách chép thư mục dữ liệu.
- **Dữ liệu CBC cũ:** v3.6.5 chặn mở vault, ghi chú và bản sao lưu cũ mã hóa CBC vì định dạng này không kiểm tra được tính toàn vẹn. Nếu có dữ liệu loại này, hãy tạo và kiểm tra một bản sao riêng trước khi cập nhật.
- **Cụm mật khẩu sao lưu:** khóa backup được dẫn xuất bằng PBKDF2-SHA256 (600.000 vòng) và mã hóa AES-256-GCM. Nếu quên cụm mật khẩu, không thể khôi phục bản sao lưu trên máy khác.
- **Chưa kiểm toán độc lập:** không nên dùng TQLTK làm bản lưu duy nhất cho thông tin đăng nhập quan trọng.

## Ghi chú phát hành

Xem [trang Releases](https://github.com/Namtudifreefire/TQLTK/releases) để tải bản phát hành và đọc lịch sử đầy đủ.

### v3.6.7 (đang phát triển)

- Kiểm tra manifest, số lượng và tổng dung lượng trước khi chấp nhận bước xem trước khôi phục; bản sao lưu mới được xác minh trước khi thay file đích.
- Ghi vault, ghi chú, thiết lập khôi phục, CSV và backup qua file tạm bền vững để lần ghi bị gián đoạn vẫn giữ file cũ.
- Tăng thời gian khóa đăng nhập tạm thời theo từng nhóm nhập sai: 5, 10, 20, 40 rồi 60 phút; đăng nhập thành công hoặc đặt lại mật khẩu sẽ xóa mức tăng. Hash mật khẩu đã dùng PBKDF2-HMAC-SHA256 với 600.000 vòng; hash cũ được nâng cấp sau lần đăng nhập thành công.
- Hiển thị tài khoản Google Drive đã liên kết và cho phép xóa token lưu cục bộ trên thiết bị này mà không xóa backup trên Drive. Xóa token cục bộ không thu hồi quyền ứng dụng trong tài khoản Google.
- Mở rộng tổng quan bảo mật với cảnh báo theo từng tài khoản về mật khẩu yếu, dùng lại, lâu chưa đổi, chưa rõ ngày đổi hoặc bị lộ; có nút mở nhanh mục cần xử lý. Kết quả kiểm tra rò rỉ được đánh dấu cũ khi vault thay đổi.
- Hiển thị số mã khôi phục còn lại, trạng thái cụm mật khẩu sao lưu và lưu ý khóa mã hóa file cá nhân gắn với tài khoản Windows. Nêu rõ đổi mật khẩu đăng nhập TQLTK không đổi khóa mã hóa vault; khuyến nghị tạo backup có cụm mật khẩu trước khi đổi tài khoản Windows hoặc cài lại Windows.
- Thêm script phát hành có thể lặp lại: tạo ZIP có EXE và LICENSE ở gốc, tạo SHA-256, đồng thời ký và xác minh EXE khi đã cấu hình chứng thư code-signing đáng tin cậy.
- Lưu tệp đính kèm của tài khoản bên trong vault đã mã hóa và kèm chúng trong bản sao lưu vault. Mỗi tệp tối đa 10 MB, tổng mỗi tài khoản tối đa 25 MB.
- Thêm lịch sử chỉnh sửa có giới hạn, cho xem trước và hoàn tác tên dịch vụ, tên đăng nhập, URL website, thư mục, nhãn, mục yêu thích, ghi chú và trường tùy chỉnh. Mật khẩu vẫn dùng lịch sử riêng.
- Khi nhập CSV, cho chọn bỏ qua, ghi đè hoặc tạo bản sao đối với mục trùng; hỗ trợ hoàn tác lô nhập gần nhất. Ứng dụng từ chối hoàn tác nếu tài khoản liên quan đã thay đổi sau lần nhập.
- Thêm thao tác hàng loạt để đổi thư mục, thay nhãn, chuyển vào thùng rác và xuất các mục đã chọn.
- Cảnh báo URL đã lưu nếu dùng HTTP, địa chỉ IP trực tiếp, Punycode, trộn bảng chữ cái hoặc gần giống một số domain phổ biến. Đây là kiểm tra heuristic; ứng dụng không theo dõi tab trình duyệt và không bao phủ mọi domain.
- Thêm kiểm tra xung đột cho bản sao lưu Google Drive. Khi phát hiện thay đổi đồng thời, ứng dụng dừng tải tự động; người dùng có thể xem bản từ xa hoặc chủ động tải lên thành một phiên bản riêng. Đây là sao lưu có cảnh báo xung đột, chưa phải đồng bộ trực tiếp hay tự gộp bản ghi.
- Thêm gói chia sẻ tài khoản được mã hóa và tự hết hạn, gửi cho một tài khoản Google cụ thể. Drive cấp quyền đọc trong 1, 7 hoặc 30 ngày; khóa giải mã phải gửi qua kênh riêng. Có thể thu hồi quyền Drive, nhưng không thể lấy lại gói đã tải xuống hoặc dữ liệu người nhận đã giải mã. Kiểm tra hết hạn trên máy người nhận dựa vào đồng hồ hệ thống và không chống chỉnh sửa.
- Giữ tính năng đã có trong tổng quan bảo mật: phát hiện mật khẩu dùng lại và mở nhanh tài khoản cần xử lý.

### v3.6.6 (BETA)

- Thêm quản lý TOTP cho từng tài khoản; hỗ trợ nhập từ mã QR, liên kết otpauth:// và dữ liệu chuyển từ Google Authenticator.
- Thêm URL website, thư mục, nhãn, mục yêu thích và trường tùy chỉnh; trường nhạy cảm được mã hóa cùng vault.
- Mở rộng nhập CSV từ Chrome, Firefox, KeePass, 1Password, Bitwarden, LastPass và CSV chung; CSV TQLTK giữ metadata tài khoản, TOTP và trường tùy chỉnh.
- Thêm thùng rác giữ mục đã xóa trong 30 ngày, cho phép khôi phục hoặc xóa vĩnh viễn.
- Thêm lịch sử mật khẩu và ngày đổi mật khẩu.
- Cho phép chọn thời gian tự khóa: 1, 5, 10 hoặc 15 phút.
- Nâng cấp trình tạo mật khẩu với tùy chỉnh độ dài, nhóm ký tự, loại trừ ký tự dễ nhầm và tạo cụm mật khẩu.
- Thêm kiểm tra mật khẩu bị lộ qua Have I Been Pwned; chỉ gửi năm ký tự đầu của SHA-1 hash mật khẩu.
- Thêm bước xem trước trước khi khôi phục vault hoặc file cá nhân từ bản sao lưu.
- Thêm hỗ trợ giao diện tiếng Việt và tiếng Anh; có thể đổi ở màn hình đăng nhập hoặc Cài đặt.
- Yêu cầu xác thực lại cho thao tác nhạy cảm, chặn vault của tài khoản không hoạt động, bảo vệ CSV xuất khỏi spreadsheet formula injection và làm sạch dữ liệu trước khi ghi activity log.

### v3.6.5 (BETA)

- Tăng cường kiểm tra tài khoản và phiên đăng nhập trước thao tác nhạy cảm với vault, backup và Google Drive.
- Chống nội dung CSV có thể bị bảng tính hiểu thành công thức.
- Làm sạch dữ liệu nhật ký để ngăn chèn dòng giả mạo.
- Chặn dữ liệu CBC cũ không thể xác minh toàn vẹn.
- Bổ sung giấy phép MIT vào dự án và bộ file phát hành.

### v3.6.4

- Khôi phục vault từ Google Drive và dùng cụm mật khẩu sao lưu để khôi phục trên máy khác.
- Tách dữ liệu sao lưu và đăng nhập Google theo từng người dùng ứng dụng.
- Cải thiện tách biệt dữ liệu tài khoản và chống dùng lại mã 2FA, mã khôi phục ở nhiều phiên bản ứng dụng.

## Build từ mã nguồn

Cần cài .NET 10 SDK trên Windows.

    cd Nguon
    dotnet build

`Nguon\publish.bat` tạo EXE đơn, ZIP đặt EXE và giấy phép MIT ở thư mục gốc, kiểm tra nội dung ZIP và ghi `SHA256SUMS.txt`. Muốn ký EXE, cài chứng thư code-signing được Windows tin cậy trong kho Personal của tài khoản Windows hiện tại rồi đặt thumbprint vào `TQLTK_SIGNING_CERT_THUMBPRINT` hoặc truyền `-CertificateThumbprint` cho `release-artifacts.ps1`. Dùng `-RequireSignature` để build thất bại nếu chưa cấu hình chứng thư. Nếu không có chứng thư, file phát hành chưa được ký; SHA-256 không xác nhận danh tính nhà phát hành.

Tính năng Google Drive cần OAuth client của bạn. Trong Google Cloud Console, tạo OAuth client ID loại **Desktop app**, lưu thành credentials.json cạnh PersonalInfoManager.csproj rồi build. File được nhúng lúc build và bị git-ignore nên không nằm trong repo. Nếu không có file này, các phần còn lại vẫn hoạt động nhưng không dùng được Drive sync.

## Giấy phép

TQLTK phát hành theo [giấy phép MIT](LICENSE). Bản quyền © 2026 Namtudifreefire.
