using System;
using System.Collections.Generic;
using System.Linq;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services;

public sealed record AccountSearchFilters(
    string Query = "",
    string Domain = "",
    string Folder = "",
    string Tag = "",
    bool FavoritesOnly = false,
    bool MissingTotpOnly = false,
    bool HasSecurityWarningOnly = false);

public static class AccountSearchService
{
    public static List<SavedAccount> Filter(
        IEnumerable<SavedAccount> accounts,
        AccountSearchFilters filters,
        IReadOnlySet<Guid>? warningAccountIds = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(filters);

        return accounts.Where(account => account != null && !account.IsDeleted)
            .Where(account => MatchesQuery(account, filters.Query))
            .Where(account => string.IsNullOrWhiteSpace(filters.Domain) ||
                string.Equals(GetHost(account.WebsiteUrl), filters.Domain, StringComparison.OrdinalIgnoreCase))
            .Where(account => string.IsNullOrWhiteSpace(filters.Folder) ||
                string.Equals((account.Folder ?? "").Trim(), filters.Folder, StringComparison.OrdinalIgnoreCase))
            .Where(account => string.IsNullOrWhiteSpace(filters.Tag) ||
                (account.Tags ?? new List<string>()).Any(tag =>
                    string.Equals((tag ?? "").Trim(), filters.Tag, StringComparison.OrdinalIgnoreCase)))
            .Where(account => !filters.FavoritesOnly || account.IsFavorite)
            .Where(account => !filters.MissingTotpOnly || string.IsNullOrWhiteSpace(account.TotpSecret))
            .Where(account => !filters.HasSecurityWarningOnly || warningAccountIds?.Contains(account.Id) == true)
            .ToList();
    }

    public static bool MatchesQuery(SavedAccount account, string? query)
    {
        string value = (query ?? "").Trim();
        if (value.Length == 0) return true;

        static bool Contains(string? field, string term) =>
            (field ?? "").Contains(term, StringComparison.CurrentCultureIgnoreCase);

        return Contains(account.ServiceName, value) ||
               Contains(account.Username, value) ||
               Contains(account.Note, value) ||
               Contains(account.WebsiteUrl, value) ||
               Contains(account.Folder, value) ||
               (account.Tags ?? new List<string>()).Any(tag => Contains(tag, value)) ||
               (account.CustomFields ?? new List<AccountCustomField>()).Any(field =>
                   field != null && (Contains(field.Name, value) || (!field.IsSensitive && Contains(field.Value, value))));
    }

    public static string GetHost(string? websiteUrl) =>
        WebsiteDomainService.TryGetHost(websiteUrl ?? "", out string host) ? host.TrimEnd('.').ToLowerInvariant() : "";
}
