USE PharmacyInventoryDB_New;
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL,
        PasswordHash NVARCHAR(500) NOT NULL,
        FullName NVARCHAR(200) NOT NULL,
        Email NVARCHAR(200) NULL,
        Mobile NVARCHAR(50) NULL,
        Gender NVARCHAR(20) NULL,
        DateOfBirth DATE NULL,
        Role NVARCHAR(50) NOT NULL CONSTRAINT DF_Users_Role DEFAULT (N'Staff'),
        IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
        LastLoginAt DATETIME2 NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSDATETIME()),
        CONSTRAINT UX_Users_Username UNIQUE (Username)
    );
END;
GO

IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserPermissions
    (
        UserId INT NOT NULL CONSTRAINT PK_UserPermissions PRIMARY KEY,
        ViewInventory BIT NOT NULL CONSTRAINT DF_UserPermissions_ViewInventory DEFAULT (0),
        ImportInventory BIT NOT NULL CONSTRAINT DF_UserPermissions_ImportInventory DEFAULT (0),
        ViewReports BIT NOT NULL CONSTRAINT DF_UserPermissions_ViewReports DEFAULT (0),
        DeleteReports BIT NOT NULL CONSTRAINT DF_UserPermissions_DeleteReports DEFAULT (0),
        EditReports BIT NOT NULL CONSTRAINT DF_UserPermissions_EditReports DEFAULT (0),
        UserManagement BIT NOT NULL CONSTRAINT DF_UserPermissions_UserManagement DEFAULT (0),
        ManageCategories BIT NOT NULL CONSTRAINT DF_UserPermissions_ManageCategories DEFAULT (0),
        CONSTRAINT FK_UserPermissions_User FOREIGN KEY (UserId)
            REFERENCES dbo.Users(Id) ON DELETE CASCADE
    );
END;
GO

IF COL_LENGTH(N'dbo.UserPermissions', N'EditReports') IS NULL
BEGIN
    ALTER TABLE dbo.UserPermissions
        ADD EditReports BIT NOT NULL CONSTRAINT DF_UserPermissions_EditReports DEFAULT (0);
END;
GO

SELECT
    u.Id,
    u.Username,
    u.FullName,
    u.Email,
    u.Mobile,
    u.Gender,
    u.DateOfBirth,
    u.Role,
    u.IsActive,
    u.LastLoginAt,
    u.CreatedAt
FROM dbo.Users u
ORDER BY u.Id;
GO
