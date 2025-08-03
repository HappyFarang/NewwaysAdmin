// NewwaysAdmin.WebAdmin/Services/Testing/IOcrTestingService.cs
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.Testing
{
    public interface IOcrTestingService
    {
        Task<OcrTestResult> ProcessImageWithExistingPipelineAsync(string imagePath, SlipCollection? collection = null);
        Task<List<RegexTestResult>> TestRegexPatterns(string text, List<string> patterns);
        Task<List<SlipCollection>> GetAvailableCollectionsAsync();
        Task<SlipCollection> CreateTestCollectionAsync(ProcessingParameters? customSettings = null);
    }

    public class OcrTestResult
    {
        public bool Success { get; set; }
        public string ExtractedText { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public TimeSpan ProcessingTime { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string ImageDimensions { get; set; } = string.Empty;
        public string ProcessedImagePath { get; set; } = string.Empty;
        public ProcessingParameters UsedSettings { get; set; } = new();
        public string CollectionUsed { get; set; } = string.Empty;
    }

    public class RegexTestResult
    {
        public string Pattern { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<RegexMatch> Matches { get; set; } = new();
        public int MatchCount { get; set; }
    }

    public class RegexMatch
    {
        public string Value { get; set; } = string.Empty;
        public int StartIndex { get; set; }
        public int Length { get; set; }
        public Dictionary<string, string> Groups { get; set; } = new();
    }
}