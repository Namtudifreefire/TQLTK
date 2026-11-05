using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services
{
    public enum SecurityFindingReviewStatus
    {
        Open,
        Fixed,
        Snoozed,
        Ignored
    }

    public sealed record SecurityFindingReviewState(
        SecurityFindingReviewStatus Status,
        DateTime LastCheckedUtc,
        DateTime? RemindAtUtc,
        string Reason);

    public static class SecurityFindingReviewService
    {
        private static readonly object Sync = new();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TQLTK.SecurityFindingReview.v1");

        private sealed class ReviewRecord
        {
            public Guid AccountId { get; set; }
            public PasswordSecurityFindingKind Kind { get; set; }
            public string AccountVersion { get; set; } = "";
            public SecurityFindingReviewStatus Status { get; set; }
            public DateTime LastCheckedUtc { get; set; }
            public DateTime? RemindAtUtc { get; set; }
            public string Reason { get; set; } = "";
        }

        public static IReadOnlyDictionary<string, SecurityFindingReviewState> SyncFindings(
            string username,
            IReadOnlyList<PasswordSecurityFinding> findings,
            IReadOnlyDictionary<Guid, SavedAccount> accounts)
        {
            lock (Sync)
            {
                List<ReviewRecord> records = Load(username);
                DateTime now = DateTime.UtcNow;
                var active = new Dictionary<string, SecurityFindingReviewState>(StringComparer.Ordinal);
                foreach (PasswordSecurityFinding finding in findings)
                {
                    if (!accounts.TryGetValue(finding.AccountId, out SavedAccount? account)) continue;
                    string key = Key(finding.AccountId, finding.Kind);
                    string version = ComputeAccountVersion(account);
                    ReviewRecord? record = records.FirstOrDefault(item =>
                        item.AccountId == finding.AccountId && item.Kind == finding.Kind);
                    if (record == null)
                    {
                        record = new ReviewRecord
                        {
                            AccountId = finding.AccountId,
                            Kind = finding.Kind,
                            AccountVersion = version,
                            Status = SecurityFindingReviewStatus.Open
                        };
                        records.Add(record);
                    }
                    else if (record.AccountVersion != version)
                    {
                        record.AccountVersion = version;
                        record.Status = SecurityFindingReviewStatus.Open;
                        record.RemindAtUtc = null;
                        record.Reason = "";
                    }

                    if (record.Status == SecurityFindingReviewStatus.Snoozed &&
                        record.RemindAtUtc <= now)
                    {
                        record.Status = SecurityFindingReviewStatus.Open;
                        record.RemindAtUtc = null;
                    }

                    record.LastCheckedUtc = now;
                    active[key] = ToState(record);
                }

                records = records
                    .OrderByDescending(record => record.LastCheckedUtc)
                    .Take(1000)
                    .ToList();
                Save(username, records);
                return active;
            }
        }

        public static void Set(
            string username,
            SavedAccount account,
            PasswordSecurityFindingKind kind,
            SecurityFindingReviewStatus status,
            DateTime? remindAtUtc,
            string? reason)
        {
            if (status == SecurityFindingReviewStatus.Snoozed && (!remindAtUtc.HasValue || remindAtUtc.Value <= DateTime.UtcNow))
                throw new ArgumentException("Thời điểm nhắc lại phải nằm trong tương lai.", nameof(remindAtUtc));
            if (status == SecurityFindingReviewStatus.Ignored && string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Cần ghi lý do khi bỏ qua cảnh báo.", nameof(reason));

            lock (Sync)
            {
                List<ReviewRecord> records = Load(username);
                ReviewRecord? record = records.FirstOrDefault(item =>
                    item.AccountId == account.Id && item.Kind == kind);
                if (record == null)
                {
                    record = new ReviewRecord { AccountId = account.Id, Kind = kind };
                    records.Add(record);
                }
                record.AccountVersion = ComputeAccountVersion(account);
                record.Status = status;
                record.LastCheckedUtc = DateTime.UtcNow;
                record.RemindAtUtc = status == SecurityFindingReviewStatus.Snoozed ? remindAtUtc?.ToUniversalTime() : null;
                record.Reason = status == SecurityFindingReviewStatus.Ignored
                    ? new string((reason ?? "").Where(character => !char.IsControl(character)).Take(300).ToArray()).Trim()
                    : "";
                Save(username, records.OrderByDescending(item => item.LastCheckedUtc).Take(1000).ToList());
            }
        }

        public static string Key(Guid accountId, PasswordSecurityFindingKind kind) =>
            accountId.ToString("N") + ":" + ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static SecurityFindingReviewState ToState(ReviewRecord record) =>
            new(record.Status, record.LastCheckedUtc, record.RemindAtUtc, record.Reason);

        private static string ComputeAccountVersion(SavedAccount account)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] timestamp = Encoding.UTF8.GetBytes(account.UpdatedAt.ToUniversalTime().Ticks.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            byte[] password = Encoding.UTF8.GetBytes(account.Password ?? "");
            byte[] passwordDigest = SHA256.HashData(password);
            byte[]? digest = null;
            try
            {
                hash.AppendData(timestamp);
                hash.AppendData(passwordDigest);
                digest = hash.GetHashAndReset();
                return Convert.ToHexString(digest);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(timestamp);
                CryptographicOperations.ZeroMemory(password);
                CryptographicOperations.ZeroMemory(passwordDigest);
                if (digest != null) CryptographicOperations.ZeroMemory(digest);
            }
        }

        private static string GetPath(string username)
        {
            string root = Path.Combine(ApplicationDataPath.Root,
                "PersonalInfoManager");
            return Path.Combine(root, "security_reviews_" + BackupCrypto.UserTag(username) + ".dat");
        }

        private static List<ReviewRecord> Load(string username)
        {
            string path = GetPath(username);
            if (!File.Exists(path)) return new List<ReviewRecord>();
            var info = new FileInfo(path);
            if (info.Length is < 1 or > 1024 * 1024)
                throw new InvalidDataException("Tệp trạng thái xem xét bảo mật vượt giới hạn kích thước.");

            byte[] protectedData = File.ReadAllBytes(path);
            byte[]? json = null;
            try
            {
                json = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<List<ReviewRecord>>(json)
                    ?? throw new InvalidDataException("Tệp trạng thái xem xét bảo mật không hợp lệ.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedData);
                if (json != null) CryptographicOperations.ZeroMemory(json);
            }
        }

        private static void Save(string username, List<ReviewRecord> records)
        {
            string path = GetPath(username);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(records);
            byte[] protectedData = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
            try { AtomicFileService.WriteAllBytes(path, protectedData); }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
                CryptographicOperations.ZeroMemory(protectedData);
            }
        }
    }
}
