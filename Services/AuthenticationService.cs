using System;
using PharmacyInventorySystem.Models;

namespace PharmacyInventorySystem.Services
{
    public static class AuthenticationService
    {
        private static readonly UserService UserService = new UserService();

        public static UserRecord? CurrentUser { get; private set; }

        public static bool TryLogin(string username, string password, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                errorMessage = "Enter your username and password.";
                return false;
            }

            try
            {
                var user = UserService.Authenticate(username, password);
                if (user == null)
                {
                    errorMessage = "Invalid username or password, or the account is inactive.";
                    return false;
                }

                CurrentUser = user;
                return true;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                errorMessage = GetFriendlyDatabaseMessage(ex);
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = "The sign-in service could not complete the request.\n\n" + ex.Message;
                return false;
            }
        }

        private static string GetFriendlyDatabaseMessage(Microsoft.Data.SqlClient.SqlException ex)
        {
            if (ex.Number == 53 || ex.Number == 40 || ex.Number == 26 || ex.Number == 2)
                return "Unable to connect to the pharmacy user database.\n\nPlease make sure SQL Server is running and the configured server is available.";

            if (ex.Number == 4060)
                return "The pharmacy user database is unavailable.\n\nPlease verify that PharmacyInventoryDB_New exists on the configured SQL Server.";

            if (ex.Number == 18456)
                return "SQL Server rejected the sign-in connection.\n\nPlease check the Windows authentication configuration.";

            if (ex.Number == -2)
                return "The database connection timed out.\n\nPlease make sure SQL Server is running and try again.";

            return "The pharmacy user database could not be reached.\n\nPlease check the database configuration and try again.";
        }

        public static bool HasPermission(Func<UserRecord, bool> permission)
        {
            return CurrentUser != null && permission(CurrentUser);
        }

        public static void SignOut()
        {
            CurrentUser = null;
        }
    }
}
