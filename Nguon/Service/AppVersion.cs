using System.Reflection;
namespace PersonalInfoManager.Services;
public static class AppVersion
{
    public static string Current { get; } = GetCurrent();
    private static string GetCurrent()
    {
        string? value = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Application version metadata is missing.");
        return value.Split('+')[0];
    }
}
