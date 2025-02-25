using NewwaysAdmin.WebAdmin.Models.Navigation;
using NewwaysAdmin.WebAdmin.Services.Navigation;

namespace NewwaysAdmin.WebAdmin.Services.Modules
{
    public interface IModuleRegistry
    {
        void RegisterModule(NavigationItem module);
        Task InitializeAsync();
        Task<List<NavigationItem>> GetRegisteredModulesAsync();
    }

    public class ModuleRegistry : IModuleRegistry
    {
        private readonly ILogger<ModuleRegistry> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly List<NavigationItem> _moduleRegistry = new();

        public ModuleRegistry(
            ILogger<ModuleRegistry> logger,
            IServiceProvider serviceProvider)  // Inject IServiceProvider instead of INavigationService
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public void RegisterModule(NavigationItem module)
        {
            _moduleRegistry.Add(module);
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Create a scope to use scoped services
                using var scope = _serviceProvider.CreateScope();
                var navigationService = scope.ServiceProvider.GetRequiredService<INavigationService>();

                // Simply save our current module registry, overwriting whatever was there before
                await navigationService.SaveNavigationItemsAsync(_moduleRegistry.ToList());
                _logger.LogInformation("Initialized navigation with {Count} modules", _moduleRegistry.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing module registry");
                throw;
            }
        }

        public async Task<List<NavigationItem>> GetRegisteredModulesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var navigationService = scope.ServiceProvider.GetRequiredService<INavigationService>();
            return await navigationService.GetAllNavigationItemsAsync();
        }
    }

    // Extension method to make registration easy in Program.cs
    public static class ModuleRegistryExtensions
    {
        public static IServiceCollection AddModuleRegistry(this IServiceCollection services)
        {
            services.AddSingleton<IModuleRegistry, ModuleRegistry>();
            return services;
        }
    }
}