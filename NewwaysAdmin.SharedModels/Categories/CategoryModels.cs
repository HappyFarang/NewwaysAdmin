// File: NewwaysAdmin.SharedModels/Categories/CategoryModels.cs
using System.ComponentModel.DataAnnotations;

namespace NewwaysAdmin.SharedModels.Categories
{
    /// <summary>
    /// Main category system - categories are now location-independent
    /// </summary>
    public class CategorySystem
    {
        public List<Category> Categories { get; set; } = new();
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public int Version { get; set; } = 1;
        public string ModifiedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Global location system - separate from categories
    /// </summary>
    public class LocationSystem
    {
        public List<BusinessLocation> Locations { get; set; } = new();
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public int Version { get; set; } = 1;
        public string ModifiedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Business location - applies to all categories
    /// </summary>
    public class BusinessLocation
    {
        [Required]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public int SortOrder { get; set; } = 0;
    }

    /// <summary>
    /// Top-level category (Transportation, Tax, VAT, etc.) - NO location dependencies
    /// </summary>
    public class Category
    {
        [Required]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
        public List<SubCategory> SubCategories { get; set; } = new();
        public bool IsActive { get; set; } = true;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public int SortOrder { get; set; } = 0;
    }

    /// <summary>
    /// Sub-category (Green Buses, VAT Payment, etc.) - NO location dependencies
    /// </summary>
    public class SubCategory
    {
        [Required]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>
        /// Full path for clipboard copy: "Transportation/Green Buses"
        /// </summary>
        public string FullPath => $"{ParentCategoryName}/{Name}";

        public string ParentCategoryName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public int SortOrder { get; set; } = 0;
    }

    /// <summary>
    /// Usage tracking - NOW includes location selection at usage time
    /// </summary>
    public class CategoryUsage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SubCategoryId { get; set; } = string.Empty;
        public string SubCategoryPath { get; set; } = string.Empty; // For display

        /// <summary>
        /// Location selected at usage time - can be null for VAT/Tax/etc
        /// </summary>
        public string? LocationId { get; set; }
        public string? LocationName { get; set; } // For display

        public DateTime UsedDate { get; set; } = DateTime.UtcNow;
        public string UsedBy { get; set; } = string.Empty; // User or device
        public string? TransactionNote { get; set; } // Bank transfer note
        public decimal? Amount { get; set; } // Optional transaction amount
    }

    /// <summary>
    /// For MAUI sync - lightweight mobile-optimized structure
    /// </summary>
    public class MobileCategorySync
    {
        public DateTime LastUpdated { get; set; }
        public int CategoryVersion { get; set; }
        public int LocationVersion { get; set; }

        public List<MobileCategoryItem> Categories { get; set; } = new();
        public List<MobileLocationItem> Locations { get; set; } = new();
    }

    public class MobileCategoryItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<MobileSubCategoryItem> SubCategories { get; set; } = new();
        public int SortOrder { get; set; }
    }

    public class MobileSubCategoryItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public int TotalUsageCount { get; set; } // Across all locations
        public List<LocationUsageCount> LocationUsage { get; set; } = new(); // Usage per location
    }

    public class MobileLocationItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    public class LocationUsageCount
    {
        public string LocationId { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public int UsageCount { get; set; }
    }

    /// <summary>
    /// SignalR message types
    /// </summary>
    public class CategoryUpdateMessage
    {
        public string MessageType { get; set; } = string.Empty; // "CategoryAdded", "CategoryUpdated", "CategoryDeleted"
        public string CategoryId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; } = string.Empty;
    }

    public class LocationUpdateMessage
    {
        public List<BusinessLocation> Locations { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; } = string.Empty;
    }

    public class CategoryUsageMessage
    {
        public string SubCategoryId { get; set; } = string.Empty;
        public string SubCategoryPath { get; set; } = string.Empty;
        public string? LocationId { get; set; }
        public string? LocationName { get; set; }
        public DateTime UsedDate { get; set; } = DateTime.UtcNow;
        public string UsedBy { get; set; } = string.Empty;
    }
}