using NewwaysAdmin.WebAdmin.Models.Auth;
using NewwaysAdmin.WebAdmin.Models.Navigation;

namespace NewwaysAdmin.WebAdmin.Services.Modules
{
    public static class ModuleDefinitions
    {
        // Define which modules are admin-only
        private static readonly HashSet<string> AdminOnlyModules = new()
        {
            "settings",
            "security"
        };

        // Define default access levels for modules
        private static readonly Dictionary<string, AccessLevel> DefaultAccessLevels = new()
        {
            { "home", AccessLevel.Read },
            { "test", AccessLevel.ReadWrite },
            { "settings", AccessLevel.ReadWrite },
            { "sales", AccessLevel.Read },
            { "accounting", AccessLevel.ReadWrite },           // Main accounting module
            { "accounting.bankslips", AccessLevel.ReadWrite }, // Bank slips sub-module
            { "worker-activity", AccessLevel.Read },
            { "accounting.reports", AccessLevel.Read },        // Future reports sub-module
            { "accounting.reconcile", AccessLevel.ReadWrite }, // Future reconciliation sub-module
            { "security", AccessLevel.ReadWrite }
        };

        // Define which modules require specific user configurations
        private static readonly HashSet<string> ModulesRequiringConfig = new()
        {
            "accounting.bankslips"
        };

        public static List<NavigationItem> GetModules()
        {
            return new List<NavigationItem>
            {
                new NavigationItem
                {
                    Id = "home",
                    Name = "Home",
                    Path = "/home",
                    Icon = "bi bi-house",
                    Description = "Dashboard and overview"
                },
                new NavigationItem
                {
                    Id = "sales",
                    Name = "Sales",
                    Path = "/sales",
                    Icon = "bi bi-graph-up-arrow",
                    Description = "Sales overview and statistics"
                },
                new NavigationItem
                {
                    Id = "accounting",
                    Name = "Accounting",
                    Path = "/accounting",
                    Icon = "bi bi-calculator",
                    Description = "Accounting tools and reports"
                },
                new NavigationItem  // Add this block - matching your existing pattern
                {
                    Id = "security",
                    Name = "Security",
                    Path = "/admin/security",
                    Icon = "fas fa-shield-alt",
                    IsActive = true,
                    Description = "DoS protection and security monitoring"
                },
                new NavigationItem
                {
                    Id = "worker-activity",
                    Name = "Worker Activity",
                    Path = "/worker-activity",
                    Icon = "bi bi-people",
                    Description = "Monitor worker attendance and activity"
                },
                new NavigationItem
                {
                    Id = "settings",
                    Name = "Settings",
                    Path = "/settings",
                    Icon = "bi bi-gear",
                    Description = "Admin page for accounts and other settings"
                },
                new NavigationItem
                {
                    Id = "test",
                    Name = "Test Page",
                    Path = "/test",
                    Icon = "bi bi-bug",
                    Description = "Test page for development"
                }
            };
        }

        // Get sub-modules for accounting
        public static List<NavigationItem> GetAccountingSubModules()
        {
            return new List<NavigationItem>
            {
                new NavigationItem
                {
                    Id = "accounting.bankslips",
                    Name = "Bank Slips",
                    Path = "/accounting/bankslips",
                    Icon = "bi bi-receipt",
                    Description = "Process and manage bank slip OCR"
                },
                new NavigationItem
                {
                    Id = "accounting.reports",
                    Name = "Reports",
                    Path = "/accounting/reports",
                    Icon = "bi bi-file-earmark-text",
                    Description = "Financial reports and summaries"
                },
                new NavigationItem
                {
                    Id = "accounting.reconcile",
                    Name = "Reconcile",
                    Path = "/accounting/reconcile",
                    Icon = "bi bi-check2-square",
                    Description = "Bank reconciliation tools"
                }
            };
        }

        // Get all modules including sub-modules (for permission management)
        public static List<NavigationItem> GetAllModulesFlat()
        {
            var allModules = GetModules().ToList();
            allModules.AddRange(GetAccountingSubModules());
            return allModules;
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

        public static bool RequiresConfiguration(string moduleId)
        {
            return ModulesRequiringConfig.Contains(moduleId);
        }

        // Helper to check if user has access to accounting sub-module
        public static bool HasAccountingSubModuleAccess(User user, string subModuleId)
        {
            // Must have access to main accounting module
            var accountingAccess = user.PageAccess.FirstOrDefault(p => p.NavigationId == "accounting");
            if (accountingAccess == null || accountingAccess.AccessLevel == AccessLevel.None)
                return false;

            // Check specific sub-module access
            var subModuleAccess = user.PageAccess.FirstOrDefault(p => p.NavigationId == subModuleId);
            return subModuleAccess != null && subModuleAccess.AccessLevel != AccessLevel.None;
        }
    }

    public static class ModuleRegistryInitializationExtensions
    {
        public static async Task InitializeModulesAsync(this IModuleRegistry registry)
        {
            // Register main modules
            foreach (var module in ModuleDefinitions.GetModules())
            {
                registry.RegisterModule(module);
            }

            // Register sub-modules for navigation service
            foreach (var subModule in ModuleDefinitions.GetAccountingSubModules())
            {
                registry.RegisterModule(subModule);
            }

            await registry.InitializeAsync();
        }
    }
}