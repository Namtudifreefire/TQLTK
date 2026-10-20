using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Sao lưu file vault (đã mã hóa) lên Google Drive.
    /// - credentials.json được NHÚNG vào .exe (EmbeddedResource), không đọc từ thư mục hiện tại nữa.
    /// - Scope drive.file: app chỉ thấy/sửa được file do chính nó tạo.
    /// - Bản sao lưu nằm trong thư mục "TQLTK Backup", chỉ giữ N bản gần nhất (bản cũ được chuyển vào Thùng rác).
    /// - Lỗi không còn bị nuốt: xem LastError sau khi UploadVaultAsync trả về false.
    /// </summary>
    public class CloudSyncService
    {
        private const string ResourceName = "credentials.json";
        private const string ApplicationName = "TQLTK";
        private const string FolderName = "TQLTK Backup";
        private const string FolderMime = "application/vnd.google-apps.folder";
        private const string BackupPrefix = "vault_backup_";

        private readonly string[] Scopes = { DriveService.Scope.DriveFile };

        private static readonly string TokenDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalInfoManager", "GoogleToken");

        /// <summary>Thông báo lỗi dễ hiểu của lần gọi gần nhất (null nếu thành công).</summary>
        public string? LastError { get; private set; }

        /// <summary>Tên file đã tạo trên Drive ở lần gọi thành công gần nhất.</summary>
        public string? LastUploadedName { get; private set; }

        private sealed class CredentialsMissingException : Exception
        {
            public CredentialsMissingException()
                : base("Bản build này không có credentials.json nhúng sẵn. " +
                       "Nếu bạn tự build từ mã nguồn, hãy tạo credentials.json của riêng bạn " +
                       "(Google Cloud Console → OAuth client kiểu Desktop) và đặt cạnh file .csproj.") { }
        }

        /// <summary>
        /// Tải file vault lên Google Drive. Trả về true nếu thành công.
        /// Nếu false, đọc LastError để biết lý do.
        /// </summary>
        /// <param name="localFilePath">Đường dẫn file cần sao lưu.</param>
        /// <param name="keep">Số bản sao lưu gần nhất được giữ lại.</param>
        public async Task<bool> UploadVaultAsync(
            string localFilePath, int keep = 5, CancellationToken ct = default)
        {
            LastError = null;
            LastUploadedName = null;

            try
            {
                if (!File.Exists(localFilePath))
                    throw new FileNotFoundException("Chưa có file dữ liệu để sao lưu.", localFilePath);

                // Giới hạn thời gian để không treo mãi nếu người dùng đóng trình duyệt giữa chừng
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(5));

                try
                {
                    LastUploadedName = await UploadCoreAsync(localFilePath, keep, cts.Token);
                }
                catch (TokenResponseException)
                {
                    // Token hết hạn hoặc bị thu hồi (hay gặp khi app Google ở chế độ Testing:
                    // token hết hạn sau 7 ngày). Xóa token cũ, đăng nhập lại 1 lần rồi thử lại.
                    if (Directory.Exists(TokenDir))
                        Directory.Delete(TokenDir, recursive: true);

                    LastUploadedName = await UploadCoreAsync(localFilePath, keep, cts.Token);
                }

                return true;
            }
            catch (Exception ex)
            {
                LastError = Explain(ex);
                return false;
            }
        }

        private async Task<string> UploadCoreAsync(string filePath, int keep, CancellationToken ct)
        {
            using DriveService service = await GetDriveServiceAsync(ct);

            string folderId = await GetOrCreateFolderAsync(service, ct);

            string name = $"{BackupPrefix}{DateTime.Now:yyyyMMdd_HHmmss}.dat";
            var meta = new DriveFile
            {
                Name = name,
                Parents = new List<string> { folderId }
            };

            // FileShare.ReadWrite|Delete để không lỗi nếu file đang được app ghi hoặc xóa cùng lúc
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var request = service.Files.Create(meta, stream, "application/octet-stream");
            request.Fields = "id,name";

            IUploadProgress progress = await request.UploadAsync(ct);
            if (progress.Status != UploadStatus.Completed)
                throw new IOException(
                    "Tải lên Google Drive thất bại: " +
                    (progress.Exception?.Message ?? progress.Status.ToString()),
                    progress.Exception);

            await TrimOldBackupsAsync(service, folderId, keep, ct);
            return name;
        }

        private async Task<DriveService> GetDriveServiceAsync(CancellationToken ct)
        {
            using Stream secretStream =
                Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                ?? throw new CredentialsMissingException();

            var secrets = GoogleClientSecrets.FromStream(secretStream).Secrets;

            UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets, Scopes, "user", ct, new FileDataStore(TokenDir, true));

            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName
            });
        }

        private static async Task<string> GetOrCreateFolderAsync(DriveService service, CancellationToken ct)
        {
            var list = service.Files.List();
            list.Q = $"mimeType='{FolderMime}' and name='{FolderName}' and trashed=false";
            list.Fields = "files(id)";
            list.PageSize = 1;

            var found = (await list.ExecuteAsync(ct)).Files?.FirstOrDefault();
            if (found != null) return found.Id;

            var create = service.Files.Create(new DriveFile
            {
                Name = FolderName,
                MimeType = FolderMime
            });
            create.Fields = "id";

            return (await create.ExecuteAsync(ct)).Id;
        }

        private static async Task TrimOldBackupsAsync(
            DriveService service, string folderId, int keep, CancellationToken ct)
        {
            if (keep < 1) return;

            var list = service.Files.List();
            list.Q = $"'{folderId}' in parents and trashed=false and name contains '{BackupPrefix}'";
            list.OrderBy = "createdTime desc";
            list.Fields = "files(id,name,createdTime)";
            list.PageSize = 100;

            var files = (await list.ExecuteAsync(ct)).Files ?? new List<DriveFile>();

            // Chuyển bản cũ vào Thùng rác (khôi phục được trong 30 ngày), không xóa vĩnh viễn
            foreach (var old in files.Skip(keep))
                await service.Files.Update(new DriveFile { Trashed = true }, old.Id).ExecuteAsync(ct);
        }

        /// <summary>
        /// Chuyển exception thành thông báo dễ hiểu, vẫn giữ chi tiết kỹ thuật để biết lỗi thật.
        /// </summary>
        public static string Explain(Exception ex)
        {
            string detail = ex.Message;

            return ex switch
            {
                CredentialsMissingException
                    => detail,

                FileNotFoundException
                    => "Chưa có file dữ liệu để sao lưu.\n\n" + detail,

                OperationCanceledException
                    => "Đã hủy hoặc hết thời gian chờ đăng nhập Google.",

                HttpRequestException
                    => "Lỗi kết nối mạng. Hãy kiểm tra Internet rồi thử lại.\n\n" + detail,

                Google.GoogleApiException g when (int)g.HttpStatusCode == 403
                    => "Google từ chối quyền truy cập (403). Kiểm tra Google Drive API đã được bật, " +
                       "và tài khoản của bạn nằm trong danh sách Test users nếu app đang ở chế độ Testing.\n\n" + detail,

                Google.GoogleApiException
                    => "Google Drive báo lỗi.\n\n" + detail,

                TokenResponseException
                    => "Đăng nhập Google không thành công.\n\n" + detail,

                _ => detail
            };
        }
    }
}