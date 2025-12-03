// File: Mobile/NewwaysAdmin.Mobile/Config/AppConfig.cs
namespace NewwaysAdmin.Mobile.Config
{
    public static class AppConfig
    {
#if DEBUG
        public const string ServerUrl = "http://localhost:5080";
        public const bool IsDebug = true;
#else
        public const string ServerUrl = "http://newwaysadmin.hopto.org:5080";
        public const bool IsDebug = false;
#endif
    }
}