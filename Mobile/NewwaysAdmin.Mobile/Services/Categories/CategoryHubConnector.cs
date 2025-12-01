// File: Mobile/NewwaysAdmin.Mobile/Services/Categories/CategoryHubConnector.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Connectivity;
using NewwaysAdmin.Mobile.Services.SignalR;
using NewwaysAdmin.SharedModels.Categories;
using System.Text.Json;
using NewwaysAdmin.SignalR.Contracts.Models;

namespace NewwaysAdmin.Mobile.Services.Categories
{
    /// <summary>
    /// Connects CategoryDataService to SignalR hub
    /// Call ConnectAsync() after successful login
    /// </summary>
    public class CategoryHubConnector
    {
        private readonly ILogger<CategoryHubConnector> _logger;
        private readonly CategoryDataService _categoryDataService;
        private readonly ConnectionState _connectionState;
        private readonly SyncState _syncState;

        private HubConnection? _hubConnection;
        private bool _isConnected = false;
        private string _serverUrl = "http://localhost:5080";

        public CategoryHubConnector(
            ILogger<CategoryHubConnector> logger,
            CategoryDataService categoryDataService,
            ConnectionState connectionState,
            SyncState syncState)
        {
            _logger = logger;
            _categoryDataService = categoryDataService;
            _connectionState = connectionState;
            _syncState = syncState;

            _connectionState.OnConnectionChanged += OnConnectionStateChanged;
        }

        // ===== PUBLIC METHODS =====

        private async void OnConnectionStateChanged(object? sender, bool isOnline)
        {
            if (isOnline && !_isConnected)
            {
                _logger.LogInformation("HTTP connection online - attempting SignalR connect");
                await ConnectAsync();
            }
        }

        /// <summary>
        /// Set the server URL (call before ConnectAsync)
        /// </summary>
        public void SetServerUrl(string serverUrl)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            _logger.LogDebug("Server URL set to: {Url}", _serverUrl);
        }

        /// <summary>
        /// Connect to SignalR hub and start category sync
        /// Call this after successful login
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (_isConnected && _hubConnection?.State == HubConnectionState.Connected)
            {
                _logger.LogDebug("Already connected");
                return true;
            }

            try
            {
                _logger.LogInformation("Connecting to category sync hub at {Url}", _serverUrl);

                // Create hub connection
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl($"{_serverUrl}/hubs/universal")
                    .WithAutomaticReconnect(new[] {
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(30)
                    })
                    .Build();

                // Register event handlers
                RegisterHubEvents();

                // Start connection
                await _hubConnection.StartAsync();

                // Wire up CategoryDataService to use this hub connection
                // _categoryDataService.SetHubConnection(_hubConnection);

                _isConnected = true;
                _connectionState.SetOnline();

                _logger.LogInformation("Connected to SignalR hub");

                // Register as MAUI app
                await RegisterAppAsync();

                // Do initial version exchange
                await DoVersionExchangeAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to SignalR hub");
                _connectionState.SetOffline();
                return false;
            }
        }

        /// <summary>
        /// Disconnect from hub
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_hubConnection != null)
            {
                try
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                    _logger.LogInformation("Disconnected from SignalR hub");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during disconnect");
                }
                finally
                {
                    _hubConnection = null;
                    _isConnected = false;
                    _connectionState.SetOffline();
                }
            }
        }

        /// <summary>
        /// Manually trigger sync
        /// </summary>
        public async Task<bool> SyncNowAsync()
        {
            if (_hubConnection?.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Cannot sync - not connected");
                return false;
            }

            return await DoVersionExchangeAsync();
        }

        // ===== PRIVATE METHODS =====

        private void RegisterHubEvents()
        {
            if (_hubConnection == null) return;

            // Connection state events
            _hubConnection.Reconnecting += error =>
            {
                _logger.LogWarning("SignalR reconnecting: {Error}", error?.Message);
                _connectionState.SetOffline();
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += connectionId =>
            {
                _logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
                _connectionState.SetOnline();

                // Re-register and sync after reconnect
                _ = RegisterAppAsync();
                _ = DoVersionExchangeAsync();

                return Task.CompletedTask;
            };

            _hubConnection.Closed += error =>
            {
                _logger.LogWarning("SignalR connection closed: {Error}", error?.Message);
                _connectionState.SetOffline();
                _isConnected = false;
                return Task.CompletedTask;
            };

            // MESSAGE RESPONSE HANDLER - This is where responses come!
            _hubConnection.On<object>("MessageResponse", response =>
            {
                _logger.LogInformation("*** MessageResponse RECEIVED! Type: {Type}", response?.GetType().Name ?? "null");

                try
                {
                    var json = JsonSerializer.SerializeToElement(response);
                    _logger.LogInformation("*** Response content: {Json}", json.ToString().Substring(0, Math.Min(300, json.ToString().Length)));
                    _versionExchangeResponse?.TrySetResult(json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process MessageResponse");
                    _versionExchangeResponse?.TrySetResult(null);
                }
            });

            // Server push: new version available
            _hubConnection.On<int>("NewVersionAvailable", async version =>
            {
                _logger.LogInformation("Server notified new version: v{Version}", version);
                _syncState.SetRemoteVersion(version);

                if (_syncState.NeedsDownload)
                {
                    await DoVersionExchangeAsync();
                }
            });

            // Server push: full data (initial or update)
            _hubConnection.On<FullCategoryData>("CategoryData", async data =>
            {
                _logger.LogInformation("Received category data push: v{Version}", data.DataVersion);
                await HandleReceivedDataAsync(data);
            });

            _logger.LogDebug("Hub events registered");
        }

        private async Task RegisterAppAsync()
        {
            if (_hubConnection?.State != HubConnectionState.Connected) return;

            try
            {
                var registration = new
                {
                    AppName = "MAUI_ExpenseTracker",
                    AppVersion = "1.0.0",
                    DeviceId = GetDeviceId(),
                    DeviceType = DeviceInfo.Current.Platform.ToString()
                };

                await _hubConnection.InvokeAsync("RegisterAppAsync", registration);
                _logger.LogDebug("App registered with hub");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register app");
            }
        }

        private TaskCompletionSource<JsonElement?>? _versionExchangeResponse;

        private async Task<bool> DoVersionExchangeAsync()
        {
            if (_hubConnection?.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Cannot do version exchange - not connected");
                return false;
            }

            try
            {
                _logger.LogInformation("Starting version exchange (local: v{Version})", _syncState.LocalVersion);

                // Set up response listener
                _versionExchangeResponse = new TaskCompletionSource<JsonElement?>();

                var request = new VersionExchangeMessage
                {
                    MyVersion = _syncState.LocalVersion,
                    DeviceId = GetDeviceId(),
                    DeviceType = "MAUI"
                };

                var messageId = Guid.NewGuid().ToString();

                // Send message (response comes via MessageResponse event)
                await _hubConnection.SendAsync("SendMessageAsync", new UniversalMessage
                {
                    MessageId = messageId,
                    MessageType = "VersionExchange",
                    SourceApp = "MAUI_ExpenseTracker",
                    TargetApp = "MAUI_ExpenseTracker",
                    Data = JsonSerializer.SerializeToElement(request),
                    Timestamp = DateTime.UtcNow
                });

                // Wait for response with timeout
                var responseTask = _versionExchangeResponse.Task;
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));

                if (await Task.WhenAny(responseTask, timeoutTask) == timeoutTask)
                {
                    _logger.LogWarning("Version exchange timed out");
                    return false;
                }

                var resultJson = await responseTask;

                if (resultJson == null)
                {
                    _logger.LogWarning("No response received");
                    return false;
                }

                _logger.LogDebug("Received response: {Json}", resultJson.Value.ToString().Substring(0, Math.Min(500, resultJson.Value.ToString().Length)));

                // Parse the response
                if (resultJson.Value.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                {
                    if (resultJson.Value.TryGetProperty("data", out var dataProp))
                    {
                        var response = JsonSerializer.Deserialize<VersionExchangeResponse>(dataProp.GetRawText(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (response != null)
                        {
                            _syncState.SetRemoteVersion(response.ServerVersion);

                            if (response.YouNeedToDownload && response.Data != null)
                            {
                                _logger.LogInformation("Server has v{Version}, downloading...", response.ServerVersion);
                                await HandleReceivedDataAsync(response.Data);
                                return true;
                            }
                            else
                            {
                                _logger.LogDebug("Already up to date (v{Version})", _syncState.LocalVersion);
                                return true;
                            }
                        }
                    }
                }
                else if (resultJson.Value.TryGetProperty("error", out var errorProp))
                {
                    _logger.LogWarning("Server error: {Error}", errorProp.GetString());
                }

                _logger.LogWarning("Invalid response from version exchange");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Version exchange failed");
                _syncState.MarkDownloadFailed();
                return false;
            }
        }

        private async Task HandleReceivedDataAsync(FullCategoryData data)
        {
            try
            {
                // Save to cache via CategoryDataService
                // We need to access the private save method, so we'll trigger it via the public API
                // For now, save directly here

                var cacheDir = Path.Combine(FileSystem.AppDataDirectory, "CategoryCache");
                Directory.CreateDirectory(cacheDir);
                var cacheFilePath = Path.Combine(cacheDir, "category_data.json");

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(cacheFilePath, json);

                _syncState.MarkDownloadComplete(data.DataVersion);

                _logger.LogInformation(
                    "Saved data v{Version}: {CatCount} categories, {LocCount} locations, {PerCount} persons",
                    data.DataVersion,
                    data.Categories.Count,
                    data.Locations.Count,
                    data.Persons.Count);

                // Notify CategoryDataService that data changed
                _categoryDataService.NotifyDataChanged(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save received data");
                _syncState.MarkDownloadFailed();
            }
        }

        private string GetDeviceId()
        {
            try
            {
                return $"{DeviceInfo.Current.Platform}_{DeviceInfo.Current.Name}";
            }
            catch
            {
                return "Unknown_Device";
            }
        }

        // ===== HELPER CLASSES =====

        private class VersionExchangeResponseWrapper
        {
            public VersionExchangeResponse? Data { get; set; }
        }
    }
}