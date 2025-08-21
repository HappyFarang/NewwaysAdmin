// NewwaysAdmin.WebAdmin/Services/BankSlips/Templates/OcrFieldAnalyzerService.cs
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Templates
{
    /// <summary>
    /// Analyzes OCR processing results to discover available fields and their usage statistics
    /// Currently works with current session results (_lastResults from BankSlipsPage)
    /// Future: Will be extended to analyze stored results from IO manager
    /// </summary>
    public class OcrFieldAnalyzerService
    {
        private readonly ILogger<OcrFieldAnalyzerService> _logger;

        public OcrFieldAnalyzerService(ILogger<OcrFieldAnalyzerService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Analyze current session OCR results to get field statistics
        /// </summary>
        /// <param name="sessionResults">Current session results from BankSlipsPage._lastResults</param>
        /// <param name="collectionName">Collection name (KBIZ, SCB, TTB, etc.) for logging context</param>
        /// <returns>Field analysis with usage statistics</returns>
        public FieldAnalysisResult AnalyzeSessionFields(List<Dictionary<string, string>>? sessionResults, string collectionName = "")
        {
            var result = new FieldAnalysisResult
            {
                CollectionName = collectionName,
                AnalyzedAt = DateTime.UtcNow
            };

            if (sessionResults == null || !sessionResults.Any())
            {
                _logger.LogInformation("No session results to analyze for collection {Collection}", collectionName);
                return result;
            }

            result.TotalDocuments = sessionResults.Count;

            // Discover all unique field names across all documents
            var allFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var document in sessionResults)
            {
                // Skip error documents (they might have different structure)
                if (document.ContainsKey("Error"))
                    continue;

                foreach (var fieldName in document.Keys)
                {
                    allFieldNames.Add(fieldName);
                }
            }

            _logger.LogInformation("Discovered {FieldCount} unique fields in {DocumentCount} documents for collection {Collection}",
                allFieldNames.Count, result.TotalDocuments, collectionName);

            // Analyze usage statistics for each field
            foreach (var fieldName in allFieldNames.OrderBy(f => f))
            {
                var fieldStats = AnalyzeFieldUsage(sessionResults, fieldName);
                result.FieldStatistics[fieldName] = fieldStats;

                _logger.LogDebug("Field '{Field}': {Found}/{Total} documents ({Percentage:F1}%)",
                    fieldName, fieldStats.FoundInDocuments, fieldStats.TotalDocuments, fieldStats.UsagePercentage);
            }

            _logger.LogInformation("✅ Field analysis completed for collection {Collection}: {FieldCount} fields analyzed",
                collectionName, result.FieldStatistics.Count);

            return result;
        }

        /// <summary>
        /// Analyze usage statistics for a specific field
        /// </summary>
        private FieldUsageStatistics AnalyzeFieldUsage(List<Dictionary<string, string>> sessionResults, string fieldName)
        {
            var stats = new FieldUsageStatistics
            {
                FieldName = fieldName,
                TotalDocuments = sessionResults.Count
            };

            var foundCount = 0;
            var nonEmptyCount = 0;
            var valueLengths = new List<int>();

            foreach (var document in sessionResults)
            {
                // Skip error documents
                if (document.ContainsKey("Error"))
                    continue;

                // Check if field exists (case-insensitive)
                var actualKey = document.Keys.FirstOrDefault(k =>
                    k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

                if (actualKey != null)
                {
                    foundCount++;
                    var value = document[actualKey];

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        nonEmptyCount++;
                        valueLengths.Add(value.Length);

                        // Collect sample values for analysis
                        if (stats.SampleValues.Count < 3)
                        {
                            stats.SampleValues.Add(value);
                        }
                    }
                }
            }

            stats.FoundInDocuments = foundCount;
            stats.NonEmptyValues = nonEmptyCount;
            stats.AverageValueLength = valueLengths.Any() ? (int)valueLengths.Average() : 0;

            return stats;
        }

        /// <summary>
        /// Get recommended fields based on usage statistics
        /// Fields found in most documents are recommended for templates
        /// </summary>
        /// <param name="analysisResult">Result from AnalyzeSessionFields</param>
        /// <param name="minimumUsagePercentage">Minimum usage percentage to be recommended (default 70%)</param>
        /// <returns>List of recommended field names</returns>
        public List<string> GetRecommendedFields(FieldAnalysisResult analysisResult, double minimumUsagePercentage = 70.0)
        {
            return analysisResult.FieldStatistics
                .Where(kvp => kvp.Value.UsagePercentage >= minimumUsagePercentage)
                .OrderByDescending(kvp => kvp.Value.UsagePercentage)
                .ThenBy(kvp => kvp.Key)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// Detect likely data types based on field names and sample values
        /// This will be useful for setting default column types in templates
        /// </summary>
        public string SuggestDataType(string fieldName, FieldUsageStatistics stats)
        {
            // Date-related fields
            if (fieldName.Contains("Date", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("Time", StringComparison.OrdinalIgnoreCase))
            {
                return "Date";
            }

            // Amount/money-related fields
            if (fieldName.Contains("Amount", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("Fee", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("Cost", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("Price", StringComparison.OrdinalIgnoreCase))
            {
                return "Currency";
            }

            // Account numbers
            if (fieldName.Contains("Account", StringComparison.OrdinalIgnoreCase))
            {
                return "Text";
            }

            // Default to text
            return "Text";
        }
    }

    #region Data Models

    /// <summary>
    /// Result of field analysis containing statistics for all discovered fields
    /// </summary>
    public class FieldAnalysisResult
    {
        public string CollectionName { get; set; } = string.Empty;
        public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
        public int TotalDocuments { get; set; } = 0;
        public Dictionary<string, FieldUsageStatistics> FieldStatistics { get; set; } = new();

        /// <summary>
        /// Get all field names sorted by usage percentage (most used first)
        /// </summary>
        public List<string> GetFieldNamesByUsage()
        {
            return FieldStatistics
                .OrderByDescending(kvp => kvp.Value.UsagePercentage)
                .ThenBy(kvp => kvp.Key)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// Get summary text for logging/display
        /// </summary>
        public string GetSummary()
        {
            var recommendedCount = FieldStatistics.Count(kvp => kvp.Value.UsagePercentage >= 70);
            return $"Analyzed {TotalDocuments} documents, found {FieldStatistics.Count} fields, {recommendedCount} recommended";
        }
    }

    /// <summary>
    /// Usage statistics for a single field
    /// </summary>
    public class FieldUsageStatistics
    {
        public string FieldName { get; set; } = string.Empty;
        public int TotalDocuments { get; set; } = 0;
        public int FoundInDocuments { get; set; } = 0;
        public int NonEmptyValues { get; set; } = 0;
        public int AverageValueLength { get; set; } = 0;
        public List<string> SampleValues { get; set; } = new();

        /// <summary>
        /// Percentage of documents that contain this field
        /// </summary>
        public double UsagePercentage => TotalDocuments > 0 ? (double)FoundInDocuments / TotalDocuments * 100 : 0;

        /// <summary>
        /// Percentage of found fields that have non-empty values
        /// </summary>
        public double DataQualityPercentage => FoundInDocuments > 0 ? (double)NonEmptyValues / FoundInDocuments * 100 : 0;

        /// <summary>
        /// Is this field recommended for templates? (found in most documents with good data quality)
        /// </summary>
        public bool IsRecommended => UsagePercentage >= 70 && DataQualityPercentage >= 80;

        /// <summary>
        /// Get display text for UI (e.g., "15/15" or "8/15")
        /// </summary>
        public string GetUsageDisplay()
        {
            return $"{FoundInDocuments}/{TotalDocuments}";
        }

        /// <summary>
        /// Get CSS class for UI styling based on usage
        /// </summary>
        public string GetUsageCssClass()
        {
            return UsagePercentage switch
            {
                >= 90 => "text-success",      // Green for excellent availability
                >= 70 => "text-primary",      // Blue for good availability  
                >= 50 => "text-warning",      // Yellow for moderate availability
                _ => "text-muted"              // Gray for poor availability
            };
        }
    }

    #endregion
}