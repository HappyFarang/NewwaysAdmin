using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.WebAdmin.Models.Auth;
using NewwaysAdmin.WebAdmin.Models.Navigation;

namespace NewwaysAdmin.WebAdmin.Services.Navigation;

public interface INavigationService
{
    Task<List<NavigationItem>> GetAllNavigationItemsAsync();
    Task<List<NavigationItem>> GetUserNavigationItemsAsync(User user);
    Task<NavigationItem?> GetNavigationItemAsync(string id);
    Task SaveNavigationItemsAsync(List<NavigationItem> items);
}

public class NavigationService : INavigationService
{
    private readonly IDataStorage<List<NavigationItem>> _navigationStorage;
    private readonly ILogger<NavigationService> _logger;

    public NavigationService(
        EnhancedStorageFactory storageFactory,
        ILogger<NavigationService> logger)
    {
        _navigationStorage = storageFactory.GetStorage<List<NavigationItem>>("Navigation");
        _logger = logger;
    }

    public async Task<List<NavigationItem>> GetUserNavigationItemsAsync(User user)
    {
        if (user.IsAdmin)
        {
            return await GetAllNavigationItemsAsync();
        }

        var allItems = await GetAllNavigationItemsAsync();
        return allItems
            .Where(item => item.IsActive && user.PageAccess.Any(access =>
                access.NavigationId == item.Id &&
                access.AccessLevel != AccessLevel.None))
            .ToList();
    }
    public async Task<List<NavigationItem>> GetAllNavigationItemsAsync()
    {
        try
        {
            return await _navigationStorage.LoadAsync("navigation-items") ?? new List<NavigationItem>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading navigation items");
            return new List<NavigationItem>();
        }
    }

    public async Task<List<NavigationItem>> GetUserNavigationItemsAsync(List<string> allowedIds)
    {
        var allItems = await GetAllNavigationItemsAsync();
        return allItems
            .Where(item => item.IsActive && allowedIds.Contains(item.Id))
            .ToList();
    }

    public async Task<NavigationItem?> GetNavigationItemAsync(string id)
    {
        var items = await GetAllNavigationItemsAsync();
        return items.FirstOrDefault(i => i.Id == id);
    }

    public async Task SaveNavigationItemsAsync(List<NavigationItem> items)
    {
        try
        {
            await _navigationStorage.SaveAsync("navigation-items", items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving navigation items");
            throw;
        }
    }
}

// Extension method to initialize default navigation items
/*public static class NavigationServiceExtensions
{
    public static async Task InitializeDefaultNavigationItems(this INavigationService navigationService)
    {
        var existingItems = await navigationService.GetAllNavigationItemsAsync();
        if (!existingItems.Any())
        {
            var defaultItems = new List<NavigationItem>
            {
                new()
                {
                    Id = "daily-sales",
                    Name = "Daily Sales",
                    Path = "/daily-sales",
                    Icon = "bi bi-graph-up",
                    Description = "View daily sales reports"
                },
                new()
                {
                    Id = "estimated-earnings",
                    Name = "Estimated Earnings",
                    Path = "/estimated-earnings",
                    Icon = "bi bi-calculator",
                    Description = "View estimated daily earnings"
                }
            };

            await navigationService.SaveNavigationItemsAsync(defaultItems);
        }
    }
}
*/