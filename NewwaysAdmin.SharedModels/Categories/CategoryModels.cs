// File: NewwaysAdmin.SharedModels/Categories/CategoryModels.cs
using System.ComponentModel.DataAnnotations;

namespace NewwaysAdmin.SharedModels.Categories
{
    /// <summary>
    /// Master system - contains all category data, locations, and persons
    /// Single file synced between server and mobile via IOManager
    /// </summary>
    public class CategorySystem
    {
        public List<Category> Categories { get; set; } = new();
        public List<BusinessLocation> Locations { get; set; } = new();
        public List<Person> Persons { get; set; } = new();
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public int Version { get; set; } = 1;
        public string ModifiedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Top-level category (Production, Tax, Shipping, etc.)
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
    /// Sub-category (Blue bags, White bags, Labels, etc.)
    /// </summary>
    public class SubCategory
    {
        [Required]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
        public string FullPath => $"{ParentCategoryName}/{Name}";
        public string ParentCategoryName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Does this expense type include VAT? (7% Thailand)
        /// </summary>
        public bool HasVat { get; set; } = false;
    }

    /// <summary>
    /// Business location (Phrae, Chiang Mai, etc.)
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
    /// Person responsible for purchases (Nop, Thomas, Pui, etc.)
    /// The person who receives money and must provide receipts
    /// </summary>
    public class Person
    {
        [Required]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 0;
    }
}