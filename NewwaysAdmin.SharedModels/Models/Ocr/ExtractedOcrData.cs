// NewwaysAdmin.SharedModels/Models/Ocr/ExtractedOcrData.cs
using System;
using System.Collections.Generic;

namespace NewwaysAdmin.SharedModels.Models.Ocr
{
    /// <summary>
    /// Contains extracted OCR data from a document using pattern-based extraction
    /// Uses dynamic field names based on pattern keys for maximum flexibility
    /// </summary>
    public class ExtractedOcrData
    {
        /// <summary>
        /// Full path to the original scanned file (for later reference, display, printing)
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Document type from pattern hierarchy (e.g., "BankSlips", "Invoices", "Bills")
        /// </summary>
        public string DocumentType { get; set; } = string.Empty;

        /// <summary>
        /// Format/sub-collection from pattern hierarchy (e.g., "KBIZ", "KBank", "SCB")
        /// </summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>
        /// Dynamic dictionary of extracted entries using pattern keys as field names
        /// Key = pattern name (e.g., "To", "Date", "Total", "Fee")
        /// Value = extracted text after regex processing (empty string if extraction failed)
        /// </summary>
        public Dictionary<string, string> ExtractedEntries { get; set; } = new();

        /// <summary>
        /// When the extraction was processed
        /// </summary>
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Overall success status (true if at least one pattern was extracted successfully)
        /// </summary>
        public bool IsSuccess { get; set; } = false;

        /// <summary>
        /// General processing errors (not field-specific)
        /// Individual field failures are handled by leaving entries empty
        /// </summary>
        public List<string> Errors { get; set; } = new();

        #region Helper Methods

        /// <summary>
        /// Get extracted value for a specific pattern key (returns empty string if not found)
        /// </summary>
        /// <param name="key">Pattern key (e.g., "Date", "Total", "To")</param>
        /// <returns>Extracted value or empty string</returns>
        public string GetEntry(string key)
        {
            return ExtractedEntries.TryGetValue(key, out var value) ? value : string.Empty;
        }

        /// <summary>
        /// Set extracted value for a specific pattern key
        /// </summary>
        /// <param name="key">Pattern key</param>
        /// <param name="value">Extracted value</param>
        public void SetEntry(string key, string value)
        {
            ExtractedEntries[key] = value ?? string.Empty;
        }

        /// <summary>
        /// Check if a specific pattern key has any extracted value
        /// </summary>
        /// <param name="key">Pattern key to check</param>
        /// <returns>True if key exists and has non-empty value</returns>
        public bool HasEntry(string key)
        {
            return ExtractedEntries.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
        }

        /// <summary>
        /// Get count of successfully extracted entries (non-empty values)
        /// </summary>
        public int SuccessfulEntryCount
        {
            get
            {
                return ExtractedEntries.Count(kvp => !string.IsNullOrWhiteSpace(kvp.Value));
            }
        }

        /// <summary>
        /// Get all pattern keys that had successful extractions
        /// </summary>
        public List<string> SuccessfulKeys
        {
            get
            {
                return ExtractedEntries
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
                    .Select(kvp => kvp.Key)
                    .ToList();
            }
        }

        /// <summary>
        /// Add a general processing error
        /// </summary>
        /// <param name="error">Error message</param>
        public void AddError(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                Errors.Add($"[{DateTime.Now:HH:mm:ss}] {error}");
            }
        }

        /// <summary>
        /// Get a summary of the extraction for logging/debugging
        /// </summary>
        /// <returns>Human-readable summary</returns>
        public string GetSummary()
        {
            var successCount = SuccessfulEntryCount;
            var totalCount = ExtractedEntries.Count;

            if (!IsSuccess)
            {
                return $"FAILED: {string.Join(", ", Errors)}";
            }

            return $"SUCCESS: {successCount}/{totalCount} patterns extracted from {DocumentType}/{Format}";
        }

        #endregion
    }
}