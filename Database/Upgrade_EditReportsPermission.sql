USE PharmacyInventoryDB_New;
GO

IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NULL
BEGIN
    PRINT 'UserPermissions table does not exist. Run AddUserManagementTables.sql first.';
    RETURN;
END;
GO

IF COL_LENGTH(N'dbo.UserPermissions', N'EditReports') IS NULL
BEGIN
    ALTER TABLE dbo.UserPermissions
        ADD EditReports BIT NOT NULL
            CONSTRAINT DF_UserPermissions_EditReports DEFAULT (0);
END;
GO

SELECT UserId, EditReports
FROM dbo.UserPermissions
ORDER BY UserId;
GO
