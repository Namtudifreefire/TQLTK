using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PersonalInfoManager.Services
{
    public class CloudSyncService
    {
        private readonly string[] Scopes = { DriveService.Scope.DriveFile };
        private readonly string ApplicationName = "TQLTK";

        private async Task<DriveService> GetDriveServiceAsync()
        {
            UserCredential credential;
            // Đọc file credentials.json (đã copy vào thư mục bin khi build)
            using (var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None);
            }

            return new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName
            });
        }

        /// <summary>
        /// Upload file .vault lên Google Drive.
        /// Trả về true nếu thành công.
        /// </summary>
        public async Task<bool> UploadVaultAsync(string localFilePath)
        {
            try
            {
                var service = await GetDriveServiceAsync();
                
                var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = Path.GetFileName(localFilePath),
                    MimeType = "application/octet-stream" 
                };

                using var stream = new FileStream(localFilePath, FileMode.Open);
                
                var request = service.Files.Create(fileMetadata, stream, "application/octet-stream");
                request.Fields = "id";
                
                var progress = await request.UploadAsync();
                
                return progress.Status == UploadStatus.Completed;
            }
            catch
            {
                return false;
            }
        }
    }
}