namespace PersonalInfoManager.Models;

public sealed record TotpCredential(
    string Name,
    string Issuer,
    string Secret,
    string Algorithm,
    int Digits,
    int PeriodSeconds);
