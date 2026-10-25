USE PharmacyInventoryDB_New;
GO

IF OBJECT_ID(N'dbo.InventorySessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventorySessions
    (
        Id INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_InventorySessions PRIMARY KEY,
        LocationId INT NOT NULL,
        StartedAt DATETIME2 NOT NULL,
        CompletedAt DATETIME2 NULL,
        Status TINYINT NOT NULL
            CONSTRAINT DF_InventorySessions_Status DEFAULT (0),
        CreatedBy INT NULL,
        Notes NVARCHAR(500) NULL,
        CONSTRAINT FK_InventorySessions_Location
            FOREIGN KEY (LocationId)
            REFERENCES dbo.StockLocations(Id)
    );

    CREATE INDEX IX_InventorySessions_LocationId
        ON dbo.InventorySessions(LocationId);

    CREATE INDEX IX_InventorySessions_StartedAt
        ON dbo.InventorySessions(StartedAt DESC);
END;
GO

IF OBJECT_ID(N'dbo.InventoryCounts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryCounts
    (
        Id INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_InventoryCounts PRIMARY KEY,
        SessionId INT NOT NULL,
        ProductId INT NOT NULL,
        SystemQuantity DECIMAL(18,3) NOT NULL,
        ActualQuantity DECIMAL(18,3) NOT NULL,
        CountedAt DATETIME2 NOT NULL,
        CONSTRAINT FK_InventoryCounts_Session
            FOREIGN KEY (SessionId)
            REFERENCES dbo.InventorySessions(Id),
        CONSTRAINT FK_InventoryCounts_Product
            FOREIGN KEY (ProductId)
            REFERENCES dbo.Products(Id)
    );

    CREATE UNIQUE INDEX UX_InventoryCounts_Session_Product
        ON dbo.InventoryCounts(SessionId, ProductId);

    CREATE INDEX IX_InventoryCounts_ProductId
        ON dbo.InventoryCounts(ProductId);
END;
GO

SELECT
    'InventorySessions' AS TableName,
    COUNT(*) AS [Rows]
FROM dbo.InventorySessions
UNION ALL
SELECT
    'InventoryCounts',
    COUNT(*)
FROM dbo.InventoryCounts;
GO
