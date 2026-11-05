using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalInfoManager.Models;

namespace PersonalInfoManager.Services;

public sealed record PwnedPasswordAuditState(
    DateTime CheckedAtUtc,
    int CheckedUniquePasswords,
    int BreachedUniquePasswords,
    List<Guid> BreachedAccountIds,
    Dictionary<Guid, DateTime> AccountVersions);

public static class PwnedPasswordAuditStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TQLTK.PwnedPasswordAudit.v1");
    private const int MaximumStateBytes = 1024 * 1024;

    public static PwnedPasswordAuditState CreateState(
        IEnumerable<SavedAccount> accounts,
        IEnumerable<Guid> breachedAccountIds,
        int checkedUniquePasswords,
        int breachedUniquePasswords,
        DateTime checkedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (checkedUniquePasswords < 0 || breachedUniquePasswords < 0 || breachedUniquePasswords > checkedUniquePasswords)
            throw new ArgumentOutOfRangeException(nameof(checkedUniquePasswords));

        var active = accounts.Where(account => account != null && !account.IsDeleted).ToList();
        if (active.Count > 100_000) throw new InvalidDataException("Too many accounts to record a password audit.");
        var ids = active.Select(account => account.Id).ToHashSet();
        var breached = breachedAccountIds.Where(ids.Contains).Distinct().ToList();
        return new PwnedPasswordAuditState(
            checkedAtUtc.Kind == DateTimeKind.Utc ? checkedAtUtc : checkedAtUtc.ToUniversalTime(),
            checkedUniquePasswords,
            breachedUniquePasswords,
            breached,
            active.ToDictionary(account => account.Id, account => account.UpdatedAt.ToUniversalTime()));
    }

    public static void Save(string username, PwnedPasswordAuditState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        string path = GetPath(username);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(state);
        byte[]? encrypted = null;
        try
        {
            if (plain.Length > MaximumStateBytes) throw new InvalidDataException("Password audit state exceeds the size limit.");
            encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            AtomicFileService.WriteAllBytes(path, encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (encrypted != null) CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    public static PwnedPasswordAuditState? Load(string username)
    {
        string path = GetPath(username);
        if (!File.Exists(path)) return null;
        byte[] encrypted = File.ReadAllBytes(path);
        byte[]? plain = null;
        try
        {
            if (encrypted.Length == 0 || encrypted.Length > MaximumStateBytes)
                throw new InvalidDataException("Stored password audit state has an invalid size.");
            plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            if (plain.Length > MaximumStateBytes) throw new InvalidDataException("Stored password audit state exceeds the size limit.");
            var state = JsonSerializer.Deserialize<PwnedPasswordAuditState>(plain);
            if (state == null || state.AccountVersions == null || state.BreachedAccountIds == null ||
                state.AccountVersions.Count > 100_000 || state.BreachedAccountIds.Count > state.AccountVersions.Count ||
                state.CheckedUniquePasswords < 0 || state.BreachedUniquePasswords < 0 ||
                state.BreachedUniquePasswords > state.CheckedUniquePasswords)
                throw new InvalidDataException("Stored password audit state is invalid.");
            return state;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            if (plain != null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static bool MatchesCurrentVault(PwnedPasswordAuditState? state, IEnumerable<SavedAccount> accounts)
    {
        if (state == null) return false;
        var active = accounts.Where(account => account != null && !account.IsDeleted).ToList();
        return active.Count == state.AccountVersions.Count && active.All(account =>
            state.AccountVersions.TryGetValue(account.Id, out DateTime version) &&
            version == account.UpdatedAt.ToUniversalTime());
    }

    public static bool IsReminderDue(PwnedPasswordAuditState? state, int reminderDays, DateTime utcNow)
    {
        if (state == null || reminderDays == 0) return state == null;
        if (reminderDays is not (30 or 90 or 180)) throw new ArgumentOutOfRangeException(nameof(reminderDays));
        DateTime checkedAt = state.CheckedAtUtc.Kind == DateTimeKind.Utc
            ? state.CheckedAtUtc
            : state.CheckedAtUtc.ToUniversalTime();
        return utcNow.ToUniversalTime() - checkedAt >= TimeSpan.FromDays(reminderDays);
    }

    private static string GetPath(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required.", nameof(username));
        byte[] name = Encoding.UTF8.GetBytes(username.Trim().ToUpperInvariant());
        byte[] hash = SHA256.HashData(name);
        try
        {
            return Path.Combine(ApplicationDataPath.Root, "PersonalInfoManager", "PwnedPasswordAudit", Convert.ToHexString(hash) + ".dat");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(name);
            CryptographicOperations.ZeroMemory(hash);
        }
    }
}
