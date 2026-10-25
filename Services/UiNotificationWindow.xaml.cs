using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PharmacyInventorySystem.Services
{
    public partial class UiNotificationWindow : Window
    {
        private readonly MessageBoxButton _buttons;

        public UiNotificationWindow(
            string message,
            string caption,
            MessageBoxButton buttons,
            MessageBoxImage image)
        {
            InitializeComponent();

            Message = message;
            Caption = caption;
            _buttons = buttons;
            IconText = GetIconText(image);
            IconBackgroundBrush = new SolidColorBrush(GetIconColor(image));

            DataContext = this;
            ConfigureButtons(buttons);
        }

        public string Message { get; }
        public string Caption { get; }
        public string IconText { get; }
        public System.Windows.Media.Brush IconBackgroundBrush { get; }
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        private void ConfigureButtons(MessageBoxButton buttons)
        {
            OkButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Visibility = Visibility.Collapsed;

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    OkButton.Visibility = Visibility.Visible;
                    break;

                case MessageBoxButton.OKCancel:
                    OkButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    break;

                case MessageBoxButton.YesNo:
                    YesButton.Visibility = Visibility.Visible;
                    NoButton.Visibility = Visibility.Visible;
                    break;

                case MessageBoxButton.YesNoCancel:
                    YesButton.Visibility = Visibility.Visible;
                    NoButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    break;

                default:
                    OkButton.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            Close();
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Result = _buttons switch
            {
                MessageBoxButton.YesNo => MessageBoxResult.No,
                MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
                MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
                _ => MessageBoxResult.OK
            };

            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private static string GetIconText(MessageBoxImage image)
        {
            return image switch
            {
                MessageBoxImage.Error => "×",
                MessageBoxImage.Warning => "!",
                MessageBoxImage.Question => "?",
                _ => "i"
            };
        }

        private static System.Windows.Media.Color GetIconColor(MessageBoxImage image)
        {
            string resourceKey = image switch
            {
                MessageBoxImage.Error => "NegativeTextBrush",
                MessageBoxImage.Warning => "WarningTextBrush",
                MessageBoxImage.Question => "InfoTextBrush",
                _ => "PositiveTextBrush"
            };

            if (Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush)
                return brush.Color;

            return image switch
            {
                MessageBoxImage.Error => System.Windows.Media.Color.FromRgb(220, 38, 38),
                MessageBoxImage.Warning => System.Windows.Media.Color.FromRgb(180, 83, 9),
                MessageBoxImage.Question => System.Windows.Media.Color.FromRgb(47, 111, 237),
                _ => System.Windows.Media.Color.FromRgb(22, 163, 74)
            };
        }
    }
}
