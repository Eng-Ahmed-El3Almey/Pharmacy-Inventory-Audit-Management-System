using System.Collections.Generic;
using System.ComponentModel;

namespace PharmacyInventorySystem.Models
{
    /// <summary>
    /// One item from the master stock sheet ("رصيد الأصناف فى جميع المخازن"), loaded straight into
    /// memory from Excel (no database layer). Each item carries its quantity in every branch
    /// (<see cref="BranchStock"/>). "System" and "Court" only make sense once a specific branch is
    /// selected: System = the quantity from the sheet for that branch, Court = the physical count
    /// the staff types in during the audit.
    /// </summary>
    public class Product : INotifyPropertyChanged
    {
        private decimal _system;
        private decimal _court;
        private bool _isCounted;

        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal Price { get; set; }

        /// <summary>Quantity of this item in every branch, keyed by branch name exactly as it appears in the sheet.</summary>
        public Dictionary<string, decimal> BranchStock { get; set; } = new();

        /// <summary>Sum of the quantity across every branch.</summary>
        public decimal TotalQuantity { get; set; }

        /// <summary>Stock value across every branch — used by the left "Stock Inventory Table".</summary>
        public decimal StockValue => TotalQuantity * Price;

        /// <summary>The sheet's recorded quantity for whichever branch is currently selected.</summary>
        public decimal System
        {
            get => _system;
            set { _system = value; OnPropertyChanged(nameof(System)); OnPropertyChanged(nameof(Diff)); OnPropertyChanged(nameof(Total)); OnPropertyChanged(nameof(Status)); }
        }

        /// <summary>The physical count the staff enters for the currently selected branch (editable in the grid).</summary>
        public decimal Court
        {
            get => _court;
            set
            {
                _court = value;
                _isCounted = true;
                OnPropertyChanged(nameof(Court));
                OnPropertyChanged(nameof(IsCounted));
                OnPropertyChanged(nameof(Diff));
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(Status));
            }
        }

        public bool IsCounted
        {
            get => _isCounted;
            private set
            {
                if (_isCounted == value)
                    return;

                _isCounted = value;
                OnPropertyChanged(nameof(IsCounted));
            }
        }

        /// <summary>Court - System, for the currently selected branch. Positive = surplus, negative = deficit, zero = perfect match.</summary>
        public decimal Diff => Court - System;

        /// <summary>Diff × Price — the money value of the discrepancy for the currently selected branch.</summary>
        public decimal Total => Diff * Price;

        public ProductStatus Status => Diff == 0
            ? ProductStatus.PerfectMatch
            : Diff > 0
                ? ProductStatus.Surplus
                : ProductStatus.Deficit;

        /// <summary>Sets System/Court to that branch's recorded quantity (Court starts equal to System, ready to be corrected during the audit).</summary>
        public void ApplyBranch(string branchName)
        {
            decimal qty;

            if (string.IsNullOrWhiteSpace(branchName))
            {
                qty = TotalQuantity;
            }
            else
            {
                // A branch that has no entry in the dictionary has zero stock.
                qty = BranchStock.TryGetValue(branchName, out var v) ? v : 0m;
            }

            _system = qty;
            _court = qty;
            _isCounted = false;
            OnPropertyChanged(nameof(System));
            OnPropertyChanged(nameof(Court));
            OnPropertyChanged(nameof(IsCounted));
            OnPropertyChanged(nameof(Diff));
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(Status));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public enum ProductStatus
    {
        PerfectMatch,
        Surplus,
        Deficit
    }
}
