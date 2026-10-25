using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Threading;

namespace PharmacyInventorySystem.Services
{
    /// <summary>
    /// Application-styled modal notifications and notification sounds.
    /// </summary>
    public static class UiNotificationService
    {
        private static readonly object SoundLock = new object();
        private static SoundPlayer? _finishedSoundPlayer;
        private static string? _loadedFinishedSoundPath;
        private static System.Windows.Forms.NotifyIcon? _notifyIcon;


        public static void InitializeWindowsNotifications()
        {
            try
            {
                if (_notifyIcon != null)
                    return;

                _notifyIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Application,
                    Visible = true,
                    Text = "Pharmacy Inventory Audit & Management System"
                };

                if (Application.Current != null)
                    Application.Current.Exit += (_, _) => DisposeWindowsNotifications();
            }
            catch
            {
                // Windows notifications are optional and must never stop the application.
            }
        }

        public static void ShowWindowsNotification(string title, string message)
        {
            try
            {
                InitializeWindowsNotifications();

                if (_notifyIcon == null)
                    return;

                _notifyIcon.BalloonTipTitle = title ?? string.Empty;
                _notifyIcon.BalloonTipText = message ?? string.Empty;
                _notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
                _notifyIcon.ShowBalloonTip(6000);
            }
            catch
            {
                // A notification failure must never affect inventory processing.
            }
        }

        public static void DisposeWindowsNotifications()
        {
            try
            {
                _notifyIcon?.Dispose();
                _notifyIcon = null;
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }

        public static MessageBoxResult Show(
            string message,
            string caption,
            MessageBoxButton button,
            MessageBoxImage image)
        {
            if (Application.Current == null)
                return button == MessageBoxButton.YesNo
                    ? MessageBoxResult.No
                    : MessageBoxResult.OK;

            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(
                    () => Show(message, caption, button, image));
            }

            PlayMessageSound(image);

            Window owner = Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive)
                ?? Application.Current.MainWindow;

            var dialog = new UiNotificationWindow(
                message ?? string.Empty,
                caption ?? string.Empty,
                button,
                image)
            {
                Owner = owner
            };

            dialog.ShowDialog();
            return dialog.Result;
        }

        public static void PlayMessageSound(MessageBoxImage image)
        {
            try
            {
                switch (image)
                {
                    case MessageBoxImage.Error:
                        SystemSounds.Hand.Play();
                        break;

                    case MessageBoxImage.Warning:
                        SystemSounds.Exclamation.Play();
                        break;

                    case MessageBoxImage.Question:
                        SystemSounds.Question.Play();
                        break;

                    case MessageBoxImage.Information:
                        SystemSounds.Asterisk.Play();
                        break;

                    default:
                        break;
                }
            }
            catch
            {
                // A sound failure must never prevent the dialog from opening.
            }
        }

        public static void PlayFinishedSound()
        {
            try
            {
                string path = Path.Combine(
                    AppContext.BaseDirectory,
                    "Sounds",
                    "DownloadFinished.wav");

                lock (SoundLock)
                {
                    if (!File.Exists(path))
                    {
                        SystemSounds.Asterisk.Play();
                        return;
                    }

                    if (!string.Equals(
                            _loadedFinishedSoundPath,
                            path,
                            StringComparison.OrdinalIgnoreCase)
                        || _finishedSoundPlayer == null)
                    {
                        _finishedSoundPlayer?.Dispose();
                        _finishedSoundPlayer = new SoundPlayer(path);
                        _finishedSoundPlayer.Load();
                        _loadedFinishedSoundPath = path;
                    }

                    _finishedSoundPlayer.Play();
                }
            }
            catch
            {
                try
                {
                    SystemSounds.Asterisk.Play();
                }
                catch
                {
                    // Ignore sound failures.
                }
            }
        }
    }
}
