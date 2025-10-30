// File: Mobile/NewwaysAdmin.Mobile/Services/Auth/PermissionsCache.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Mobile.Services.Auth
{
    /// <summary>
    /// Caches user permissions locally for offline access
    /// Single responsibility: Store and retrieve user permissions
    /// </summary>
    public class PermissionsCache
    {
        private readonly ILogger<PermissionsCache> _logger;
        private readonly EnhancedStorageFactory _storageFactory;

        public PermissionsCache(
            ILogger<PermissionsCache> logger,
            EnhancedStorageFactory storageFactory)
        {
            _logger = logger;
            _storageFactory = storageFactory;
        }

        // ===== SAVE & LOAD PERMISSIONS =====

        public async Task SavePermissionsAsync(string username, List<string> permissions)
        {
            try
            {
                var permissionData = new CachedPermissions
                {
                    Username = username,
                    Permissions = permissions,
                    LastUpdated = DateTime.UtcNow,
                    ServerVersion = DateTime.UtcNow.Ticks // Simple versioning
                };

                var storage = _storageFactory.GetStorage<CachedPermissions>("MobileAuth");
                await storage.SaveAsync($"permissions_{username}", permissionData);

                _logger.LogInformation("Saved {Count} permissions for user {Username}",
                    permissions.Count, username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving permissions for user {Username}", username);
                throw;
            }
        }

        public async Task<List<string>?> GetCachedPermissionsAsync(string username)
        {
            try
            {
                var storage = _storageFactory.GetStorage<CachedPermissions>("MobileAuth");
                var permissionData = await storage.LoadAsync($"permissions_{username}");

                if (permissionData != null)
                {
                    _logger.LogInformation("Loaded {Count} cached permissions for user {Username} (last updated: {LastUpdated})",
                        permissionData.Permissions.Count, username, permissionData.LastUpdated);

                    return permissionData.Permissions;
                }
                else
                {
                    _logger.LogInformation("No cached permissions found for user {Username}", username);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading cached permissions for user {Username}", username);
                return null;
            }
        }

        // ===== PERMISSION CHECKING =====

        public async Task<bool> HasPermissionAsync(string username, string permission)
        {
            var permissions = await GetCachedPermissionsAsync(username);
            return permissions?.Contains(permission) ?? false;
        }

        public async Task<bool> HasAnyPermissionAsync(string username, params string[] permissions)
        {
            var userPermissions = await GetCachedPermissionsAsync(username);
            if (userPermissions == null) return false;

            return permissions.Any(p => userPermissions.Contains(p));
        }

        // ===== CLEANUP =====

        public async Task ClearPermissionsAsync(string username)
        {
            try
            {
                var storage = _storageFactory.GetStorage<CachedPermissions>("MobileAuth");
                await storage.DeleteAsync($"permissions_{username}");
                _logger.LogInformation("Cleared cached permissions for user {Username}", username);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error clearing permissions for user {Username}", username);
            }
        }
    }

    /// <summary>
    /// Cached permission data
    /// </summary>
    public class CachedPermissions
    {
        public string Username { get; set; } = "";
        public List<string> Permissions { get; set; } = new();
        public DateTime LastUpdated { get; set; }
        public long ServerVersion { get; set; }
    }
}