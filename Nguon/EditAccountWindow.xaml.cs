using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;
namespace PersonalInfoManager
{
    public partial class EditAccountWindow : Window
    {
        private readonly SavedAccount? _existing;
        private bool _showPwd = false;
        private TotpCredential? _totp;
        private readonly List<DeletedNoteEntry> _deletedNotes = new();
        private readonly List<AccountCustomField> _customFields = new();
        private bool _noteMovedToTrash;
                 public SavedAccount Result { get; private set; } = new();
        public EditAccountWindow(SavedAccount? existing)
        {
            InitializeComponent();
            _existing = existing;
            if (existing != null)
            {
                lblTitle.Text = "✏️ Sửa tài khoản";
                txtService.Text = existing.ServiceName;
                txtUsername.Text = existing.Username;
                txtPassword.Password = existing.Password;
                txtPasswordVisible.Text = existing.Password;
                txtNote.Text = existing.Note;
                txtWebsiteUrl.Text = existing.WebsiteUrl;
                txtFolder.Text = existing.Folder;
                txtTags.Text = string.Join(", ", existing.Tags ?? new List<string>());
                chkFavorite.IsChecked = existing.IsFavorite;
                _deletedNotes.AddRange((existing.DeletedNotes ?? new List<DeletedNoteEntry>()).Select(item => item.Clone()));
                _customFields.AddRange((existing.CustomFields ?? new List<AccountCustomField>()).Select(item => item.Clone()));
                if (!string.IsNullOrWhiteSpace(existing.TotpSecret))
                    _totp = new TotpCredential(existing.ServiceName, existing.TotpIssuer, existing.TotpSecret,
                        existing.TotpAlgorithm, existing.TotpDigits, existing.TotpPeriodSeconds);
                btnDeleteNote.Visibility = string.IsNullOrWhiteSpace(existing.Note) ? Visibility.Collapsed : Visibility.Visible;
            }
            UpdateCustomFieldsStatus();
            UpdateTotpStatus();
            UpdatePasswordStrength();
            Loaded += (_, _) => txtService.Focus();
        }
        private void BtnTogglePwd_Click(object sender, RoutedEventArgs e)
        {
            _showPwd = !_showPwd;
            if (_showPwd)
            {
                txtPasswordVisible.Text = txtPassword.Password;
                txtPassword.Visibility = Visibility.Collapsed;
                txtPasswordVisible.Visibility = Visibility.Visible;
                btnTogglePwd.Content = "🙈";
            }
            else
            {
                txtPassword.Password = txtPasswordVisible.Text;
                txtPassword.Visibility = Visibility.Visible;
                txtPasswordVisible.Visibility = Visibility.Collapsed;
                btnTogglePwd.Content = "👁️";
            }
        }
        private void BtnGeneratePwd_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new PasswordGeneratorWindow { Owner = this };
            if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.GeneratedPassword)) return;
            string pwd = dialog.GeneratedPassword;
            if (_showPwd)
                txtPasswordVisible.Text = pwd;
            else
                txtPassword.Password = pwd;
            UpdatePasswordStrength();
        }
        private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e) => UpdatePasswordStrength();
        private void TxtPasswordVisible_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdatePasswordStrength();
        private void UpdatePasswordStrength()
        {
            if (lblPwdStrength == null || txtPassword == null || txtPasswordVisible == null) return;
            string password = _showPwd ? txtPasswordVisible.Text : txtPassword.Password;
            if (string.IsNullOrEmpty(password))
            {
                LanguageService.SetLocalizedText(lblPwdStrength, "");
                return;
            }
            int score = PasswordStrengthService.CalculateScore(password);
            LanguageService.SetLocalizedText(lblPwdStrength, $"Độ mạnh: {PasswordStrengthService.GetLabel(score)}");
            lblPwdStrength.Foreground = score switch
            {
                <= 2 => Brushes.Red,
                3 => Brushes.Orange,
                4 => Brushes.Green,
                _ => Brushes.DarkGreen
            };
        }
        private void BtnManageTotp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TotpSetupWindow(txtService.Text.Trim()) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result == null) return;
            _totp = dialog.Result;
            UpdateTotpStatus();
        }
        private void BtnRemoveTotp_Click(object sender, RoutedEventArgs e)
        {
            if (_totp == null) return;
            if (MessageBox.Show(this, "Xóa mã TOTP khỏi tài khoản này?", "Xóa TOTP",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _totp = null;
            UpdateTotpStatus();
        }
        private void UpdateTotpStatus()
        {
            if (lblTotpStatus == null) return;
            bool hasTotp = _totp != null;
            btnRemoveTotp.Visibility = hasTotp ? Visibility.Visible : Visibility.Collapsed;
            lblTotpStatus.Text = hasTotp
                ? $"Đã thiết lập • {_totp!.Algorithm} • {_totp.Digits} chữ số • mỗi {_totp.PeriodSeconds} giây"
                : "Chưa có mã xác thực cho website này.";
        }
        private void BtnManageCustomFields_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CustomFieldsWindow(_customFields) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            _customFields.Clear();
            _customFields.AddRange(dialog.Result.Select(field => field.Clone()));
            UpdateCustomFieldsStatus();
        }
        private void UpdateCustomFieldsStatus()
        {
            if (lblCustomFieldsStatus == null) return;
            int sensitive = _customFields.Count(field => field.IsSensitive);
            lblCustomFieldsStatus.Text = _customFields.Count == 0
                ? "Chưa có trường tùy chỉnh"
                : $"{_customFields.Count} trường • {sensitive} trường nhạy cảm";
        }
        private void BtnDeleteNote_Click(object sender, RoutedEventArgs e)
        {
            string content = txtNote.Text.Trim();
            if (content.Length == 0) return;
            if (MessageBox.Show(this, "Đưa ghi chú hiện tại vào thùng rác 30 ngày?", "Xóa ghi chú",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _deletedNotes.Add(new DeletedNoteEntry { Content = content, DeletedAt = DateTime.UtcNow });
            _noteMovedToTrash = true;
            txtNote.Clear();
            btnDeleteNote.Visibility = Visibility.Collapsed;
        }
        private void TxtNote_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (btnDeleteNote == null || _existing == null) return;
            btnDeleteNote.Visibility = string.IsNullOrWhiteSpace(txtNote.Text)
                ? Visibility.Collapsed : Visibility.Visible;
        }
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string service = txtService.Text.Trim();
            string username = txtUsername.Text.Trim();
            string password = _showPwd
                ? txtPasswordVisible.Text
                : txtPassword.Password;
            string note = txtNote.Text.Trim();
            string websiteUrl = txtWebsiteUrl.Text.Trim();
            string folder = txtFolder.Text.Trim();
            List<string> tags = txtTags.Text
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            Uri? normalizedWebsiteUri = null;
            if (!string.IsNullOrWhiteSpace(websiteUrl) &&
                !WebsiteDomainService.TryNormalizeUrl(websiteUrl, out normalizedWebsiteUri, out _))
            {
                MessageBox.Show(this, "URL phải là địa chỉ HTTP hoặc HTTPS hợp lệ và không chứa thông tin đăng nhập trong URL.",
                    "URL không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtWebsiteUrl.Focus();
                return;
            }
            if (normalizedWebsiteUri != null) websiteUrl = normalizedWebsiteUri.AbsoluteUri;
            var urlWarnings = WebsiteDomainService.GetRiskWarnings(websiteUrl);
            if (urlWarnings.Count > 0 && MessageBox.Show(this,
                    LanguageService.Translate("Website URL có dấu hiệu cần kiểm tra:") + "\n\n" +
                    string.Join("\n", urlWarnings.Select(LanguageService.Translate)) + "\n\n" +
                    LanguageService.Translate("Chỉ tiếp tục nếu bạn đã xác minh domain chính thức."),
                    LanguageService.Translate("Cảnh báo domain"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            if (_existing != null && WebsiteDomainService.TryGetHost(_existing.WebsiteUrl, out string oldHost) &&
                WebsiteDomainService.TryGetHost(websiteUrl, out string newHost) &&
                !string.Equals(oldHost, newHost, StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this,
                    $"Hostname thay đổi từ '{oldHost}' sang '{newHost}'. Hãy xác minh đây là domain chính thức trước khi lưu. Tiếp tục?",
                    "Xác nhận thay đổi domain", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            if (_existing != null && !_noteMovedToTrash && string.IsNullOrWhiteSpace(note) &&
                !string.IsNullOrWhiteSpace(_existing.Note))
            {
                _deletedNotes.Add(new DeletedNoteEntry
                {
                    Content = _existing.Note,
                    DeletedAt = DateTime.UtcNow
                });
            }
            if (string.IsNullOrWhiteSpace(service))
            {
                MessageBox.Show("Vui lòng nhập tên dịch vụ.");
                txtService.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Vui lòng nhập tên đăng nhập.");
                txtUsername.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Vui lòng nhập mật khẩu.");
                return;
            }
            if (_existing != null)
            {
                bool passwordChanged = !string.Equals(_existing.Password, password, StringComparison.Ordinal);
                var history = (_existing.PasswordHistory ?? new List<PasswordHistoryEntry>())
                    .Where(entry => entry != null)
                    .Select(entry => entry.Clone()).ToList();
                if (!string.Equals(_existing.Password, password, StringComparison.Ordinal))
                {
                    history.RemoveAll(entry =>
                        string.Equals(entry.Password, password, StringComparison.Ordinal) ||
                        string.Equals(entry.Password, _existing.Password, StringComparison.Ordinal));
                    history.Insert(0, new PasswordHistoryEntry
                    {
                        Password = _existing.Password,
                        ChangedAt = DateTime.Now
                    });
                    history = history.Take(5).ToList();
                }
                Result = new SavedAccount
                {
                    Id = _existing.Id,
                    ServiceName = service,
                    Username = username,
                    Password = password,
                    Note = note,
                    WebsiteUrl = websiteUrl,
                    Folder = folder,
                    Tags = tags,
                    IsFavorite = chkFavorite.IsChecked == true,
                    CustomFields = _customFields.Select(item => item.Clone()).ToList(),
                    PasswordHistory = history,
                    DeletedNotes = _deletedNotes.Select(item => item.Clone()).ToList(),
                    TotpSecret = _totp?.Secret ?? "",
                    TotpIssuer = _totp?.Issuer ?? "",
                    TotpAlgorithm = _totp?.Algorithm ?? "SHA1",
                    TotpDigits = _totp?.Digits ?? 6,
                    TotpPeriodSeconds = _totp?.PeriodSeconds ?? 30,
                    CreatedAt = _existing.CreatedAt,
                    UpdatedAt = DateTime.Now,
                    PasswordChangedAt = passwordChanged ? DateTime.Now : _existing.PasswordChangedAt,
                    IsDeleted = _existing.IsDeleted,
                    DeletedAt = _existing.DeletedAt
                };
            }
            else
            {
                Result = new SavedAccount
                {
                    ServiceName = service,
                    Username = username,
                    Password = password,
                    Note = note,
                    WebsiteUrl = websiteUrl,
                    Folder = folder,
                    Tags = tags,
                    IsFavorite = chkFavorite.IsChecked == true,
                    CustomFields = _customFields.Select(item => item.Clone()).ToList(),
                    DeletedNotes = _deletedNotes.Select(item => item.Clone()).ToList(),
                    TotpSecret = _totp?.Secret ?? "",
                    TotpIssuer = _totp?.Issuer ?? "",
                    TotpAlgorithm = _totp?.Algorithm ?? "SHA1",
                    TotpDigits = _totp?.Digits ?? 6,
                    TotpPeriodSeconds = _totp?.PeriodSeconds ?? 30,
                    PasswordChangedAt = DateTime.Now
                };
            }
            DialogResult = true;
            Close();
        }
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
