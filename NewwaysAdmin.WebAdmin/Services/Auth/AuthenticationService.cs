using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.WebAdmin.Models.Auth;
using NewwaysAdmin.WebAdmin.Services.Circuit;
using NewwaysAdmin.WebAdmin.Services.Modules;

namespace NewwaysAdmin.WebAdmin.Services.Auth;

public class AuthenticationService : IAuthenticationService
{
    private readonly IOManager _ioManager;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICircuitManager _circuitManager;
    private IDataStorage<List<User>>? _userStorage;
    private IDataStorage<List<UserSession>>? _sessionStorage;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private const string DEFAULT_ADMIN_USERNAME = "Superfox";
    private const string DEFAULT_ADMIN_PASSWORD = "Admin75";

    public AuthenticationService(
        IOManager ioManager,
        ILogger<AuthenticationService> logger,
        IHttpContextAccessor httpContextAccessor,
        ICircuitManager circuitManager)
    {
        _ioManager = ioManager;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _circuitManager = circuitManager;
    }

    private async Task EnsureStorageInitializedAsync()
    {
        if (_userStorage != null && _sessionStorage != null)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_userStorage == null)
                _userStorage = await _ioManager.GetStorageAsync<List<User>>("Users");

            if (_sessionStorage == null)
                _sessionStorage = await _ioManager.GetStorageAsync<List<UserSession>>("Sessions");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<(bool success, string? error)> LoginAsync(LoginModel model)
    {
        try
        {
            await EnsureStorageInitializedAsync();

            var users = await _userStorage!.LoadAsync("users-list") ?? new List<User>();
            var user = users.FirstOrDefault(u =>
                u.Username.Equals(model.Username, StringComparison.OrdinalIgnoreCase));

            if (user == null || !user.IsActive)
            {
                _logger.LogWarning("Login attempt failed for username: {Username} - User not found or inactive", model.Username);
                return (false, "Invalid username or password");
            }

            if (!VerifyPassword(model.Password, user.PasswordHash, user.Salt))
            {
                _logger.LogWarning("Login attempt failed for username: {Username} - Invalid password", model.Username);
                return (false, "Invalid username or password");
            }

            var circuitId = _circuitManager.GetCurrentCircuitId();
            var connectionId = _httpContextAccessor.HttpContext?.Connection.Id;

            if (string.IsNullOrEmpty(circuitId) || string.IsNullOrEmpty(connectionId))
            {
                _logger.LogError("Could not establish secure session - Circuit or Connection ID missing");
                return (false, "Could not establish secure session");
            }

            // Create new session
            var session = new UserSession
            {
                Username = user.Username,
                PageAccess = user.PageAccess,  
                IsAdmin = user.IsAdmin,
                LoginTime = DateTime.UtcNow,
                SessionId = GenerateSessionId(),
                CircuitId = circuitId,
                ConnectionId = connectionId
            };

            // Get existing sessions and clean up old ones for this user
            var sessions = await _sessionStorage!.LoadAsync("active-sessions") ?? new List<UserSession>();
            sessions = sessions.Where(s =>
                s.Username != user.Username &&
                (DateTime.UtcNow - s.LoginTime).TotalHours <= 48
            ).ToList();

            sessions.Add(session);
            await _sessionStorage!.SaveAsync("active-sessions", sessions);

            // Update user's last login
            user.LastLoginAt = DateTime.UtcNow;
            await _userStorage!.SaveAsync("users-list", users);

            // Notify the auth state provider

            _logger.LogInformation("User {Username} logged in successfully", user.Username);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login attempt for user {Username}", model.Username);
            return (false, "An error occurred during login");
        }
    }

    public async Task<bool> ValidateSessionAsync(string sessionId)
    {
        try
        {
            await EnsureStorageInitializedAsync();

            var sessions = await _sessionStorage!.LoadAsync("active-sessions") ?? new List<UserSession>();
            var session = sessions.FirstOrDefault(s => s.SessionId == sessionId);

            if (session == null)
            {
                return false;
            }

            var circuitId = _circuitManager.GetCurrentCircuitId();
            var connectionId = _httpContextAccessor.HttpContext?.Connection.Id;

            // Validate circuit and connection match
            if (session.CircuitId != circuitId || session.ConnectionId != connectionId)
            {
                await RemoveSessionAsync(sessionId);
                return false;
            }

            // Check if session is not expired (48 hours)
            if (DateTime.UtcNow - session.LoginTime > TimeSpan.FromHours(48))
            {
                await RemoveSessionAsync(sessionId);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        await EnsureStorageInitializedAsync();

        var session = await GetCurrentSessionAsync();
        if (session != null)
        {
            await RemoveSessionAsync(session.SessionId);
            _logger.LogInformation("User {Username} logged out", session.Username);
        }
    }

    public async Task<UserSession?> GetCurrentSessionAsync()
    {
        try
        {
            await EnsureStorageInitializedAsync();

            var circuitId = _circuitManager.GetCurrentCircuitId();
            var connectionId = _httpContextAccessor.HttpContext?.Connection.Id;

            if (string.IsNullOrEmpty(circuitId) || string.IsNullOrEmpty(connectionId))
            {
                _logger.LogDebug("No circuit or connection ID found");
                return null;
            }

            var sessions = await _sessionStorage!.LoadAsync("active-sessions") ?? new List<UserSession>();

            // Clean up expired sessions
            var now = DateTime.UtcNow;
            sessions = sessions.Where(s =>
                (now - s.LoginTime).TotalHours <= 48 &&  // 48-hour timeout
                s.CircuitId == circuitId &&              // Must match current circuit
                s.ConnectionId == connectionId           // Must match current connection
            ).ToList();

            await _sessionStorage!.SaveAsync("active-sessions", sessions);

            return sessions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current session");
            return null;
        }
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        await EnsureStorageInitializedAsync();
        return await _userStorage!.LoadAsync("users-list") ?? new List<User>();
    }

    public async Task UpdateUserAsync(User user)
    {
        await EnsureStorageInitializedAsync();

        var users = await GetAllUsersAsync();
        var index = users.FindIndex(u => u.Username == user.Username);
        if (index != -1)
        {
            users[index] = user;
            await _userStorage!.SaveAsync("users-list", users);
            _logger.LogInformation("User {Username} updated", user.Username);
        }
    }

    private async Task RemoveSessionAsync(string sessionId)
    {
        await EnsureStorageInitializedAsync();

        var sessions = await _sessionStorage!.LoadAsync("active-sessions") ?? new List<UserSession>();
        sessions = sessions.Where(s => s.SessionId != sessionId).ToList();
        await _sessionStorage!.SaveAsync("active-sessions", sessions);
    }

    private static string GenerateSessionId()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        var saltBytes = Convert.FromBase64String(storedSalt);
        var hash = Convert.ToBase64String(KeyDerivation.Pbkdf2(
            password: password,
            salt: saltBytes,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 100000,
            numBytesRequested: 256 / 8));

        return hash == storedHash;
    }

    public async Task<User?> GetUserByNameAsync(string username)
    {
        try
        {
            await EnsureStorageInitializedAsync();

            _logger.LogInformation("Loading users list for username: {Username}", username);
            var users = await _userStorage!.LoadAsync("users-list") ?? new List<User>();
            _logger.LogInformation("Found {Count} users in storage", users.Count);

            var user = users.FirstOrDefault(u => u.Username == username);
            if (user != null)
            {
                _logger.LogInformation("Found user {Username}, IsAdmin: {IsAdmin}", user.Username, user.IsAdmin);
                _logger.LogInformation("User has {Count} page access entries", user.PageAccess.Count);
            }
            else
            {
                _logger.LogWarning("User {Username} not found in users list", username);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by name {Username}", username);
            return null;
        }
    }

    public async Task InitializeDefaultAdminAsync()
    {
        try
        {
            await EnsureStorageInitializedAsync();

            var users = await _userStorage!.LoadAsync("users-list") ?? new List<User>();

            if (!users.Any())
            {
                _logger.LogInformation("No users found. Creating default admin user.");

                var salt = GenerateSalt();
                var hash = HashPassword(DEFAULT_ADMIN_PASSWORD, salt);

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
                    PageAccess = adminAccess,
                    IsAdmin = true,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                users.Add(adminUser);
                await _userStorage!.SaveAsync("users-list", users);
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

    public async Task<bool> CreateUserAsync(User user)
    {
        try
        {
            await EnsureStorageInitializedAsync();

            var users = await _userStorage!.LoadAsync("users-list") ?? new List<User>();
            _logger.LogInformation($"Loaded {users.Count} existing users");

            users.Add(user);
            await _userStorage!.SaveAsync("users-list", users);

            // Verify save
            var verifyUsers = await _userStorage!.LoadAsync("users-list");
            _logger.LogInformation($"After save: {verifyUsers?.Count ?? 0} users, including new user: {verifyUsers?.Any(u => u.Username == user.Username)}");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user {Username}", user.Username);
            return false;
        }
    }

    public async Task<bool> DeleteUserAsync(string username)
    {
        try
        {
            await EnsureStorageInitializedAsync();

            // Can't delete admin
            if (username.Equals("admin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Load all users
            var users = await GetAllUsersAsync();

            // Find and remove user
            var userToRemove = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (userToRemove == null)
            {
                return false;
            }

            users.Remove(userToRemove);

            // Save back to storage - note: fixed the key to be "users-list" to match other methods
            await _userStorage!.SaveAsync("users-list", users);

            // Also remove any active sessions for this user
            await RemoveUserSessionsAsync(username);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {Username}", username);
            return false;
        }
    }

    private async Task RemoveUserSessionsAsync(string username)
    {
        try
        {
            await EnsureStorageInitializedAsync();

            // Changed key from "sessions" to "active-sessions" to match other methods
            var sessions = await _sessionStorage!.LoadAsync("active-sessions") ?? new List<UserSession>();
            sessions.RemoveAll(s => s.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            await _sessionStorage!.SaveAsync("active-sessions", sessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing sessions for user {Username}", username);
            // Don't throw - this is a cleanup operation
        }
    }
}