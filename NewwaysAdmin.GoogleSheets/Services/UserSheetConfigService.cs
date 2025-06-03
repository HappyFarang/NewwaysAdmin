// NewwaysAdmin.GoogleSheets/Services/UserSheetConfigService.cs
using Microsoft.Extensions.Logging;
using MessagePack;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.GoogleSheets.Services
{
    public class UserSheetConfigService
    {
        private readonly IDataStorage<List<UserSheetConfig>> _userConfigStorage;
        private readonly IDataStorage<List<AdminSheetConfig>> _adminConfigStorage;
        private readonly ILogger<UserSheetConfigService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public UserSheetConfigService(
            IDataStorage<List<UserSheetConfig>> userConfigStorage,
            IDataStorage<List<AdminSheetConfig>> adminConfigStorage,
            ILogger<UserSheetConfigService> logger)
        {
            _userConfigStorage = userConfigStorage ?? throw new ArgumentNullException(nameof(userConfigStorage));
            _adminConfigStorage = adminConfigStorage ?? throw new ArgumentNullException(nameof(adminConfigStorage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region User Configuration

        public async Task<UserSheetConfig?> GetUserConfigAsync(string userId, string moduleName)
        {
            try
            {
                await _lock.WaitAsync();

                var configs = await _userConfigStorage.LoadAsync("user-sheet-configs") ?? new List<UserSheetConfig>();
                var config = configs.FirstOrDefault(c => c.UserId == userId && c.ModuleName == moduleName);

                _logger.LogDebug("Retrieved config for user {UserId}, module {ModuleName}: {Found}",
                    userId, moduleName, config != null ? "Found" : "Not Found");

                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user config for {UserId}, module {ModuleName}", userId, moduleName);
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> SaveUserConfigAsync(UserSheetConfig config)
        {
            try
            {
                await _lock.WaitAsync();

                var configs = await _userConfigStorage.LoadAsync("user-sheet-configs") ?? new List<UserSheetConfig>();

                // Remove existing config for this user/module
                configs.RemoveAll(c => c.UserId == config.UserId && c.ModuleName == config.ModuleName);

                // Add updated config
                config.LastUpdated = DateTime.UtcNow;
                configs.Add(config);

                await _userConfigStorage.SaveAsync("user-sheet-configs", configs);

                _logger.LogInformation("Saved user config for {UserId}, module {ModuleName}", config.UserId, config.ModuleName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving user config for {UserId}, module {ModuleName}", config.UserId, config.ModuleName);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<List<UserSheetConfig>> GetAllUserConfigsAsync(string userId)
        {
            try
            {
                await _lock.WaitAsync();

                var configs = await _userConfigStorage.LoadAsync("user-sheet-configs") ?? new List<UserSheetConfig>();
                return configs.Where(c => c.UserId == userId).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all configs for user {UserId}", userId);
                return new List<UserSheetConfig>();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> DeleteUserConfigAsync(string userId, string moduleName)
        {
            try
            {
                await _lock.WaitAsync();

                var configs = await _userConfigStorage.LoadAsync("user-sheet-configs") ?? new List<UserSheetConfig>();
                var removed = configs.RemoveAll(c => c.UserId == userId && c.ModuleName == moduleName);

                if (removed > 0)
                {
                    await _userConfigStorage.SaveAsync("user-sheet-configs", configs);
                    _logger.LogInformation("Deleted user config for {UserId}, module {ModuleName}", userId, moduleName);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user config for {UserId}, module {ModuleName}", userId, moduleName);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        #endregion

        #region Admin Configuration

        public async Task<AdminSheetConfig?> GetAdminConfigAsync(string moduleName)
        {
            try
            {
                await _lock.WaitAsync();

                var configs = await _adminConfigStorage.LoadAsync("admin-sheet-configs") ?? new List<AdminSheetConfig>();
                var config = configs.FirstOrDefault(c => c.ModuleName == moduleName);

                _logger.LogDebug("Retrieved admin config for module {ModuleName}: {Found}",
                    moduleName, config != null ? "Found" : "Not Found");

                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving admin config for module {ModuleName}", moduleName);
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> SaveAdminConfigAsync(AdminSheetConfig config)
        {
            try
            {
                await _lock.WaitAsync();

                var configs = await _adminConfigStorage.LoadAsync("admin-sheet-configs") ?? new List<AdminSheetConfig>();

                // Remove existing config for this module
                configs.RemoveAll(c => c.ModuleName == config.ModuleName);

                // Add updated config
                config.LastUpdated = DateTime.UtcNow;
                configs.Add(config);

                await _adminConfigStorage.SaveAsync("admin-sheet-configs", configs);

                _logger.LogInformation("Saved admin config for module {ModuleName}", config.ModuleName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving admin config for module {ModuleName}", config.ModuleName);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<List<AdminSheetConfig>> GetAllAdminConfigsAsync()
        {
            try
            {
                await _lock.WaitAsync();

                var configs = await _adminConfigStorage.LoadAsync("admin-sheet-configs") ?? new List<AdminSheetConfig>();
                return configs.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all admin configs");
                return new List<AdminSheetConfig>();
            }
            finally
            {
                _lock.Release();
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get a setting value from user config with fallback to admin config
        /// </summary>
        public async Task<T?> GetSettingAsync<T>(string userId, string moduleName, string settingKey, T? defaultValue = default)
        {
            try
            {
                // First try user config
                var userConfig = await GetUserConfigAsync(userId, moduleName);
                if (userConfig?.Settings.TryGetValue(settingKey, out var userValue) == true)
                {
                    if (userValue is T typedValue)
                        return typedValue;

                    // Try to convert
                    try
                    {
                        return (T)Convert.ChangeType(userValue, typeof(T));
                    }
                    catch
                    {
                        _logger.LogWarning("Could not convert user setting {SettingKey} value to type {Type}", settingKey, typeof(T).Name);
                    }
                }

                // Fallback to admin config
                var adminConfig = await GetAdminConfigAsync(moduleName);
                if (adminConfig?.ColumnSettings.TryGetValue(settingKey, out var adminValue) == true)
                {
                    try
                    {
                        return (T)Convert.ChangeType(adminValue, typeof(T));
                    }
                    catch
                    {
                        _logger.LogWarning("Could not convert admin setting {SettingKey} value to type {Type}", settingKey, typeof(T).Name);
                    }
                }

                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting setting {SettingKey} for user {UserId}, module {ModuleName}", settingKey, userId, moduleName);
                return defaultValue;
            }
        }

        /// <summary>
        /// Set a user setting
        /// </summary>
        public async Task<bool> SetUserSettingAsync(string userId, string moduleName, string settingKey, object value, string updatedBy)
        {
            try
            {
                var config = await GetUserConfigAsync(userId, moduleName) ?? new UserSheetConfig
                {
                    UserId = userId,
                    ModuleName = moduleName
                };

                config.Settings[settingKey] = value;
                config.UpdatedBy = updatedBy;

                return await SaveUserConfigAsync(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting user setting {SettingKey} for user {UserId}, module {ModuleName}", settingKey, userId, moduleName);
                return false;
            }
        }

        #endregion
    }
}