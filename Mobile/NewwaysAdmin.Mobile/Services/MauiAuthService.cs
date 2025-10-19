// File: Mobile/NewwaysAdmin.Mobile/Services/MauiAuthService.cs
using System.Text.Json;
using NewwaysAdmin.SharedModels.Models.Mobile;
using NewwaysAdmin.Mobile.Services;
using System.Net.Http.Json;

namespace NewwaysAdmin.Mobile.Services
{
    public class MauiAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly CredentialStorageService _credentialStorage;

        public MauiAuthService(HttpClient httpClient, CredentialStorageService credentialStorage)
        {
            _httpClient = httpClient;
            _credentialStorage = credentialStorage;
        }

        public async Task<AuthResult> TryAutoLoginAsync()
        {
            var savedCreds = await _credentialStorage.GetSavedCredentialsAsync();
            if (savedCreds != null)
            {
                return await LoginAsync(savedCreds.Username, savedCreds.Password, saveCredentials: false);
            }

            return new AuthResult { RequiresManualLogin = true };
        }

        public async Task<AuthResult> LoginAsync(string username, string password, bool saveCredentials = true)
        {
            try
            {
                var request = new MobileAuthRequest
                {
                    Username = username,
                    Password = password
                };

                var response = await _httpClient.PostAsJsonAsync("api/mobile/auth", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<MobileAuthResponse>();

                    if (result?.Success == true)
                    {
                        if (saveCredentials)
                        {
                            await _credentialStorage.SaveCredentialsAsync(username, password);
                        }

                        return new AuthResult
                        {
                            Success = true,
                            Message = result.Message,
                            Permissions = result.Permissions
                        };
                    }
                }

                return new AuthResult
                {
                    Success = false,
                    Message = "Login failed"
                };
            }
            catch (Exception ex)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = $"Connection error: {ex.Message}"
                };
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/mobile/ping");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}