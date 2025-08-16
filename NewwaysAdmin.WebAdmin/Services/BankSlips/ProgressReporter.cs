// NewwaysAdmin.WebAdmin/Services/BankSlips/ProgressReporter.cs

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    /// <summary>
    /// Simple progress reporting - no interface needed, just a concrete class
    /// </summary>
    public class ProgressReporter
    {
        public void ReportProgress(int processedCount, int totalCount, string currentFileName = "")
        {
            // Simple implementation - can be extended as needed
            var percentage = totalCount > 0 ? (double)processedCount / totalCount * 100 : 0;
            Console.WriteLine($"Progress: {processedCount}/{totalCount} ({percentage:F1}%) - {currentFileName}");
        }
    }
}