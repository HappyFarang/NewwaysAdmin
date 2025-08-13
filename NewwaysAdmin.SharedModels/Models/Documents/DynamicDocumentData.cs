// NewwaysAdmin.SharedModels/Models/Documents/GenericDocumentData.cs
using System;
using System.Collections.Generic;
using MessagePack;

namespace NewwaysAdmin.SharedModels.Models.Documents
{
    /// <summary>
    /// Simple generic document data structure
    /// Contains extracted fields with type hints for downstream processing
    /// </summary>
    [MessagePackObject]
    public class GenericDocumentData
    {
        [Key(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Key(1)]
        public string DocumentType { get; set; } = string.Empty; // "BankSlips", "Invoices", "Receipts"

        [Key(2)]
        public string DocumentFormat { get; set; } = string.Empty; // "KBIZ", "SCB", "TTB"

        [Key(3)]
        public string FilePath { get; set; } = string.Empty;

        [Key(4)]
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        [Key(5)]
        public string ProcessedBy { get; set; } = string.Empty;

        [Key(6)]
        public DocumentProcessingStatus Status { get; set; } = DocumentProcessingStatus.Pending;

        [Key(7)]
        public string ErrorReason { get; set; } = string.Empty;

        /// <summary>
        /// Extracted fields from patterns
        /// Key = pattern name from JSON (e.g., "To", "Date", "Total", "Fee")
        /// Value = ExtractedFieldData with type hint and raw text
        /// </summary>
        [Key(8)]
        public Dictionary<string, ExtractedFieldData> ExtractedFields { get; set; } = new();

        /// <summary>
        /// Processing metadata
        /// </summary>
        [Key(9)]
        public Dictionary<string, string> ProcessingNotes { get; set; } = new();

        #region Simple Field Access

        /// <summary>
        /// Get raw text value for a field (case-insensitive)
        /// </summary>
        public string GetFieldText(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return string.Empty;

            // Try exact match first (faster)
            if (ExtractedFields.TryGetValue(fieldName, out var exactField))
                return exactField.RawText;

            // Fall back to case-insensitive search
            var key = ExtractedFields.Keys.FirstOrDefault(k =>
                k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            return key != null && ExtractedFields.TryGetValue(key, out var field) ? field.RawText : string.Empty;
        }

        /// <summary>
        /// Get field data (including type information) - case-insensitive
        /// </summary>
        public ExtractedFieldData? GetField(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return null;

            // Try exact match first (faster)
            if (ExtractedFields.TryGetValue(fieldName, out var exactField))
                return exactField;

            // Fall back to case-insensitive search
            var key = ExtractedFields.Keys.FirstOrDefault(k =>
                k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            return key != null && ExtractedFields.TryGetValue(key, out var field) ? field : null;
        }

        /// <summary>
        /// Set field with type hint
        /// </summary>
        public void SetField(string fieldName, string rawText, ExtractedDataType dataType = ExtractedDataType.Text)
        {
            // Basic validation to prevent OCR artifacts from creating problematic field names
            if (string.IsNullOrWhiteSpace(fieldName) || fieldName.Length > 100)
                return;

            // Truncate extremely long values to prevent memory issues
            var cleanText = rawText?.Length > 2000 ? rawText.Substring(0, 2000) : rawText ?? string.Empty;

            ExtractedFields[fieldName] = new ExtractedFieldData
            {
                RawText = cleanText,
                DataType = dataType
            };
        }

        /// <summary>
        /// Check if field exists and has content
        /// </summary>
        public bool HasField(string fieldName)
        {
            var field = GetField(fieldName);
            return field != null && !string.IsNullOrWhiteSpace(field.RawText);
        }

        /// <summary>
        /// Get all field names
        /// </summary>
        public List<string> GetFieldNames()
        {
            return new List<string>(ExtractedFields.Keys);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Add processing note
        /// </summary>
        public void AddNote(string key, string value)
        {
            ProcessingNotes[key] = value ?? string.Empty;
        }

        /// <summary>
        /// Get summary for logging/display
        /// </summary>
        public string GetSummary()
        {
            var fieldCount = ExtractedFields.Count;
            var populatedCount = 0;

            foreach (var field in ExtractedFields.Values)
            {
                if (!string.IsNullOrWhiteSpace(field.RawText))
                    populatedCount++;
            }

            if (Status == DocumentProcessingStatus.Failed)
            {
                return $"FAILED: {ErrorReason}";
            }

            return $"{DocumentType}/{DocumentFormat}: {populatedCount}/{fieldCount} fields extracted";
        }

        #endregion
    }

    /// <summary>
    /// Represents a single extracted field with type information
    /// </summary>
    [MessagePackObject]
    public class ExtractedFieldData
    {
        /// <summary>
        /// Raw text extracted from OCR (always preserved as-is)
        /// </summary>
        [Key(0)]
        public string RawText { get; set; } = string.Empty;

        /// <summary>
        /// Hint about what type of data this represents
        /// Used by downstream parsers to handle conversion appropriately
        /// </summary>
        [Key(1)]
        public ExtractedDataType DataType { get; set; } = ExtractedDataType.Text;

        public override string ToString() => RawText;
    }

    /// <summary>
    /// Type hints for extracted data - helps downstream parsers
    /// </summary>
    public enum ExtractedDataType
    {
        Text = 0,        // Generic text
        Amount = 1,      // Money/currency values
        Date = 2,        // Date values  
        Time = 3,        // Time values
        Number = 4,      // Numeric values (not money)
        Account = 5,     // Account numbers
        Code = 6,        // Reference codes, IDs
        Name = 7,        // Person/company names
        Address = 8,     // Address information
        VATNumber = 9,   // Tax identification numbers
        Currency = 10,   // Currency codes (THB, USD, etc.)
        Reference = 11   // Invoice numbers, transaction IDs
    }

    /// <summary>
    /// Document processing status
    /// </summary>
    public enum DocumentProcessingStatus
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
        Failed = 3,
        RequiresReview = 4
    }
}