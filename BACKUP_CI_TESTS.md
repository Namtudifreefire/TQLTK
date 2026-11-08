# Kiểm thử backup/restore trong CI

Các nhóm này chạy trên GitHub Actions Windows. Hosted Windows GitLab đã được thử bằng script độc lập ở job [17842386796](https://gitlab.com/duyngadocton/TQLTK/-/jobs/17842386796): DPAPI CurrentUser.Protect trả 0x80070005 dù profile/HKCU hiện diện. Theo lựa chọn dự phòng của người dùng, không chạy lại các test phụ thuộc DPAPI trên môi trường đó; GitLab giữ SAST, Secret Detection, Dependency Scanning và NuGet Audit.

| Nhóm | Nơi chạy | Điều được kiểm tra |
| --- | --- | --- |
| Regression cũ và lỗi backup cơ bản | GitHub job build | 31 nhóm, gồm 4 nhóm mới về dữ liệu hỏng, sai cụm, tương thích fixture và lỗi ghi/rollback |
| Chuyển máy | backup-transfer-producer → backup-transfer-consumer | Hai VM riêng, xác nhận machine name và Windows SID khác nhau. Backup dùng cụm mật khẩu được khôi phục, giữ dữ liệu consumer; ciphertext khóa máy không mở được bằng khóa consumer |
| Backup schema cũ | Consumer và regression | Fixture tổng hợp schema 3.6.2/v3p, PBKDF2 100.000 vòng được tạo độc lập và nhập trên máy khác. CBC vẫn bị từ chối. Chưa có corpus backup từ mọi bản phát hành cũ; không tuyên bố mọi phiên bản đều tương thích |
| Rollback thất bại | Bước backup rollback trong job build | Tiêm lỗi sau khi lưu vault và trước rollback; kiểm tra thông báo lỗi không che giấu trạng thái một phần, tài khoản gốc còn nguyên, retry hoàn tất không trùng |
| Tiến trình bị gián đoạn | Bước process termination trong job build | Tiến trình con thật dừng tại checkpoint sau khi vault đã lưu; tiến trình cha kill đúng child do mình tạo rồi đọc lại dữ liệu và retry restore |
| Hết dung lượng thật | backup-disk-full | Tạo VHD tối đa 96 MB trên VM GitHub dùng một lần, kiểm tra đúng disk/partition/label mới tạo rồi làm đầy chỉ volume đó; backup cũ giữ nguyên và còn giải mã được sau lỗi ghi |

## Giới hạn có chủ đích của kết quả PASS

**Restore hiện chưa có tính nguyên tử giữa mọi thành phần khi crash hoặc rollback cũng lỗi.** Hai test rollback/crash PASS khi app giữ dữ liệu gốc, không báo thành công sai và retry thủ công hoàn tất. Chúng còn kiểm tra rõ trạng thái một phần khác baseline, thay vì đánh dấu dữ liệu chưa hề thay đổi. Không được dùng kết quả này để quảng bá app có automatic crash rollback. Nếu yêu cầu sản phẩm là tự phục hồi giao dịch toàn bộ, đây là khoảng trống P1 cần thiết kế journal/staging và đánh giá tương thích riêng; không tự sửa crypto/format trong công việc thêm test này.

Checkpoint `RestoreTestCheckpoint` chỉ được biên dịch với `SECURITY_TEST` (project test đã dùng cấu hình này). Bản Release thông thường không có property/checkpoint. Không thay format, thuật toán hoặc tham số mã hóa. Test không đổi quyền Windows, dùng tài khoản thật, token Google hoặc backup người dùng.

Phép thử VHD kiểm tra lỗi ghi **đích backup** khi hết dung lượng; chưa xác nhận mọi điểm ghi trong restore/rollback khi ổ lưu vault cũng đầy. Không gây đầy ổ hệ thống. Script chỉ cho phép GitHub-hosted runner dùng một lần, không cho chạy trên máy phát triển hoặc self-hosted runner. Khi không tạo/mount được VHD, job phải FAIL, không giả PASS. Disk-full thực không đồng nghĩa mô phỏng mất điện, hỏng thiết bị hoặc lỗi sector.

## Chạy lại

Workflow Windows CI tự chạy các nhóm trên cho push main và PR vào main, hoặc chạy thủ công workflow_dispatch. Bốn job phải thành công trên cùng commit; GitLab xanh không thay thế kết quả Windows này.

```powershell
./ci/run-windows-security.ps1
./ci/run-windows-security.ps1 -BackupLabMode rollback
./ci/run-windows-security.ps1 -BackupLabMode crash
```

Transfer cần hai máy/VM Windows độc lập, không sửa metadata để lách kiểm tra khác máy. Chỉ upload bốn fixture tổng hợp được chọn, giữ artifact 1 ngày; không upload cả test root, users.json, key.dat hoặc vault sống. Cụm mật khẩu trong code chỉ dành cho dữ liệu giả, công khai; không dùng để bảo vệ dữ liệu thật.

Tài liệu tham khảo: [runner GitHub](https://docs.github.com/en/actions/how-tos/manage-runners/github-hosted-runners/use-github-hosted-runners), [DiskPart của Microsoft](https://learn.microsoft.com/windows-server/administration/windows-commands/diskpart).
