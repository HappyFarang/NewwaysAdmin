// File: NewwaysAdmin.WebAdmin/Services/Auth/UserInitializationService.cs
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.WebAdmin.Models.Auth;
using NewwaysAdmin.WebAdmin.Services.Modules;

namespace NewwaysAdmin.WebAdmin.Services.Auth;

public class UserInitializationService
{
    private readonly IDataStorage<List<User>> _userStorage;
    private readonly ILogger<UserInitializationService> _logger;

    // Path to admin config (outside of git repo)
    private const string ADMIN_CONFIG_PATH = "C:/NewwaysAdmin/Security/admin-config.json";

    public UserInitializationService(
        StorageManager storageManager,
        ILogger<UserInitializationService> logger)
    {
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

                var adminConfig = await LoadAdminConfigAsync();
                if (adminConfig == null)
                {
                    _logger.LogError("Cannot create admin user - admin-config.json not found at {Path}", ADMIN_CONFIG_PATH);
                    throw new FileNotFoundException($"Admin config not found at {ADMIN_CONFIG_PATH}");
                }

                var salt = GenerateSalt();
                var hash = HashPassword(adminConfig.DefaultAdminPassword, salt);

                var modules = ModuleDefinitions.GetModules();
                var adminAccess = modules.Select(m => new UserPageAccess
                {
                    NavigationId = m.Id,
                    AccessLevel = AccessLevel.ReadWrite
                }).ToList();

                var adminUser = new User
                {
                    Username = adminConfig.DefaultAdminUsername,
                    PasswordHash = hash,
                    Salt = Convert.ToBase64String(salt),
                    PageAccess = adminAccess,
                    IsAdmin = true,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                await _userStorage.SaveAsync("users-list", new List<User> { adminUser });
                _logger.LogInformation("Default admin user '{Username}' created successfully.", adminConfig.DefaultAdminUsername);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring admin user exists");
            throw;
        }
    }

    private async Task<AdminConfig?> LoadAdminConfigAsync()
    {
        try
        {
            if (!File.Exists(ADMIN_CONFIG_PATH))
            {
                _logger.LogWarning("Admin config file not found at {Path}", ADMIN_CONFIG_PATH);
                return null;
            }

            var json = await File.ReadAllTextAsync(ADMIN_CONFIG_PATH);
            return JsonSerializer.Deserialize<AdminConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading admin config from {Path}", ADMIN_CONFIG_PATH);
            return null;
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

    private class AdminConfig
    {
        public string DefaultAdminUsername { get; set; } = "";
        public string DefaultAdminPassword { get; set; } = "";
    }
}