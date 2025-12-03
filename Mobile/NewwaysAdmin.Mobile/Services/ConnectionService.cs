// File: Mobile/NewwaysAdmin.Mobile/Services/ConnectionService.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Config;

namespace NewwaysAdmin.Mobile.Services
{
    public interface IConnectionService
    {
        Task<ConnectionResult> TestConnectionAsync();
        Task<ConnectionResult> TestAuthEndpointAsync();
        string GetBaseUrl();
    }

    public class ConnectionService : IConnectionService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ConnectionService> _logger;

        public ConnectionService(HttpClient httpClient, ILogger<ConnectionService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public string GetBaseUrl()
        {
            return AppConfig.ServerUrl;
        }

        public async Task<ConnectionResult> TestConnectionAsync()
        {
            try
            {
                _logger.LogInformation("Testing basic connection to: {BaseUrl}", GetBaseUrl());

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.GetAsync("api/mobile/ping", cts.Token);

                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Connection test response: {StatusCode}, Content: {Content}",
                    response.StatusCode, responseContent);

                return new ConnectionResult
                {
                    Success = response.IsSuccessStatusCode,
                    Message = response.IsSuccessStatusCode
                        ? "Connection successful"
                        : $"Server returned: {response.StatusCode}",
                    ResponseContent = responseContent,
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Connection test timed out after 10 seconds");
                return new ConnectionResult
                {
                    Success = false,
                    Message = "Connection timed out - server may not be running",
                    Exception = ex
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("HTTP request failed: {Message}", ex.Message);
                return new ConnectionResult
                {
                    Success = false,
                    Message = $"Network error: {ex.Message}",
                    Exception = ex
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during connection test");
                return new ConnectionResult
                {
                    Success = false,
                    Message = $"Unexpected error: {ex.Message}",
                    Exception = ex
                };
            }
        }

        public async Task<ConnectionResult> TestAuthEndpointAsync()
        {
            try
            {
                _logger.LogInformation("Testing auth endpoint availability");

                // Send an empty POST to see if the endpoint responds (should return 400 but proves it's there)
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.PostAsync("api/mobile/auth", null, cts.Token);

                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Auth endpoint test response: {StatusCode}, Content: {Content}",
                    response.StatusCode, responseContent);

                return new ConnectionResult
                {
                    Success = true, // Any response means the endpoint exists
                    Message = $"Auth endpoint responding with {response.StatusCode}",
                    ResponseContent = responseContent,
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing auth endpoint");
                return new ConnectionResult
                {
                    Success = false,
                    Message = $"Auth endpoint error: {ex.Message}",
                    Exception = ex
                };
            }
        }
    }

    public class ConnectionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string ResponseContent { get; set; } = "";
        public int StatusCode { get; set; }
        public Exception? Exception { get; set; }
    }
}