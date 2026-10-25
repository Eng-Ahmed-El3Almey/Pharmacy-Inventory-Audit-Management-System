using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PharmacyInventorySystem.Models;
using PharmacyInventorySystem.Services;

namespace PharmacyInventorySystem
{
    public partial class UserManagementView : System.Windows.Controls.UserControl
    {
        private readonly UserService _userService = new UserService();
        private List<UserRecord> _users = new List<UserRecord>();

        public UserManagementView()
        {
            InitializeComponent();
        }

        public void RefreshUsers() => LoadUsers();

        private void LoadUsers()
        {
            try
            {
                _users = _userService.GetUsers();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                UsersGrid.ItemsSource = null;
                UiNotificationService.Show(
                    "The user list could not be loaded.\n\n" + ex.Message,
                    "User Management",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            string term = SearchBox?.Text?.Trim() ?? string.Empty;

            var filtered = string.IsNullOrWhiteSpace(term)
                ? _users
                : _users.Where(u =>
                    u.Username.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    u.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    u.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    u.Mobile.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    u.Role.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    u.Gender.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

            UsersGrid.ItemsSource = filtered;
            if (UserCountText != null)
                UserCountText.Text = filtered.Count.ToString();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            var window = new UserManagementWindow
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() == true)
                LoadUsers();
        }

        private void EditUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not UserRecord user)
                return;

            var window = new UserManagementWindow(user)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() == true)
                LoadUsers();
        }

        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not UserRecord user)
                return;

            if (string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase))
            {
                UiNotificationService.Show(
                    "The default administrator account cannot be deleted.",
                    "Delete User",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirmation = UiNotificationService.Show(
                $"Delete user '{user.Username}'?\n\nThis action cannot be undone.",
                "Delete User",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                _userService.DeleteUser(user.Id);
                UiNotificationService.Show(
                    "The user account was deleted successfully.",
                    "User Deleted",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                LoadUsers();
            }
            catch (Exception ex)
            {
                UiNotificationService.Show(
                    "The user account could not be deleted.\n\n" + ex.Message,
                    "Delete User",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ToggleUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not UserRecord user)
                return;

            if (AuthenticationService.CurrentUser != null &&
                user.Id == AuthenticationService.CurrentUser.Id)
            {
                UiNotificationService.Show(
                    "You cannot deactivate the account you are currently using.",
                    "Account Status",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase))
            {
                UiNotificationService.Show(
                    "The default administrator account cannot be deactivated.",
                    "Account Status",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string action = user.IsActive ? "deactivate" : "activate";
            var confirmation = UiNotificationService.Show(
                $"Do you want to {action} user '{user.Username}'?",
                "Account Status",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                user.IsActive = !user.IsActive;
                _userService.UpdateUserStatus(user.Id, user.IsActive);

                UiNotificationService.Show(
                    user.IsActive
                        ? "The user account has been activated."
                        : "The user account has been deactivated.",
                    "Account Status",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadUsers();
            }
            catch (Exception ex)
            {
                user.IsActive = !user.IsActive;
                UiNotificationService.Show(
                    "The account status could not be changed.\n\n" + ex.Message,
                    "Account Status",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
