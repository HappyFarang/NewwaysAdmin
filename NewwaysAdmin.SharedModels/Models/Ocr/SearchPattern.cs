// NewwaysAdmin.SharedModels/Models/Ocr/SearchPattern.cs
// UPDATED with proper 3-level structure + Date Parsing Support + Number Parsing Support

using NewwaysAdmin.SharedModels.Models.Ocr;
using NewwaysAdmin.SharedModels.Services.Parsing;

namespace NewwaysAdmin.SharedModels.Models.Ocr
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

        // Date parsing support
        public bool NeedDateParsing { get; set; } = false;
        public DateParsingType DateParsingType { get; set; } = DateParsingType.Thai; // Default to Thai

        // Number parsing support
        public bool NeedNumberParsing { get; set; } = false;
        public NumberParsingType NumberParsingType { get; set; } = NumberParsingType.Thai; // 
    }

    public class PatternSubCollection
    {
        public string Name { get; set; }  // e.g., "KBIZ", "KBank", "SCB", "HomePro"
        public Dictionary<string, SearchPattern> SearchPatterns { get; set; }
        // Key = "Date", "Total", "Note" etc. (field names)
        // Value = the SearchPattern for that field

        public PatternSubCollection()
        {
            SearchPatterns = new Dictionary<string, SearchPattern>();
        }
    }

    public class PatternCollection
    {
        public string Name { get; set; }  // e.g., "BankSlips", "Invoices", "Bills"
        public Dictionary<string, PatternSubCollection> SubCollections { get; set; }
        // Key = Sub groups (KBIZ, KBank, HomePro, etc)
        // Value = the PatternSubCollection containing specific patterns

        public PatternCollection()
        {
            SubCollections = new Dictionary<string, PatternSubCollection>();
        }
    }

    public class PatternLibrary
    {
        public Dictionary<string, PatternCollection> Collections { get; set; }
        // Key = Document type ("BankSlips", "Invoices", "Bills", etc)
        // Value = the PatternCollection containing sub-collections

        public PatternLibrary()
        {
            Collections = new Dictionary<string, PatternCollection>();
        }
    }
}