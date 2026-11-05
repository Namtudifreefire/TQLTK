using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PersonalInfoManager.Services;

public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
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
                return dateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            return LanguageService.Translate("Chưa đăng nhập");
        }

        if (mode == "DaysRemaining" && value is int days)
            return LanguageService.IsEnglish ? $"{days} days" : $"{days} ngày";

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
