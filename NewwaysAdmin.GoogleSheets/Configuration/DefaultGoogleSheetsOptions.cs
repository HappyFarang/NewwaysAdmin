using NewwaysAdmin.GoogleSheets.Models;

namespace NewwaysAdmin.GoogleSheets.Configuration
{
    public static class DefaultGoogleSheetsConfig
    {
        public static GoogleSheetsConfig Create(string credentialsPath)
        {
            return new GoogleSheetsConfig
            {
                CredentialsPath = credentialsPath,
                ApplicationName = "NewwaysAdmin Google Sheets",
                AutoShareWithUser = false
            };
        }

        public static GoogleSheetsConfig CreateForDevelopment()
        {
            return Create(@"C:\Keys\newwaysadmin-sheets-v2.json");
        }

        public static GoogleSheetsConfig CreateForProduction(string keyPath)
        {
            return Create(keyPath);
        }
    }
}