// NewwaysAdmin.SharedModels/BankSlips/BankSlipScanResult.cs

using System;
using System.Collections.Generic;
using System.Linq;

namespace NewwaysAdmin.SharedModels.BankSlips
{
    /// <summary>
    /// Contains the results of an automatic OCR scan for a bank slip image
    /// Stored as .bin files for fast access and indexed for quick date range queries
    /// </summary>
    public class BankSlipScanResult
    {
        /// <summary>
        /// Collection name this scan belongs to (e.g., "KBIZ", "AmyKplus")
        /// Redundant with folder structure but helpful for logging
        /// </summary>
        public string CollectionName { get; set; } = string.Empty;

        /// <summary>
        /// Full path to the original image file (critical for reference)
        /// </summary>
        public string OriginalFilePath { get; set; } = string.Empty;

        /// <summary>
        /// When the scan was processed (fallback timestamp)
        /// </summary>
        public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The actual OCR results - same format as current bank slip system
        /// Key = pattern name (e.g., "Date", "Total", "To", "Fee")
        /// Value = extracted text after regex processing
        /// </summary>
        public Dictionary<string, string> ExtractedData { get; set; } = new();

        /// <summary>
        /// Parsed date from ExtractedData["Date"] for indexing and date range queries
        /// Falls back to ScannedAt if no valid date could be extracted
        /// </summary>
        public DateTime? DocumentDate { get; set; }

        /// <summary>
        /// Processing status for tracking
        /// </summary>
        public string ProcessingStatus { get; set; } = "Completed";

        /// <summary>
        /// Any errors that occurred during processing
        /// </summary>
        public List<string> ProcessingErrors { get; set; } = new();

        #region Helper Properties

        /// <summary>
        /// Returns true if a valid document date was extracted
        /// </summary>
        public bool HasValidDate => DocumentDate.HasValue;

        /// <summary>
        /// Gets the effective date for queries (DocumentDate or fallback to ScannedAt)
        /// </summary>
        public DateTime EffectiveDate => DocumentDate ?? ScannedAt;

        /// <summary>
        /// Returns true if the scan was successful (has extracted data and no errors)
        /// </summary>
        public bool IsSuccessful => ExtractedData.Any() && !ProcessingErrors.Any();

        /// <summary>
        /// Gets the file name from the original path for display
        /// </summary>
        public string FileName => !string.IsNullOrEmpty(OriginalFilePath)
            ? Path.GetFileName(OriginalFilePath)
            : "Unknown";

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get extracted value for a specific pattern key (returns empty string if not found)
        /// </summary>
        /// <param name="key">Pattern key (e.g., "Date", "Total", "To")</param>
        /// <returns>Extracted value or empty string</returns>
        public string GetExtractedValue(string key)
        {
            return ExtractedData.TryGetValue(key, out var value) ? value : string.Empty;
        }

        /// <summary>
        /// Set extracted value for a specific pattern key
        /// </summary>
        /// <param name="key">Pattern key</param>
        /// <param name="value">Extracted value</param>
        public void SetExtractedValue(string key, string value)
        {
            ExtractedData[key] = value ?? string.Empty;
        }

        /// <summary>
        /// Add a processing error
        /// </summary>
        /// <param name="error">Error message</param>
        public void AddError(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                ProcessingErrors.Add(error);
                ProcessingStatus = "Failed";
            }
        }

        /// <summary>
        /// Gets a summary of extracted data for logging
        /// </summary>
        /// <returns>Summary string</returns>
        public string GetSummary()
        {
            var total = GetExtractedValue("Total");
            var date = GetExtractedValue("Date");
            var to = GetExtractedValue("To");

            return $"Date: {date}, Total: {total}, To: {to?.Substring(0, Math.Min(20, to.Length))}...";
        }

        #endregion
    }
}