namespace PersonalInfoManager.Services
{
    public static class PasswordStrengthService
    {
        public static int CalculateScore(string? password)
        {
            if (string.IsNullOrEmpty(password)) return 0;
            int score = 0;
            if (password.Length >= 8) score++;
            if (password.Length >= 12) score++;
            if (password.Any(char.IsUpper) && password.Any(char.IsLower)) score++;
            if (password.Any(char.IsDigit)) score++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) score++;
            return score;
        }
        public static string GetLabel(int score) => score switch
        {
            <= 2 => "Yếu",
            3 => "Trung bình",
            4 => "Mạnh",
            _ => "Rất mạnh"
        };
    }
}
