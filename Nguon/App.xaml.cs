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

            ShutdownMode = ShutdownMode.OnMainWindowClose;

            try { ThemeService.Initialize(); } catch { }

            try
            {
                if (RegistryService.GetAutoThemeEnabled())
                {
                    AutoThemeService.Start();
                }
            }
            catch { }

            try { ActivityLogger.CleanupOldLogs(30); } catch { }

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

        protected override void OnExit(ExitEventArgs e)
        {
            try { AutoThemeService.Stop(); } catch { }

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