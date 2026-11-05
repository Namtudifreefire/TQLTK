using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using PersonalInfoManager.Models;
using PersonalInfoManager.Services;

namespace PersonalInfoManager;

public partial class AccountAttachmentsWindow : Window
{
    public const int MaximumFileBytes = 10 * 1024 * 1024;
    public const int MaximumTotalBytes = 25 * 1024 * 1024;
    private readonly List<AccountAttachment> _items;
    public List<AccountAttachment> Result { get; private set; } = new();

    public AccountAttachmentsWindow(IEnumerable<AccountAttachment> attachments)
    {
        InitializeComponent();
        _items = (attachments ?? Array.Empty<AccountAttachment>()).Select(item => item.Clone()).ToList();
        dgAttachments.ItemsSource = _items.Select(item => new AttachmentView(item)).ToList();
        UpdateSize();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => LanguageService.ApplyToWindow(this);

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Chọn tệp đính kèm" };
        LocalizedFileDialog.Apply(dialog);
        if (dialog.ShowDialog(this) != true) return;

        foreach (string path in dialog.FileNames)
        {
            var info = new FileInfo(path);
            if (!info.Exists) continue;
            if (info.Length > MaximumFileBytes)
            {
                ShowError(string.Format(LanguageService.Translate("Tệp '{0}' vượt quá giới hạn 10 MB."), Path.GetFileName(path)));
                continue;
            }
            if (CurrentBytes() + info.Length > MaximumTotalBytes)
            {
                ShowError("Tổng dung lượng tệp đính kèm vượt quá giới hạn 25 MB.");
                break;
            }

            byte[] content;
            try
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length > MaximumFileBytes)
                {
                    ShowError(string.Format(LanguageService.Translate("Tệp '{0}' vượt quá giới hạn 10 MB."), Path.GetFileName(path)));
                    continue;
                }
                using var output = new MemoryStream((int)input.Length);
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaximumFileBytes)
                    throw new InvalidDataException("Tệp vượt quá giới hạn 10 MB.");
                    output.Write(buffer, 0, read);
                }
                content = output.ToArray();
                Array.Clear(buffer);
            }
            catch (Exception ex)
            {
                ShowError(string.Format(LanguageService.Translate("Không thể đọc tệp '{0}': {1}"),
                    Path.GetFileName(path), LanguageService.Translate(ex.Message)));
                continue;
            }
            if (content.LongLength != info.Length)
            {
                ShowError(string.Format(LanguageService.Translate("Tệp '{0}' đã thay đổi trong lúc đọc."), Path.GetFileName(path)));
                continue;
            }
            _items.Add(new AccountAttachment
            {
                FileName = Path.GetFileName(path),
                ContentType = GetContentType(path),
                Content = content
            });
        }
        RefreshRows();
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (dgAttachments.SelectedItem is not AttachmentView selected) return;
        _items.RemoveAll(item => item.Id == selected.Id);
        RefreshRows();
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: Guid id }) return;
        var attachment = _items.FirstOrDefault(item => item.Id == id);
        if (attachment == null || attachment.Content.Length > MaximumFileBytes) return;

        var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(attachment.FileName),
            Title = "Xuất tệp đính kèm"
        };
        LocalizedFileDialog.Apply(dialog);
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            string destination = Path.GetFullPath(dialog.FileName);
            File.WriteAllBytes(destination, attachment.Content);
            PersonalInfoManager.Services.LocalizedMessageBox.Show(this, "Đã xuất tệp đính kèm.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError("Không thể xuất tệp: " + ex.Message); }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentBytes() > MaximumTotalBytes)
        {
            ShowError("Tổng dung lượng tệp đính kèm vượt quá giới hạn 25 MB.");
            return;
        }
        Result = _items.Select(item => item.Clone()).ToList();
        DialogResult = true;
        Close();
    }

    private long CurrentBytes() => _items.Sum(item => (long)(item.Content?.Length ?? 0));

    private void RefreshRows()
    {
        dgAttachments.ItemsSource = null;
        dgAttachments.ItemsSource = _items.Select(item => new AttachmentView(item)).ToList();
        UpdateSize();
    }

    private void UpdateSize() => lblSize.Text = $"{CurrentBytes() / 1024.0:0.#} KB / {MaximumTotalBytes / 1024.0:0} KB";

    private void ShowError(string message) => PersonalInfoManager.Services.LocalizedMessageBox.Show(this, LanguageService.Translate(message),
        LanguageService.Translate("Lỗi"), MessageBoxButton.OK, MessageBoxImage.Warning);

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".txt" => "text/plain",
        ".json" => "application/json",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };

    private sealed record AttachmentView(Guid Id, string FileName, string SizeText, string AddedAtText)
    {
        public AttachmentView(AccountAttachment item) : this(item.Id, item.FileName,
            $"{item.Content.Length / 1024.0:0.#} KB", item.AddedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm")) { }
    }
}
