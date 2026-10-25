using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClosedXML.Excel;
using PharmacyInventorySystem.Models;

namespace PharmacyInventorySystem.Services
{
    /// <summary>Result of loading the master stock sheet.</summary>
    public class InventoryLoadResult
    {
        public ObservableCollection<Product> Products { get; set; } = new();
        public List<string> BranchNames { get; set; } = new();
        public List<string> CompanyNames { get; set; } = new();
    }

    /// <summary>
    /// Reads "رصيد الأصناف فى جميع المخازن" (one row per item, one column per branch) straight into
    /// Product objects, in memory — no database involved. Layout (row 2 = headers, data starts
    /// row 3): A=Code, B=Name, C=Price, D=Company, E=Unit, F..(last-1)=one column per branch,
    /// last=Total ("الإجمالى", read but not relied on — TotalQuantity is summed from the branch columns).
    /// </summary>
    public static class ExcelInventoryLoader
    {
        private const int HeaderRow = 2;
        private const int FirstDataRow = 3;
        private const int CodeCol = 1;
        private const int NameCol = 2;
        private const int PriceCol = 3;
        private const int CompanyCol = 4;
        private const int UnitCol = 5;
        private const int FirstBranchCol = 6;

        public static InventoryLoadResult Load(string filePath)
        {
            return Load(filePath, null);
        }

        public static InventoryLoadResult Load(string filePath, IProgress<int> progress)
        {
            var result = new InventoryLoadResult();

            progress?.Report(5);

            using var workbook = new XLWorkbook(filePath);
            var sheet = workbook.Worksheets.First();
            progress?.Report(10);

            int lastCol = sheet.Row(HeaderRow).LastCellUsed().Address.ColumnNumber;
            int totalCol = lastCol;
            int lastBranchCol = totalCol - 1;

            var branchColumns = new List<(int Column, string Name)>();
            for (int c = FirstBranchCol; c <= lastBranchCol; c++)
            {
                var header = sheet.Cell(HeaderRow, c).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(header))
                    branchColumns.Add((c, header));
            }

            result.BranchNames = branchColumns
                .Select(x => x.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            progress?.Report(20);

            var companies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lastRow = sheet.LastRowUsed().RowNumber();

            for (int r = FirstDataRow; r <= lastRow; r++)
            {
                var codeCell = sheet.Cell(r, CodeCol);
                if (codeCell.IsEmpty()) continue;

                var code = codeCell.DataType == XLDataType.Number
                    ? ((long)codeCell.GetDouble()).ToString()
                    : codeCell.GetValue<string>().Trim();
                if (string.IsNullOrWhiteSpace(code)) continue;

                var name = CleanText(sheet.Cell(r, NameCol).GetValue<string>());
                var company = sheet.Cell(r, CompanyCol).GetValue<string>().Trim();
                var unit = sheet.Cell(r, UnitCol).GetValue<string>().Trim();
                var price = TryGetDecimal(sheet.Cell(r, PriceCol));

                var branchStock = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                decimal total = 0m;

                foreach (var branch in branchColumns)
                {
                    var qty = TryGetDecimal(sheet.Cell(r, branch.Column));

                    // Keep only non-zero balances. A missing key represents zero stock,
                    // which greatly reduces memory usage for large pharmacy sheets.
                    if (qty != 0m)
                        branchStock[branch.Name] = qty;

                    total += qty;
                }

                result.Products.Add(new Product
                {
                    Code = code,
                    Name = name,
                    Company = company,
                    Unit = unit,
                    Price = price,
                    BranchStock = branchStock,
                    TotalQuantity = total
                });

                if (!string.IsNullOrWhiteSpace(company))
                    companies.Add(company);

                if (progress != null && lastRow >= FirstDataRow)
                {
                    var processed = r - FirstDataRow + 1;
                    var totalRows = lastRow - FirstDataRow + 1;

                    // Do not post a progress update for every Excel row.
                    // Large sheets can contain tens of thousands of rows and
                    // thousands of UI callbacks can make the application appear frozen.
                    if (processed == 1 || processed == totalRows || processed % 250 == 0)
                    {
                        var ratio = processed * 68.0 / totalRows;
                        progress.Report(22 + (int)Math.Round(ratio));
                    }
                }
            }

            result.CompanyNames = companies.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            progress?.Report(90);
            return result;
        }

        private static string CleanText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw ?? string.Empty;
            var chars = raw.Where(ch => !char.IsControl(ch)).ToArray();
            return new string(chars).Trim();
        }

        private static decimal TryGetDecimal(IXLCell cell)
        {
            if (cell.IsEmpty()) return 0m;
            return cell.TryGetValue(out double d) ? (decimal)d : 0m;
        }
    }
}
