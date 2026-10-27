# TQLTK

Ứng dụng C# thử nghiệm, được tạo ra cho vui bởi **thienthuongthienha_duyngadocton_**.

## Cập nhật v3.6.4

### Tính năng và cải tiến

- Khôi phục dữ liệu từ Google Drive tại **Tiện ích → Khôi phục từ Google Drive**.
- Dùng cụm mật khẩu sao lưu để khôi phục dữ liệu trên máy khác.
- Tách dữ liệu sao lưu và đăng nhập Google theo từng người dùng.
- Khi nâng cấp từ bản cũ, có thể cần đăng nhập Google lại và chọn nhận cụm mật khẩu cũ.

### Sửa lỗi và tăng cường bảo mật

- Đồng bộ các thao tác quản trị với bước kiểm tra quyền để tránh dùng quyền admin đã bị thu hồi.
- Tuần tự hóa các thao tác 2FA và mã khôi phục để tránh lỗi khi nhiều thao tác diễn ra đồng thời trong ứng dụng.
- Tính năng đồng bộ Google Drive không còn phụ thuộc vào file `.json`.
- Khắc phục lỗi file `.dat` bị mất hoặc đổi tên làm ảnh hưởng đến dữ liệu trong ứng dụng.
- Có thể khôi phục bản sao lưu sau khi cài lại Windows bằng cụm mật khẩu sao lưu.
- Cài đặt mới không tạo tài khoản quản trị với thông tin đăng nhập mặc định; người dùng tự tạo quản trị viên đầu tiên. Tài khoản cũ còn dùng mật khẩu mặc định sẽ phải đổi trước khi vào ứng dụng.
- Trạng thái bật 2FA được lưu trong hồ sơ tài khoản. Nếu file cấu hình bị thiếu hoặc không đọc được, đăng nhập sẽ bị từ chối thay vì bỏ qua lớp bảo mật.
- File 2FA còn tồn tại từ bản cũ sẽ được tự chuyển trạng thái. Nếu file đã mất trước lần chạy đầu tiên sau cập nhật, ứng dụng không thể tự xác định trạng thái 2FA cũ; cần khôi phục cấu hình hoặc liên hệ quản trị viên.

### Tính năng Beta

- Đăng nhập và sử dụng Windows Hello làm lớp bảo mật.
- Sao lưu kho dữ liệu (Backup Vault) lên Google Drive.

> **Lưu ý:** Các tính năng Beta mới được thử nghiệm trên máy của nhà phát triển. Một số máy có thể gặp lỗi hoặc hoạt động chưa ổn định. Nhãn Beta sẽ được gỡ khi quá trình kiểm nghiệm hoàn tất.

## Tải về

- Mã nguồn đã cập nhật lên v3.6.4. Bản phát hành v3.6.4 chưa được tạo; bản cài đặt mới nhất hiện có là [v3.6.3](https://github.com/Namtudifreefire/TQLTK/releases/tag/v3.6.3).
- Theo dõi [GitHub Releases](https://github.com/Namtudifreefire/TQLTK/releases) để tải các bản mới.

## Winget

Hiện chưa phát hành gói Winget. Khi có, README sẽ được cập nhật lệnh cài đặt :))
