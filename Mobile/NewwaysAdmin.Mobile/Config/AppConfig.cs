// File: Mobile/NewwaysAdmin.Mobile/Config/AppConfig.cs
namespace NewwaysAdmin.Mobile.Config
{
    public static class AppConfig
    {
        // Always use real server (phone can't reach localhost)
        public const string ServerUrl = "http://newwaysadmin.hopto.org:5080";

    #if DEBUG
            public const bool IsDebug = true;
    #else
        public const bool IsDebug = false;
    #endif

        // Must match server's MobileApiSecurity.ApiKey
        public const string MobileApiKey = "NwAdmin2024!Mx9$kL#pQ7zR";
    }
    /*    public static class AppConfig
        {
    #if DEBUG
            public const string ServerUrl = "http://localhost:5080";
            public const bool IsDebug = true;
    #else
            public const string ServerUrl = "http://newwaysadmin.hopto.org:5080";
            public const bool IsDebug = false;
    #endif
            // Must match server's MobileApiSecurity.ApiKey
            public const string MobileApiKey = "NwAdmin2024!Mx9$kL#pQ7zR";
        }
    */
}