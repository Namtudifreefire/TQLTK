using System;
using System.Windows;
using PersonalInfoManager.Services;

namespace PersonalInfoManager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Chỉ thoát app khi cửa sổ chính (Application.MainWindow) đóng.
            // Code đăng nhập / đăng xuất / tự khóa luôn gán lại MainWindow cho cửa sổ mới TRƯỚC khi đóng cửa sổ cũ,
            // nên đăng xuất và tự khóa không làm tắt app, còn thoát thật thì tắt hẳn
            // dù vẫn còn cửa sổ nào đó đang bị ẩn (mặc định WPF chờ MỌI cửa sổ, kể cả cửa sổ ẩn, đóng hết).
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // 1. Load theme từ Registry
            try { ThemeService.Initialize(); } catch { }

            // 2. Khởi động Auto Theme nếu đã bật
            try
            {
                if (RegistryService.GetAutoThemeEnabled())
                {
                    AutoThemeService.Start();
                }
            }
            catch { }

            // 3. Dọn log cũ
            try { ActivityLogger.CleanupOldLogs(30); } catch { }

            // 4. Bắt lỗi toàn cục
            DispatcherUnhandledException += (_, args) =>
            {
                try
                {
                    MessageBox.Show(
                        $"Đã xảy ra lỗi:\n\n{args.Exception.Message}",
                        "Lỗi hệ thống",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }

                try
                {
                    ActivityLogger.Log(
                        SessionService.CurrentUser?.Username ?? "SYSTEM",
                        "UNHANDLED_ERROR",
                        args.Exception.Message);
                }
                catch { }

                args.Handled = true;
            };

            // 5. Bắt lỗi Task
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                try
                {
                    ActivityLogger.Log(
                        SessionService.CurrentUser?.Username ?? "SYSTEM",
                        "UNOBSERVED_TASK_ERROR",
                        args.Exception.Message);
                }
                catch { }
                args.SetObserved();
            };

            // 6. Mở LoginWindow
            try
            {
                var login = new LoginWindow();
                Application.Current.MainWindow = login;
                login.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Không thể khởi động ứng dụng:\n\n{ex.Message}",
                    "Lỗi khởi động",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        /// <summary>
        /// Khi app tắt → dừng auto theme timer.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            try { AutoThemeService.Stop(); } catch { }

            // Chẩn đoán lỗi "thoát rồi mà vẫn còn trong Task Manager":
            // - Log có dòng EXIT_APP (menu Thoát) nhưng KHÔNG có APP_EXIT → WPF chưa hề tắt app (còn cửa sổ giữ lại).
            // - Log có cả APP_EXIT mà tiến trình vẫn còn → còn một luồng nền giữ tiến trình.
            try
            {
                ActivityLogger.Log(
                    SessionService.CurrentUser?.Username ?? "SYSTEM",
                    "APP_EXIT",
                    $"OnExit đã chạy, mã thoát {e.ApplicationExitCode}");
            }
            catch { }

            base.OnExit(e);
        }
    }
}