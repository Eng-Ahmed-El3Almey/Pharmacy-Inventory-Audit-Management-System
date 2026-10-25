using System;
using System.Windows;
using PharmacyInventorySystem.Services;
using QuestPDF.Infrastructure;

namespace PharmacyInventorySystem
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            UiNotificationService.InitializeWindowsNotifications();

            // QuestPDF community license (free for small businesses / individuals).
            QuestPDF.Settings.License = LicenseType.Community;

            // Keep the application alive while the modal login window is open.
            // Otherwise WPF's default OnLastWindowClose behavior can shut down
            // the application when LoginWindow closes before MainWindow is shown.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var loginWindow = new LoginWindow();
            bool? loginResult = loginWindow.ShowDialog();

            if (loginResult != true)
            {
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            // From this point on, close the application when the main window closes.
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
    }
}
