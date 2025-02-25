using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Web.Models;

namespace NewwaysAdmin.Web.Services
{
    public class UserService : IUserService
    {
        private readonly IOManager _ioManager;
        private IDataStorage<List<UserCredential>>? _storage;
        private const string USER_STORE_ID = "users";
        private readonly ILogger<UserService> _logger;

        public UserService(IOManager ioManager, ILogger<UserService> logger)
        {
            _ioManager = ioManager;
            _logger = logger;
            InitializeStorageAsync().GetAwaiter().GetResult();
        }

        private async Task InitializeStorageAsync()
        {
            try
            {
                _logger.LogInformation("Initializing UserService");
                _storage = await _ioManager.GetStorageAsync<List<UserCredential>>("Users");
                _logger.LogInformation("UserService initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize UserService");
                throw;
            }
        }

        public async Task<bool> ValidateUserAsync(string username, string password)
        {
            try
            {
                _logger.LogInformation("Attempting to validate user: {Username}", username);

                var users = await LoadUsersAsync();
                var user = users.FirstOrDefault(u =>
                    u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (user == null)
                {
                    _logger.LogWarning("User not found: {Username}", username);
                    return false;
                }

                if (user.ValidatePassword(password))
                {
                    _logger.LogInformation("User validated successfully: {Username}", username);
                    user.LastLogin = DateTime.UtcNow;
                    await SaveUsersAsync(users);
                    return true;
                }

                _logger.LogWarning("Invalid password for user: {Username}", username);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating user {Username}", username);
                return false;
            }
        }

        public async Task CreateUserAsync(string username, string password, string role = "User")
        {
            try
            {
                _logger.LogInformation("Attempting to create user: {Username} with role: {Role}", username, role);

                var users = await LoadUsersAsync();

                if (users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Username already exists: {Username}", username);
                    throw new InvalidOperationException($"Username '{username}' already exists");
                }

                var newUser = new UserCredential
                {
                    Username = username,
                    Role = role,
                    LastLogin = DateTime.MinValue
                };
                newUser.SetPassword(password);

                users.Add(newUser);
                await SaveUsersAsync(users);

                _logger.LogInformation("User created successfully: {Username}", username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user: {Username}", username);
                throw;
            }
        }

        public async Task<bool> UserExistsAsync(string username)
        {
            try
            {
                _logger.LogInformation("Checking if user exists: {Username}", username);

                var users = await LoadUsersAsync();
                var exists = users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                _logger.LogInformation("User {Username} exists: {Exists}", username, exists);
                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if user exists: {Username}", username);
                throw;
            }
        }

        private async Task<List<UserCredential>> LoadUsersAsync()
        {
            if (_storage == null)
                throw new InvalidOperationException("Storage not initialized");

            try
            {
                _logger.LogDebug("Loading users from storage");

                if (await _storage.ExistsAsync(USER_STORE_ID))
                {
                    var users = await _storage.LoadAsync(USER_STORE_ID);
                    _logger.LogDebug("Loaded {Count} users from storage", users.Count);
                    return users;
                }

                _logger.LogDebug("No existing users found, returning empty list");
                return new List<UserCredential>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load users from storage");
                throw;
            }
        }

        private async Task SaveUsersAsync(List<UserCredential> users)
        {
            if (_storage == null)
                throw new InvalidOperationException("Storage not initialized");

            await _storage.SaveAsync(USER_STORE_ID, users);
        }
    }
}