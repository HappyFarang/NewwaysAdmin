// NewwaysAdmin.SharedModels/Models/Ocr/SearchPattern.cs
using NewwaysAdmin.SharedModels.Models.Ocr.Patterns;

namespace NewwaysAdmin.SharedModels.Models.Ocr.Patterns
{

    public class SearchPattern
    {
        public string SearchName { get; set; }      // e.g., "Date", "Total", "Note"
        public string KeyWord { get; set; }         // e.g., "Code", "Amount" 
        public int ToleranceX { get; set; }
        public int ToleranceY { get; set; }
        public string StopWords { get; set; }       // comma separated
        public string PatternType { get; set; }     // "VerticalColumn" or "Horizontal"
        public List<string> RegexPatterns { get; set; }  // Multiple regex patterns to test
    }
    public class PatternCollection
    {
        public string Name { get; set; }  // e.g., "KBIZ", "KBank", "SCB"
        public Dictionary<string, SearchPattern> SearchPatterns { get; set; }
        // Key = "Date", "Total", "Note" etc. (user-created names)
        // Value = the SearchPattern for that field
    }

    // NewwaysAdmin.SharedModels/Models/Ocr/PatternLibrary.cs
    public class PatternLibrary
    {
        public Dictionary<string, PatternCollection> Collections { get; set; }
        // Key = collection name ("KBIZ", "KBank" etc.)
        // Value = the PatternCollection
    }
}
public class PatternCollection
{
    public string Name { get; set; }  // e.g., "Bank slips", "KBIZ", etc.

    // THIS IS THE KEY CHANGE - PatternCollection can contain EITHER:
    public Dictionary<string, PatternCollection>? SubCollections { get; set; }  // For level 2 (KBIZ, KBank)
    public Dictionary<string, SearchPattern>? SearchPatterns { get; set; }      // For level 3 (Date, Total)
}
