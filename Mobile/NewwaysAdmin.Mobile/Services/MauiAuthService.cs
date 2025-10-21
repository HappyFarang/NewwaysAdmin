// File: Mobile/NewwaysAdmin.Mobile/Services/MauiAuthService.cs
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Models.Mobile;

namespace NewwaysAdmin.Mobile.Services
{
    public interface IMauiAuthService
    {
        Task<AuthResult> TryAutoLoginAsync();
        Task<AuthResult> LoginAsync(string username, string password, bool saveCredentials = true);
    }

    public class MauiAuthService : IMauiAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly CredentialStorageService _credentialStorage;
        private readonly ILogger<MauiAuthService> _logger;

        public MauiAuthService(
            HttpClient httpClient,
            CredentialStorageService credentialStorage,
            ILogger<MauiAuthService> logger)
        {
            _httpClient = httpClient;
            _credentialStorage = credentialStorage;
            _logger = logger;
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
                    return await LoginAsync(savedCreds.Username, savedCreds.Password, saveCredentials: false);
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

                        return new AuthResult
                        {
                            Success = true,
                            Message = result.Message,
                            Permissions = result.Permissions
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
                return new AuthResult
                {
                    Success = false,
                    Message = "Login request timed out. Please check your connection and try again."
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error during login for user: {Username}", username);
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