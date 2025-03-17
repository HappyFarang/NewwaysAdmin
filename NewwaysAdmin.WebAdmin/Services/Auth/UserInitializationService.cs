using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.WebAdmin.Models.Auth;
using NewwaysAdmin.WebAdmin.Services.Modules;
// using NewwaysAdmin.WebAdmin.Infrastructure.Storage;

namespace NewwaysAdmin.WebAdmin.Services.Auth;

public class UserInitializationService
{
    private readonly IDataStorage<List<User>> _userStorage;
    private readonly ILogger<UserInitializationService> _logger;
    private const string DEFAULT_ADMIN_USERNAME = "Superfox";
    private const string DEFAULT_ADMIN_PASSWORD = "Admin75";

    public UserInitializationService(
        StorageManager storageManager,
        ILogger<UserInitializationService> logger)
    {
        // Use GetStorageSync instead of GetStorage to properly unwrap the Task
        _userStorage = storageManager.GetStorageSync<List<User>>("Users");
        _logger = logger;
    }
    public async Task EnsureAdminUserExistsAsync()
    {
        try
        {
            var users = await _userStorage.LoadAsync("users-list");
            if (users == null || !users.Any())
            {
                _logger.LogInformation("No users found. Creating default admin user.");
                var salt = GenerateSalt();
                var hash = HashPassword(DEFAULT_ADMIN_PASSWORD, salt);

                // Get all available modules and create access entries for each
                var modules = ModuleDefinitions.GetModules();
                var adminAccess = modules.Select(m => new UserPageAccess
                {
                    NavigationId = m.Id,
                    AccessLevel = AccessLevel.ReadWrite
                }).ToList();

                var adminUser = new User
                {
                    Username = DEFAULT_ADMIN_USERNAME,
                    PasswordHash = hash,
                    Salt = Convert.ToBase64String(salt),
                    PageAccess = adminAccess,  // Give admin access to all modules
                    IsAdmin = true,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                await _userStorage.SaveAsync("users-list", new List<User> { adminUser });
                _logger.LogInformation("Default admin user created successfully.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring admin user exists");
            throw;
        }
    }

    private static byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(128 / 8);
    }

    private static string HashPassword(string password, byte[] salt)
    {
        return Convert.ToBase64String(KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 100000,
            numBytesRequested: 256 / 8));
    }
}