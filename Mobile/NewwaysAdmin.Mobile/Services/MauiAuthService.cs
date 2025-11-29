// File: Mobile/NewwaysAdmin.Mobile/Services/MauiAuthService.cs
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Models.Mobile;
using NewwaysAdmin.Mobile.Services.Auth;

namespace NewwaysAdmin.Mobile.Services
{
    public interface IMauiAuthService
    {
        Task<AuthResult> TryAutoLoginAsync();
        Task<AuthResult> LoginAsync(string username, string password, bool saveCredentials = true);
        Task<AuthResult> CheckSavedCredentialsAsync();
    }

    public class MauiAuthService : IMauiAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly CredentialStorageService _credentialStorage;
        private readonly PermissionsCache _permissionsCache;
        private readonly ILogger<MauiAuthService> _logger;

        public MauiAuthService(
            HttpClient httpClient,
            CredentialStorageService credentialStorage,
            PermissionsCache permissionsCache,
            ILogger<MauiAuthService> logger)
        {
            _httpClient = httpClient;
            _credentialStorage = credentialStorage;
            _permissionsCache = permissionsCache;
            _logger = logger;
        }

        public async Task<AuthResult> CheckSavedCredentialsAsync()
        {
            try
            {
                _logger.LogInformation("Checking for saved credentials (local only)");

                var savedCreds = await _credentialStorage.GetSavedCredentialsAsync();

                if (savedCreds == null)
                {
                    _logger.LogInformation("No saved credentials found");
                    return new AuthResult
                    {
                        Success = false,
                        RequiresManualLogin = true,
                        Message = "No saved credentials"
                    };
                }

                // We have credentials - check for cached permissions
                var cachedPermissions = await _permissionsCache.GetCachedPermissionsAsync(savedCreds.Username);

                _logger.LogInformation("Found saved credentials for user: {Username}, Permissions: {Count}",
                    savedCreds.Username, cachedPermissions?.Count ?? 0);

                return new AuthResult
                {
                    Success = true,
                    Username = savedCreds.Username,
                    Permissions = cachedPermissions ?? new List<string>(),
                    IsOfflineMode = true, // Assume offline until ConnectionMonitor says otherwise
                    Message = "Loaded from cache"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking saved credentials");
                return new AuthResult
                {
                    Success = false,
                    RequiresManualLogin = true,
                    Message = "Error checking credentials"
                };
            }
        }

        public async Task<AuthResult> TryAutoLoginAsync()
        {
            try
            {
                _logger.LogInformation("Attempting auto-login with saved credentials");

                var savedCreds = await _credentialStorage.GetSavedCredentialsAsync();
                if (savedCreds != null)
                {
                    _logger.LogInformation("Found saved credentials for user: {Username}", savedCreds.Username);

                    // Try online login first
                    var onlineResult = await LoginAsync(savedCreds.Username, savedCreds.Password, saveCredentials: false);

                    if (onlineResult.Success)
                    {
                        _logger.LogInformation("Online auto-login successful");
                        return onlineResult;
                    }
                    else
                    {
                        _logger.LogWarning("Online auto-login failed, checking cached permissions");

                        // Fall back to cached permissions for offline mode
                        var cachedPermissions = await _permissionsCache.GetCachedPermissionsAsync(savedCreds.Username);

                        if (cachedPermissions != null && cachedPermissions.Count > 0)
                        {
                            _logger.LogInformation("Using cached permissions for offline access");
                            return new AuthResult
                            {
                                Success = true,
                                Message = "Working offline with cached permissions",
                                Permissions = cachedPermissions,
                                IsOfflineMode = true,
                                Username = savedCreds.Username
                            };
                        }
                        else
                        {
                            _logger.LogWarning("No cached permissions available - manual login required");
                            return new AuthResult
                            {
                                RequiresManualLogin = true,
                                Message = "Server unavailable and no cached permissions found"
                            };
                        }
                    }
                }

                _logger.LogInformation("No saved credentials found - manual login required");
                return new AuthResult { RequiresManualLogin = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during auto-login attempt");
                return new AuthResult
                {
                    Success = false,
                    Message = "Error checking saved credentials",
                    RequiresManualLogin = true
                };
            }
        }

        public async Task<AuthResult> LoginAsync(string username, string password, bool saveCredentials = true)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Username and password are required"
                };
            }

            try
            {
                _logger.LogInformation("Attempting login for user: {Username}", username);

                var request = new MobileAuthRequest
                {
                    Username = username,
                    Password = password
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await _httpClient.PostAsJsonAsync("api/mobile/auth", request, cts.Token);

                _logger.LogInformation("Login request completed with status: {StatusCode}", response.StatusCode);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<MobileAuthResponse>(cancellationToken: cts.Token);

                    if (result?.Success == true)
                    {
                        _logger.LogInformation("Login successful for user: {Username}", username);

                        if (saveCredentials)
                        {
                            try
                            {
                                await _credentialStorage.SaveCredentialsAsync(username, password);
                                _logger.LogInformation("Credentials saved successfully");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to save credentials, but login was successful");
                            }
                        }

                        // Cache permissions for offline use
                        if (result.Permissions != null && result.Permissions.Count > 0)
                        {
                            try
                            {
                                await _permissionsCache.SavePermissionsAsync(username, result.Permissions);
                                _logger.LogInformation("Cached {Count} permissions for offline use", result.Permissions.Count);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to cache permissions, but login was successful");
                            }
                        }

                        return new AuthResult
                        {
                            Success = true,
                            Message = result.Message,
                            Permissions = result.Permissions,
                            IsOfflineMode = false,
                            Username = username
                        };
                    }
                    else
                    {
                        _logger.LogWarning("Login failed for user {Username}: Server returned success=false", username);
                        return new AuthResult
                        {
                            Success = false,
                            Message = result?.Message ?? "Authentication failed"
                        };
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Login failed for user {Username} with status {StatusCode}: {ErrorContent}",
                        username, response.StatusCode, errorContent);

                    return new AuthResult
                    {
                        Success = false,
                        Message = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                            ? "Invalid username or password"
                            : $"Server error: {response.StatusCode}"
                    };
                }
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Login request timed out for user: {Username}", username);

                // Network timeout - try cached permissions if we have credentials already
                if (!saveCredentials) // This means it's an auto-login attempt
                {
                    var cachedPermissions = await _permissionsCache.GetCachedPermissionsAsync(username);
                    if (cachedPermissions != null && cachedPermissions.Count > 0)
                    {
                        _logger.LogInformation("Using cached permissions due to network timeout");
                        return new AuthResult
                        {
                            Success = true,
                            Message = "Working offline - server timeout",
                            Permissions = cachedPermissions,
                            IsOfflineMode = true,
                            Username = username
                        };
                    }
                }

                return new AuthResult
                {
                    Success = false,
                    Message = "Login request timed out. Please check your connection and try again."
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error during login for user: {Username}", username);

                // Network error - try cached permissions if we have credentials already
                if (!saveCredentials) // This means it's an auto-login attempt
                {
                    var cachedPermissions = await _permissionsCache.GetCachedPermissionsAsync(username);
                    if (cachedPermissions != null && cachedPermissions.Count > 0)
                    {
                        _logger.LogInformation("Using cached permissions due to network error");
                        return new AuthResult
                        {
                            Success = true,
                            Message = "Working offline - network unavailable",
                            Permissions = cachedPermissions,
                            IsOfflineMode = true,
                            Username = username
                        };
                    }
                }

                return new AuthResult
                {
                    Success = false,
                    Message = "Network error. Please check your connection and try again."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during login for user: {Username}", username);
                return new AuthResult
                {
                    Success = false,
                    Message = "An unexpected error occurred. Please try again."
                };
            }
        }
    }
}