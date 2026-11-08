# GitLab Security CI/CD — TQLTK

Ngày đối chiếu: 2026-11-08. Baseline: `1134e7059e97dbc57052e8839c2ca20f0048c7ef`, nhánh `main`, project `duyngadocton/TQLTK` (88625078).

## Cập nhật sau pipeline thực tế — lựa chọn áp dụng hiện tại

Các nhóm kiểm thử backup nâng cao đã được bổ sung vào workflow Windows của GitHub theo [BACKUP_CI_TESTS.md](BACKUP_CI_TESTS.md). GitLab giữ các scanner và NuGet Audit. Chi tiết NOT TESTED trong phần lịch sử bên dưới cần đối chiếu phạm vi mới: transfer giữa hai VM, lỗi rollback, kill tiến trình con và volume VHD giới hạn được kiểm tra riêng; chưa có cam kết restore nguyên tử sau crash hoặc tương thích mọi backup cũ.

Phần bên dưới ghi lại thiết kế và kiểm tra ban đầu. Sau khi pipeline [3041940691](https://gitlab.com/duyngadocton/TQLTK/-/pipelines/3041940691) chạy, người dùng chọn **GitHub Actions cho kiểm thử Windows, GitLab cho scanner và NuGet Audit**. Lựa chọn này thay thế kế hoạch chạy regression bằng hosted Windows của GitLab trong các mục lịch sử bên dưới.

- Pipeline đó có bảy job thành công, gồm NuGet Audit trên Linux và ba scanner bắt buộc; job Windows đạt 11 nhóm rồi lỗi DPAPI `CurrentUser` khi tạo tài khoản giả, 16 nhóm sau chưa chạy. SDK 10.0.403 đã tải/cài thành công. Git Credential Manager cũng báo lỗi `wincredman` trong checkout/cleanup; lỗi quyết định của regression nằm tại `ProtectedData.Protect`, không phải cài SDK.
- Chưa chứng minh được nguyên nhân gốc trong phiên đăng nhập/profile của hosted runner. Không đổi phạm vi DPAPI, không thay crypto và không tính lỗi môi trường là PASS.
- Đã chuyển regression về job Windows hiện có của GitHub Actions; giữ build, publish, ZIP và upload của workflow. Bước regression gọi `ci/run-windows-security.ps1`, chọn SDK 10 và chạy toàn bộ suite.
- `ci/test-windows-dpapi.ps1` kiểm tra protect/unprotect `CurrentUser` bằng dữ liệu ngẫu nhiên giả ngay trước khi restore/run. Nếu môi trường không đáp ứng, job thất bại rõ ràng, không skip suite. Không in dữ liệu mẫu hoặc ciphertext; các buffer được xóa sau kiểm tra.
- Xóa job hosted Windows khỏi root GitLab CI theo lựa chọn rõ ràng của người dùng. NuGet Audit có `dependencies: []`, tránh tự tải report scanner của stage trước mà job không sử dụng. Ba Security Profiles trùng vẫn cần xử lý riêng.
- GitLab xanh chỉ xác nhận các job GitLab. Trước khi phát hành phải kiểm tra **cả GitLab pipeline và GitHub Windows CI thành công trên cùng SHA**. Hiện chưa cấu hình cơ chế để kết quả GitHub tự chặn merge ở GitLab; gate đó cần cấu hình riêng nếu muốn bắt buộc ở server.
- Kiểm tra sau sửa trên máy Windows local: preflight đạt trong Windows PowerShell và PowerShell 7; script regression chạy SDK 10.0.403 đạt 27 nhóm, 0 lỗi. Parse PowerShell và `git diff --check` đạt. Pipeline mới trên hai dịch vụ cần chạy trước khi xác nhận hoàn tất.

Đây là bản nâng cấp cấu hình đang chờ kiểm chứng trên GitLab. Không thay đổi logic ứng dụng, cơ chế mã hóa, package, GitHub Actions, quyền truy cập hay security policy. Lần rà soát ban đầu không commit/push theo yêu cầu; sau đó người dùng đã cho phép đồng bộ các thay đổi lên GitHub và GitLab. Việc đồng bộ không xác nhận các bước CI còn thiếu đã đạt.

## 1. Kết quả đối chiếu trước khi sửa

- Root CI chỉ include ba template: SAST, Dependency Scanning v2 (độ sâu -1), Secret Detection. Không có workflow, stage, runner tag, build/test/publish riêng trong file này.
- Pipeline baseline [3041803434](https://gitlab.com/duyngadocton/TQLTK/-/pipelines/3041803434) thành công; cả sáu job đều `allow_failure: true`.
- Stage `test`: `gitlab-advanced-sast`, `dependency-scanning`, `secret_detection`.
- Stage `application-security-testing`: `gitlab-advanced-sast-profile-1`, `dependency-scanning-profile-0`, `secret-detection-profile-2`. Đây là ba scanner trùng được profile bên ngoài bổ sung; không được khai báo trong root CI.
- Cả sáu job dùng shared Linux amd64 runner. Metadata đã ghi nhận artifacts loại `sast`, `dependency_scanning`, `cyclonedx`, `secret_detection`. Điều này xác nhận baseline đã đăng ký report, không xác nhận report không có lỗ hổng hoặc dashboard/MR của cấu hình mới đã hoạt động.
- Shared/group runners đang bật. Runner manager báo `19.6.0~pre.2461.g7a0f1264`; đây KHÔNG phải phiên bản GitLab server.
- API đọc server version và CI Lint trả HTTP 403 bằng thông tin xác thực Git hiện có. Connector đọc project/pipeline được, nhưng không cung cấp CI Lint. Chưa xác minh được server version chính xác hoặc merged YAML trên server. Ba template cũ đã chạy thành công trên chính project; các override mới vẫn cần CI Lint trước khi sử dụng.
- GitHub Actions đang build, chạy regression, publish và đóng ZIP trên Windows. Giữ nguyên toàn bộ workflow đó.
- App và regression cùng target .NET 10/WPF. Có app `packages.lock.json` đã theo dõi trong Git; test project không có package riêng hoặc lockfile đã commit. Chưa có test project độc lập chạy Linux.
- `PROJECT_CONTEXT.md` trong thư mục phát triển là tài liệu bàn giao cũ, còn mô tả PR #1; không dùng trạng thái đó thay cho Git/project/pipeline hiện tại.

## 2. Trước và sau

| Thành phần | Trước | Cấu hình đề xuất | Xác minh |
| --- | --- | --- | --- |
| Advanced SAST | Đã chạy trên Linux; lỗi job được bỏ qua | Bật rõ Advanced SAST và MR pipelines; lỗi analyzer bắt buộc làm pipeline fail | Baseline có SAST report; cấu hình mới chưa chạy trên GitLab |
| Dependency Scanning | Stable v2, app lockfile, SBOM và vulnerability report | Giữ scanner; lỗi job làm pipeline fail; không loại trừ thư mục tests; giữ dev dependencies | Baseline có CycloneDX + dependency report; cần kiểm tra danh sách package trong SBOM mới |
| Secret Detection | Đã chạy; có profile trùng | Giữ scanner; lỗi job làm pipeline fail; không bật historic mặc định | Baseline có secret report; chưa xác nhận đã từng quét toàn lịch sử |
| NuGet Audit | Không có job GitLab riêng | SDK 10 trên Linux; restore app locked và test; audit direct + transitive | Hai lệnh audit đạt trên Windows/SDK 10.0.403 với HTTP cache riêng; chưa chạy trong Linux container |
| Regression | Chỉ có job GitHub Windows | Thêm job GitLab Windows chạy toàn bộ suite dữ liệu giả hiện có bằng SDK 10 | 27/27 đạt local Windows/SDK 10.0.403; chưa chạy trên hosted GitLab Windows |
| Reports | Template cho Developer tải report | Override quyền tải xuống thành Maintainer, lưu 7 ngày; giữ nguyên `reports` và đường dẫn của template | Kiểm tra merged YAML và quyền thực tế; restriction này không thay thế quyền Security Dashboard |
| Pipeline trùng | Chưa có workflow; có ba scanner profile trùng | Chuyển từ branch push sang MR pipeline khi mở MR; giữ main push, web theo branch, schedule main | Profile trùng vẫn cần chủ repo xử lý thủ công |
| Merge gate/push protection | Pipeline success gate và Secret Push Protection đang tắt | Liệt kê hướng dẫn bên dưới; không tự đổi setting | Chưa áp dụng |

Không thêm scanner SAST/secret/SBOM thứ hai. Hai job mới là NuGet Audit và regression Windows. Không tự tạo schedule; nếu sau này cần làm mới advisory khi không có commit, có thể tạo lịch tuần trên main sau khi kiểm chứng thời gian chạy/chi phí.

## 3. Cấu hình và runner

- `.gitlab-ci.yml`: giữ ba include; thêm workflow và các override; dùng stage `test` mặc định. Không khai báo lại stages để không làm mất stage do profile bổ sung. Không thêm `needs` hoặc truyền artifacts giữa scanner và runtime tests. Hai job mới có rules rõ cho MR và branch, tránh mặc định bỏ qua MR của job không có rules.
- Scanner tiếp tục dùng Linux như baseline. NuGet Audit dùng `mcr.microsoft.com/dotnet/sdk:10.0` và tag `saas-linux-small-amd64`.
- Regression dùng tag GitLab.com `saas-windows-medium-amd64`, không dùng Linux image. Hosted Windows vẫn là beta; chưa xác nhận runner này nhận job cho project, hạn mức hoặc thời gian chờ. Nếu pending, kiểm tra runner/quota hoặc gán tag một Windows runner tin cậy; không đổi thành `allow_failure: true`.
- `ci/run-windows-security.ps1`: chọn stable SDK 10; khi CI truyền `-InstallSdk`, tải installer chính thức Microsoft qua HTTPS và cài SDK trong thư mục tạm của runner. Tạo `global.json` tạm khi chưa có, rồi chỉ xóa đúng file do script tạo nếu nội dung không đổi. Từ chối filter chỉ chạy một phần test và từ chối `CI_DEBUG_TRACE=true`. Mỗi lệnh restore/run phải thành công. Không thu thập vault, credential, dữ liệu tạm hoặc raw test output thành artifact.
- Script bootstrap dùng kênh GA 10.0, không ghim patch/installer digest. Nếu cần build tái lập, ghim SDK và xác minh digest qua quy trình cập nhật có kiểm soát. Không sử dụng runner dùng chung với dữ liệu cá nhân thật cho MR không tin cậy.
- NuGet Audit dùng `NuGetAuditMode=all`, mức `low`: NU1901/NU1902 (Low/Moderate) vẫn là cảnh báo; NU1903/NU1904 (High/Critical) và lỗi nguồn NU1900/NU1905 trở thành lỗi. Các lỗi restore/lockfile cũng làm job fail. Không tự nâng package hoặc thêm suppressions.
- Các scanner có thể exit 0 khi tìm ra lỗ hổng. `allow_failure: false` chỉ chặn lỗi công cụ; chặn finding mới High/Critical cần approval policy riêng.
- Root workflow không chạy tag/API/trigger pipeline. Hiện chỉ xác nhận baseline push main, không thấy quy trình tag/API/trigger đang dùng; nếu cần thêm nguồn chạy mới, bổ sung workflow có chủ đích và lint lại.

## 4. Coverage và khoảng trống

### Phân tích mã và dependency

Advanced SAST phân tích C#, gồm những luồng đầu vào nguy hiểm như injection, path traversal và deserialization theo ruleset hỗ trợ. Không mặc định rằng scanner bao phủ đầy đủ mọi cách dùng crypto, RNG, DPAPI, cache giải mã, phân quyền, race condition hoặc luồng WPF. Không tạo custom rules chỉ để tạo cảm giác đã bao phủ. Tài liệu hiện công bố hỗ trợ C# đến 13.0 trong khi target .NET 10 có thể dùng C# 14: cần kiểm tra warning/parser và các cú pháp mới thực tế, không tự hạ ngôn ngữ của app.

Dependency Scanning v2 dùng lockfile NuGet để tạo SBOM và đối chiếu advisory cho phụ thuộc trực tiếp/gián tiếp. NuGet Audit bổ sung kiểm tra khi restore, bao gồm graph test. Chưa có test lockfile riêng; không tuyên bố SBOM baseline chứa lockfile test. Đây không phải chứng minh package có thể bị khai thác trong luồng app; cũng không phát hiện lỗ hổng chưa công bố.

Secret Detection hỗ trợ các kiểu bí mật theo rules mặc định; không có bằng chứng cần định dạng bí mật riêng nên chưa thêm custom ruleset. Không phải mọi OAuth client ID hoặc cấu hình Google đều là secret. Nếu có finding, phân loại và thu hồi/đổi bí mật thực trước; xóa khỏi file hiện tại không xóa khỏi lịch sử.

### Kiểm thử runtime

Suite hiện có 27 nhóm, dùng thư mục tạm riêng và dọn dữ liệu giả sau khi chạy. Bao gồm hash và xác minh hash cũ, localization/log, nhập JSON/XML an toàn, vault/notes/attachments/TOTP round-trip, draft DPAPI, backup/drill/file hỏng, full/selective restore, retention, xoay khóa và khôi phục transaction gián đoạn, phân quyền xoay khóa, chính sách lock/sleep, layout hai ngôn ngữ và script release từ chối artifact không ký.

Giới hạn phải giữ rõ:

- Windows Hello sinh trắc/PIN thực, màn hình bảo mật, màn hình sleep/lock thật và thao tác UI trong desktop tương tác: kiểm thử thủ công; không được tính PASS từ hosted runner.
- Test round-trip secret TOTP không thay thế test thuật toán theo RFC, chống replay và sử dụng mã khôi phục một lần. Chưa thấy test đầy đủ cho các trường hợp này trong suite đã đọc.
- Kiểm tra journal gián đoạn không tương đương stress test nhiều tiến trình ghi đồng thời hoặc mất điện thật.
- Tương thích hash cũ không đồng nghĩa mọi định dạng vault/backup cũ còn được hỗ trợ. Không khôi phục khả năng đọc CBC đã chủ đích chặn.
- Chưa có kiểm chứng toàn bộ nhánh sai mật khẩu/file lỗi/permission/I/O trên mọi định dạng backup, hoặc quét động tất cả log để tìm mọi loại secret. Không đưa dữ liệu thật vào test để lấp khoảng trống.
- Không chứng nhận chạy lâu dài, chống malware cùng tài khoản Windows hay an toàn mật mã chỉ từ một lần chạy suite.

## 5. Những bước chủ repo cần cấu hình trong GitLab

1. **Bỏ scanner profile trùng:** trong Security configuration/Security Profiles, đối chiếu profile đang gán và chọn một nguồn cấu hình. Bản đề xuất dùng ba template trong repo; sau khi lint và xác nhận job tương đương, ngắt đúng ba profile trùng. Không tắt mọi scanner. Nếu profile thuộc policy bắt buộc, làm việc với chủ policy thay vì ghi đè tên job để lách policy.
2. **Merge checks:** bật yêu cầu pipeline thành công; bảo vệ main, chỉ cho merge sau review, hạn chế direct push và force push theo vai trò phù hợp. Không coi setting `security_policy_pipeline_must_succeed` là đã bật; metadata hiện đang false.
3. **Approval policy:** vào Security & Compliance → Policies, dùng trình tạo policy của phiên bản đang chạy. Chọn SAST, Dependency Scanning và Secret Detection, so sánh finding mới trong MR với main; yêu cầu người duyệt phù hợp cho High/Critical mới. Thiết lập xử lý report thiếu/scanner lỗi theo hướng không bỏ qua, thử trên MR mẫu; không dùng finding ID giả trong policy thực.
4. **Scan Execution Policy:** chỉ cần nếu muốn bảo đảm scanner trên nhiều repo hoặc branch được bảo vệ. Chọn template repo hoặc policy làm nguồn chuẩn để tránh nhân đôi job; không thêm một bộ scanner song song với Security Profiles.
5. **Secret Push Protection:** bật trong cấu hình Secret Detection nếu quyền/tier cho phép. Metadata hiện false. Việc bật không thay thế quét lịch sử; đánh giá false positives, giới hạn loại secret và luồng bypass có kiểm soát.
6. **Historic scan một lần:** sau khi CI Lint hợp lệ và kiểm tra chưa từng có historic scan, chạy pipeline bằng giao diện trên main với `SECRET_DETECTION_HISTORIC_SCAN=true`; bảo đảm scanner lấy đủ lịch sử, xem warning fetch/depth. Có thể dùng `GIT_DEPTH=0` cho lần web đó nếu cần. Không lưu hai biến này làm mặc định dự án. Đọc report bằng quyền hạn chế, không dán giá trị secret vào issue công khai; thu hồi bí mật còn hiệu lực.
7. **Reports và triage:** chạy main để có baseline, rồi MR để có so sánh. Kiểm tra vulnerability report/Security Dashboard/MR widget nhận đủ scanner, branch, SHA và SBOM; phân loại Critical/High/Medium/Low, trạng thái triage và người chịu trách nhiệm. Low/Medium vẫn phải được xem xét; một scanner lỗi, không có report hoặc bị skip không phải kết quả sạch.
8. **Bảo mật runner/artifact:** giữ masked/protected variables, không bật debug trace, không cấp token/credential sản xuất cho scanner/regression hoặc MR không tin cậy. Kiểm tra quyền tải artifact thực tế; project hiện public và public_jobs=true, nên không đưa secret vào output công khai. Report secret có thể chứa dữ liệu nhạy cảm ngay cả khi log không in ra. Muốn giảm quyền xem jobs cần một thay đổi setting riêng do chủ repo duyệt.

## 6. Quy trình nghiệm thu trước khi đưa lên GitLab

1. Lint root YAML, kiểm tra mapping key trùng và parse script PowerShell; `git diff --check`.
2. Trong Pipeline Editor → Validate/CI Lint, dán toàn bộ cấu hình mới, bật mô phỏng và xem merged configuration. Chạy với main push, MR, branch chưa có MR, branch đã có MR, web main và historic web. Không tạo hai pipeline cho cùng một push khi MR mở; tag phải không chạy theo workflow hiện tại.
3. Trong merged YAML, kiểm tra `gitlab-advanced-sast`, `dependency-scanning`, `secret_detection` giữ `script`, `rules`, Linux image và `artifacts:reports` từ template. Kiểm tra `allow_failure: false` không bị rules/profile ghi đè; không có analyzer thay thế chạy trùng C#.
4. Kiểm tra artifacts `sast`, `dependency_scanning`, `cyclonedx`, `secret_detection` đúng đường dẫn, thời hạn và quyền. Regression không phụ thuộc artifact scanner; hai job mới cùng stage test không có `needs` không hợp lệ.
5. Khi được phép commit/push trong một yêu cầu riêng, thử trên branch/MR; kiểm tra scanner thực sự được xếp lịch, Linux/Windows runner nhận job, SDK 10 được chọn và toàn bộ regression chạy. Test Hello thực tế làm riêng trên máy kiểm nghiệm.
6. Xem nội dung report bằng quyền được phép để xác nhận coverage/file/package, không in toàn report vào log. Xác nhận không có credential/vault/data thật trong trace hoặc artifacts. Các report baseline đã tồn tại nhưng chưa được kiểm tra toàn bộ nội dung trong lần rà soát này.
7. Thử cơ chế fail: job bắt buộc fail phải chặn merge; finding mới High/Critical phải yêu cầu phê duyệt theo policy. Dùng fixture giả có kiểm soát trên branch thử nghiệm, không commit secret thật. Không suy ra gate hoạt động chỉ từ YAML.

## 7. Kết quả thực thi và ưu tiên

Kết quả đã thực hiện:

- YAML parse, mapping key không trùng, kiểm tra ba include còn giữ nguyên, các job bắt buộc không bỏ qua lỗi, stage/runner/report override và parse PowerShell: đạt. Mô phỏng offline root workflow/rules đạt 10 tình huống, gồm MR, main, branch có/chưa có MR, web, schedule, tag và API. Đây chỉ kiểm tra biểu thức root; chưa mô phỏng rules từ template/profile trên server, không thay thế merged CI Lint.
- Gọi script Windows mới bằng SDK 10.0.403: 27 nhóm đạt, 0 thất bại. Lần đầu trong môi trường hạn chế quyền ghi tệp tạm chỉ đạt 9, lỗi 3 rồi dừng; sau khi được cấp quyền thực thi phù hợp, toàn bộ suite đạt. Không sửa app để che lỗi môi trường.
- Script từ chối `TQLTK_REGRESSION_FILTER` và `CI_DEBUG_TRACE=true` bằng mã thoát lỗi như dự kiến.
- Hai lệnh NuGet Audit chạy thành công trên Windows với SDK 10.0.403, cache HTTP tạm riêng và truy cập mạng được cấp phép; không báo NU190x trong lần đó. MSBuild nhận đúng bốn mã `NU1900;NU1903;NU1904;NU1905` làm lỗi. Đây không phải thử nghiệm trong Linux container hoặc xác nhận không bao giờ có CVE mới.
- Nhánh bootstrap `-InstallSdk` tải/cài SDK, hosted Linux/Windows runner, tìm finding thực, policy gate và nội dung Security Reports mới chưa chạy/kiểm chứng. Không tính những bước này là PASS.
- Không đọc/đưa credential thật vào test hoặc artifact mới. Chưa kiểm tra toàn bộ nội dung artifact/trace cũ nên không chứng nhận chúng hoàn toàn không có secret.
- Tại thời điểm nghiệm thu local, chưa có pipeline cấu hình mới vì chưa được phép commit/push. Yêu cầu tiếp theo đã cho phép đồng bộ; kết quả pipeline sau push phải được kiểm tra riêng. HTTP 403 của CI Lint là giới hạn quyền API, không phải chứng nhận YAML hợp lệ hoặc thất bại từ GitLab.

- **P0 trước merge:** CI Lint trên server, kiểm tra scanner/report không bị thiếu, xử lý profile trùng có kiểm soát, xác nhận runner Windows, bật merge gate/approval theo quyết định chủ repo. Không bỏ qua lỗi để pipeline xanh.
- **P1:** historic secret scan một lần nếu chưa có; bật push protection; bổ sung kiểm thử TOTP/recovery anti-replay, sai mật khẩu và cạnh tranh nhiều tiến trình, rồi chạy kiểm nghiệm Windows Hello thực tế.
- **P2:** ghim SDK/bootstrap có kiểm tra digest; benchmark thời gian/quota, kiểm tra report không chứa dữ liệu không cần thiết và chạy lịch advisory phù hợp.
- **P3:** mở rộng threat model/rules dựa trên finding đã xác minh, đánh giá reachability khi GitLab hỗ trợ NuGet thực tế; không thêm scanner trùng hoặc tự đổi crypto.

## Tài liệu chính thức đã đối chiếu

- [Advanced SAST: C#, cấu hình và giới hạn](https://docs.gitlab.com/user/application_security/sast/gitlab_advanced_sast/).
- [Dependency Scanning dựa trên SBOM và NuGet](https://docs.gitlab.com/user/application_security/dependency_scanning/dependency_scanning_sbom/).
- [Secret Detection, MR và historic scan](https://docs.gitlab.com/user/application_security/secret_detection/pipeline/).
- [Hosted Windows runners và tag](https://docs.gitlab.com/ci/runners/hosted_runners/windows/).
- [NuGet Audit: graph, mức độ và mã lỗi](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages).
- [Microsoft dotnet-install](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script).
- [GitLab CI Lint API](https://docs.gitlab.com/api/lint/).
- [Merge Request Approval Policies](https://docs.gitlab.com/user/application_security/policies/merge_request_approval_policies/).
- [Scan Execution Policies](https://docs.gitlab.com/user/application_security/policies/scan_execution_policies/).

Đã đọc source ba template chính thức trên nhánh `master` của GitLab để đối chiếu cơ chế inheritance/rules/reports. Source `master` và tài liệu online thay đổi theo thời gian, không phải bản chụp template của server này. CI Lint merged configuration trên server vẫn là bước bắt buộc để xác nhận phiên bản thực tế tương thích.
