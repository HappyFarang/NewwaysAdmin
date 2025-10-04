using Microsoft.Extensions.Logging;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.WebAdmin.Models.Auth;
using NewwaysAdmin.WebAdmin.Services.Circuit;
using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.WebAdmin.Services.Auth
{
    public interface IAuthenticationService
    {
        Task<(bool success, string? error)> LoginAsync(LoginModel loginModel);
        Task LogoutAsync();
        Task<UserSession?> GetCurrentSessionAsync();
        Task<User?> GetUserByNameAsync(string username);
        Task<List<User>> GetAllUsersAsync();
        Task<bool> CreateUserAsync(User user);
        Task UpdateUserAsync(User user);
        Task<bool> DeleteUserAsync(string username);
    }

    public class AuthenticationService : IAuthenticationService
    {
        private readonly StorageManager _storageManager;
        private readonly ICircuitManager _circuitManager;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuthenticationService> _logger;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        private IDataStorage<List<User>>? _userStorage;
        private IDataStorage<List<UserSession>>? _sessionStorage;
        private UserSession? _currentSession;

        public AuthenticationService(
            StorageManager storageManager,
            ICircuitManager circuitManager,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuthenticationService> logger)
        {
            _storageManager = storageManager;
            _circuitManager = circuitManager;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        private async Task EnsureStorageInitializedAsync()
        {
            if (_userStorage != null && _sessionStorage != null) return;

            await _initLock.WaitAsync();
            try
            {
                _userStorage ??= await _storageManager.GetStorage<List<User>>("Users");
                _sessionStorage ??= await _storageManager.GetStorage<List<UserSession>>("Sessions");
                _logger.LogInformation("Authentication storage initialized");
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<(bool success, string? error)> LoginAsync(LoginModel loginModel)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                _logger.LogInformation("Login attempt for user: {Username}", loginModel.Username);

                if (string.IsNullOrWhiteSpace(loginModel.Username) || string.IsNullOrWhiteSpace(loginModel.Password))
                {
                    return (false, "Username and password are required");
                }

                var users = await _userStorage!.LoadAsync("users-list") ?? new List<User>();
                var user = users.FirstOrDefault(u => u.Username.Equals(loginModel.Username, StringComparison.OrdinalIgnoreCase));

                if (user == null)
                {
                    _logger.LogWarning("User not found: {Username}", loginModel.Username);
                    return (false, "Invalid username or password");
                }

                if (!user.IsActive)
                {
                    _logger.LogWarning("Inactive user attempted login: {Username}", loginModel.Username);
                    return (false, "Account is inactive");
                }

                // Verify password
                if (!PasswordHasher.VerifyPassword(loginModel.Password, user.Salt, user.PasswordHash))
                {
                    _logger.LogWarning("Invalid password for user: {Username}", loginModel.Username);
                    return (false, "Invalid username or password");
                }

                // Create session
                var circuitId = _circuitManager.GetCurrentCircuitId() ?? Guid.NewGuid().ToString();
                var connectionId = _httpContextAccessor.HttpContext?.Connection.Id ?? Guid.NewGuid().ToString();

                _currentSession = new UserSession
                {
                    Username = user.Username,
                    SessionId = Guid.NewGuid().ToString(),
                    PageAccess = user.PageAccess,
                    IsAdmin = user.IsAdmin,
                    LoginTime = DateTime.UtcNow,
                    CircuitId = circuitId,
                    ConnectionId = connectionId
                };

                // Save session
                var sessions = await _sessionStorage!.LoadAsync("active-sessions") ?? new List<UserSession>();
                sessions.RemoveAll(s => s.Username == user.Username);
                sessions.Add(_currentSession);
                await _sessionStorage.SaveAsync("active-sessions", sessions);

                // Update user's last login
                user.LastLoginAt = DateTime.UtcNow;
                await _userStorage.SaveAsync("users-list", users);

                _logger.LogInformation("User {Username} logged in successfully", user.Username);
                _circuitManager.MarkAsAuthenticated();
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user {Username}", loginModel.Username);
                return (false, "An error occurred during login");
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                if (_currentSession != null)
                {
                    await EnsureStorageInitializedAsync();

                    var sessions = await _sessionStorage!.LoadAsync("active-sessions") ?? new List<UserSession>();
                    sessions.RemoveAll(s => s.SessionId == _currentSession.SessionId);
                    await _sessionStorage.SaveAsync("active-sessions", sessions);

                    _logger.LogInformation("User {Username} logged out", _currentSession.Username);
                    _currentSession = null;

                    // ADD THIS LINE:
                    // Note: Circuit will be cleared when circuit closes, but we can clear auth flag now
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
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
                    _logger.LogDebug("Session not found: {SessionId}", sessionId);
                    return false;
                }

                // Optionally check if session is expired (if you have expiration logic)
                // For now, just check if session exists
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating session: {SessionId}", sessionId);
                return false;
            }
        }

        public async Task<UserSession?> GetCurrentSessionAsync()
        {
            if (_currentSession != null)
                return _currentSession;

            try
            {
                await EnsureStorageInitializedAsync();

                // Try to get by CircuitId first
                var circuitId = _circuitManager.GetCurrentCircuitId();
                if (!string.IsNullOrEmpty(circuitId))
                {
                    var sessions = await _sessionStorage!.LoadAsync("active-sessions") ?? new List<UserSession>();
                    _currentSession = sessions.FirstOrDefault(s => s.CircuitId == circuitId);

                    if (_currentSession != null)
                        return _currentSession;
                }

                // Fallback: Try to get by SessionId cookie
                var sessionId = _httpContextAccessor.HttpContext?.Request.Cookies["SessionId"];
                if (!string.IsNullOrEmpty(sessionId))
                {
                    var sessions = await _sessionStorage!.LoadAsync("active-sessions") ?? new List<UserSession>();
                    _currentSession = sessions.FirstOrDefault(s => s.SessionId == sessionId);

                    if (_currentSession != null)
                    {
                        _logger.LogInformation("Found session by cookie for user {Username}", _currentSession.Username);
                        return _currentSession;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current session");
                return null;
            }
        }

        public async Task<User?> GetUserByNameAsync(string username)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                var users = await _userStorage!.LoadAsync("users-list") ?? new List<User>();
                return users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by name: {Username}", username);
                return null;
            }
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            try
            {
                await EnsureStorageInitializedAsync();

                var users = await _userStorage!.LoadAsync("users-list") ?? new List<User>();
                return users.Where(u => u.IsActive).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all users");
                return new List<User>();
            }
        }

        public async Task<bool> CreateUserAsync(User user)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                var users = await _userStorage!.LoadAsync("users-list") ?? new List<User>();

                if (users.Any(u => u.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Attempted to create user with existing username: {Username}", user.Username);
                    return false;
                }

                users.Add(user);
                await _userStorage.SaveAsync("users-list", users);

                _logger.LogInformation("User created: {Username}", user.Username);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user: {Username}", user.Username);
                return false;
            }
        }

        public async Task UpdateUserAsync(User user)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                var users = await _userStorage!.LoadAsync("users-list") ?? new List<User>();
                var existingUserIndex = users.FindIndex(u => u.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));

                if (existingUserIndex >= 0)
                {
                    users[existingUserIndex] = user;
                    await _userStorage.SaveAsync("users-list", users);
                    _logger.LogInformation("User updated: {Username}", user.Username);
                }
                else
                {
                    _logger.LogWarning("Attempted to update non-existent user: {Username}", user.Username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user: {Username}", user.Username);
                throw;
            }
        }

        public async Task<bool> DeleteUserAsync(string username)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                var users = await _userStorage!.LoadAsync("users") ?? new List<User>();
                var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (user != null)
                {
                    user.IsActive = false;
                    await _userStorage.SaveAsync("users-list", users);
                    _logger.LogInformation("User deactivated: {Username}", username);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user: {Username}", username);
                return false;
            }
        }
    }
}