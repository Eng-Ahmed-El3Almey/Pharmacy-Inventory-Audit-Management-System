using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PharmacyInventorySystem.Models;
using PharmacyInventorySystem.Services;

namespace PharmacyInventorySystem
{
    public partial class ItemMovementView : UserControl
    {
        private readonly List<ItemMovementRecord> _allMovements = new();

        public ItemMovementView()
        {
            InitializeComponent();
            FromDateBox.Text = DateTime.Now.Date.AddDays(-30).ToString("dd/MM/yyyy");
            ToDateBox.Text = DateTime.Now.Date.ToString("dd/MM/yyyy");
            ApplyFilters();
        }

        public void RefreshView()
        {
            LoadLocationFilters();
            ApplyFilters();
        }

        public void SetMovements(IEnumerable<ItemMovementRecord> movements)
        {
            _allMovements.Clear();
            if (movements != null)
                _allMovements.AddRange(movements);

            ApplyFilters();
        }

        private void LoadLocationFilters()
        {
            try
            {
                var locations = new DatabaseService()
                    .GetAllBranchNames()
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var selectedBranch = BranchBox.SelectedItem as string;
                var selectedWarehouse = WarehouseBox.SelectedItem as string;

                BranchBox.Items.Clear();
                BranchBox.Items.Add("All Branches");
                foreach (var location in locations)
                    BranchBox.Items.Add(location);

                WarehouseBox.Items.Clear();
                WarehouseBox.Items.Add("All Warehouses");
                foreach (var location in locations)
                    WarehouseBox.Items.Add(location);

                BranchBox.SelectedItem = locations.Contains(selectedBranch ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    ? locations.First(x => string.Equals(x, selectedBranch, StringComparison.OrdinalIgnoreCase))
                    : "All Branches";

                WarehouseBox.SelectedItem = locations.Contains(selectedWarehouse ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    ? locations.First(x => string.Equals(x, selectedWarehouse, StringComparison.OrdinalIgnoreCase))
                    : "All Warehouses";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Item movement location filter load failed: " + ex.Message);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshView();
        }

        private void FilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                ApplyFilters();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded)
                ApplyFilters();
        }

        private void ApplyFilters()
        {
            string search = SearchBox?.Text?.Trim() ?? string.Empty;
            string branch = BranchBox?.SelectedItem as string ?? "All Branches";
            string warehouse = WarehouseBox?.SelectedItem as string ?? "All Warehouses";
            string type = GetSelectedMovementType();
            DateTime? fromDate = ParseFilterDate(FromDateBox?.Text);
            DateTime? toDate = ParseFilterDate(ToDateBox?.Text)?.Date.AddDays(1).AddTicks(-1);

            var filtered = _allMovements.Where(x =>
                (!fromDate.HasValue || x.DateTime >= fromDate.Value) &&
                (!toDate.HasValue || x.DateTime <= toDate.Value) &&
                (string.IsNullOrWhiteSpace(search) ||
                 x.ProductCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 x.ProductName.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (string.Equals(branch, "All Branches", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.Branch, branch, StringComparison.OrdinalIgnoreCase)) &&
                (string.Equals(warehouse, "All Warehouses", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)) &&
                (string.Equals(type, "All Types", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.MovementType, type, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.DateTime)
                .ToList();

            MovementsGrid.ItemsSource = filtered;
            ResultsText.Text = $"{filtered.Count:N0} record{(filtered.Count == 1 ? string.Empty : "s")}";
            TotalMovementsText.Text = filtered.Count.ToString("N0");
            TotalInText.Text = filtered.Where(x => string.Equals(x.MovementType, "IN", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Quantity).ToString("N2");
            TotalOutText.Text = filtered.Where(x => string.Equals(x.MovementType, "OUT", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Quantity).ToString("N2");
            AdjustmentsText.Text = filtered.Count(x => string.Equals(x.MovementType, "ADJ", StringComparison.OrdinalIgnoreCase)).ToString("N0");
            TransfersText.Text = filtered.Count(x => string.Equals(x.MovementType, "TRF", StringComparison.OrdinalIgnoreCase)).ToString("N0");
            EmptyMovementOverlay.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (filtered.Count == 0)
                ClearDetails();
        }

        private static DateTime? ParseFilterDate(string? value)
        {
            if (DateTime.TryParse(value, out var parsed))
                return parsed.Date;

            return null;
        }

        private string GetSelectedMovementType()
        {
            return MovementTypeBox?.SelectedItem is ComboBoxItem item
                ? item.Content?.ToString() ?? "All Types"
                : "All Types";
        }

        private void MovementsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MovementsGrid.SelectedItem is not ItemMovementRecord movement)
            {
                ClearDetails();
                return;
            }

            EmptyMovementOverlay.Visibility = Visibility.Collapsed;
            DetailProductName.Text = string.IsNullOrWhiteSpace(movement.ProductName) ? "—" : movement.ProductName;
            DetailProductCode.Text = string.IsNullOrWhiteSpace(movement.ProductCode) ? "—" : movement.ProductCode;
            DetailDate.Text = movement.DateTime.ToString("dd/MM/yyyy hh:mm tt");
            DetailType.Text = string.IsNullOrWhiteSpace(movement.MovementType) ? "—" : movement.MovementType;
            DetailQuantity.Text = movement.Quantity.ToString("N2");
            DetailBranch.Text = string.IsNullOrWhiteSpace(movement.Branch) ? "—" : movement.Branch;
            DetailWarehouse.Text = string.IsNullOrWhiteSpace(movement.Warehouse) ? "—" : movement.Warehouse;
            DetailUser.Text = string.IsNullOrWhiteSpace(movement.UserName) ? "—" : movement.UserName;
            DetailNotes.Text = string.IsNullOrWhiteSpace(movement.Notes) ? "No notes were added." : movement.Notes;
        }

        private void ClearDetails()
        {
            DetailProductName.Text = "No record selected";
            DetailProductCode.Text = "—";
            DetailDate.Text = "—";
            DetailType.Text = "—";
            DetailQuantity.Text = "—";
            DetailBranch.Text = "—";
            DetailWarehouse.Text = "—";
            DetailUser.Text = "—";
            DetailNotes.Text = "No movement selected.";
        }
    }
}
