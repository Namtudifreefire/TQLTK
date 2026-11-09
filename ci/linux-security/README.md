# Kiểm thử GitLab GL-09

## Phạm vi và cách chạy

Project console `net10.0` độc lập, không tham chiếu project WPF. Job
`gl09-backup-paths` dùng image Linux .NET 10 và runner
`saas-linux-small-amd64`. Không thay đổi các scanner hoặc rules hiện có.

Từ gốc repo, trên Linux:

```sh
cd ci/linux-security
dotnet restore Tqltk.LinuxSecurity.csproj --locked-mode
dotnet build Tqltk.LinuxSecurity.csproj -c Release --no-restore --disable-build-servers
dotnet run --project Tqltk.LinuxSecurity.csproj -c Release --no-build -- --require-linux --junit ../../artifacts/gl09/junit.xml
```

`--require-linux` từ chối chạy trên hệ điều hành khác. Khi phát triển có thể bỏ
cờ này; JUnit ghi rõ hệ điều hành thực thi. Kết quả chạy Windows không chứng minh
đã chạy trên Linux. Exit code khác 0 khi một test thất bại hoặc harness gặp lỗi.
JUnit ghi từng ca, thời gian, failure và skip; không chứa dữ liệu tài khoản thật.

## Mã được kiểm tra

Project liên kết trực tiếp, không sao chép thuật toán từ:

- `Nguon/Service/FileBackupService.cs`: guard `IsUserFileEntry` và API `InspectBackup`.
- `Nguon/Service/BackupCrypto.cs`: tạo/đọc gói TQFB thật với cụm mật khẩu giả.
- `Nguon/Service/AtomicFileService.cs`, `ApplicationDataPath.cs`, `BackupPreviewItem.cs`.

`UnsupportedStorage.cs` chỉ là adapter trong assembly test. Mọi lời gọi vào kho
cục bộ hoặc mã hóa file cục bộ đều tăng bộ đếm và ném lỗi; test cuối yêu cầu bộ
đếm bằng 0. Localization chỉ trả về mã thông báo, không tham gia quyết định kiểm
tra tên mục. Không giả lập việc ghi kho thành công, không thay thế mã hóa backup.
Các hàm lưu cụm mật khẩu bằng DPAPI trong `BackupCrypto` không được gọi: mọi ca
đều truyền cụm mật khẩu trực tiếp và đặt AppData test dưới thư mục tạm riêng.
Build vẫn hiện cảnh báo CA1416 từ những hàm Windows không thực thi này; không
tắt cảnh báo và không xem đó là bằng chứng DPAPI chạy được trên Linux.

## Ma trận mới, tránh lặp test cũ

Đã đối chiếu `Nguon/tests/Program.cs`, `BackupFailureRegression.cs` và `BackupLab.cs`.
Các bài cũ đã kiểm tra backup hỏng/sai mật khẩu, metadata, tương thích, lỗi ghi,
rollback và gián đoạn. Không chép lại các nhóm đó.

GL-09 bổ sung 39 ca thực thi:

- 6 tên file phẳng hợp lệ, gồm Unicode, khoảng trắng, file ẩn, dấu chấm và chuỗi
  percent-encoded được giữ nguyên, không giải mã thành đường dẫn.
- 21 ca guard từ chối: `../`, đường dẫn lồng, tuyệt đối `/`, `C:/`, UNC dùng `/`,
  `.`, `..`, mục thư mục, tên thiết bị Windows, dấu chấm/khoảng trắng cuối và NUL,
  cùng đường dẫn manifest không được xem là file người dùng.
- 1 gói mã hóa hợp lệ làm đối chứng.
- 9 gói mã hóa trộn file hợp lệ với đường dẫn/tên độc hại: chỉ file hợp lệ được
  tính, tổng byte đúng, sentinel và backup nguồn không đổi, không tạo kho AppData.
- 1 gói chỉ có traversal bị từ chối.
- 1 kiểm tra không đi vào adapter kho cục bộ.

Tên ca dùng SHA-256 rút gọn của tên mục để ký tự NUL không làm hỏng XML. Corpus
và kỳ vọng nằm ngay trong `Program.cs`. Chính sách hiện tại **bỏ qua** mục không
an toàn nếu gói còn file hợp lệ và manifest khớp; test không yêu cầu từ chối mọi
gói có chứa một mục xấu.

## SKIPPED và phần nên kiểm tra trên GitHub Actions

Ba mục được ghi `skipped` rõ trong JUnit, không tính PASS:

1. Quy tắc đường dẫn riêng Windows: backslash, UNC `\\server`, drive-relative
   `C:...`, ADS `file:stream`, các ký tự cấm Windows. `Path` trên Linux có ngữ nghĩa
   khác; không sửa guard hoặc giả Windows để lấy kết quả PASS.
2. `RestoreFromFile`/`DrillRestore` ghi kho mã hóa dùng DPAPI.
3. Kiểm thử đường dẫn xuyên suốt `CompleteBackupService` và `LocalFileService`.

Đề xuất bổ sung corpus vào bộ SecurityRegression đang chạy trên GitHub Actions,
với dữ liệu giả dưới thư mục tạm; kiểm tra sentinel ngoài kho và dữ liệu gốc sau
thất bại. Bộ Linux hiện chỉ xác minh guard và bước đọc/xác minh gói file cá nhân,
không chứng minh an toàn cho mọi thao tác khôi phục hoặc toàn bộ định dạng backup.

## Kết quả tại thời điểm triển khai, 2026-11-09

- PASS: build .NET SDK 10.0.403; chạy trên Windows .NET 10.0.14: 39 PASS.
- FAIL: 0 test trong lượt chạy Windows cuối.
- SKIPPED: 3 nhóm Windows/restore nêu trên.
- NOT TESTED: thực thi Linux và pipeline GitLab với thay đổi này. Máy hiện tại
  chưa cài WSL, không có Docker/Podman; chưa commit hoặc push theo yêu cầu.
- GL-11: chưa triển khai/chưa chạy. Chỉ tiếp tục sau khi GL-09 được thực thi trên
  Linux và không phát sinh vấn đề lớn, đúng điều kiện người dùng yêu cầu.

JUnit local nằm ngoài source CI: `artifacts/gl09-ci-work/results/gl09-windows.xml`
trong thư mục phát triển. Job GitLab sẽ tạo `artifacts/gl09/junit.xml`; không
đưa báo cáo Windows vào artifact Linux để thay kết quả chưa chạy.
