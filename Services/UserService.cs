using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using PharmacyInventorySystem.Models;

namespace PharmacyInventorySystem.Services
{
    public sealed class UserService
    {
        private const string ConnectionString =
            @"Server=EL3ALMEY;Database=PharmacyInventoryDB_New;Trusted_Connection=True;TrustServerCertificate=True;";

        public UserRecord? Authenticate(string username, string password)
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            const string sql = @"
SELECT u.Id, u.Username, u.PasswordHash, u.FullName, u.Email, u.Mobile, u.Gender,
       u.DateOfBirth, u.Role, u.IsActive, u.LastLoginAt, u.CreatedAt,
       ISNULL(p.ViewInventory, 0), ISNULL(p.ImportInventory, 0), ISNULL(p.ViewReports, 0),
       ISNULL(p.DeleteReports, 0), ISNULL(p.EditReports, 0), ISNULL(p.UserManagement, 0), ISNULL(p.ManageCategories, 0)
FROM dbo.Users u
LEFT JOIN dbo.UserPermissions p ON p.UserId = u.Id
WHERE u.Username = @Username;";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Username", username.Trim());

            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;

            string passwordHash = reader.GetString(2);
            if (!reader.GetBoolean(9) || !PasswordHasher.Verify(password, passwordHash))
                return null;

            var user = ReadUser(reader);
            reader.Close();

            using var update = new SqlCommand(
                "UPDATE dbo.Users SET LastLoginAt = SYSDATETIME() WHERE Id = @Id", connection);
            update.Parameters.AddWithValue("@Id", user.Id);
            update.ExecuteNonQuery();
            user.LastLoginAt = DateTime.Now;
            return user;
        }

        public List<UserRecord> GetUsers()
        {
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                connection.Open();

                const string sql = @"
SELECT u.Id, u.Username, u.PasswordHash, u.FullName, u.Email, u.Mobile, u.Gender,
       u.DateOfBirth, u.Role, u.IsActive, u.LastLoginAt, u.CreatedAt,
       ISNULL(p.ViewInventory, 0), ISNULL(p.ImportInventory, 0), ISNULL(p.ViewReports, 0),
       ISNULL(p.DeleteReports, 0), ISNULL(p.EditReports, 0), ISNULL(p.UserManagement, 0), ISNULL(p.ManageCategories, 0)
FROM dbo.Users u
LEFT JOIN dbo.UserPermissions p ON p.UserId = u.Id
ORDER BY u.Username;";

                var result = new List<UserRecord>();
                using var command = new SqlCommand(sql, connection);
                using var reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadUser(reader));
                return result;
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(GetFriendlySqlMessage(ex), ex);
            }
        }

        public void SaveUser(UserRecord user, string password)
        {
            using var connection = new SqlConnection(ConnectionString);

            try
            {
                connection.Open();
                using var transaction = connection.BeginTransaction();

                try
                {
                    if (user.Id == 0)
                    {
                        if (string.IsNullOrWhiteSpace(password))
                            throw new InvalidOperationException("Password is required for a new user.");

                        const string insert = @"
INSERT INTO dbo.Users
    (Username, PasswordHash, FullName, Email, Mobile, Gender, DateOfBirth, Role, IsActive)
VALUES
    (@Username, @PasswordHash, @FullName, @Email, @Mobile, @Gender, @DateOfBirth, @Role, @IsActive);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                        using var command = new SqlCommand(insert, connection, transaction);
                        AddUserParameters(command, user);
                        command.Parameters.AddWithValue("@PasswordHash", PasswordHasher.Hash(password));
                        user.Id = Convert.ToInt32(command.ExecuteScalar());
                    }
                    else
                    {
                        string sql = @"
UPDATE dbo.Users
SET Username = @Username,
    FullName = @FullName,
    Email = @Email,
    Mobile = @Mobile,
    Gender = @Gender,
    DateOfBirth = @DateOfBirth,
    Role = @Role,
    IsActive = @IsActive" +
                            (string.IsNullOrWhiteSpace(password) ? "" : ", PasswordHash = @PasswordHash") +
                            " WHERE Id = @Id;";

                        using var command = new SqlCommand(sql, connection, transaction);
                        AddUserParameters(command, user);
                        command.Parameters.AddWithValue("@Id", user.Id);
                        if (!string.IsNullOrWhiteSpace(password))
                            command.Parameters.AddWithValue("@PasswordHash", PasswordHasher.Hash(password));
                        command.ExecuteNonQuery();
                    }

                    const string permissionSql = @"
IF EXISTS (SELECT 1 FROM dbo.UserPermissions WHERE UserId = @UserId)
    UPDATE dbo.UserPermissions
    SET ViewInventory=@ViewInventory, ImportInventory=@ImportInventory, ViewReports=@ViewReports,
        DeleteReports=@DeleteReports, EditReports=@EditReports, UserManagement=@UserManagement, ManageCategories=@ManageCategories
    WHERE UserId=@UserId;
ELSE
    INSERT INTO dbo.UserPermissions
        (UserId, ViewInventory, ImportInventory, ViewReports, DeleteReports, EditReports, UserManagement, ManageCategories)
    VALUES
        (@UserId, @ViewInventory, @ImportInventory, @ViewReports, @DeleteReports, @EditReports, @UserManagement, @ManageCategories);";

                    using var permissionCommand = new SqlCommand(permissionSql, connection, transaction);
                    permissionCommand.Parameters.AddWithValue("@UserId", user.Id);
                    permissionCommand.Parameters.AddWithValue("@ViewInventory", user.ViewInventory);
                    permissionCommand.Parameters.AddWithValue("@ImportInventory", user.ImportInventory);
                    permissionCommand.Parameters.AddWithValue("@ViewReports", user.ViewReports);
                    permissionCommand.Parameters.AddWithValue("@DeleteReports", user.DeleteReports);
                    permissionCommand.Parameters.AddWithValue("@EditReports", user.EditReports);
                    permissionCommand.Parameters.AddWithValue("@UserManagement", user.UserManagement);
                    permissionCommand.Parameters.AddWithValue("@ManageCategories", user.ManageCategories);
                    permissionCommand.ExecuteNonQuery();

                    transaction.Commit();
                }
                catch (SqlException ex)
                {
                    transaction.Rollback();
                    throw new InvalidOperationException(GetFriendlySqlMessage(ex), ex);
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(GetFriendlySqlMessage(ex), ex);
            }
        }

        public void DeleteUser(int userId)
        {
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                connection.Open();
                using var command = new SqlCommand("DELETE FROM dbo.Users WHERE Id = @Id", connection);
                command.Parameters.AddWithValue("@Id", userId);
                command.ExecuteNonQuery();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(GetFriendlySqlMessage(ex), ex);
            }
        }

        public void UpdateUserStatus(int userId, bool isActive)
        {
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                connection.Open();
                using var command = new SqlCommand(
                    "UPDATE dbo.Users SET IsActive = @IsActive WHERE Id = @Id",
                    connection);
                command.Parameters.AddWithValue("@IsActive", isActive);
                command.Parameters.AddWithValue("@Id", userId);
                command.ExecuteNonQuery();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(GetFriendlySqlMessage(ex), ex);
            }
        }

        private static string GetFriendlySqlMessage(SqlException ex)
        {
            if (ex.Number == 2627 || ex.Number == 2601)
                return "This username already exists. Please choose a different username.";

            if (ex.Number == 53 || ex.Number == 40 || ex.Number == 26 || ex.Number == 2)
                return "The application could not reach SQL Server. Please make sure the SQL Server service is running and the server name is correct.";

            if (ex.Number == 4060)
                return "The pharmacy user database could not be opened. Please verify that PharmacyInventoryDB_New exists on the configured SQL Server.";

            if (ex.Number == 18456)
                return "SQL Server rejected the database connection. Please check the Windows/SQL Server authentication configuration.";

            if (ex.Number == -2)
                return "SQL Server did not respond before the connection timed out. Please try again.";

            return "The user database operation could not be completed. Please try again.\n\nDetails: " + ex.Message;
        }

        private static void AddUserParameters(SqlCommand command, UserRecord user)
        {
            command.Parameters.AddWithValue("@Username", user.Username.Trim());
            command.Parameters.AddWithValue("@FullName", user.FullName.Trim());
            command.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(user.Email) ? (object)DBNull.Value : user.Email.Trim());
            command.Parameters.AddWithValue("@Mobile", string.IsNullOrWhiteSpace(user.Mobile) ? (object)DBNull.Value : user.Mobile.Trim());
            command.Parameters.AddWithValue("@Gender", string.IsNullOrWhiteSpace(user.Gender) ? (object)DBNull.Value : user.Gender.Trim());
            command.Parameters.AddWithValue("@DateOfBirth", user.DateOfBirth.HasValue ? user.DateOfBirth.Value.Date : (object)DBNull.Value);
            command.Parameters.AddWithValue("@Role", user.Role.Trim());
            command.Parameters.AddWithValue("@IsActive", user.IsActive);
        }

        private static UserRecord ReadUser(SqlDataReader reader)
        {
            return new UserRecord
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                FullName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Email = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Mobile = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Gender = reader.IsDBNull(6) ? "" : reader.GetString(6),
                DateOfBirth = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                Role = reader.IsDBNull(8) ? "Staff" : reader.GetString(8),
                IsActive = reader.GetBoolean(9),
                LastLoginAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                CreatedAt = reader.GetDateTime(11),
                ViewInventory = reader.GetBoolean(12),
                ImportInventory = reader.GetBoolean(13),
                ViewReports = reader.GetBoolean(14),
                DeleteReports = reader.GetBoolean(15),
                EditReports = reader.GetBoolean(16),
                UserManagement = reader.GetBoolean(17),
                ManageCategories = reader.GetBoolean(18)
            };
        }
    }
}
