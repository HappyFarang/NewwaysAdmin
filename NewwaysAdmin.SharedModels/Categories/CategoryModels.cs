// File: NewwaysAdmin.SharedModels/Categories/CategoryModels.cs
using System.ComponentModel.DataAnnotations;

namespace NewwaysAdmin.SharedModels.Categories
{
    // ===== MAIN DATA STRUCTURE =====

    /// <summary>
    /// Unified category data - single file, single version
    /// Used by both server and mobile for bidirectional sync
    /// </summary>
    public class FullCategoryData
    {
        /// <summary>
        /// Single version number - increments on ANY change (categories, locations, or persons)
        /// </summary>
        public int DataVersion { get; set; } = 1;

        /// <summary>
        /// When this data was last modified
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Who made the last change (username or device ID)
        /// </summary>
        public string LastModifiedBy { get; set; } = string.Empty;

        public List<Category> Categories { get; set; } = new();
        public List<BusinessLocation> Locations { get; set; } = new();
        public List<ResponsiblePerson> Persons { get; set; } = new();
    }

    // ===== ENTITIES =====

    /// <summary>
    /// Top-level category (Transportation, Production, Tax, etc.)
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
    /// Sub-category (Green Buses, B2 Boxes, VAT Payment, etc.)
    /// </summary>
    public class SubCategory
    {
        [Required]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
        public string ParentCategoryName { get; set; } = string.Empty;

        /// <summary>
        /// Full path for clipboard/display: "Transportation/Green Buses"
        /// </summary>
        public string FullPath => $"{ParentCategoryName}/{Name}";

        /// <summary>
        /// Indicates if transactions in this subcategory include VAT (for VAT reporting)
        /// </summary>
        public bool HasVAT { get; set; } = false;

        public bool IsActive { get; set; } = true;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public int SortOrder { get; set; } = 0;
    }

    /// <summary>
    /// Business location (Chiang Mai, Bangkok, etc.)
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
    /// Person responsible for payment (Thomas, Nok, etc.)
    /// </summary>
    public class ResponsiblePerson
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

    // ===== SYNC MESSAGE =====

    /// <summary>
    /// Version exchange message for SignalR sync
    /// </summary>
    public class VersionExchangeMessage
    {
        public int MyVersion { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty; // "MAUI" or "Blazor"
    }

    /// <summary>
    /// Response to version exchange
    /// </summary>
    public class VersionExchangeResponse
    {
        public int ServerVersion { get; set; }
        public bool YouNeedUpdate { get; set; }        // Match server
        public bool ServerNeedsYourData { get; set; }
        public FullCategoryData? FullData { get; set; } // Match server
    }
}