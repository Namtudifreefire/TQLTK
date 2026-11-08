using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace PersonalInfoManager.Services;
public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is BackupItemStatus status) return LocalizationAuditService.Get("backup.status." + status);
        if (value is BackupRecoverySource source) return LocalizationAuditService.Get("backup.source." + source);
        string mode = parameter as string ?? "";
        if (mode == "Role")
        {
            string role = value?.ToString() ?? "";
            return role switch
            {
                "User" => LanguageService.IsEnglish ? "User" : "Người dùng",
                "Admin" => LanguageService.IsEnglish ? "Administrator" : "Quản trị viên",
                _ => value?.ToString() ?? ""
            };
        }
        if (mode == "LastLoginAt")
        {
            if (value is DateTime dateTime)
                return LocalizationAuditService.FormatDateTime(dateTime);
            if (value is DateTimeOffset dateTimeOffset)
                return LocalizationAuditService.FormatDateTime(dateTimeOffset);
            return LanguageService.Translate("Chưa đăng nhập");
        }
        if (mode == "DateTime" && value is DateTime displayedDateTime)
            return LocalizationAuditService.FormatDateTime(displayedDateTime);
        if (mode == "DateTime" && value is DateTimeOffset displayedDateTimeOffset)
            return LocalizationAuditService.FormatDateTime(displayedDateTimeOffset);
        if (mode == "Date" && value is DateTime displayedDate)
            return LocalizationAuditService.FormatDate(displayedDate);
        if (mode == "Date" && value is DateTimeOffset displayedDateOffset)
            return LocalizationAuditService.FormatDate(displayedDateOffset);
        if (mode == "DaysRemaining" && value is int days)
            return LocalizationAuditService.FormatCount(days, "day", "days", "ngày");
        if (mode == "BackupDetails" && value is string details)
        {
            const string originalNamePrefix = "Tên gốc: ";
            if (details.Equals("Nội dung đã có", StringComparison.Ordinal))
                return LanguageService.Translate(details);
            if (details.StartsWith(originalNamePrefix, StringComparison.Ordinal))
                return LanguageService.Translate(originalNamePrefix) + details[originalNamePrefix.Length..];
            return details;
        }
        return value is string text ? LanguageService.Translate(text) : value ?? DependencyProperty.UnsetValue;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
