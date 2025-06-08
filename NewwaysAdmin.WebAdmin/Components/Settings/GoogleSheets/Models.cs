namespace NewwaysAdmin.WebAdmin.Components.Settings.GoogleSheets.Models
{
    public enum TemplateType
    {
        Basic,
        Enhanced
    }

    public enum DisplayMode
    {
        Welcome,
        List,
        Designer
    }

    public class GoogleSheetTemplate
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TemplateType Type { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModified { get; set; }
        // Add other template properties as needed
    }

    public class ColumnDefinition
    {
        public string Header { get; set; } = string.Empty;
        public string DataType { get; set; } = "Text";
        public string Format { get; set; } = "Default";
        public bool IsRequired { get; set; } = false;
        public string ValidationRule { get; set; } = string.Empty;
        public string DefaultValue { get; set; } = string.Empty;
        public bool AllowEdit { get; set; } = true;
    }

    public class FormulaDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Formula { get; set; } = string.Empty;
        public string TargetColumn { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
