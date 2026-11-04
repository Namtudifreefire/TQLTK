using System;
using System.Collections.Generic;
using System.Linq;
using PersonalInfoManager.Models;
namespace PersonalInfoManager.Services
{
    public enum PasswordSecurityFindingKind
    {
        Breached,
        Reused,
        Weak,
        OlderThanYear,
        UnknownChangeDate
    }
    public sealed record PasswordSecurityFinding(Guid AccountId, PasswordSecurityFindingKind Kind);
    public static class PasswordSecurityAuditService
    {
        public const int PasswordReviewAgeDays = 365;
        public static IReadOnlyList<PasswordSecurityFinding> Analyze(
            IEnumerable<SavedAccount> accounts,
            IReadOnlySet<Guid> breachedAccountIds,
            bool breachResultsAreCurrent,
            DateTime localNow)
        {
            var activeAccounts = accounts.Where(account => !account.IsDeleted).ToList();
            var findings = new List<PasswordSecurityFinding>();
            foreach (var account in activeAccounts)
            {
                if (breachResultsAreCurrent && breachedAccountIds.Contains(account.Id))
                    findings.Add(new PasswordSecurityFinding(account.Id, PasswordSecurityFindingKind.Breached));
                if (!string.IsNullOrEmpty(account.Password) &&
                    PasswordStrengthService.CalculateScore(account.Password) <= 2)
                    findings.Add(new PasswordSecurityFinding(account.Id, PasswordSecurityFindingKind.Weak));
                if (account.PasswordChangedAt is DateTime changedAt)
                {
                    if ((localNow - changedAt).TotalDays >= PasswordReviewAgeDays)
                        findings.Add(new PasswordSecurityFinding(account.Id, PasswordSecurityFindingKind.OlderThanYear));
                }
                else
                {
                    findings.Add(new PasswordSecurityFinding(account.Id, PasswordSecurityFindingKind.UnknownChangeDate));
                }
            }
            foreach (var group in activeAccounts
                .Where(account => !string.IsNullOrEmpty(account.Password))
                .GroupBy(account => account.Password, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
            {
                findings.AddRange(group.Select(account =>
                    new PasswordSecurityFinding(account.Id, PasswordSecurityFindingKind.Reused)));
            }
            int Priority(PasswordSecurityFindingKind kind) => kind switch
            {
                PasswordSecurityFindingKind.Breached => 0,
                PasswordSecurityFindingKind.Reused => 1,
                PasswordSecurityFindingKind.Weak => 2,
                PasswordSecurityFindingKind.OlderThanYear => 3,
                _ => 4
            };
            return findings
                .Distinct()
                .OrderBy(finding => Priority(finding.Kind))
                .ThenBy(finding => finding.AccountId)
                .ToList();
        }
    }
}
