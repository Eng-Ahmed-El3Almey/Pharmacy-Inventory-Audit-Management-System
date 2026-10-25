using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PharmacyInventorySystem.Services;

namespace PharmacyInventorySystem
{
    public partial class LoginWindow : Window
    {
        private const int DWMWA_NCRENDERING_POLICY = 2;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMNCRP_ENABLED = 2;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        private void LoginWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var renderingPolicy = DWMNCRP_ENABLED;
                var cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref renderingPolicy, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
            catch { }
        }

        public LoginWindow() : this("Sign In", "Welcome Back", "Sign in to continue")
        {
        }

        public LoginWindow(string windowTitle, string heading, string description)
        {
            InitializeComponent();

            Title = windowTitle;
            LoginHeadingText.Text = heading;
            LoginDescriptionText.Text = description;

            Loaded += (_, _) => UsernameBox.Focus();
        }

        private async void Login_Click(object sender, RoutedEventArgs e) => await TryLoginAsync();

        private async void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await TryLoginAsync();
            }
        }

        private async Task TryLoginAsync()
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = string.Empty;

            string username = UsernameBox.Text;
            string password = PasswordBox.Password;

            SetLoginBusy(true);

            try
            {
                var loginResult = await Task.Run(() =>
                {
                    bool ok = AuthenticationService.TryLogin(username, password, out string message);
                    return (ok, message);
                });

                bool success = loginResult.ok;
                string errorMessage = loginResult.message;

                if (success)
                {
                    DialogResult = true;
                    return;
                }

                ErrorText.Text = errorMessage;
                ErrorPanel.Visibility = Visibility.Visible;
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
            finally
            {
                if (IsVisible)
                    SetLoginBusy(false);
            }
        }

        private void SetLoginBusy(bool busy)
        {
            LoginLoadingOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            UsernameBox.IsEnabled = !busy;
            PasswordBox.IsEnabled = !busy;
            PasswordTextBox.IsEnabled = !busy;
            PasswordVisibilityButton.IsEnabled = !busy;
        }

        private void PasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            bool show = PasswordBox.Visibility == Visibility.Visible;

            if (show)
            {
                PasswordTextBox.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordTextBox.Visibility = Visibility.Visible;
                PasswordVisibilityButton.Content = "\uE7B3";
                PasswordVisibilityButton.ToolTip = "Hide password";
                PasswordTextBox.Focus();
                PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
            }
            else
            {
                PasswordBox.Password = PasswordTextBox.Text;
                PasswordTextBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordVisibilityButton.Content = "\uE890";
                PasswordVisibilityButton.ToolTip = "Show password";
                PasswordBox.Focus();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
