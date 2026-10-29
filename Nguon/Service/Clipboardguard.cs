using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PersonalInfoManager.Services
{






    public static class ClipboardGuard
    {
        public const int ClearAfterSeconds = 30;

        private static DispatcherTimer? _timer;
        private static string? _copiedText;

        public static bool CopySensitive(string text)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ClearAfterSeconds) };
            timer.Tick += (_, _) => ClearNow();

            try
            {
                var data = new DataObject();
                data.SetText(text);


                data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
                data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
                data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));

                SetWithRetry(data);
            }
            catch
            {

                return false;
            }

            _timer?.Stop();
            _copiedText = text;
            _timer = timer;
            try { timer.Start(); }
            catch
            {
                ClearNow();
                return false;
            }

            return true;
        }




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

            }
        }


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
