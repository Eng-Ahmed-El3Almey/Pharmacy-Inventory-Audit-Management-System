using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmacyInventorySystem.Models;
using PharmacyInventorySystem.Services;

namespace PharmacyInventorySystem
{
    public partial class UserManagementWindow : Window
    {
        private const int DWMWA_NCRENDERING_POLICY = 2;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMNCRP_ENABLED = 2;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        private void UserManagementWindow_SourceInitialized(object? sender, EventArgs e)
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

        private readonly UserService _userService = new UserService();
        private readonly UserRecord _editingUser;

        public UserManagementWindow(UserRecord? user = null)
        {
            InitializeComponent();
            _editingUser = user;
            InitializeBirthDateSelectors();

            if (_editingUser == null)
            {
                Title = "Add User";
                TitleText.Text = "Add New User";
                SubtitleText.Text = "Create a user account and assign permissions";
                RoleBox.SelectedIndex = 4;
                ActiveBox.IsChecked = true;
            }
            else
            {
                Title = "Edit User";
                TitleText.Text = "Edit User";
                SubtitleText.Text = "Update account information and permissions";
                PasswordBox.Clear();
                ConfirmPasswordBox.Clear();
                PasswordTextBox.Clear();
                ConfirmPasswordTextBox.Clear();
                LoadUser(_editingUser);
            }
        }

        private void LoadUser(UserRecord user)
        {
            UsernameBox.Text = user.Username;
            FullNameBox.Text = user.FullName;
            EmailBox.Text = user.Email;
            MobileBox.Text = user.Mobile;
            GenderBox.SelectedIndex = user.Gender.Equals("Female", StringComparison.OrdinalIgnoreCase) ? 1 :
                user.Gender.Equals("Male", StringComparison.OrdinalIgnoreCase) ? 0 : -1;
            SetBirthDate(user.DateOfBirth);
            RoleBox.SelectedItem = RoleBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(x => string.Equals(x.Content?.ToString(), user.Role, StringComparison.OrdinalIgnoreCase));
            ActiveBox.IsChecked = user.IsActive;
            ViewInventoryBox.IsChecked = user.ViewInventory;
            ImportInventoryBox.IsChecked = user.ImportInventory;
            ViewReportsBox.IsChecked = user.ViewReports;
            DeleteReportsBox.IsChecked = user.DeleteReports;
            EditReportsBox.IsChecked = user.EditReports;
            UserManagementBox.IsChecked = user.UserManagement;
            ManageCategoriesBox.IsChecked = user.ManageCategories;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            HideError();

            string username = UsernameBox.Text.Trim();
            string fullName = FullNameBox.Text.Trim();
            string password = PasswordBox.Visibility == Visibility.Visible
                ? PasswordBox.Password
                : PasswordTextBox.Text;
            string confirm = ConfirmPasswordBox.Visibility == Visibility.Visible
                ? ConfirmPasswordBox.Password
                : ConfirmPasswordTextBox.Text;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName))
            {
                ShowError("Username and Full Name are required.");
                return;
            }

            if (_editingUser == null && string.IsNullOrWhiteSpace(password))
            {
                ShowError("Password is required for a new user.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(password) && password != confirm)
            {
                ShowError("Password and Confirm Password do not match.");
                return;
            }

            if (RoleBox.SelectedItem == null)
            {
                ShowError("Please select a role.");
                return;
            }

            var user = _editingUser ?? new UserRecord();
            user.Username = username;
            user.FullName = fullName;
            user.Email = EmailBox.Text.Trim();
            user.Mobile = MobileBox.Text.Trim();
            user.Gender = GetSelectedText(GenderBox);
            if (!TryGetBirthDate(out DateTime? birthDate))
            {
                ShowError("Please select a valid Date of Birth or leave all three fields empty.");
                return;
            }
            user.DateOfBirth = birthDate?.Date;
            user.Role = GetSelectedText(RoleBox);
            user.IsActive = ActiveBox.IsChecked == true;
            user.ViewInventory = ViewInventoryBox.IsChecked == true;
            user.ImportInventory = ImportInventoryBox.IsChecked == true;
            user.ViewReports = ViewReportsBox.IsChecked == true;
            user.DeleteReports = DeleteReportsBox.IsChecked == true;
            user.EditReports = EditReportsBox.IsChecked == true;
            user.UserManagement = UserManagementBox.IsChecked == true;
            user.ManageCategories = ManageCategoriesBox.IsChecked == true;

            try
            {
                _userService.SaveUser(user, password);

                UiNotificationService.Show(
                    _editingUser == null
                        ? "The user account was created successfully."
                        : "The user account was updated successfully.",
                    _editingUser == null ? "User Created" : "User Updated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void InitializeBirthDateSelectors()
        {
            BirthDayBox.ItemsSource = Enumerable.Range(1, 31).ToList();
            BirthMonthBox.ItemsSource = Enumerable.Range(1, 12).ToList();

            int currentYear = DateTime.Today.Year;
            BirthYearBox.ItemsSource = Enumerable.Range(1900, currentYear - 1899).Reverse().ToList();
        }

        private void SetBirthDate(DateTime? date)
        {
            if (!date.HasValue)
            {
                BirthDayBox.SelectedItem = null;
                BirthMonthBox.SelectedItem = null;
                BirthYearBox.SelectedItem = null;
                return;
            }

            BirthDayBox.SelectedItem = date.Value.Day;
            BirthMonthBox.SelectedItem = date.Value.Month;
            BirthYearBox.SelectedItem = date.Value.Year;
        }

        private bool TryGetBirthDate(out DateTime? date)
        {
            date = null;

            bool anySelected = BirthDayBox.SelectedItem != null ||
                               BirthMonthBox.SelectedItem != null ||
                               BirthYearBox.SelectedItem != null;

            if (!anySelected)
                return true;

            if (BirthDayBox.SelectedItem is not int day ||
                BirthMonthBox.SelectedItem is not int month ||
                BirthYearBox.SelectedItem is not int year)
                return false;

            try
            {
                date = new DateTime(year, month, day);
                return true;
            }
            catch
            {
                return false;
            }
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

        private void ConfirmPasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            bool show = ConfirmPasswordBox.Visibility == Visibility.Visible;
            if (show)
            {
                ConfirmPasswordTextBox.Text = ConfirmPasswordBox.Password;
                ConfirmPasswordBox.Visibility = Visibility.Collapsed;
                ConfirmPasswordTextBox.Visibility = Visibility.Visible;
                ConfirmPasswordVisibilityButton.Content = "\uE7B3";
                ConfirmPasswordVisibilityButton.ToolTip = "Hide password";
                ConfirmPasswordTextBox.Focus();
                ConfirmPasswordTextBox.CaretIndex = ConfirmPasswordTextBox.Text.Length;
            }
            else
            {
                ConfirmPasswordBox.Password = ConfirmPasswordTextBox.Text;
                ConfirmPasswordTextBox.Visibility = Visibility.Collapsed;
                ConfirmPasswordBox.Visibility = Visibility.Visible;
                ConfirmPasswordVisibilityButton.Content = "\uE890";
                ConfirmPasswordVisibilityButton.ToolTip = "Show password";
                ConfirmPasswordBox.Focus();
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            HideError();

            if (_editingUser == null)
            {
                UsernameBox.Clear();
                FullNameBox.Clear();
                EmailBox.Clear();
                MobileBox.Clear();
                PasswordBox.Clear();
                ConfirmPasswordBox.Clear();
                PasswordTextBox.Clear();
                ConfirmPasswordTextBox.Clear();
                GenderBox.SelectedIndex = -1;
                BirthDayBox.SelectedItem = null;
                BirthMonthBox.SelectedItem = null;
                BirthYearBox.SelectedItem = null;
                RoleBox.SelectedIndex = 4;
                ActiveBox.IsChecked = true;
                ViewInventoryBox.IsChecked = false;
                ImportInventoryBox.IsChecked = false;
                ViewReportsBox.IsChecked = false;
                DeleteReportsBox.IsChecked = false;
                EditReportsBox.IsChecked = false;
                UserManagementBox.IsChecked = false;
                ManageCategoriesBox.IsChecked = false;
            }
            else
            {
                LoadUser(_editingUser);
                PasswordBox.Clear();
                ConfirmPasswordBox.Clear();
                PasswordTextBox.Clear();
                ConfirmPasswordTextBox.Clear();
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorText.Text = string.Empty;
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        private static string GetSelectedText(System.Windows.Controls.ComboBox comboBox)
        {
            return comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item
                ? item.Content?.ToString() ?? string.Empty
                : string.Empty;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
