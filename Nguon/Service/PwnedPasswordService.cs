using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PersonalInfoManager.Services
{
    public sealed record PwnedPasswordAuditResult(
        int TotalPasswords,
        int CheckedUniquePasswords,
        int BreachedUniquePasswords,
        IReadOnlyList<Guid> AffectedAccountIds)
    {
        public int AffectedAccounts => AffectedAccountIds.Count;
    }

    public static class PwnedPasswordService
    {
        private const int MaximumResponseBytes = 2 * 1024 * 1024;
        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

        public static async Task<PwnedPasswordAuditResult> CheckAsync(
            IEnumerable<(Guid AccountId, string Password)> accounts,
            CancellationToken cancellationToken = default)
        {
            var passwordToAccounts = accounts
                .Where(account => !string.IsNullOrEmpty(account.Password))
                .GroupBy(account => account.Password, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(account => account.AccountId).Distinct().ToArray(),
                    StringComparer.Ordinal);

            var breachedPasswords = new ConcurrentBag<string>();
            await Parallel.ForEachAsync(
                passwordToAccounts.Keys,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = 3
                },
                async (password, token) =>
                {
                    if (await IsBreachedAsync(password, token).ConfigureAwait(false))
                        breachedPasswords.Add(password);
                }).ConfigureAwait(false);

            Guid[] affectedAccountIds = breachedPasswords
                .SelectMany(password => passwordToAccounts[password])
                .Distinct()
                .ToArray();

            return new PwnedPasswordAuditResult(
                TotalPasswords: passwordToAccounts.Values.Sum(ids => ids.Length),
                CheckedUniquePasswords: passwordToAccounts.Count,
                BreachedUniquePasswords: breachedPasswords.Count,
                AffectedAccountIds: affectedAccountIds);
        }

        private static async Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken)
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] hashBytes = Array.Empty<byte>();

            try
            {
                hashBytes = SHA1.HashData(passwordBytes);
                string hash = Convert.ToHexString(hashBytes);
                string prefix = hash[..5];
                string suffix = hash[5..];

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://api.pwnedpasswords.com/range/{prefix}");
                request.Headers.UserAgent.ParseAdd("PersonalInfoManager/3.6.7 (Pwned Passwords audit)");
                request.Headers.TryAddWithoutValidation("Add-Padding", "true");

                using HttpResponseMessage response = await Client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength is long length && length > MaximumResponseBytes)
                    throw new InvalidOperationException("Phản hồi Pwned Passwords vượt giới hạn an toàn.");

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var output = new System.IO.MemoryStream();
                byte[] buffer = new byte[8192];
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (output.Length + read > MaximumResponseBytes)
                        throw new InvalidOperationException("Phản hồi Pwned Passwords vượt giới hạn an toàn.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                string body = Encoding.ASCII.GetString(output.GetBuffer(), 0, checked((int)output.Length));
                foreach (string line in body.Split('\n'))
                {
                    if (HasSuffix(line, suffix))
                        return true;
                }

                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
                if (hashBytes.Length > 0) CryptographicOperations.ZeroMemory(hashBytes);
            }
        }

        private static bool HasSuffix(string line, string suffix)
        {
            ReadOnlySpan<char> candidate = line.AsSpan().TrimEnd('\r');
            if (candidate.Length <= 36 || candidate[35] != ':' ||
                !candidate[..35].Equals(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return false;

            ReadOnlySpan<char> countText = candidate[36..];
            return ulong.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong count) &&
                   count > 0;
        }
    }
}
