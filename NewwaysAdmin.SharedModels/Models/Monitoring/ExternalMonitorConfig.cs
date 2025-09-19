// NewwaysAdmin.SharedModels/Models/Monitoring/ExternalMonitorConfig.cs

using System;
using System.Collections.Generic;

namespace NewwaysAdmin.SharedModels.Models.Monitoring
{
    /// <summary>
    /// Configuration for external folder monitoring with OCR pattern processing
    /// Links external folders to OCR pattern sets for automatic document processing
    /// </summary>
    public class ExternalMonitorConfig
    {
        /// <summary>
        /// Unique identifier for this monitor configuration
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// User-friendly name for this monitor (e.g., "KBIZ Bank Slips 2024")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// External folder path to monitor (outside IO system)
        /// Example: @"\\NAS\BankSlips\KBIZ\2024"
        /// </summary>
        public string ExternalPath { get; set; } = string.Empty;

        /// <summary>
        /// OCR Document Type (top level of pattern hierarchy)
        /// Example: "BankSlips", "Invoices", "Receipts"
        /// </summary>
        public string DocumentType { get; set; } = string.Empty;

        /// <summary>
        /// OCR Format Name (second level of pattern hierarchy)
        /// Example: "KBIZ", "KBank", "SCB"
        /// </summary>
        public string FormatName { get; set; } = string.Empty;

        /// <summary>
        /// File extensions to monitor and process
        /// </summary>
        public List<string> MonitoredExtensions { get; set; } = new()
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".tiff", ".bmp"
        };

        /// <summary>
        /// User IDs who can access the processed results
        /// Empty list = no users (admin only)
        /// </summary>
        public List<string> AuthorizedUserIds { get; set; } = new();

        /// <summary>
        /// Is this monitor currently active?
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When this monitor was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Who created this monitor configuration
        /// </summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// Last modification timestamp
        /// </summary>
        public DateTime? ModifiedAt { get; set; }

        /// <summary>
        /// Who last modified this configuration
        /// </summary>
        public string? ModifiedBy { get; set; }

        // Statistics (updated by background service)

        /// <summary>
        /// Last time the folder was scanned for new files
        /// </summary>
        public DateTime? LastScanned { get; set; }

        /// <summary>
        /// Number of files successfully processed
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// Number of files that failed processing
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// Number of files currently pending processing
        /// </summary>
        public int PendingCount { get; set; }

        // Helper Methods

        /// <summary>
        /// Check if a user has access to this monitor's results
        /// </summary>
        public bool HasUserAccess(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return false;

            return AuthorizedUserIds.Contains(userId, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Add user access (if not already present)
        /// </summary>
        public void AddUserAccess(string userId)
        {
            if (!string.IsNullOrEmpty(userId) && !HasUserAccess(userId))
            {
                AuthorizedUserIds.Add(userId);
            }
        }

        /// <summary>
        /// Remove user access
        /// </summary>
        public void RemoveUserAccess(string userId)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                AuthorizedUserIds.RemoveAll(id =>
                    id.Equals(userId, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Get a display-friendly status summary
        /// </summary>
        public string GetStatusSummary()
        {
            if (!IsActive)
                return "Inactive";

            if (LastScanned == null)
                return "Never scanned";

            var timeSinceLastScan = DateTime.UtcNow - LastScanned.Value;
            var scanStatus = timeSinceLastScan.TotalMinutes < 10 ? "Active" : "Idle";

            return $"{scanStatus} - {ProcessedCount} processed, {PendingCount} pending";
        }

        /// <summary>
        /// Validate the configuration has all required fields
        /// </summary>
        public (bool IsValid, string ErrorMessage) Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return (false, "Name is required");

            if (string.IsNullOrWhiteSpace(ExternalPath))
                return (false, "External path is required");

            if (string.IsNullOrWhiteSpace(DocumentType))
                return (false, "Document type is required");

            if (string.IsNullOrWhiteSpace(FormatName))
                return (false, "Format name is required");

            if (MonitoredExtensions == null || MonitoredExtensions.Count == 0)
                return (false, "At least one file extension must be monitored");

            return (true, string.Empty);
        }
    }
}