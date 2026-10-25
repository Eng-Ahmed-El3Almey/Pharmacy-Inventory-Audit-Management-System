using System.Collections.ObjectModel;

namespace PharmacyInventorySystem.Models
{
    public class BranchNode
    {
        public string Name { get; set; } = string.Empty;

        // Actual value used to search the Excel inventory.
        public string Value { get; set; } = string.Empty;

        public bool IsGroup { get; set; }

        public ObservableCollection<BranchNode> Children { get; } = new();

        public bool HasChildren => Children.Count > 0;
    }
}
