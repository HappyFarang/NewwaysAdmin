// NewwaysAdmin.WebAdmin/Components/Features/Settings/OcrAnalyzer/SpatialOcrAnalyzer/SpatialOcrAnalyzerState.cs
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using NewwaysAdmin.SharedModels.Models.Ocr.Core;

namespace NewwaysAdmin.WebAdmin.Components.Features.Settings.OcrAnalyzer.Core
{
    /// <summary>
    /// Shared state service for SpatialOcrAnalyzer components
    /// Manages all data and state that needs to be shared between components
    /// </summary>
    public class SpatialOcrAnalyzerState
    {
        // Core state
        public bool IsProcessing { get; private set; } = false;
        public string Status { get; private set; } = string.Empty;
        public OcrExtractionResult? SpatialResult { get; private set; }

        // View state  
        public string ActiveView { get; private set; } = "heatmap";
        public string SearchFilter { get; set; } = string.Empty;
        public WordBoundingBox? SelectedWord { get; set; }

        // Visualization settings
        public string VisualizationSize { get; set; } = "medium";
        public bool ShowLabels { get; set; } = false;
        public bool ShowOutOfBounds { get; set; } = false;
        public bool ShowDebugInfo { get; set; } = false;

        // Pattern testing state (NEW)
        public string SelectedPatternType { get; set; } = "VerticalColumn";
        public string PatternSearchTerm { get; set; } = "";
        public int PatternYTolerance { get; set; } = 20;
        public int PatternXTolerance { get; set; } = 10;
        public string PatternStopWords { get; set; } = "";
        public bool ShowAllWordsInPattern { get; set; } = true;
        public bool ShowPatternOverlay { get; set; } = true;
        public PatternTestResult? CurrentPatternResult { get; set; }

        // Colors for visualization
        public readonly string[] Colors = new[]
        {
            "#FF6B6B", "#4ECDC4", "#45B7D1", "#96CEB4", "#FFEAA7",
            "#DDA0DD", "#98D8C8", "#F7DC6F", "#BB8FCE", "#85C1E9"
        };

        // Events for notifying components of state changes
        public event Action? StateChanged;

        // Methods for updating state
        public void SetProcessing(bool processing)
        {
            IsProcessing = processing;
            NotifyStateChanged();
        }

        public void SetStatus(string status)
        {
            Status = status;
            NotifyStateChanged();
        }

        public void SetSpatialResult(OcrExtractionResult? result)
        {
            SpatialResult = result;
            NotifyStateChanged();
        }

        public void SetActiveView(string view)
        {
            ActiveView = view;
            NotifyStateChanged();
        }

        public void ClearResults()
        {
            SpatialResult = null;
            SelectedWord = null;
            CurrentPatternResult = null;
            Status = string.Empty;
            NotifyStateChanged();
        }

        public void ClearPatternResults()
        {
            CurrentPatternResult = null;
            NotifyStateChanged();
        }

        public void SetPatternResult(PatternTestResult? result)
        {
            CurrentPatternResult = result;
            NotifyStateChanged();
        }

        // Helper methods
        public IEnumerable<WordBoundingBox> GetFilteredWords()
        {
            if (SpatialResult?.Document?.Words == null) return Enumerable.Empty<WordBoundingBox>();

            return SpatialResult.Document.Words.Where(w =>
                string.IsNullOrEmpty(SearchFilter) ||
                w.Text.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<WordBoundingBox> FilteredWords => GetFilteredWords();

        public bool IsWordHighlighted(WordBoundingBox word)
        {
            return !string.IsNullOrEmpty(SearchFilter) &&
                   word.Text.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsWordSelected(WordBoundingBox word)
        {
            return SelectedWord == word;
        }

        public void SelectWord(WordBoundingBox? word)
        {
            SelectedWord = word == SelectedWord ? null : word;
            NotifyStateChanged();
        }

        public void ClearSelection()
        {
            SelectedWord = null;
            NotifyStateChanged();
        }

        // Visualization helpers
        public string GetVisualizationStyle()
        {
            var size = VisualizationSize switch
            {
                "small" => "400px",
                "medium" => "600px",
                "large" => "800px",
                _ => "600px"
            };
            return $"width: {size}; height: {size}; position: relative; border: 2px solid #dee2e6; background: #fafafa; border-radius: 8px; overflow: hidden;";
        }

        public string GetWordColor(int index)
        {
            return Colors[index % Colors.Length];
        }

        // Debug helpers
        public IEnumerable<WordBoundingBox> GetInBoundsWords()
        {
            return GetFilteredWords().Where(w =>
                w.NormX1 >= 0 && w.NormX1 <= 1 &&
                w.NormY1 >= 0 && w.NormY1 <= 1 &&
                w.NormX2 >= 0 && w.NormX2 <= 1 &&
                w.NormY2 >= 0 && w.NormY2 <= 1);
        }

        public IEnumerable<WordBoundingBox> GetOutOfBoundsWords()
        {
            return GetFilteredWords().Where(w =>
                w.NormX1 < 0 || w.NormX1 > 1 ||
                w.NormY1 < 0 || w.NormY1 > 1 ||
                w.NormX2 < 0 || w.NormX2 > 1 ||
                w.NormY2 < 0 || w.NormY2 > 1);
        }

        public IEnumerable<WordBoundingBox> GetTooSmallWords()
        {
            return GetFilteredWords().Where(w =>
                (w.NormX2 - w.NormX1) < 0.001 || (w.NormY2 - w.NormY1) < 0.001);
        }

        public string GetMetadataInfo()
        {
            if (SpatialResult?.Metadata == null) return "No metadata available";

            var metadata = SpatialResult.Metadata;
            return $"Google Vision API: {metadata.GoogleVisionApiVersion}\n" +
                   $"Processing Engine: {metadata.ProcessingEngine}\n" +
                   $"Original Size: {metadata.OriginalImageWidth}x{metadata.OriginalImageHeight}\n" +
                   $"Raw Annotations: {metadata.RawAnnotationsCount}\n" +
                   $"Filtered Words: {metadata.FilteredWordsCount}";
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Pattern test result data class
    /// Holds the results of pattern matching operations
    /// </summary>
    public class PatternTestResult
    {
        public bool Success { get; set; } = false;
        public NewwaysAdmin.SharedModels.Models.Ocr.Core.WordBoundingBox? AnchorWord { get; set; }
        public List<NewwaysAdmin.SharedModels.Models.Ocr.Core.WordBoundingBox> GroupedWords { get; set; } = new();
        public string CombinedText { get; set; } = "";
        public Dictionary<string, object> Metadata { get; set; } = new();

        public void AddMetadata(string key, object value) => Metadata[key] = value;
    }
}