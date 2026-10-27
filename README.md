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

### Tính năng Beta

- Đăng nhập và sử dụng Windows Hello làm lớp bảo mật.
- Sao lưu kho dữ liệu (Backup Vault) lên Google Drive.

> **Lưu ý:** Các tính năng Beta mới được thử nghiệm trên máy của nhà phát triển. Một số máy có thể gặp lỗi hoặc hoạt động chưa ổn định. Nhãn Beta sẽ được gỡ khi quá trình kiểm nghiệm hoàn tất.

## Tải về

Bản phát hành v3.6.4 sẽ được đăng tại trang [GitHub Releases](https://github.com/Namtudifreefire/TQLTK/releases) sau khi được tạo.

## Winget

Hiện chưa có lệnh winget — PR vẫn đang chờ cứ đợi thêm :))