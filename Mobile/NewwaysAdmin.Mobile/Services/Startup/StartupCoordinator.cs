// File: Mobile/NewwaysAdmin.Mobile/Services/Startup/StartupCoordinator.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.Services.Sync;

namespace NewwaysAdmin.Mobile.Services.Startup
{
    /// <summary>
    /// Coordinates app startup flow: auto-login, SignalR connection, sync
    /// Single responsibility: Handle app startup sequence
    /// </summary>
    public class StartupCoordinator
    {
        private readonly ILogger<StartupCoordinator> _logger;
        private readonly IMauiAuthService _authService;
        private readonly SyncCoordinator _syncCoordinator;

        public StartupCoordinator(
            ILogger<StartupCoordinator> logger,
            IMauiAuthService authService,
            SyncCoordinator syncCoordinator)
        {
            _logger = logger;
            _authService = authService;
            _syncCoordinator = syncCoordinator;
        }

        // ===== STARTUP FLOW =====

        public async Task<StartupResult> StartupAsync(string serverUrl)
        {
            try
            {
                _logger.LogInformation("Starting app startup sequence");

                // Step 1: Try auto-login with saved credentials
                var authResult = await _authService.TryAutoLoginAsync();

                if (authResult.Success)
                {
                    _logger.LogInformation("Auto-login successful - proceeding to main app");

                    // Step 2: Connect to SignalR and start sync
                    var signalRConnected = await _syncCoordinator.ConnectAndRegisterAsync(serverUrl);

                    if (signalRConnected)
                    {
                        _logger.LogInformation("SignalR connected - startup complete");
                        return new StartupResult
                        {
                            Success = true,
                            GoToMainApp = true,
                            IsOnline = true,
                            Message = "Welcome back! You're connected and ready to go."
                        };
                    }
                    else
                    {
                        _logger.LogWarning("Auto-login successful but SignalR connection failed - offline mode");
                        return new StartupResult
                        {
                            Success = true,
                            GoToMainApp = true,
                            IsOnline = false,
                            Message = "Welcome back! Working offline - will sync when connection is restored."
                        };
                    }
                }
                else if (authResult.RequiresManualLogin)
                {
                    _logger.LogInformation("No saved credentials found - showing login page");
                    return new StartupResult
                    {
                        Success = false,
                        GoToMainApp = false,
                        ShowLoginPage = true,
                        Message = "Please log in to continue"
                    };
                }
                else
                {
                    _logger.LogWarning("Auto-login failed: {Message}", authResult.Message);
                    return new StartupResult
                    {
                        Success = false,
                        GoToMainApp = false,
                        ShowLoginPage = true,
                        Message = authResult.Message ?? "Please log in to continue"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during app startup");
                return new StartupResult
                {
                    Success = false,
                    GoToMainApp = false,
                    ShowLoginPage = true,
                    Message = "Startup error - please try logging in"
                };
            }
        }

        // ===== MANUAL LOGIN FLOW =====

        public async Task<LoginResult> LoginAsync(string username, string password, string serverUrl)
        {
            try
            {
                _logger.LogInformation("Manual login attempt for user: {Username}", username);

                // Step 1: Authenticate with server
                var authResult = await _authService.LoginAsync(username, password, saveCredentials: true);

                if (authResult.Success)
                {
                    _logger.LogInformation("Manual login successful - connecting to SignalR");

                    // Step 2: Connect to SignalR and start sync
                    var signalRConnected = await _syncCoordinator.ConnectAndRegisterAsync(serverUrl);

                    return new LoginResult
                    {
                        Success = true,
                        GoToMainApp = true,
                        IsOnline = signalRConnected,
                        Message = signalRConnected
                            ? "Login successful! You're connected and ready to go."
                            : "Login successful! Working offline - will sync when connection is restored."
                    };
                }
                else
                {
                    _logger.LogWarning("Manual login failed: {Message}", authResult.Message);
                    return new LoginResult
                    {
                        Success = false,
                        GoToMainApp = false,
                        Message = authResult.Message ?? "Login failed"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during manual login");
                return new LoginResult
                {
                    Success = false,
                    GoToMainApp = false,
                    Message = "Login error - please try again"
                };
            }
        }
    }

    /// <summary>
    /// Result of app startup sequence
    /// </summary>
    public class StartupResult
    {
        public bool Success { get; set; }
        public bool GoToMainApp { get; set; }
        public bool ShowLoginPage { get; set; }
        public bool IsOnline { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Result of manual login
    /// </summary>
    public class LoginResult
    {
        public bool Success { get; set; }
        public bool GoToMainApp { get; set; }
        public bool IsOnline { get; set; }
        public string Message { get; set; } = "";
    }
}