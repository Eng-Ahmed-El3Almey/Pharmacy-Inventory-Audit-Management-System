using System;

namespace PharmacyInventorySystem.Models
{
    public sealed class UserRecord
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Mobile { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string Role { get; set; } = "Staff";
        public bool IsActive { get; set; } = true;
        public bool ViewInventory { get; set; }
        public bool ImportInventory { get; set; }
        public bool ViewReports { get; set; }
        public bool DeleteReports { get; set; }
        public bool EditReports { get; set; }
        public bool UserManagement { get; set; }
        public bool ManageCategories { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
