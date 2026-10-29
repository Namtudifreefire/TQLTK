using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace PersonalInfoManager.Services
{

    public enum BackupKind
    {

        Vault,

        Files
    }


    public enum BackupOwner
    {

        Mine,

        Legacy,

        Other
    }


    public sealed class BackupInfo
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public DateTimeOffset? Created { get; init; }
        public long? Size { get; init; }
        public BackupOwner Owner { get; init; }

        public string OwnerText => Owner switch
        {
            BackupOwner.Mine => "Của bạn",
            BackupOwner.Legacy => "Bản cũ",
            _ => "Người dùng khác"
        };

        public string CreatedText =>
            Created?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "?";

        public string SizeText
        {
            get
            {
                if (Size is null) return "?";
                long s = Size.Value;
                return s < 1024 * 1024 ? $"{s / 1024.0:0.#} KB" : $"{s / 1048576.0:0.#} MB";
            }
        }
    }
















    public class CloudSyncService
    {
        private const string ResourceName = "credentials.json";
        private const string ApplicationName = "TQLTK";
        private const string FolderName = "TQLTK Backup";
        private const string FolderMime = "application/vnd.google-apps.folder";
        private const string VaultPrefix = "vault_backup_";
        private const string FilesPrefix = "files_backup_";


        private static readonly Regex TaggedVaultName =
            new(@"^vault_backup_u[0-9a-f]{8}_", RegexOptions.Compiled);

        private static readonly Regex TaggedFilesName =
            new(@"^files_backup_u[0-9a-f]{8}_", RegexOptions.Compiled);

        private static string PrefixOf(BackupKind kind) => kind == BackupKind.Files ? FilesPrefix : VaultPrefix;
        private static string ExtensionOf(BackupKind kind) => kind == BackupKind.Files ? ".tqf" : ".dat";
        private static Regex TaggedNameOf(BackupKind kind) => kind == BackupKind.Files ? TaggedFilesName : TaggedVaultName;

        private readonly string[] Scopes = { DriveService.Scope.DriveFile };

        private readonly string _tag;
        private readonly string _tokenDir;


        private string MyPrefixOf(BackupKind kind) => $"{PrefixOf(kind)}{_tag}_";


        public string? LastError { get; private set; }


        public string? LastUploadedName { get; private set; }


        public CloudSyncService(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Thiếu tên người dùng.", nameof(username));

            _tag = BackupCrypto.UserTag(username);


            _tokenDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PersonalInfoManager", "GoogleTokens", _tag);
        }

        private sealed class CredentialsMissingException : Exception
        {
            public CredentialsMissingException()
                : base("Bản build này không có credentials.json nhúng sẵn. " +
                       "Nếu bạn tự build từ mã nguồn, hãy tạo credentials.json của riêng bạn " +
                       "(Google Cloud Console → OAuth client kiểu Desktop) và đặt cạnh file .csproj.") { }
        }











        public Task<bool> UploadVaultAsync(
            string localFilePath, int keep = 5, CancellationToken ct = default)
            => UploadAsync(BackupKind.Vault, localFilePath, keep, ct);







        public Task<bool> UploadFilesBackupAsync(
            string localFilePath, int keep = 3, CancellationToken ct = default)
            => UploadAsync(BackupKind.Files, localFilePath, keep, ct);

        private async Task<bool> UploadAsync(
            BackupKind kind, string localFilePath, int keep, CancellationToken ct)
        {
            LastError = null;
            LastUploadedName = null;

            try
            {
                if (!File.Exists(localFilePath))
                    throw new FileNotFoundException("Chưa có file dữ liệu để sao lưu.", localFilePath);



                using var cts = LinkedWithTimeout(ct, kind == BackupKind.Files ? 20 : 5);

                try
                {
                    LastUploadedName = await UploadCoreAsync(kind, localFilePath, keep, cts.Token);
                }
                catch (TokenResponseException)
                {


                    if (Directory.Exists(_tokenDir))
                        Directory.Delete(_tokenDir, recursive: true);

                    LastUploadedName = await UploadCoreAsync(kind, localFilePath, keep, cts.Token);
                }

                return true;
            }
            catch (Exception ex)
            {
                LastError = Explain(ex);
                return false;
            }
        }

        private async Task<string> UploadCoreAsync(
            BackupKind kind, string filePath, int keep, CancellationToken ct)
        {
            using DriveService service = await GetDriveServiceAsync(ct);

            string folderId = await GetOrCreateFolderAsync(service, ct);

            string name = $"{MyPrefixOf(kind)}{DateTime.Now:yyyyMMdd_HHmmss}{ExtensionOf(kind)}";
            var meta = new DriveFile
            {
                Name = name,
                Parents = new List<string> { folderId }
            };


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

            await TrimOldBackupsAsync(service, folderId, kind, keep, ct);
            return name;
        }












        public Task<List<BackupInfo>?> ListBackupsAsync(
            bool includeOthers = false, CancellationToken ct = default)
            => ListBackupsAsync(BackupKind.Vault, includeOthers, ct);


        public async Task<List<BackupInfo>?> ListBackupsAsync(
            BackupKind kind, bool includeOthers = false, CancellationToken ct = default)
        {
            LastError = null;
            try
            {
                using var cts = LinkedWithTimeout(ct);
                return await WithAuthRetryAsync(() => ListCoreAsync(kind, includeOthers, cts.Token), cts.Token);
            }
            catch (Exception ex)
            {
                LastError = Explain(ex);
                return null;
            }
        }









        public async Task<bool> DownloadBackupAsync(
            string fileId, string destPath, CancellationToken ct = default)
        {
            LastError = null;

            try
            {

                using var cts = LinkedWithTimeout(ct, 20);
                await WithAuthRetryAsync(async () =>
                {
                    await DownloadCoreAsync(fileId, destPath, cts.Token);
                    return true;
                }, cts.Token);

                if (new FileInfo(destPath).Length == 0)
                    throw new InvalidDataException("File tải về từ Google Drive bị rỗng.");

                return true;
            }
            catch (Exception ex)
            {
                LastError = Explain(ex);


                try { if (File.Exists(destPath)) File.Delete(destPath); } catch {   }

                return false;
            }
        }

        private async Task<List<BackupInfo>> ListCoreAsync(
            BackupKind kind, bool includeOthers, CancellationToken ct)
        {
            using DriveService service = await GetDriveServiceAsync(ct);

            string? folderId = await FindFolderAsync(service, ct);
            if (folderId == null) return new List<BackupInfo>();

            var result = new List<BackupInfo>();

            foreach (var f in await ListBackupFilesAsync(service, folderId, PrefixOf(kind), ct))
            {
                string name = f.Name ?? "";
                BackupOwner owner = ClassifyOwner(kind, name);

                if (owner == BackupOwner.Other && !includeOthers) continue;

                result.Add(new BackupInfo
                {
                    Id = f.Id,
                    Name = name,
                    Created = f.CreatedTimeDateTimeOffset,
                    Size = f.Size,
                    Owner = owner
                });
            }

            return result;
        }

        private async Task DownloadCoreAsync(string fileId, string destPath, CancellationToken ct)
        {
            using DriveService service = await GetDriveServiceAsync(ct);

            using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var request = service.Files.Get(fileId);
            IDownloadProgress progress = await request.DownloadAsync(fs, ct);

            if (progress.Status != DownloadStatus.Completed)
                throw new IOException(
                    "Tải xuống từ Google Drive thất bại: " +
                    (progress.Exception?.Message ?? progress.Status.ToString()),
                    progress.Exception);
        }

        private BackupOwner ClassifyOwner(BackupKind kind, string name)
        {
            if (name.StartsWith(MyPrefixOf(kind), StringComparison.Ordinal)) return BackupOwner.Mine;
            if (TaggedNameOf(kind).IsMatch(name)) return BackupOwner.Other;
            return BackupOwner.Legacy;
        }


        private async Task<T> WithAuthRetryAsync<T>(Func<Task<T>> op, CancellationToken ct)
        {
            try
            {
                return await op();
            }
            catch (TokenResponseException)
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(_tokenDir))
                    Directory.Delete(_tokenDir, recursive: true);

                return await op();
            }
        }

        private static CancellationTokenSource LinkedWithTimeout(CancellationToken ct, int minutes = 5)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(minutes));
            return cts;
        }





        private async Task<DriveService> GetDriveServiceAsync(CancellationToken ct)
        {
            using Stream secretStream =
                Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                ?? throw new CredentialsMissingException();

            var secrets = GoogleClientSecrets.FromStream(secretStream).Secrets;

            UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets, Scopes, "user", ct, new DpapiDataStore(_tokenDir));

            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName
            });
        }

        private static async Task<string?> FindFolderAsync(DriveService service, CancellationToken ct)
        {
            var list = service.Files.List();
            list.Q = $"mimeType='{FolderMime}' and name='{FolderName}' and trashed=false";
            list.Fields = "files(id)";
            list.PageSize = 1;

            return (await list.ExecuteAsync(ct)).Files?.FirstOrDefault()?.Id;
        }

        private static async Task<string> GetOrCreateFolderAsync(DriveService service, CancellationToken ct)
        {
            string? existing = await FindFolderAsync(service, ct);
            if (existing != null) return existing;

            var create = service.Files.Create(new DriveFile
            {
                Name = FolderName,
                MimeType = FolderMime
            });
            create.Fields = "id";

            return (await create.ExecuteAsync(ct)).Id;
        }


        private static async Task<List<DriveFile>> ListBackupFilesAsync(
            DriveService service, string folderId, string prefix, CancellationToken ct)
        {
            var all = new List<DriveFile>();
            string? pageToken = null;

            do
            {
                var list = service.Files.List();
                list.Q = $"'{folderId}' in parents and trashed=false and name contains '{prefix}'";
                list.OrderBy = "createdTime desc";
                list.Fields = "nextPageToken,files(id,name,createdTime,size)";
                list.PageSize = 100;
                list.PageToken = pageToken;

                var page = await list.ExecuteAsync(ct);

                if (page.Files != null)
                    all.AddRange(page.Files.Where(f =>
                        f.Name != null && f.Name.StartsWith(prefix, StringComparison.Ordinal)));

                pageToken = page.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            return all;
        }





        private async Task TrimOldBackupsAsync(
            DriveService service, string folderId, BackupKind kind, int keep, CancellationToken ct)
        {
            if (keep < 1) return;

            string mine = MyPrefixOf(kind);

            var mineFiles = (await ListBackupFilesAsync(service, folderId, PrefixOf(kind), ct))
                .Where(f => f.Name!.StartsWith(mine, StringComparison.Ordinal));


            foreach (var old in mineFiles.Skip(keep))
                await service.Files.Update(new DriveFile { Trashed = true }, old.Id).ExecuteAsync(ct);
        }




        public static string Explain(Exception ex)
        {
            string detail = ex.Message;

            return ex switch
            {
                CredentialsMissingException
                    => detail,

                InvalidDataException
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

                Google.GoogleApiException g when (int)g.HttpStatusCode == 404
                    => "Không tìm thấy bản sao lưu này trên Google Drive (có thể đã bị xóa hoặc chuyển vào Thùng rác).\n\n" + detail,

                Google.GoogleApiException
                    => "Google Drive báo lỗi.\n\n" + detail,

                TokenResponseException
                    => "Đăng nhập Google không thành công.\n\n" + detail,

                _ => detail
            };
        }
    }
}