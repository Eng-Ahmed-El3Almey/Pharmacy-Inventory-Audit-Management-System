using System;

namespace PharmacyInventorySystem.Models
{
    public sealed class ItemMovementRecord
    {
        public int Id { get; set; }
        public DateTime DateTime { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string MovementType { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string Branch { get; set; } = string.Empty;
        public string Warehouse { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}
