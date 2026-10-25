using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using PharmacyInventorySystem.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmacyInventorySystem.Services
{
    /// <summary>Handles Export Audit to Excel / PDF and the "Add Stock via Excel" count import. No database involved.</summary>
    public static class ExportService
    {
        // Column order: Code, Product Name, System, Court, Diff, Price, Total.
        public static void ExportAuditToExcel(IEnumerable<Product> products, string branchLabel, string filePath)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Audit");

            string[] headers = { "Code", "Product Name", "System", "Court", "Diff", "Price", "Total" };
            WriteHeaderRow(sheet, headers);

            int row = 2;
            foreach (var p in products)
            {
                sheet.Cell(row, 1).Value = p.Code;
                sheet.Cell(row, 2).Value = p.Name;
                sheet.Cell(row, 3).Value = p.System;
                sheet.Cell(row, 4).Value = p.Court;
                sheet.Cell(row, 5).Value = p.Diff;
                sheet.Cell(row, 6).Value = p.Price;
                sheet.Cell(row, 7).Value = p.Total;
                row++;
            }

            sheet.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
        }

        public static void ExportAuditToPdf(IEnumerable<Product> products, string branchLabel, string filePath,
            int perfect, int surplus, int deficit)
        {
            var headers = new[] { "Code", "Product Name", "System", "Court", "Diff", "Price", "Total" };
            var rows = products.Select(p => new[]
            {
                p.Code, p.Name, p.System.ToString("0.##"), p.Court.ToString("0.##"),
                (p.Diff > 0 ? "+" : "") + p.Diff.ToString("0.##"), p.Price.ToString("N2"), p.Total.ToString("N2")
            });

            var footer = $"Perfect Match: {perfect}   |   Surplus/Overstock: {surplus}   |   Deficit/Shortage: {deficit}";
            BuildPdf($"Pharmacy Inventory Audit — {branchLabel}", headers, rows, footer, filePath);
        }

        // ================= Add Stock via Excel: import physical counts for the active branch =================
        /// <summary>Reads a simple two-column sheet (Code, Court) and returns it as (Code, Count) pairs.</summary>
        public static List<(string Code, decimal Count)> ImportCounts(string filePath)
        {
            var result = new List<(string Code, decimal Count)>();
            using var workbook = new XLWorkbook(filePath);
            var sheet = workbook.Worksheets.First();

            bool firstRow = true;
            foreach (var row in sheet.RowsUsed())
            {
                if (firstRow) { firstRow = false; continue; } // skip header row

                var code = row.Cell(1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(code)) continue;

                var count = row.Cell(2).TryGetValue(out double c) ? (decimal)c : 0m;
                result.Add((code, count));
            }

            return result;
        }

        // ================= Shared helpers =================
        private static void WriteHeaderRow(IXLWorksheet sheet, string[] headers)
        {
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = sheet.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#122A4D");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void BuildPdf(string title, string[] headers, IEnumerable<string[]> rows, string footer, string filePath)
        {
            var rowList = rows.ToList();

            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Text(title).FontSize(16).Bold();

                    page.Content().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(55);
                            columns.RelativeColumn(3);
                            foreach (var _ in headers.Skip(2))
                                columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            foreach (var h in headers)
                            {
                                header.Cell().Background("#122A4D").Padding(4)
                                    .Text(h).FontColor("#FFFFFF").Bold();
                            }
                        });

                        foreach (var r in rowList)
                        {
                            foreach (var value in r)
                                table.Cell().Padding(3).Text(value);
                        }
                    });

                    if (!string.IsNullOrEmpty(footer))
                        page.Footer().AlignCenter().Text(footer);
                });
            }).GeneratePdf(filePath);
        }
    }
}
