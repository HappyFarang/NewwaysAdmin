using NewwaysAdmin.GoogleSheets.Models; 

namespace NewwaysAdmin.GoogleSheets.Configuration
{
    public static class DefaultGoogleSheetsOptions
    {
        public static GoogleSheetsOptions Create(string serviceAccountKeyPath)
        {
            return new GoogleSheetsOptions
            {
                ServiceAccountKeyPath = serviceAccountKeyPath,
                ApplicationName = "NewwaysAdmin Google Sheets",
                DefaultScope = "https://www.googleapis.com/auth/spreadsheets",
                DriveScope = "https://www.googleapis.com/auth/drive"
            };
        }

        public static GoogleSheetsOptions CreateForDevelopment()
        {
            return Create(@"C:\Keys\newwaysadmin-sheets-service-account.json");
        }

        public static GoogleSheetsOptions CreateForProduction(string keyPath)
        {
            return Create(keyPath);
        }
    }
}