// NewwaysAdmin.WebAdmin/Services/BankSlips/CustomColumnStorageService.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    /// <summary>
    /// Service for persisting custom checkbox columns to storage
    /// </summary>
    public class CustomColumnStorageService
    {
        private readonly IDataStorage<List<SavedCustomColumn>> _storage;
        private readonly ILogger<CustomColumnStorageService> _logger;

        public CustomColumnStorageService(
            StorageManager storageManager,
            ILogger<CustomColumnStorageService> logger)
        {
            _storage = storageManager.GetStorageSync<List<SavedCustomColumn>>("BankSlip_CustomColumns");
            _logger = logger;
        }

        /// <summary>
        /// Load saved custom columns for current user
        /// </summary>
        public async Task<List<SavedCustomColumn>> LoadUserCustomColumnsAsync()
        {
            try
            {
                // For now, use a global identifier - can be made user-specific later
                var identifier = "global-custom-columns";
                _logger.LogInformation("Loading custom columns");

                if (await _storage.ExistsAsync(identifier))
                {
                    var columns = await _storage.LoadAsync(identifier);
                    _logger.LogInformation("Loaded {Count} custom columns", columns.Count);
                    return columns;
                }

                _logger.LogInformation("No saved custom columns found");
                return new List<SavedCustomColumn>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load custom columns");
                return new List<SavedCustomColumn>();
            }
        }

        /// <summary>
        /// Save custom columns for current user
        /// </summary>
        public async Task SaveUserCustomColumnsAsync(List<SavedCustomColumn> columns)
        {
            try
            {
                // For now, use a global identifier - can be made user-specific later
                var identifier = "global-custom-columns";
                _logger.LogInformation("Saving {Count} custom columns", columns.Count);

                // Clean up the data before saving
                var cleanColumns = columns.Select(col => new SavedCustomColumn
                {
                    Id = col.Id,
                    Name = col.Name?.Trim() ?? string.Empty,
                    SumFieldName = col.SumFieldName?.Trim() ?? string.Empty,
                    CreatedAt = col.CreatedAt == default ? DateTime.UtcNow : col.CreatedAt,
                    LastModified = DateTime.UtcNow
                }).ToList();

                await _storage.SaveAsync(identifier, cleanColumns);
                _logger.LogInformation("Successfully saved {Count} custom columns", cleanColumns.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save custom columns");
                throw;
            }
        }

        /// <summary>
        /// Add a new custom column to user's saved collection
        /// </summary>
        public async Task AddCustomColumnAsync(SavedCustomColumn column)
        {
            try
            {
                var existingColumns = await LoadUserCustomColumnsAsync();

                // Check for duplicate names
                if (existingColumns.Any(c => c.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Custom column with name '{Name}' already exists for user", column.Name);
                    return;
                }

                // Set metadata
                column.Id = Guid.NewGuid().ToString();
                column.CreatedAt = DateTime.UtcNow;
                column.LastModified = DateTime.UtcNow;

                existingColumns.Add(column);
                await SaveUserCustomColumnsAsync(existingColumns);

                _logger.LogInformation("Added new custom column '{Name}' for user", column.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add custom column '{Name}'", column.Name);
                throw;
            }
        }

        /// <summary>
        /// Update an existing custom column
        /// </summary>
        public async Task UpdateCustomColumnAsync(SavedCustomColumn column)
        {
            try
            {
                var existingColumns = await LoadUserCustomColumnsAsync();
                var existingIndex = existingColumns.FindIndex(c => c.Id == column.Id);

                if (existingIndex == -1)
                {
                    _logger.LogWarning("Custom column with ID '{Id}' not found for update", column.Id);
                    return;
                }

                // Update metadata
                column.LastModified = DateTime.UtcNow;
                existingColumns[existingIndex] = column;

                await SaveUserCustomColumnsAsync(existingColumns);
                _logger.LogInformation("Updated custom column '{Name}' (ID: {Id})", column.Name, column.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update custom column '{Name}' (ID: {Id})", column.Name, column.Id);
                throw;
            }
        }

        /// <summary>
        /// Delete a custom column
        /// </summary>
        public async Task DeleteCustomColumnAsync(string columnId)
        {
            try
            {
                var existingColumns = await LoadUserCustomColumnsAsync();
                var columnToRemove = existingColumns.FirstOrDefault(c => c.Id == columnId);

                if (columnToRemove == null)
                {
                    _logger.LogWarning("Custom column with ID '{Id}' not found for deletion", columnId);
                    return;
                }

                existingColumns.Remove(columnToRemove);
                await SaveUserCustomColumnsAsync(existingColumns);

                _logger.LogInformation("Deleted custom column '{Name}' (ID: {Id})", columnToRemove.Name, columnId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete custom column with ID '{Id}'", columnId);
                throw;
            }
        }

        /// <summary>
        /// Get statistics about user's custom columns
        /// </summary>
        public async Task<CustomColumnStats> GetCustomColumnStatsAsync()
        {
            try
            {
                var columns = await LoadUserCustomColumnsAsync();
                return new CustomColumnStats
                {
                    TotalColumns = columns.Count,
                    ColumnsWithFormulas = columns.Count(c => !string.IsNullOrEmpty(c.SumFieldName)),
                    MostRecentlyUsed = columns.OrderByDescending(c => c.LastModified).FirstOrDefault()?.Name,
                    OldestColumn = columns.OrderBy(c => c.CreatedAt).FirstOrDefault()?.Name
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get custom column statistics");
                return new CustomColumnStats();
            }
        }

        /// <summary>
        /// Generate storage identifier for user
        /// </summary>
        private static string GetGlobalIdentifier()
        {
            return "global-custom-columns";
        }
    }

    /// <summary>
    /// Model for saved custom columns in storage
    /// </summary>
    public class SavedCustomColumn
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SumFieldName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Statistics about user's custom columns
    /// </summary>
    public class CustomColumnStats
    {
        public int TotalColumns { get; set; }
        public int ColumnsWithFormulas { get; set; }
        public string? MostRecentlyUsed { get; set; }
        public string? OldestColumn { get; set; }
    }
}