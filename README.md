# TQLTK — Personal Account Manager for Windows

[![Latest release](https://img.shields.io/github/v/release/Namtudifreefire/TQLTK?include_prereleases&label=release&logo=github)](https://github.com/Namtudifreefire/TQLTK/releases)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2B%20x64-0078D4)

TQLTK is a local account and password manager for Windows. It keeps your logins in an encrypted vault on your own PC, supports two-factor sign-in and Windows Hello for sensitive actions, and can optionally back up to **your own** Google Drive. It is written in C# (WPF, .NET) and shipped as a single portable `.exe`: no installer, no background service, no server run by the author.

> The application interface is currently **Vietnamese only**. A Vietnamese summary is at the bottom of this page. / Giao diện ứng dụng hiện chỉ có tiếng Việt; phần tiếng Việt nằm ở cuối trang.

TQLTK is a personal project maintained by one developer. It has not been independently security-audited, so please read [Security notes](#security-notes) before storing anything important.

## Features

- **Vault**: save service, username, password and notes; search; built-in random password generator.
- **Encryption**: the vault is encrypted with AES-256-GCM. The key is protected with Windows DPAPI (bound to your Windows account).
- **Two-factor sign-in (TOTP)**: works with Google Authenticator, Microsoft Authenticator, Authy and similar apps, with 8 one-time recovery codes.
- **Windows Hello** *(beta)*: optional sign-in, and confirmation before revealing or copying a password and before exporting CSV (falls back to the 2FA code).
- **Auto-lock** after 5 minutes of inactivity; a revealed password hides itself again after 30 seconds.
- **Import / export**: CSV, and an encrypted `.vault` file.
- **Google Drive backup and restore** *(beta)*: encrypted backups in a `TQLTK Backup` folder on your own Drive. An optional **backup passphrase** lets you restore on another PC.
- **Multiple users on one PC** with an admin role and an activity log.
- **Personal files** tab and light / dark / automatic theme.

## Requirements

- Windows 10 (version 1809, build 17763) or later, 64-bit.
- Nothing else to install: the release is self-contained, which is why the file is about 190 MB.
- Windows Hello features need Windows Hello (PIN, fingerprint or face) to be set up on the PC.

## Install

**Download:** get `PersonalInfoManager.exe` from the [Releases page](https://github.com/Namtudifreefire/TQLTK/releases) (choose the newest version) and run it. To uninstall, delete the file; your data stays in `%AppData%\PersonalInfoManager` until you remove it.

**winget:** a package submission is pending review in the winget community repository. Once it is approved you will be able to run:

```powershell
winget install TQLTK
```

Until then, `winget install TQLTK` will not find the package; use the download above.

## Security notes

- **Where data is kept:** `%AppData%\PersonalInfoManager` (vault, users, 2FA settings, activity logs) and `HKCU\Software\TQLTK` (window and theme settings).
- **Network:** TQLTK has no telemetry or analytics. The only network use is Google Drive sync, and only if you turn it on. It requests the `drive.file` scope, so it can see only files it created itself.
- **Key storage:** the vault key is tied to your Windows account through DPAPI. Anyone who can run programs as your Windows user can read your data, so this does not protect against malware running under your account.
- **Personal files** are stored unencrypted on disk.
- **Backup passphrase:** if you set one, backups are encrypted with a key derived from it (PBKDF2-SHA256, 600,000 iterations, then AES-256-GCM). If you lose it, there is no way to recover those backups on another PC.
- **Not audited:** no independent review has been done. Do not rely on it as your only copy of important credentials.

## Build from source

Requires the .NET 10 SDK on Windows.

```powershell
cd Nguon
dotnet build
```

`Nguon\publish.bat` builds the single-file release.

Google Drive features need your own OAuth client. In Google Cloud Console create an OAuth client ID of type **Desktop app**, save the JSON as `credentials.json` next to `PersonalInfoManager.csproj`, then build. The file is embedded at build time and is git-ignored, so it is not part of this repository. Without it everything else works and Drive sync shows a clear error message.

## Release notes

See the [Releases page](https://github.com/Namtudifreefire/TQLTK/releases) for the full list. Highlights of v3.6.2:

- Restore data from Google Drive.
- Backup passphrase, so backups can be restored on another PC.
- Backups and Google sign-in are separated per app user.

---

## Tiếng Việt

**TQLTK** là trình quản lý tài khoản và mật khẩu chạy trên máy Windows của bạn. Dữ liệu được lưu trong một kho (vault) mã hóa AES-256-GCM, có xác thực 2 bước (Authenticator), dùng Windows Hello để xác nhận các thao tác nhạy cảm, và có thể sao lưu lên Google Drive **của chính bạn**. Ứng dụng viết bằng C# (WPF, .NET), là một file `.exe` chạy trực tiếp, không cần cài đặt và không có máy chủ nào của tác giả.

**Tính năng:** kho tài khoản kèm tìm kiếm và tạo mật khẩu ngẫu nhiên; xác thực 2 bước kèm 8 mã khôi phục; Windows Hello (BETA); tự khóa sau 5 phút không dùng, mật khẩu đang hiện tự ẩn sau 30 giây; nhập/xuất CSV và file `.vault` mã hóa; sao lưu và khôi phục Google Drive (BETA) với cụm mật khẩu sao lưu để mở được trên máy khác; nhiều người dùng trên một máy, có quyền admin và nhật ký hoạt động; tab File cá nhân; giao diện sáng/tối.

**Cài đặt:** tải `PersonalInfoManager.exe` ở trang [Releases](https://github.com/Namtudifreefire/TQLTK/releases) rồi chạy. Cần Windows 10 (1809) trở lên, 64-bit. Gói winget đang chờ được duyệt; khi được duyệt, bạn dùng `winget install TQLTK`.

**Lưu ý bảo mật:**
- Dữ liệu nằm ở `%AppData%\PersonalInfoManager`. Khóa của kho gắn với tài khoản Windows qua DPAPI, nên không chống được phần mềm độc hại đang chạy dưới tài khoản của bạn.
- File cá nhân được lưu trên đĩa **không mã hóa**.
- Quên cụm mật khẩu sao lưu thì không khôi phục được các bản sao lưu đó trên máy khác.
- Dự án cá nhân do một người duy trì, chưa được kiểm toán bảo mật độc lập. Đừng dùng nó làm nơi lưu duy nhất cho thông tin quan trọng.
- Tính năng BETA (Windows Hello, Google Drive) mới được thử trên số ít máy, có thể gặp lỗi.

**Tự build:** cài .NET 10 SDK, chạy `dotnet build` trong thư mục `Nguon`. Tính năng Google Drive cần file `credentials.json` do bạn tự tạo (OAuth client kiểu Desktop app, đặt cạnh file `.csproj`); file này không nằm trong repo.