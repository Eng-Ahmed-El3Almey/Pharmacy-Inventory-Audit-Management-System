using System;
using System.Collections.Generic;

namespace PharmacyInventorySystem.Models
{
    public sealed class AuditReportSession
    {
        public int SessionId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int ItemCount { get; set; }
        public decimal ShortageValue { get; set; }
        public decimal SurplusValue { get; set; }
        public int ShortageItemCount { get; set; }
        public int SurplusItemCount { get; set; }
        public decimal NetDifference => SurplusValue - ShortageValue;
        public string Status { get; set; } = "Completed";
    }

    public sealed class AuditReportItem : System.ComponentModel.INotifyPropertyChanged
    {
        public int CountId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public decimal SystemQuantity { get; set; }
        private decimal _actualQuantity;
        public decimal ActualQuantity
        {
            get => _actualQuantity;
            set
            {
                if (_actualQuantity == value) return;
                _actualQuantity = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ActualQuantity)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Difference)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(TotalValue)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Status)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public decimal Difference => ActualQuantity - SystemQuantity;
        public decimal Price { get; set; }
        public decimal TotalValue => Difference * Price;
        public string Status =>
            Difference < 0 ? "Shortage" :
            Difference > 0 ? "Surplus" :
            "Exact";
    }
}
