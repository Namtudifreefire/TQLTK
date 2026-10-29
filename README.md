# TQLTK — Personal Account Manager for Windows

[![Latest release](https://img.shields.io/github/v/release/Namtudifreefire/TQLTK?include_prereleases&label=release&logo=github)](https://github.com/Namtudifreefire/TQLTK/releases)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2B%20x64-0078D4)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

TQLTK is a local account and password manager for Windows. It stores logins in an encrypted vault on your PC, supports TOTP and optional Windows Hello authentication, and can back up to your own Google Drive. It is written in C# with WPF and .NET and is distributed as a portable, self-contained executable.

> The latest published build is **v3.6.5 (BETA)**. Download [PersonalInfoManager-3.6.5-win-x64.zip](https://github.com/Namtudifreefire/TQLTK/releases/download/v3.6.5/PersonalInfoManager-3.6.5-win-x64.zip) or browse the [Releases page](https://github.com/Namtudifreefire/TQLTK/releases).
>
> The application interface is currently **Vietnamese only**. A Vietnamese summary is at the bottom of this page.

TQLTK is a personal project maintained by one developer. It has not been independently security-audited. Read the [Security notes](#security-notes) before storing important credentials.

## Features

- **Vault:** save and search service names, usernames, passwords and notes; generate random passwords.
- **Encryption:** AES-256-GCM vault encryption, with the key protected by Windows DPAPI and tied to your Windows account.
- **Two-factor sign-in:** TOTP codes from authenticator apps and eight one-time recovery codes.
- **Windows Hello** *(beta):* optional sign-in and confirmation for sensitive actions. When configured, Windows Hello can be used as an alternative to TOTP.
- **Automatic protection:** locks after five minutes of inactivity and hides a revealed password after 30 seconds.
- **Import and export:** CSV and encrypted `.vault` files.
- **Google Drive backup and restore** *(beta):* encrypted backups in a `TQLTK Backup` folder on your own Drive. A backup passphrase can be used to restore on another PC.
- **Multiple users:** separate accounts on one PC, an admin role and an activity log.
- **Personal files** tab and light, dark or automatic themes.

## Requirements

- Windows 10 version 1809 (build 17763) or later, 64-bit.
- No separate .NET installation is needed for the published self-contained app. The extracted executable is about 190 MB; the ZIP download is smaller.
- Windows Hello needs a PIN, fingerprint or face sign-in configured in Windows.

## Install

**Manual download:** download [PersonalInfoManager-3.6.5-win-x64.zip](https://github.com/Namtudifreefire/TQLTK/releases/download/v3.6.5/PersonalInfoManager-3.6.5-win-x64.zip) from the [Releases page](https://github.com/Namtudifreefire/TQLTK/releases), extract it, then run `PersonalInfoManager.exe`. To remove the app, delete the extracted folder. Your data remains in `%AppData%\PersonalInfoManager` unless you remove it separately.

**WinGet:** the open initial package submission [PR #463698](https://github.com/microsoft/winget-pkgs/pull/463698) targets v3.6.4 and has not been merged, so it does not yet provide v3.6.5. After the package is accepted and updated to the current release, install it with:

```powershell
winget install --id Namtudifreefire.TQLTK --exact
```

Until then, use the download above.

## Security notes

- **Local data:** vault, users, 2FA settings and activity logs are stored in `%AppData%\PersonalInfoManager`; window and theme settings are stored in `HKCU\Software\TQLTK`.
- **Network:** there is no telemetry or analytics. Google Drive sync is the only network feature and runs only when you use it. The app requests the `drive.file` scope.
- **Windows account:** DPAPI ties the vault key to your Windows account. This does not protect data from malware running as your Windows user.
- **Personal files:** files in the Personal Files tab are stored unencrypted.
- **Legacy CBC data:** v3.6.5 rejects old CBC-encrypted vault, note and backup data because that format does not verify integrity. Make and verify a separate backup before upgrading if you may have data in that format.
- **Backup passphrase:** backups use a key derived with PBKDF2-SHA256 (600,000 iterations) and AES-256-GCM. A forgotten passphrase cannot be recovered for restoring on another PC.
- **No independent audit:** do not use TQLTK as the only copy of important credentials.

## Build from source

Requires the .NET 10 SDK on Windows.

```powershell
cd Nguon
dotnet build
```

`Nguon\publish.bat` builds the single-file release.

Google Drive features need your own OAuth client. In Google Cloud Console, create an OAuth client ID of type **Desktop app**, save it as `credentials.json` next to `PersonalInfoManager.csproj`, then build. The file is embedded at build time and is git-ignored, so it is not part of this repository. Without it, the rest of the app works but Drive sync is unavailable.

## Release notes

See the [Releases page](https://github.com/Namtudifreefire/TQLTK/releases) for downloadable builds and the full release history.

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

**TQLTK** là ứng dụng quản lý tài khoản và mật khẩu chạy trên Windows. Dữ liệu tài khoản được lưu trong vault mã hóa trên máy, hỗ trợ mã TOTP và tùy chọn Windows Hello, đồng thời có thể sao lưu lên Google Drive **của chính bạn**. Ứng dụng viết bằng C# (WPF, .NET), phát hành dạng portable và không cần cài .NET riêng.

> Bản mới nhất đã phát hành là **v3.6.5 (BETA)**. Tải [PersonalInfoManager-3.6.5-win-x64.zip](https://github.com/Namtudifreefire/TQLTK/releases/download/v3.6.5/PersonalInfoManager-3.6.5-win-x64.zip) hoặc xem [trang Releases](https://github.com/Namtudifreefire/TQLTK/releases).

Giao diện ứng dụng hiện chỉ có tiếng Việt.

**Tính năng:** lưu và tìm kiếm tài khoản, tạo mật khẩu ngẫu nhiên; mã hóa vault AES-256-GCM với khóa được bảo vệ bằng Windows DPAPI; TOTP kèm 8 mã khôi phục; Windows Hello (BETA, có thể dùng thay TOTP khi được cấu hình); tự khóa sau 5 phút không hoạt động và ẩn mật khẩu đang hiện sau 30 giây; nhập/xuất CSV và file `.vault` mã hóa; sao lưu và khôi phục Google Drive (BETA) bằng cụm mật khẩu sao lưu; nhiều người dùng và quyền admin; tab File cá nhân; giao diện sáng/tối/tự động.

**Cài đặt:** tải file [PersonalInfoManager-3.6.5-win-x64.zip](https://github.com/Namtudifreefire/TQLTK/releases/download/v3.6.5/PersonalInfoManager-3.6.5-win-x64.zip) ở trang [Releases](https://github.com/Namtudifreefire/TQLTK/releases), giải nén rồi chạy `PersonalInfoManager.exe`. File chạy sau khi giải nén khoảng 190 MB. Xóa thư mục đã giải nén để gỡ ứng dụng; dữ liệu trong `%AppData%\PersonalInfoManager` vẫn được giữ lại.

**WinGet:** PR tạo gói ban đầu [#463698](https://github.com/microsoft/winget-pkgs/pull/463698) đang mở và nhắm tới v3.6.4, nên chưa có v3.6.5. Sau khi gói được chấp nhận và cập nhật lên bản hiện tại, dùng:

```powershell
winget install --id Namtudifreefire.TQLTK --exact
```

**Lưu ý bảo mật:**

- File trong tab File cá nhân được lưu trên đĩa **không mã hóa**.
- Khóa vault gắn với tài khoản Windows qua DPAPI, nên không chống được phần mềm độc hại chạy dưới tài khoản của bạn.
- v3.6.5 chặn mở vault, ghi chú và bản sao lưu cũ mã hóa CBC vì định dạng đó không kiểm tra được tính toàn vẹn. Nếu có dữ liệu ở định dạng này, hãy tạo và kiểm tra một bản sao riêng trước khi cập nhật.
- Quên cụm mật khẩu sao lưu thì không thể khôi phục bản sao lưu đó trên máy khác.
- Dự án chưa được kiểm toán bảo mật độc lập; không nên dùng làm bản lưu duy nhất cho thông tin đăng nhập quan trọng.
- Tính năng Windows Hello và Google Drive đang ở giai đoạn BETA.

### v3.6.5 (BETA)

- Tăng cường kiểm tra tài khoản và phiên đăng nhập trước các thao tác nhạy cảm với vault, sao lưu và Google Drive.
- Chống nội dung CSV có thể bị bảng tính hiểu thành công thức.
- Làm sạch dữ liệu nhật ký để ngăn chèn dòng giả mạo.
- Chặn dữ liệu CBC cũ không thể xác minh toàn vẹn.
- Bổ sung giấy phép MIT vào dự án và bộ file phát hành.

**Tự build:** cài .NET 10 SDK, chạy `dotnet build` trong thư mục `Nguon`. Tính năng Google Drive cần file `credentials.json` do bạn tự tạo (OAuth client kiểu Desktop app, đặt cạnh file `.csproj`); file này không nằm trong repo.

**Giấy phép:** MIT. Xem [LICENSE](LICENSE). Bản quyền © 2026 Namtudifreefire.
