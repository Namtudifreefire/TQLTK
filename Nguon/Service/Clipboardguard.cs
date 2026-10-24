using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PersonalInfoManager.Services
{
    /// <summary>
    /// Copy dữ liệu nhạy cảm (mật khẩu) vào clipboard rồi tự xóa sau ClearAfterSeconds giây.
    /// - Chỉ xóa nếu clipboard VẪN còn đúng nội dung vừa copy (người dùng đã copy thứ khác thì giữ nguyên).
    /// - Đánh dấu để Windows không lưu vào lịch sử Win+V và không đồng bộ lên clipboard đám mây.
    /// Phải gọi từ luồng giao diện (UI thread).
    /// </summary>
    public static class ClipboardGuard
    {
        public const int ClearAfterSeconds = 30;

        private static DispatcherTimer? _timer;
        private static string? _copiedText;

        public static void CopySensitive(string text)
        {
            try
            {
                var data = new DataObject();
                data.SetText(text);

                // Các định dạng Windows 10/11 đọc để bỏ qua lịch sử và clipboard đám mây
                data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
                data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
                data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));

                SetWithRetry(data);
            }
            catch
            {
                // Không đặt được cờ loại trừ thì vẫn copy bình thường (vẫn tự xóa sau ClearAfterSeconds giây)
                Clipboard.SetText(text);
            }

            _copiedText = text;

            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ClearAfterSeconds) };
            _timer.Tick += (_, _) => ClearNow();
            _timer.Start();
        }

        /// <summary>
        /// Xóa clipboard ngay nếu nó vẫn chứa mật khẩu đã copy. Gọi khi đăng xuất, tự khóa hoặc thoát app.
        /// </summary>
        public static void ClearNow()
        {
            _timer?.Stop();
            _timer = null;

            string? mine = _copiedText;
            _copiedText = null;
            if (mine == null) return;

            try
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == mine)
                    Clipboard.Clear();
            }
            catch
            {
                // Clipboard đang bị tiến trình khác giữ: bỏ qua, không làm sập app
            }
        }

        // Clipboard hay báo bận (CLIPBRD_E_CANT_OPEN) nếu tiến trình khác đang dùng
        private static void SetWithRetry(IDataObject data)
        {
            for (int i = 0; ; i++)
            {
                try
                {
                    Clipboard.SetDataObject(data, true);
                    return;
                }
                catch (COMException) when (i < 4)
                {
                    Thread.Sleep(60);
                }
            }
        }
    }
}