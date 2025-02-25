using NewwaysAdmin.WebAdmin.Models.Auth;
using NewwaysAdmin.WebAdmin.Models.Navigation;

namespace NewwaysAdmin.WebAdmin.Services.Modules
{
    public static class ModuleDefinitions
    {
        // Define which modules are admin-only
        private static readonly HashSet<string> AdminOnlyModules = new()
        {
            "settings"
        };

        // Define default access levels for modules
        private static readonly Dictionary<string, AccessLevel> DefaultAccessLevels = new()
        {
            { "home", AccessLevel.Read },
            { "test", AccessLevel.ReadWrite },
            { "settings", AccessLevel.ReadWrite },
            { "sales", AccessLevel.Read }
        };

        public static List<NavigationItem> GetModules()
        {
            return new List<NavigationItem>
            {
                new NavigationItem
                {
                    Id = "home",
                    Name = "Blant home",
                    Path = "/home",
                    Icon = "bi bi-graph-up",
                    Description = "Just a home with no function now. Might be removed later"
                },
                new NavigationItem
                {
                    Id = "test",
                    Name = "Test page",
                    Path = "/test",
                    Icon = "bi bi-calculator",
                    Description = "Test page that will be removed"
                },
                new NavigationItem
                {
                    Id = "settings",
                    Name = "Settings",
                    Path = "/settings",
                    Icon = "bi bi-bank",
                    Description = "Admin page for accounts and other settings"
                },
                new NavigationItem
                {
                    Id = "sales",
                    Name = "Sales",
                    Path = "/sales",
                    Icon = "bi bi-graph-up-arrow",
                    Description = "Sales overview and statistics"
                }
            };
        }

        public static bool IsAdminOnly(string moduleId)
        {
            return AdminOnlyModules.Contains(moduleId);
        }

        public static AccessLevel GetDefaultAccessLevel(string moduleId)
        {
            return DefaultAccessLevels.TryGetValue(moduleId, out var level)
                ? level
                : AccessLevel.None;
        }
    }

    public static class ModuleRegistryInitializationExtensions
    {
        public static async Task InitializeModulesAsync(this IModuleRegistry registry)
        {
            foreach (var module in ModuleDefinitions.GetModules())
            {
                registry.RegisterModule(module);
            }
            await registry.InitializeAsync();
        }
    }
}