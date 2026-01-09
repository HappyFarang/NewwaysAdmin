// File: Mobile/NewwaysAdmin.Mobile/Services/Categories/CategoryHubConnector.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Connectivity;
using NewwaysAdmin.SharedModels.Categories;
using System.Text.Json;
using NewwaysAdmin.SignalR.Contracts.Models;
using NewwaysAdmin.Mobile.Config;
using System.Collections.Concurrent;

namespace NewwaysAdmin.Mobile.Services.Categories
{
    /// <summary>
    /// Connects CategoryDataService to SignalR hub
    /// Handles ALL SignalR communication for category sync
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
        private string _serverUrl = AppConfig.ServerUrl;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement?>> _pendingResponses = new();

        // For awaiting MessageResponse
        private TaskCompletionSource<JsonElement?>? _versionExchangeResponse;

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

            // Subscribe to local changes - auto upload when data changes
            _categoryDataService.LocalDataChanged += OnLocalDataChanged;
        }

        // ===== PUBLIC PROPERTIES =====

        public bool IsConnected => _isConnected && _hubConnection?.State == HubConnectionState.Connected;

        // ===== PUBLIC METHODS =====

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
                _logger.LogDebug("Already connected to SignalR hub");
                return true;
            }

            try
            {
                _logger.LogInformation("Connecting to category sync hub at {Url}", _serverUrl);

                // Create hub connection
                _hubConnection = new HubConnectionBuilder()
                 .WithUrl($"{_serverUrl}/hubs/universal", options =>
                 {
                     options.Headers.Add("X-Mobile-Api-Key", AppConfig.MobileApiKey);
                 })
                 .WithAutomaticReconnect(new[] {
                     TimeSpan.Zero,
                     TimeSpan.FromSeconds(2),
                     TimeSpan.FromSeconds(5),
                     TimeSpan.FromSeconds(10),
                     TimeSpan.FromSeconds(30)
                 })
                 .Build();

                // Register event handlers BEFORE starting connection
                RegisterHubEvents();

                // Start connection
                await _hubConnection.StartAsync();

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

        private async void OnLocalDataChanged(object? sender, EventArgs e)
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                _logger.LogInformation("Local data changed - triggering upload...");

                // Small delay to let any rapid changes settle
                await Task.Delay(500);

                await UploadDataToServerAsync();
            }
            else
            {
                _logger.LogDebug("Local data changed but not connected - will sync on reconnect");
            }
        }

        private async void OnConnectionStateChanged(object? sender, bool isOnline)
        {
            if (isOnline && !_isConnected)
            {
                _logger.LogInformation("HTTP connection online - attempting SignalR connect");
                await ConnectAsync();
            }
        }

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

                // Fire and forget - don't block the SignalR thread
                _ = Task.Run(async () =>
                {
                    await RegisterAppAsync();
                    await DoVersionExchangeAsync();
                });

                return Task.CompletedTask;
            };

            _hubConnection.Closed += error =>
            {
                _logger.LogWarning("SignalR connection closed: {Error}", error?.Message);
                _connectionState.SetOffline();
                _isConnected = false;
                return Task.CompletedTask;
            };

            // MessageResponse handler - routes to specific waiters
            _hubConnection.On<JsonElement>("MessageResponse", response =>
            {
                _logger.LogDebug("MessageResponse received");

                try
                {
                    // Try to extract messageId for routing
                    string? messageId = null;
                    if (response.TryGetProperty("messageId", out var idProp))
                    {
                        messageId = idProp.GetString();
                    }

                    // Route to specific waiter if we have a messageId
                    if (!string.IsNullOrEmpty(messageId) && _pendingResponses.TryRemove(messageId, out var tcs))
                    {
                        _logger.LogDebug("Routing response for messageId: {MessageId}", messageId);

                        // Extract the data property if present
                        if (response.TryGetProperty("data", out var dataProp))
                        {
                            tcs.TrySetResult(dataProp);
                        }
                        else
                        {
                            tcs.TrySetResult(response);
                        }
                        return;
                    }

                    // Fallback: route to version exchange if that's waiting
                    // (for backward compatibility with existing category sync code)
                    var responseStr = response.ToString();
                    _logger.LogDebug("Response content: {Json}",
                        responseStr.Length > 300 ? responseStr.Substring(0, 300) + "..." : responseStr);

                    _versionExchangeResponse?.TrySetResult(response);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process MessageResponse");
                }
            });

            // Server push: new version available
            _hubConnection.On<int>("NewVersionAvailable", version =>
            {
                _logger.LogInformation("Server notified new version: v{Version}", version);
                _syncState.SetRemoteVersion(version);

                if (_syncState.NeedsDownload)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await DoVersionExchangeAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during version exchange from push notification");
                        }
                    });
                }
            });

            // Server push: full data
            _hubConnection.On<FullCategoryData>("CategoryData", data =>
            {
                _logger.LogInformation("Received category data push: v{Version}", data.DataVersion);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleReceivedDataAsync(data);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error handling received category data");
                    }
                });
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

                // Set up response listener BEFORE sending
                _versionExchangeResponse = new TaskCompletionSource<JsonElement?>();

                var request = new VersionExchangeMessage
                {
                    MyVersion = _syncState.LocalVersion,
                    DeviceId = GetDeviceId(),
                    DeviceType = "MAUI"
                };

                var messageId = Guid.NewGuid().ToString();

                // Send message
                await _hubConnection.SendAsync("SendMessageAsync", new UniversalMessage
                {
                    MessageId = messageId,
                    MessageType = "VersionExchange",
                    SourceApp = "MAUI_ExpenseTracker",
                    TargetApp = "MAUI_ExpenseTracker",
                    Data = JsonSerializer.SerializeToElement(request),
                    Timestamp = DateTime.UtcNow
                });

                _logger.LogDebug("Version exchange message sent, waiting for response...");

                // Wait for response with timeout
                var responseTask = _versionExchangeResponse.Task;
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));

                if (await Task.WhenAny(responseTask, timeoutTask) == timeoutTask)
                {
                    _logger.LogWarning("Version exchange timed out after 10 seconds");
                    return false;
                }

                var resultJson = await responseTask;

                if (resultJson == null || resultJson.Value.ValueKind == JsonValueKind.Undefined)
                {
                    _logger.LogWarning("No response received from version exchange");
                    return false;
                }

                _logger.LogDebug("Processing version exchange response...");

                // Parse the response
                if (resultJson.Value.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                {
                    if (resultJson.Value.TryGetProperty("data", out var dataProp))
                    {
                        var response = JsonSerializer.Deserialize<VersionExchangeResponse>(
                            dataProp.GetRawText(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (response != null)
                        {
                            _syncState.SetRemoteVersion(response.ServerVersion);

                            if (response.YouNeedUpdate && response.FullData != null)
                            {
                                // Server has newer data - download it
                                _logger.LogInformation("Server has v{Version}, downloading data...", response.ServerVersion);
                                await HandleReceivedDataAsync(response.FullData);
                                return true;
                            }
                            else if (response.ServerNeedsYourData)
                            {
                                // Server needs our data - upload it
                                _logger.LogInformation("Server needs our data (v{LocalVersion} > v{ServerVersion}), uploading...",
                                    _syncState.LocalVersion, response.ServerVersion);
                                return await UploadDataToServerAsync();
                            }
                            else
                            {
                                _logger.LogInformation("Already up to date (v{Version})", _syncState.LocalVersion);
                                return true;
                            }
                        }
                    }
                }
                else if (resultJson.Value.TryGetProperty("error", out var errorProp))
                {
                    _logger.LogWarning("Server error: {Error}", errorProp.GetString());
                }

                _logger.LogWarning("Invalid response format from version exchange");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Version exchange failed");
                _syncState.MarkDownloadFailed();
                return false;
            }
        }

        /// <summary>
        /// Upload local data to server when we have newer version
        /// </summary>
        private async Task<bool> UploadDataToServerAsync()
        {
            if (_hubConnection?.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Cannot upload - not connected");
                return false;
            }

            try
            {
                var localData = await _categoryDataService.GetDataAsync();
                if (localData == null)
                {
                    _logger.LogWarning("No local data to upload");
                    return false;
                }

                _logger.LogInformation(
                    "Uploading data to server: v{Version} - {CatCount} categories, {LocCount} locations, {PerCount} persons",
                    localData.DataVersion,
                    localData.Categories.Count,
                    localData.Locations.Count,
                    localData.Persons.Count);

                // Set up response listener
                _versionExchangeResponse = new TaskCompletionSource<JsonElement?>();

                var messageId = Guid.NewGuid().ToString();

                await _hubConnection.SendAsync("SendMessageAsync", new UniversalMessage
                {
                    MessageId = messageId,
                    MessageType = "UploadData",
                    SourceApp = "MAUI_ExpenseTracker",
                    TargetApp = "MAUI_ExpenseTracker",
                    Data = JsonSerializer.SerializeToElement(localData),
                    Timestamp = DateTime.UtcNow
                });

                _logger.LogDebug("Upload message sent, waiting for confirmation...");

                // Wait for confirmation with timeout
                var responseTask = _versionExchangeResponse.Task;
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));

                if (await Task.WhenAny(responseTask, timeoutTask) == timeoutTask)
                {
                    _logger.LogWarning("Upload timed out after 15 seconds");
                    return false;
                }

                var resultJson = await responseTask;

                if (resultJson == null)
                {
                    _logger.LogWarning("No response from upload");
                    return false;
                }

                // Check response
                if (resultJson.Value.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                {
                    if (resultJson.Value.TryGetProperty("data", out var dataProp))
                    {
                        if (dataProp.TryGetProperty("success", out var uploadSuccess) && uploadSuccess.GetBoolean())
                        {
                            _logger.LogInformation("✅ Upload successful! Server now has v{Version}", localData.DataVersion);
                            return true;
                        }
                    }
                }

                if (resultJson.Value.TryGetProperty("error", out var errorProp))
                {
                    _logger.LogWarning("Upload error: {Error}", errorProp.GetString());
                }

                _logger.LogWarning("Upload failed or invalid response");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading data to server");
                return false;
            }
        }

        private async Task HandleReceivedDataAsync(FullCategoryData data)
        {
            try
            {
                // Save to cache
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

                // Notify CategoryDataService that data changed - this triggers UI update
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
        #region Document Upload

        /// <summary>
        /// Upload a bank slip document via the existing hub connection
        /// </summary>
        public async Task<DocumentUploadResponse> UploadDocumentAsync(DocumentUploadRequest request)
        {
            if (_hubConnection?.State != HubConnectionState.Connected)
            {
                // Try to connect first
                _logger.LogInformation("Hub not connected, attempting to connect...");
                var connected = await ConnectAsync();

                if (!connected)
                {
                    _logger.LogWarning("Cannot upload document - failed to connect");
                    return DocumentUploadResponse.CreateError("Offline", "Not connected to server");
                }
            }

            try
            {
                _logger.LogInformation("Uploading document: {FileName} ({Size} bytes)",
                    request.FileName, request.ImageBase64?.Length ?? 0);

                _versionExchangeResponse = new TaskCompletionSource<JsonElement?>();

                await _hubConnection!.SendAsync("SendMessageAsync", new UniversalMessage
                {
                    MessageId = Guid.NewGuid().ToString(),
                    MessageType = "UploadDocument",
                    SourceApp = "MAUI_ExpenseTracker",
                    TargetApp = "MAUI_ExpenseTracker",
                    Data = JsonSerializer.SerializeToElement(request),
                    Timestamp = DateTime.UtcNow
                });

                _logger.LogDebug("Upload message sent, waiting for response...");

                var responseTask = _versionExchangeResponse.Task;
                if (await Task.WhenAny(responseTask, Task.Delay(30000)) == responseTask)
                {
                    var result = await responseTask;

                    if (result?.TryGetProperty("success", out var successProp) == true && successProp.GetBoolean())
                    {
                        if (result.Value.TryGetProperty("data", out var dataProp))
                        {
                            var response = JsonSerializer.Deserialize<DocumentUploadResponse>(
                                dataProp.GetRawText(),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (response?.Success == true)
                            {
                                _logger.LogInformation("✅ Document uploaded: {DocumentId}", response.DocumentId);
                                return response;
                            }
                        }

                        // Generic success if no detailed response
                        _logger.LogInformation("✅ Document uploaded successfully");
                        return new DocumentUploadResponse { Success = true };
                    }

                    if (result?.TryGetProperty("error", out var errorProp) == true)
                    {
                        var error = errorProp.GetString() ?? "Unknown error";
                        _logger.LogWarning("Upload failed: {Error}", error);
                        return DocumentUploadResponse.CreateError("ServerError", error);
                    }
                }

                _logger.LogWarning("Upload timed out after 30 seconds");
                return DocumentUploadResponse.CreateError("Timeout", "Upload timed out");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading document");
                return DocumentUploadResponse.CreateError("Exception", ex.Message);
            }
        }

        /// <summary>
        /// Send a message and await a typed response.
        /// Generic method for any SignalR request/response communication.
        /// </summary>
        /// <typeparam name="TRequest">Request type</typeparam>
        /// <typeparam name="TResponse">Expected response type</typeparam>
        /// <param name="messageType">Message type (e.g., "SyncProjectMetadata", "PullProject")</param>
        /// <param name="request">Request data</param>
        /// <param name="targetApp">Target app name (defaults to "MAUI_ExpenseTracker")</param>
        /// <param name="timeoutSeconds">Timeout in seconds (default 30)</param>
        /// <returns>Deserialized response or null on failure</returns>
        public async Task<TResponse?> SendMessageAsync<TRequest, TResponse>(
            string messageType,
            TRequest request,
            string targetApp = "MAUI_ExpenseTracker",
            int timeoutSeconds = 30)
            where TResponse : class
        {
            if (_hubConnection?.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Cannot send message - not connected");
                return null;
            }

            var messageId = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<JsonElement?>();

            try
            {
                // Register for response
                _pendingResponses[messageId] = tcs;

                // Create universal message
                var message = new UniversalMessage
                {
                    MessageId = messageId,
                    MessageType = messageType,
                    SourceApp = "MAUI_ExpenseTracker",
                    TargetApp = targetApp,
                    Data = JsonSerializer.SerializeToElement(request),
                    Timestamp = DateTime.UtcNow,
                    RequiresAck = false
                };

                _logger.LogDebug("Sending {MessageType} with messageId {MessageId}", messageType, messageId);

                // Send message
                await _hubConnection.InvokeAsync("SendMessageAsync", message);

                // Wait for response with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

                var completedTask = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(Timeout.Infinite, cts.Token));

                if (completedTask != tcs.Task)
                {
                    _logger.LogWarning("Timeout waiting for {MessageType} response", messageType);
                    return null;
                }

                var responseJson = await tcs.Task;
                if (responseJson == null)
                {
                    _logger.LogWarning("Null response for {MessageType}", messageType);
                    return null;
                }

                // Deserialize response
                var response = JsonSerializer.Deserialize<TResponse>(
                    responseJson.Value.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                _logger.LogDebug("{MessageType} completed successfully", messageType);
                return response;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Request {MessageType} was cancelled", messageType);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending {MessageType}", messageType);
                return null;
            }
            finally
            {
                // Clean up pending response
                _pendingResponses.TryRemove(messageId, out _);
            }
        }

        /// <summary>
        /// Send a message with object data and await typed response.
        /// </summary>
        public async Task<TResponse?> SendMessageAsync<TResponse>(
            string messageType,
            object request,
            string targetApp = "MAUI_ExpenseTracker",
            int timeoutSeconds = 30)
            where TResponse : class
        {
            return await SendMessageAsync<object, TResponse>(messageType, request, targetApp, timeoutSeconds);
        }

        #endregion
    }
}