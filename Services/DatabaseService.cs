using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using PharmacyInventorySystem.Models;

namespace PharmacyInventorySystem.Services
{
    /// <summary>
    /// SQL Server persistence for the current pharmacy inventory.
    /// The Excel file is parsed completely before this service changes current stock.
    /// The replacement itself is executed inside one SQL transaction.
    /// </summary>
    public sealed class DatabaseService
    {
        private const string ConnectionString =
            @"Server=EL3ALMEY;Database=PharmacyInventoryDB_New;Trusted_Connection=True;TrustServerCertificate=True;";

        private static readonly Dictionary<string, string> ParentLocationMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ستي مول"] = "سيتي مول",
                ["داخي سيتى مول"] = "سيتي مول",
                ["داخلي سيتي مول"] = "سيتي مول",
                ["داخلى سيتي مول"] = "سيتي مول",
                ["داخلى اسلام"] = "اسلام",
                ["داخلى العطيفى"] = "العطيفي",
                ["داخلي الحدائق"] = "حدائق الاهرام",
                ["داخلى امانى"] = "امانى",
                ["داخلي المول"] = "المول",
                ["داخلي مروان"] = "مروان",
                ["داخلي الكوم"] = "الكوم",
                ["داخلي الفردوس"] = "الفردوس",
                ["داخلي بولاق"] = "بولاق",
                ["داخلي المقطم"] = "المقطم"
            };

        public bool HasInventory()
        {
            using var connection = OpenConnection();
            using var command = new SqlCommand(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM StockBalances) THEN 1 ELSE 0 END",
                connection);

            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }

        public string GetLatestImportFileName()
        {
            using var connection = OpenConnection();
            using var command = new SqlCommand(
                "SELECT TOP (1) FileName FROM ImportBatches WHERE Status = N'Completed' ORDER BY Id DESC",
                connection);

            return command.ExecuteScalar() as string ?? string.Empty;
        }

        public InventoryLoadResult LoadCurrentInventory()
        {
            var result = new InventoryLoadResult();

            using var connection = OpenConnection();

            var locations = new List<(int Id, string Name)>();
            using (var locationCommand = new SqlCommand(
                       "SELECT Id, Name FROM StockLocations WHERE IsActive = 1 ORDER BY Id",
                       connection))
            using (var reader = locationCommand.ExecuteReader())
            {
                while (reader.Read())
                    locations.Add((reader.GetInt32(0), reader.GetString(1)));
            }

            result.BranchNames = locations.Select(x => x.Name).ToList();

            var products = new Dictionary<int, Product>();
            const string productSql = @"
SELECT Id, Code, Name, Company, Unit, SellingPrice
FROM Products
WHERE IsActive = 1
ORDER BY Id;";

            using (var productCommand = new SqlCommand(productSql, connection))
            using (var reader = productCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    products[id] = new Product
                    {
                        Code = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        Company = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Unit = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        Price = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5)
                    };
                }
            }

            var locationNames = locations.ToDictionary(x => x.Id, x => x.Name);
            const string balanceSql = @"
SELECT ProductId, LocationId, Quantity
FROM StockBalances
ORDER BY ProductId, LocationId;";

            using (var balanceCommand = new SqlCommand(balanceSql, connection))
            using (var reader = balanceCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    int productId = reader.GetInt32(0);
                    if (!products.TryGetValue(productId, out var product))
                        continue;

                    int locationId = reader.GetInt32(1);
                    decimal quantity = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2);

                    if (locationNames.TryGetValue(locationId, out var locationName) && quantity != 0m)
                        product.BranchStock[locationName] = quantity;
                }
            }

            foreach (var product in products.Values)
            {
                product.TotalQuantity = product.BranchStock.Values.Sum();
                result.Products.Add(product);
            }

            return result;
        }

        public void ClearCurrentInventory()
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                using (var command = new SqlCommand(
                           "DELETE FROM StockBalances;",
                           connection,
                           transaction))
                {
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void ImportInventory(
            InventoryLoadResult loaded,
            string fileName,
            IProgress<int>? progress = null)
        {
            if (loaded == null)
                throw new ArgumentNullException(nameof(loaded));

            if (loaded.Products.Count == 0)
                throw new InvalidOperationException("The inventory contains no products.");

            if (loaded.BranchNames.Count == 0)
                throw new InvalidOperationException("The inventory contains no locations.");

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                progress?.Report(2);

                int batchId = InsertImportBatch(connection, transaction, fileName, loaded.Products.Count);
                progress?.Report(5);

                var locationIds = EnsureLocations(
                    connection,
                    transaction,
                    loaded.BranchNames);
                progress?.Report(15);

                CreateProductStage(connection, transaction);
                CreateProductMap(connection, transaction);
                BulkInsertProductStage(connection, transaction, loaded.Products);
                progress?.Report(38);

                InsertProductsAndBuildMap(connection, transaction);
                progress?.Report(52);

                CreateBalanceStage(connection, transaction);
                BulkInsertBalanceStage(
                    connection,
                    transaction,
                    loaded.Products,
                    locationIds);
                progress?.Report(70);

                // Current stock is replaced only now, after the Excel file has
                // already been parsed and all staging data has been loaded.
                using (var deleteCommand = new SqlCommand(
                           "DELETE FROM StockBalances;",
                           connection,
                           transaction))
                {
                    deleteCommand.CommandTimeout = 0;
                    deleteCommand.ExecuteNonQuery();
                }
                progress?.Report(78);

                InsertCurrentBalances(connection, transaction, batchId);
                progress?.Report(90);

                InsertImportBatchItems(connection, transaction, batchId);
                progress?.Report(96);

                UpdateImportBatchCompleted(
                    connection,
                    transaction,
                    batchId,
                    loaded.Products.Count);

                transaction.Commit();
                progress?.Report(100);
            }
            catch (Exception ex)
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // Preserve the original database error.
                }

                throw new InvalidOperationException(
                    "SQL Server could not save the inventory. The previous stock was kept unchanged.",
                    ex);
            }
        }

        private static SqlConnection OpenConnection()
        {
            var connection = new SqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        private static int InsertImportBatch(
            SqlConnection connection,
            SqlTransaction transaction,
            string fileName,
            int rowsRead)
        {
            const string sql = @"
INSERT INTO ImportBatches
    (FileName, ImportedBy, RowsRead, RowsImported, Status)
OUTPUT INSERTED.Id
VALUES
    (@FileName, NULL, @RowsRead, 0, N'Started');";

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@FileName", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(fileName) ? "Inventory.xlsx" : fileName;
            command.Parameters.Add("@RowsRead", SqlDbType.Int).Value = rowsRead;

            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static Dictionary<string, int> EnsureLocations(
            SqlConnection connection,
            SqlTransaction transaction,
            IEnumerable<string> names)
        {
            var normalizedNames = names
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // First make sure every parent that is actually present in the Excel
            // file exists. This avoids relying on the column order of the sheet.
            foreach (var name in normalizedNames)
            {
                if (ParentLocationMap.ContainsKey(name))
                    continue;

                int existingId = GetLocationId(connection, transaction, name);
                if (existingId == 0)
                {
                    existingId = InsertLocation(
                        connection,
                        transaction,
                        name,
                        GetLocationType(name),
                        null);
                }
                else
                {
                    using var update = new SqlCommand(
                        "UPDATE StockLocations SET IsActive = 1 WHERE Id = @Id",
                        connection,
                        transaction);
                    update.Parameters.Add("@Id", SqlDbType.Int).Value = existingId;
                    update.ExecuteNonQuery();
                }

                result[name] = existingId;
            }

            // Now insert/update child locations. Every parent ID used here has
            // already been created in this transaction or existed beforehand.
            foreach (var name in normalizedNames)
            {
                string parentName = ParentLocationMap.TryGetValue(name, out var parent)
                    ? parent
                    : null;

                int? parentId = null;
                if (!string.IsNullOrWhiteSpace(parentName))
                {
                    if (!result.TryGetValue(parentName, out int resolvedParentId))
                        resolvedParentId = GetLocationId(connection, transaction, parentName);

                    // If the parent is not in the Excel sheet, do not create a
                    // phantom parent (for example, داخلي الكوم without الكوم).
                    if (resolvedParentId != 0)
                        parentId = resolvedParentId;
                }

                int existingId = GetLocationId(connection, transaction, name);

                if (existingId == 0)
                {
                    InsertLocation(
                        connection,
                        transaction,
                        name,
                        GetLocationType(name),
                        parentId);
                }
                else
                {
                    using var update = new SqlCommand(
                        "UPDATE StockLocations SET ParentLocationId = @ParentId, IsActive = 1 WHERE Id = @Id",
                        connection,
                        transaction);
                    update.Parameters.Add("@ParentId", SqlDbType.Int).Value =
                        parentId.HasValue ? parentId.Value : DBNull.Value;
                    update.Parameters.Add("@Id", SqlDbType.Int).Value = existingId;
                    update.ExecuteNonQuery();
                }

                result[name] = existingId != 0 ? existingId : GetLocationId(connection, transaction, name);
            }

            foreach (var name in normalizedNames)
            {
                if (!result.TryGetValue(name, out int id) || id == 0)
                {
                    id = GetLocationId(connection, transaction, name);
                    if (id == 0)
                        throw new InvalidOperationException($"Could not create stock location: {name}");
                    result[name] = id;
                }
            }

            return result;
        }

        private static int GetLocationId(
            SqlConnection connection,
            SqlTransaction transaction,
            string name)
        {
            using var command = new SqlCommand(
                "SELECT TOP (1) Id FROM StockLocations WHERE Name = @Name ORDER BY Id",
                connection,
                transaction);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 500).Value = name;

            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        private static int InsertLocation(
            SqlConnection connection,
            SqlTransaction transaction,
            string name,
            string type,
            int? parentId)
        {
            const string sql = @"
INSERT INTO StockLocations
    (Name, Code, LocationType, ParentLocationId, IsActive)
OUTPUT INSERTED.Id
VALUES
    (@Name, NULL, @LocationType, @ParentLocationId, 1);";

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 500).Value = name;
            command.Parameters.Add("@LocationType", SqlDbType.NVarChar, 100).Value = type;
            command.Parameters.Add("@ParentLocationId", SqlDbType.Int).Value =
                parentId.HasValue ? parentId.Value : DBNull.Value;

            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static string GetLocationType(string name)
        {
            if (ParentLocationMap.ContainsKey(name))
                return "InternalWarehouse";

            if (name.StartsWith("م.", StringComparison.OrdinalIgnoreCase))
                return "Warehouse";

            return "Branch";
        }

        private static void CreateProductStage(SqlConnection connection, SqlTransaction transaction)
        {
            const string sql = @"
CREATE TABLE #ProductStage
(
    SourceRowId INT NOT NULL,
    Code NVARCHAR(100) NULL,
    Name NVARCHAR(500) NULL,
    Company NVARCHAR(500) NULL,
    Unit NVARCHAR(100) NULL,
    SellingPrice DECIMAL(18,3) NOT NULL
);";

            using var command = new SqlCommand(sql, connection, transaction);
            command.ExecuteNonQuery();
        }

        private static void CreateProductMap(SqlConnection connection, SqlTransaction transaction)
        {
            const string sql = @"
CREATE TABLE #ProductMap
(
    SourceRowId INT NOT NULL,
    ProductId INT NOT NULL
);";

            using var command = new SqlCommand(sql, connection, transaction);
            command.ExecuteNonQuery();
        }

        private static void BulkInsertProductStage(
            SqlConnection connection,
            SqlTransaction transaction,
            IList<Product> products)
        {
            using var table = new DataTable();
            table.Columns.Add("SourceRowId", typeof(int));
            table.Columns.Add("Code", typeof(string));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Company", typeof(string));
            table.Columns.Add("Unit", typeof(string));
            table.Columns.Add("SellingPrice", typeof(decimal));

            for (int i = 0; i < products.Count; i++)
            {
                var product = products[i];
                table.Rows.Add(
                    i,
                    Limit(product.Code, 100),
                    Limit(product.Name, 500),
                    Limit(product.Company, 500),
                    Limit(product.Unit, 100),
                    product.Price);
            }

            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = "#ProductStage",
                BatchSize = 5000,
                BulkCopyTimeout = 0
            };

            foreach (DataColumn column in table.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

            bulk.WriteToServer(table);
        }

        private static void InsertProductsAndBuildMap(
            SqlConnection connection,
            SqlTransaction transaction)
        {
            // Deactivate the previously active product set first.
            // Existing Product rows are reused whenever possible so repeated
            // Excel imports do not create another full copy of the catalog.
            using (var deactivateCommand = new SqlCommand(
                       "UPDATE Products SET IsActive = 0 WHERE IsActive = 1;",
                       connection,
                       transaction))
            {
                deactivateCommand.CommandTimeout = 0;
                deactivateCommand.ExecuteNonQuery();
            }

            // Number rows inside the current Excel import. This lets us handle
            // repeated Codes in the source file deterministically.
            const string createNumberedStageSql = @"
CREATE TABLE #ProductStageNumbered
(
    SourceRowId INT NOT NULL,
    Code NVARCHAR(100) NULL,
    Name NVARCHAR(500) NULL,
    Company NVARCHAR(500) NULL,
    Unit NVARCHAR(100) NULL,
    SellingPrice DECIMAL(18,3) NOT NULL,
    CodeOccurrence INT NOT NULL
);

INSERT INTO #ProductStageNumbered
(
    SourceRowId,
    Code,
    Name,
    Company,
    Unit,
    SellingPrice,
    CodeOccurrence
)
SELECT
    SourceRowId,
    Code,
    Name,
    Company,
    Unit,
    SellingPrice,
    ROW_NUMBER() OVER
    (
        PARTITION BY
            CASE
                WHEN NULLIF(LTRIM(RTRIM(Code)), N'') IS NULL
                    THEN N'__BLANK_CODE__'
                ELSE LTRIM(RTRIM(Code))
            END
        ORDER BY SourceRowId
    ) AS CodeOccurrence
FROM #ProductStage;";

            using (var command = new SqlCommand(
                       createNumberedStageSql,
                       connection,
                       transaction))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }

            // Map non-blank Codes to existing Products first.
            const string existingMapSql = @"
;WITH ExistingProducts AS
(
    SELECT
        p.Id AS ProductId,
        LTRIM(RTRIM(p.Code)) AS Code,
        ROW_NUMBER() OVER
        (
            PARTITION BY LTRIM(RTRIM(p.Code))
            ORDER BY p.Id
        ) AS CodeOccurrence
    FROM Products AS p
    WHERE NULLIF(LTRIM(RTRIM(p.Code)), N'') IS NOT NULL
)
INSERT INTO #ProductMap
(
    SourceRowId,
    ProductId
)
SELECT
    s.SourceRowId,
    e.ProductId
FROM #ProductStageNumbered AS s
INNER JOIN ExistingProducts AS e
    ON e.Code = LTRIM(RTRIM(s.Code))
   AND e.CodeOccurrence = s.CodeOccurrence
WHERE NULLIF(LTRIM(RTRIM(s.Code)), N'') IS NOT NULL;";

            using (var command = new SqlCommand(
                       existingMapSql,
                       connection,
                       transaction))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }

            // Insert only rows that could not be matched to an existing Product.
            const string insertNewProductsSql = @"
INSERT INTO Products
(
    Code,
    Name,
    Company,
    Unit,
    SellingPrice,
    IsActive
)
SELECT
    NULLIF(LTRIM(RTRIM(s.Code)), N''),
    s.Name,
    s.Company,
    s.Unit,
    s.SellingPrice,
    1
FROM #ProductStageNumbered AS s
WHERE NULLIF(LTRIM(RTRIM(s.Code)), N'') IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM #ProductMap AS m
      WHERE m.SourceRowId = s.SourceRowId
  );";

            using (var command = new SqlCommand(
                       insertNewProductsSql,
                       connection,
                       transaction))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }

            // Rebuild the Code mapping after inserting the missing products.
            const string completeMapSql = @"
;WITH AllProducts AS
(
    SELECT
        p.Id AS ProductId,
        LTRIM(RTRIM(p.Code)) AS Code,
        ROW_NUMBER() OVER
        (
            PARTITION BY LTRIM(RTRIM(p.Code))
            ORDER BY p.Id
        ) AS CodeOccurrence
    FROM Products AS p
    WHERE NULLIF(LTRIM(RTRIM(p.Code)), N'') IS NOT NULL
)
INSERT INTO #ProductMap
(
    SourceRowId,
    ProductId
)
SELECT
    s.SourceRowId,
    p.ProductId
FROM #ProductStageNumbered AS s
INNER JOIN AllProducts AS p
    ON p.Code = LTRIM(RTRIM(s.Code))
   AND p.CodeOccurrence = s.CodeOccurrence
WHERE NULLIF(LTRIM(RTRIM(s.Code)), N'') IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM #ProductMap AS existingMap
      WHERE existingMap.SourceRowId = s.SourceRowId
  );";

            using (var command = new SqlCommand(
                       completeMapSql,
                       connection,
                       transaction))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }

            // Handle rows with a blank Code using the product identity fields.
            const string blankCodeMapSql = @"
;WITH BlankProducts AS
(
    SELECT
        p.Id AS ProductId,
        p.Name,
        p.Company,
        p.Unit,
        p.SellingPrice,
        ROW_NUMBER() OVER
        (
            PARTITION BY
                ISNULL(p.Name, N''),
                ISNULL(p.Company, N''),
                ISNULL(p.Unit, N''),
                p.SellingPrice
            ORDER BY p.Id
        ) AS ProductOccurrence
    FROM Products AS p
    WHERE NULLIF(LTRIM(RTRIM(p.Code)), N'') IS NULL
),
BlankStage AS
(
    SELECT
        s.SourceRowId,
        s.Name,
        s.Company,
        s.Unit,
        s.SellingPrice,
        ROW_NUMBER() OVER
        (
            PARTITION BY
                ISNULL(s.Name, N''),
                ISNULL(s.Company, N''),
                ISNULL(s.Unit, N''),
                s.SellingPrice
            ORDER BY s.SourceRowId
        ) AS ProductOccurrence
    FROM #ProductStageNumbered AS s
    WHERE NULLIF(LTRIM(RTRIM(s.Code)), N'') IS NULL
)
INSERT INTO #ProductMap
(
    SourceRowId,
    ProductId
)
SELECT
    s.SourceRowId,
    p.ProductId
FROM BlankStage AS s
INNER JOIN BlankProducts AS p
    ON ISNULL(p.Name, N'') = ISNULL(s.Name, N'')
   AND ISNULL(p.Company, N'') = ISNULL(s.Company, N'')
   AND ISNULL(p.Unit, N'') = ISNULL(s.Unit, N'')
   AND p.SellingPrice = s.SellingPrice
   AND p.ProductOccurrence = s.ProductOccurrence
WHERE NOT EXISTS
(
    SELECT 1
    FROM #ProductMap AS existingMap
    WHERE existingMap.SourceRowId = s.SourceRowId
);";

            using (var command = new SqlCommand(
                       blankCodeMapSql,
                       connection,
                       transaction))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }

            // Insert remaining blank-Code rows that still have no mapping.
            const string insertRemainingBlankProductsSql = @"
INSERT INTO Products
(
    Code,
    Name,
    Company,
    Unit,
    SellingPrice,
    IsActive
)
SELECT
    N'',
    s.Name,
    s.Company,
    s.Unit,
    s.SellingPrice,
    1
FROM #ProductStageNumbered AS s
WHERE NULLIF(LTRIM(RTRIM(s.Code)), N'') IS NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM #ProductMap AS m
      WHERE m.SourceRowId = s.SourceRowId
  );";

            using (var command = new SqlCommand(
                       insertRemainingBlankProductsSql,
                       connection,
                       transaction))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }

            // Map any blank-Code products inserted immediately above.
            const string finalBlankMapSql = @"
;WITH BlankProducts AS
(
    SELECT
        p.Id AS ProductId,
        p.Name,
        p.Company,
        p.Unit,
        p.SellingPrice,
        ROW_NUMBER() OVER
        (
            PARTITION BY
                ISNULL(p.Name, N''),
                ISNULL(p.Company, N''),
                ISNULL(p.Unit, N''),
                p.SellingPrice
            ORDER BY p.Id
        ) AS ProductOccurrence
    FROM Products AS p
    WHERE NULLIF(LTRIM(RTRIM(p.Code)), N'') IS NULL
),
BlankStage AS
(
    SELECT
        s.SourceRowId,
        s.Name,
        s.Company,
        s.Unit,
        s.SellingPrice,
        ROW_NUMBER() OVER
        (
            PARTITION BY
                ISNULL(s.Name, N''),
                ISNULL(s.Company, N''),
                ISNULL(s.Unit, N''),
                s.SellingPrice
            ORDER BY s.SourceRowId
        ) AS ProductOccurrence
    FROM #ProductStageNumbered AS s
    WHERE NULLIF(LTRIM(RTRIM(s.Code)), N'') IS NULL
)
INSERT INTO #ProductMap
(
    SourceRowId,
    ProductId
)
SELECT
    s.SourceRowId,
    p.ProductId
FROM BlankStage AS s
INNER JOIN BlankProducts AS p
    ON ISNULL(p.Name, N'') = ISNULL(s.Name, N'')
   AND ISNULL(p.Company, N'') = ISNULL(s.Company, N'')
   AND ISNULL(p.Unit, N'') = ISNULL(s.Unit, N'')
   AND p.SellingPrice = s.SellingPrice
   AND p.ProductOccurrence = s.ProductOccurrence
WHERE NOT EXISTS
(
    SELECT 1
    FROM #ProductMap AS existingMap
    WHERE existingMap.SourceRowId = s.SourceRowId
);";

            using (var command = new SqlCommand(
                       finalBlankMapSql,
                       connection,
                       transaction))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }

            // Safety check: every Excel row must have exactly one mapping.
            const string unmappedSql = @"
SELECT COUNT(*)
FROM #ProductStage AS s
LEFT JOIN #ProductMap AS m
    ON m.SourceRowId = s.SourceRowId
WHERE m.SourceRowId IS NULL;";

            using (var command = new SqlCommand(
                       unmappedSql,
                       connection,
                       transaction))
            {
                command.CommandTimeout = 0;

                int unmappedCount = Convert.ToInt32(command.ExecuteScalar());

                if (unmappedCount > 0)
                {
                    throw new InvalidOperationException(
                        $"Could not map {unmappedCount} Excel product row(s) to SQL products.");
                }
            }
        }

        private static void CreateBalanceStage(SqlConnection connection, SqlTransaction transaction)
        {
            const string sql = @"
CREATE TABLE #BalanceStage
(
    SourceRowId INT NOT NULL,
    LocationId INT NOT NULL,
    Quantity DECIMAL(18,3) NOT NULL,
    SellingPrice DECIMAL(18,3) NOT NULL
);";

            using var command = new SqlCommand(sql, connection, transaction);
            command.ExecuteNonQuery();
        }

        private static void BulkInsertBalanceStage(
            SqlConnection connection,
            SqlTransaction transaction,
            IList<Product> products,
            Dictionary<string, int> locationIds)
        {
            using var table = new DataTable();
            table.Columns.Add("SourceRowId", typeof(int));
            table.Columns.Add("LocationId", typeof(int));
            table.Columns.Add("Quantity", typeof(decimal));
            table.Columns.Add("SellingPrice", typeof(decimal));

            foreach (var pair in products.Select((product, index) => new { product, index }))
            {
                foreach (var stock in pair.product.BranchStock)
                {
                    if (stock.Value == 0m)
                        continue;

                    if (!locationIds.TryGetValue(stock.Key, out int locationId))
                        throw new InvalidOperationException($"Unknown inventory location: {stock.Key}");

                    table.Rows.Add(
                        pair.index,
                        locationId,
                        stock.Value,
                        pair.product.Price);
                }
            }

            if (table.Rows.Count == 0)
                return;

            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = "#BalanceStage",
                BatchSize = 10000,
                BulkCopyTimeout = 0
            };

            foreach (DataColumn column in table.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

            bulk.WriteToServer(table);
        }

        private static void InsertCurrentBalances(SqlConnection connection, SqlTransaction transaction, int batchId)
        {
            const string sql = @"
INSERT INTO StockBalances
    (ProductId, LocationId, Quantity, UpdatedAt, ImportBatchId)
SELECT
    pm.ProductId,
    bs.LocationId,
    bs.Quantity,
    SYSDATETIME(),
    @ImportBatchId
FROM #BalanceStage bs
INNER JOIN #ProductMap pm ON pm.SourceRowId = bs.SourceRowId;";

            using var command = new SqlCommand(sql, connection, transaction)
            {
                CommandTimeout = 0
            };
            command.Parameters.Add("@ImportBatchId", SqlDbType.Int).Value = batchId;
            command.ExecuteNonQuery();
        }

        private static void InsertImportBatchItems(
            SqlConnection connection,
            SqlTransaction transaction,
            int batchId)
        {
            const string sql = @"
INSERT INTO ImportBatchItems
    (ImportBatchId, ProductId, LocationId, Quantity, SellingPrice, ImportedAt)
SELECT
    @BatchId,
    pm.ProductId,
    bs.LocationId,
    bs.Quantity,
    bs.SellingPrice,
    SYSDATETIME()
FROM #BalanceStage bs
INNER JOIN #ProductMap pm ON pm.SourceRowId = bs.SourceRowId;";

            using var command = new SqlCommand(sql, connection, transaction)
            {
                CommandTimeout = 0
            };
            command.Parameters.Add("@BatchId", SqlDbType.Int).Value = batchId;
            command.ExecuteNonQuery();
        }

        private static void UpdateImportBatchCompleted(
            SqlConnection connection,
            SqlTransaction transaction,
            int batchId,
            int rowsImported)
        {
            const string sql = @"
UPDATE ImportBatches
SET Status = N'Completed',
    RowsImported = @RowsImported,
    ErrorMessage = NULL
WHERE Id = @Id;";

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@RowsImported", SqlDbType.Int).Value = rowsImported;
            command.Parameters.Add("@Id", SqlDbType.Int).Value = batchId;
            command.ExecuteNonQuery();
        }

        public int SaveInventoryAudit(IList<Product> products, string branchName)
        {
            if (products == null)
                throw new ArgumentNullException(nameof(products));

            if (products.Count == 0)
                throw new InvalidOperationException("There are no inventory items to save in the audit report.");

            if (string.IsNullOrWhiteSpace(branchName))
                throw new InvalidOperationException("A specific branch or warehouse must be selected before saving the audit report.");

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                EnsureAuditTables(connection, transaction);

                int locationId = GetLocationId(connection, transaction, branchName.Trim());
                if (locationId == 0)
                    throw new InvalidOperationException($"The selected branch or warehouse was not found: {branchName}");

                int sessionId;
                const string sessionSql = @"
INSERT INTO InventorySessions
    (LocationId, StartedAt, CompletedAt, Status, CreatedBy, Notes)
OUTPUT INSERTED.Id
VALUES
    (@LocationId, SYSDATETIME(), SYSDATETIME(), 1, NULL, N'Inventory audit report saved from the audit screen.');";

                using (var command = new SqlCommand(sessionSql, connection, transaction))
                {
                    command.CommandTimeout = 0;
                    command.Parameters.Add("@LocationId", SqlDbType.Int).Value = locationId;
                    sessionId = Convert.ToInt32(command.ExecuteScalar());
                }

                CreateAuditStage(connection, transaction);
                BulkInsertAuditStage(connection, transaction, products);
                CreateAuditMap(connection, transaction);
                BuildAuditProductMap(connection, transaction);

                const string insertCountsSql = @"
INSERT INTO InventoryCounts
    (SessionId, ProductId, SystemQuantity, ActualQuantity, CountedAt)
SELECT
    @SessionId,
    m.ProductId,
    a.SystemQuantity,
    a.ActualQuantity,
    SYSDATETIME()
FROM #AuditStage a
INNER JOIN #AuditMap m
    ON m.SourceRowId = a.SourceRowId;";

                using (var command = new SqlCommand(insertCountsSql, connection, transaction))
                {
                    command.CommandTimeout = 0;
                    command.Parameters.Add("@SessionId", SqlDbType.Int).Value = sessionId;
                    command.ExecuteNonQuery();
                }

                int savedCount;
                using (var command = new SqlCommand(
                           "SELECT COUNT(*) FROM InventoryCounts WHERE SessionId = @SessionId;",
                           connection,
                           transaction))
                {
                    command.CommandTimeout = 0;
                    command.Parameters.Add("@SessionId", SqlDbType.Int).Value = sessionId;
                    savedCount = Convert.ToInt32(command.ExecuteScalar());
                }

                if (savedCount != products.Count)
                {
                    throw new InvalidOperationException(
                        $"The audit report could not be saved completely. Expected {products.Count:N0} item(s), but only {savedCount:N0} were saved.");
                }

                transaction.Commit();
                return savedCount;
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // Preserve the original error.
                }

                throw;
            }
        }


        public List<string> GetAllBranchNames()
        {
            var result = new List<string>();

            using var connection = OpenConnection();

            const string tableCheckSql = @"
SELECT CASE
    WHEN OBJECT_ID(N'dbo.StockLocations', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";

            using (var check = new SqlCommand(tableCheckSql, connection))
            {
                if (Convert.ToInt32(check.ExecuteScalar()) != 1)
                    return result;
            }

            const string sql = @"
SELECT DISTINCT LTRIM(RTRIM(Name))
FROM StockLocations
WHERE NULLIF(LTRIM(RTRIM(Name)), '') IS NOT NULL
ORDER BY LTRIM(RTRIM(Name));";

            using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = 0;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim();
                if (!string.IsNullOrWhiteSpace(name) &&
                    !result.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        public List<string> GetAuditReportBranchNames()
        {
            var result = new List<string>();

            using var connection = OpenConnection();

            const string tableCheckSql = @"
SELECT CASE
    WHEN OBJECT_ID(N'dbo.InventorySessions', N'U') IS NOT NULL
     AND OBJECT_ID(N'dbo.StockLocations', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";

            using (var check = new SqlCommand(tableCheckSql, connection))
            {
                if (Convert.ToInt32(check.ExecuteScalar()) != 1)
                    return result;
            }

            const string sql = @"
SELECT DISTINCT l.Name
FROM InventorySessions s
INNER JOIN StockLocations l ON l.Id = s.LocationId
WHERE NULLIF(LTRIM(RTRIM(l.Name)), '') IS NOT NULL
ORDER BY l.Name;";

            using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = 0;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim();
                if (!string.IsNullOrWhiteSpace(name) &&
                    !result.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        public List<AuditReportSession> GetAuditReportSessions(
            string? branchName,
            DateTime from,
            DateTime to)
        {
            var result = new List<AuditReportSession>();

            using var connection = OpenConnection();

            const string tableCheckSql = @"
SELECT CASE
    WHEN OBJECT_ID(N'dbo.InventorySessions', N'U') IS NOT NULL
     AND OBJECT_ID(N'dbo.InventoryCounts', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";

            using (var check = new SqlCommand(tableCheckSql, connection))
            {
                if (Convert.ToInt32(check.ExecuteScalar()) != 1)
                    return result;
            }

            const string sql = @"
SELECT
    s.Id,
    l.Name,
    s.StartedAt,
    s.CompletedAt,
    COUNT(ic.Id) AS ItemCount,
    COALESCE(SUM(CASE WHEN ic.ActualQuantity < ic.SystemQuantity
        THEN (ic.SystemQuantity - ic.ActualQuantity) * p.SellingPrice ELSE 0 END), 0) AS ShortageValue,
    COALESCE(SUM(CASE WHEN ic.ActualQuantity > ic.SystemQuantity
        THEN (ic.ActualQuantity - ic.SystemQuantity) * p.SellingPrice ELSE 0 END), 0) AS SurplusValue,
    SUM(CASE WHEN ic.ActualQuantity < ic.SystemQuantity THEN 1 ELSE 0 END) AS ShortageItemCount,
    SUM(CASE WHEN ic.ActualQuantity > ic.SystemQuantity THEN 1 ELSE 0 END) AS SurplusItemCount,
    s.Status
FROM InventorySessions s
INNER JOIN StockLocations l ON l.Id = s.LocationId
LEFT JOIN InventoryCounts ic ON ic.SessionId = s.Id
LEFT JOIN Products p ON p.Id = ic.ProductId
WHERE s.StartedAt >= @From
  AND s.StartedAt <= @To
  AND (@BranchName IS NULL OR l.Name = @BranchName)
GROUP BY s.Id, l.Name, s.StartedAt, s.CompletedAt, s.Status
ORDER BY s.StartedAt DESC;";

            using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = 0;
            command.Parameters.Add("@From", SqlDbType.DateTime2).Value = from;
            command.Parameters.Add("@To", SqlDbType.DateTime2).Value = to;
            command.Parameters.Add("@BranchName", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(branchName) ? DBNull.Value : branchName.Trim();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new AuditReportSession
                {
                    SessionId = reader.GetInt32(0),
                    BranchName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    StartedAt = reader.GetDateTime(2),
                    CompletedAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                    ItemCount = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4)),
                    ShortageValue = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                    SurplusValue = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6),
                    ShortageItemCount = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7)),
                    SurplusItemCount = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8)),
                    Status = reader.IsDBNull(9) ? "Completed" : Convert.ToString(reader.GetByte(9)) == "1" ? "Completed" : "In Progress"
                });
            }

            return result;
        }

        public List<AuditReportItem> GetAuditReportDetails(int sessionId)
        {
            var result = new List<AuditReportItem>();

            using var connection = OpenConnection();

            const string sql = @"
SELECT
    ic.Id,
    COALESCE(p.Code, N''),
    COALESCE(p.Name, N''),
    ic.SystemQuantity,
    ic.ActualQuantity,
    COALESCE(p.SellingPrice, 0),
    CASE
        WHEN ic.ActualQuantity < ic.SystemQuantity THEN N'Shortage'
        WHEN ic.ActualQuantity > ic.SystemQuantity THEN N'Surplus'
        ELSE N'Exact'
    END AS ItemStatus
FROM InventoryCounts ic
INNER JOIN Products p ON p.Id = ic.ProductId
WHERE ic.SessionId = @SessionId
ORDER BY
    CASE
        WHEN ic.ActualQuantity < ic.SystemQuantity THEN 0
        WHEN ic.ActualQuantity > ic.SystemQuantity THEN 1
        ELSE 2
    END,
    p.Name;";

            using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = 0;
            command.Parameters.Add("@SessionId", SqlDbType.Int).Value = sessionId;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var systemQuantity = reader.GetDecimal(3);
                var actualQuantity = reader.GetDecimal(4);
                var price = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5);

                result.Add(new AuditReportItem
                {
                    CountId = reader.GetInt32(0),
                    Code = reader.GetString(1),
                    ProductName = reader.GetString(2),
                    SystemQuantity = systemQuantity,
                    ActualQuantity = actualQuantity,
                    Price = price
                });
            }

            return result;
        }

        public bool UpdateAuditReportItems(int sessionId, IList<AuditReportItem> items)
        {
            if (sessionId <= 0 || items == null || items.Count == 0)
                return false;

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                const string sql = @"
UPDATE InventoryCounts
SET ActualQuantity = @ActualQuantity,
    CountedAt = SYSDATETIME()
WHERE Id = @CountId AND SessionId = @SessionId;";

                int updated = 0;
                foreach (var item in items)
                {
                    using var command = new SqlCommand(sql, connection, transaction);
                    command.CommandTimeout = 0;
                    command.Parameters.Add("@ActualQuantity", SqlDbType.Decimal).Value = item.ActualQuantity;
                    command.Parameters.Add("@CountId", SqlDbType.Int).Value = item.CountId;
                    command.Parameters.Add("@SessionId", SqlDbType.Int).Value = sessionId;
                    command.Parameters["@ActualQuantity"].Precision = 18;
                    command.Parameters["@ActualQuantity"].Scale = 3;
                    updated += command.ExecuteNonQuery();
                }

                transaction.Commit();
                return updated > 0;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        public bool DeleteAuditReport(int sessionId)
        {
            if (sessionId <= 0)
                return false;

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                using (var countCommand = new SqlCommand(
                           "DELETE FROM InventoryCounts WHERE SessionId = @SessionId;",
                           connection,
                           transaction))
                {
                    countCommand.CommandTimeout = 0;
                    countCommand.Parameters.Add("@SessionId", SqlDbType.Int).Value = sessionId;
                    countCommand.ExecuteNonQuery();
                }

                int deletedSessions;
                using (var sessionCommand = new SqlCommand(
                           "DELETE FROM InventorySessions WHERE Id = @SessionId;",
                           connection,
                           transaction))
                {
                    sessionCommand.CommandTimeout = 0;
                    sessionCommand.Parameters.Add("@SessionId", SqlDbType.Int).Value = sessionId;
                    deletedSessions = sessionCommand.ExecuteNonQuery();
                }

                transaction.Commit();
                return deletedSessions > 0;
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // Preserve the original error.
                }

                throw;
            }
        }

        private static void EnsureAuditTables(
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
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
END;";

            using var command = new SqlCommand(sql, connection, transaction)
            {
                CommandTimeout = 0
            };
            command.ExecuteNonQuery();
        }

        private static void CreateAuditStage(
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
CREATE TABLE #AuditStage
(
    SourceRowId INT NOT NULL,
    Code NVARCHAR(100) NULL,
    Name NVARCHAR(500) NULL,
    Company NVARCHAR(500) NULL,
    Unit NVARCHAR(100) NULL,
    SellingPrice DECIMAL(18,3) NOT NULL,
    SystemQuantity DECIMAL(18,3) NOT NULL,
    ActualQuantity DECIMAL(18,3) NOT NULL
);";

            using var command = new SqlCommand(sql, connection, transaction)
            {
                CommandTimeout = 0
            };
            command.ExecuteNonQuery();
        }

        private static void BulkInsertAuditStage(
            SqlConnection connection,
            SqlTransaction transaction,
            IList<Product> products)
        {
            using var table = new DataTable();
            table.Columns.Add("SourceRowId", typeof(int));
            table.Columns.Add("Code", typeof(string));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Company", typeof(string));
            table.Columns.Add("Unit", typeof(string));
            table.Columns.Add("SellingPrice", typeof(decimal));
            table.Columns.Add("SystemQuantity", typeof(decimal));
            table.Columns.Add("ActualQuantity", typeof(decimal));

            for (int i = 0; i < products.Count; i++)
            {
                var product = products[i];
                table.Rows.Add(
                    i,
                    Limit(product.Code, 100),
                    Limit(product.Name, 500),
                    Limit(product.Company, 500),
                    Limit(product.Unit, 100),
                    product.Price,
                    product.System,
                    product.Court);
            }

            using var bulk = new SqlBulkCopy(
                connection,
                SqlBulkCopyOptions.Default,
                transaction)
            {
                DestinationTableName = "#AuditStage",
                BatchSize = 5000,
                BulkCopyTimeout = 0
            };

            foreach (DataColumn column in table.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

            bulk.WriteToServer(table);
        }

        private static void CreateAuditMap(
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
CREATE TABLE #AuditMap
(
    SourceRowId INT NOT NULL,
    ProductId INT NOT NULL
);";

            using var command = new SqlCommand(sql, connection, transaction)
            {
                CommandTimeout = 0
            };
            command.ExecuteNonQuery();
        }

        private static void BuildAuditProductMap(
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string numberedSql = @"
CREATE TABLE #AuditStageNumbered
(
    SourceRowId INT NOT NULL,
    Code NVARCHAR(100) NULL,
    Name NVARCHAR(500) NULL,
    Company NVARCHAR(500) NULL,
    Unit NVARCHAR(100) NULL,
    SellingPrice DECIMAL(18,3) NOT NULL,
    SystemQuantity DECIMAL(18,3) NOT NULL,
    ActualQuantity DECIMAL(18,3) NOT NULL,
    CodeOccurrence INT NOT NULL
);

INSERT INTO #AuditStageNumbered
(
    SourceRowId,
    Code,
    Name,
    Company,
    Unit,
    SellingPrice,
    SystemQuantity,
    ActualQuantity,
    CodeOccurrence
)
SELECT
    SourceRowId,
    Code,
    Name,
    Company,
    Unit,
    SellingPrice,
    SystemQuantity,
    ActualQuantity,
    ROW_NUMBER() OVER
    (
        PARTITION BY
            CASE
                WHEN NULLIF(LTRIM(RTRIM(Code)), N'') IS NULL
                    THEN N'__BLANK_CODE__'
                ELSE LTRIM(RTRIM(Code))
            END
        ORDER BY SourceRowId
    ) AS CodeOccurrence
FROM #AuditStage;";

            using (var command = new SqlCommand(numberedSql, connection, transaction))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }

            const string codeMapSql = @"
;WITH ExistingProducts AS
(
    SELECT
        p.Id AS ProductId,
        LTRIM(RTRIM(p.Code)) AS Code,
        ROW_NUMBER() OVER
        (
            PARTITION BY LTRIM(RTRIM(p.Code))
            ORDER BY p.Id
        ) AS CodeOccurrence
    FROM Products AS p
    WHERE NULLIF(LTRIM(RTRIM(p.Code)), N'') IS NOT NULL
)
INSERT INTO #AuditMap
(
    SourceRowId,
    ProductId
)
SELECT
    s.SourceRowId,
    p.ProductId
FROM #AuditStageNumbered AS s
INNER JOIN ExistingProducts AS p
    ON p.Code = LTRIM(RTRIM(s.Code))
   AND p.CodeOccurrence = s.CodeOccurrence
WHERE NULLIF(LTRIM(RTRIM(s.Code)), N'') IS NOT NULL;";

            using (var command = new SqlCommand(codeMapSql, connection, transaction))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }

            const string blankMapSql = @"
;WITH ExistingBlankProducts AS
(
    SELECT
        p.Id AS ProductId,
        p.Name,
        p.Company,
        p.Unit,
        p.SellingPrice,
        ROW_NUMBER() OVER
        (
            PARTITION BY
                ISNULL(p.Name, N''),
                ISNULL(p.Company, N''),
                ISNULL(p.Unit, N''),
                p.SellingPrice
            ORDER BY p.Id
        ) AS ProductOccurrence
    FROM Products AS p
    WHERE NULLIF(LTRIM(RTRIM(p.Code)), N'') IS NULL
),
BlankStage AS
(
    SELECT
        s.SourceRowId,
        s.Name,
        s.Company,
        s.Unit,
        s.SellingPrice,
        ROW_NUMBER() OVER
        (
            PARTITION BY
                ISNULL(s.Name, N''),
                ISNULL(s.Company, N''),
                ISNULL(s.Unit, N''),
                s.SellingPrice
            ORDER BY s.SourceRowId
        ) AS ProductOccurrence
    FROM #AuditStageNumbered AS s
    WHERE NULLIF(LTRIM(RTRIM(s.Code)), N'') IS NULL
)
INSERT INTO #AuditMap
(
    SourceRowId,
    ProductId
)
SELECT
    s.SourceRowId,
    p.ProductId
FROM BlankStage AS s
INNER JOIN ExistingBlankProducts AS p
    ON ISNULL(p.Name, N'') = ISNULL(s.Name, N'')
   AND ISNULL(p.Company, N'') = ISNULL(s.Company, N'')
   AND ISNULL(p.Unit, N'') = ISNULL(s.Unit, N'')
   AND p.SellingPrice = s.SellingPrice
   AND p.ProductOccurrence = s.ProductOccurrence
WHERE NOT EXISTS
(
    SELECT 1
    FROM #AuditMap AS existingMap
    WHERE existingMap.SourceRowId = s.SourceRowId
);";

            using (var command = new SqlCommand(blankMapSql, connection, transaction))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
            }

            const string unmappedSql = @"
SELECT COUNT(*)
FROM #AuditStage AS a
LEFT JOIN #AuditMap AS m
    ON m.SourceRowId = a.SourceRowId
WHERE m.SourceRowId IS NULL;";

            using (var command = new SqlCommand(unmappedSql, connection, transaction))
            {
                command.CommandTimeout = 0;
                int unmappedCount = Convert.ToInt32(command.ExecuteScalar());

                if (unmappedCount > 0)
                {
                    throw new InvalidOperationException(
                        $"Could not map {unmappedCount} audit item(s) to SQL products.");
                }
            }
        }

        private static string Limit(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength);
        }
    }
}
