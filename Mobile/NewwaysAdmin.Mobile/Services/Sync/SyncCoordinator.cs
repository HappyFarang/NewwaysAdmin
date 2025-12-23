// File: Mobile/NewwaysAdmin.Mobile/Services/Sync/SyncCoordinator.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Cache;
using NewwaysAdmin.Mobile.Services.SignalR;
using NewwaysAdmin.SignalR.Contracts.Models;

namespace NewwaysAdmin.Mobile.Services.Sync
{
    /// <summary>
    /// Coordinates between SignalR and Cache systems
    /// Single responsibility: Orchestrate sync operations between offline cache and server
    /// </summary>
    public class SyncCoordinator
    {
        private readonly ILogger<SyncCoordinator> _logger;
        private readonly SignalRConnection _connection;
        private readonly SignalRMessageSender _messageSender;
        private readonly SignalRAppRegistration _appRegistration;
        private readonly SignalREventListener _eventListener;
        private readonly CacheManager _cacheManager;

        private bool _isOnline = false;
        private bool _isSyncing = false;
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        public SyncCoordinator(
            ILogger<SyncCoordinator> logger,
            SignalRConnection connection,
            SignalRMessageSender messageSender,
            SignalRAppRegistration appRegistration,
            SignalREventListener eventListener,
            CacheManager cacheManager)
        {
            _logger = logger;
            _connection = connection;
            _messageSender = messageSender;
            _appRegistration = appRegistration;
            _eventListener = eventListener;
            _cacheManager = cacheManager;

            // Subscribe to connection events
            _eventListener.OnMessageResponseReceived += HandleMessageResponse;
            _eventListener.OnRegistrationError += HandleRegistrationError;
        }

        // ===== CONNECTION & REGISTRATION =====

        public async Task<bool> ConnectAndRegisterAsync(string serverUrl, string appName = "MAUI_ExpenseTracker")
        {
            try
            {
                _logger.LogInformation("Starting connection and registration process");

                // Step 1: Connect to SignalR
                if (!await _connection.ConnectAsync(serverUrl))
                {
                    _logger.LogError("Failed to connect to server");
                    return false;
                }

                // Step 2: Register event listeners
                _eventListener.RegisterEvents();

                // Step 3: Register as app
                if (!await _appRegistration.RegisterAsAppAsync(appName))
                {
                    _logger.LogError("Failed to register app");
                    return false;
                }

                _isOnline = true;
                _logger.LogInformation("Successfully connected and registered");

                // Step 4: Start syncing pending items
                _ = Task.Run(SyncPendingItemsAsync);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during connection and registration");
                _isOnline = false;
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            _isOnline = false;
            await _connection.DisconnectAsync();
            _logger.LogInformation("Disconnected from server");
        }

        // ===== SYNC OPERATIONS =====

        /// <summary>
        /// Add data to cache and sync immediately if online
        /// </summary>
        public async Task<string> CacheAndSyncAsync<T>(
            T data,
            string dataType,
            string messageType,
            CacheRetentionPolicy retentionPolicy = CacheRetentionPolicy.DeleteAfterSync) where T : class, new()
        {
            try
            {
                // Always cache first (offline-first approach)
                string cacheId;
                if (ShouldUseFileStorage(dataType))
                {
                    cacheId = await _cacheManager.CacheFileDataAsync(data, dataType, messageType, retentionPolicy);
                }
                else
                {
                    cacheId = await _cacheManager.CacheInlineDataAsync(data, dataType, messageType, retentionPolicy);
                }

                _logger.LogInformation("Cached {DataType} with ID {CacheId}", dataType, cacheId);

                // Try to sync immediately if online
                if (_isOnline && _connection.IsConnected)
                {
                    await SyncSingleItemAsync(cacheId);
                }
                else
                {
                    _logger.LogInformation("Offline - {DataType} will sync when connection is restored", dataType);
                }

                return cacheId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching and syncing {DataType}", dataType);
                throw;
            }
        }

        /// <summary>
        /// Sync all pending cache items
        /// </summary>
        public async Task SyncPendingItemsAsync()
        {
            if (_isSyncing || !_isOnline || !_connection.IsConnected)
            {
                _logger.LogDebug("Skipping sync - already syncing or offline");
                return;
            }

            try
            {
                _isSyncing = true;
                _logger.LogInformation("Starting sync of pending items");

                var pendingItems = await _cacheManager.GetPendingSyncItemsAsync();
                _logger.LogInformation("Found {Count} pending items to sync", pendingItems.Count);

                foreach (var item in pendingItems)
                {
                    await SyncSingleItemAsync(item.Id);

                    // Small delay to avoid overwhelming the server
                    await Task.Delay(100);
                }

                _logger.LogInformation("Completed sync of pending items");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pending items sync");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        // ===== PRIVATE METHODS =====

        private async Task SyncSingleItemAsync(string cacheId)
        {
            try
            {
                var items = await _cacheManager.GetPendingSyncItemsAsync();
                var item = items.FirstOrDefault(i => i.Id == cacheId);

                if (item == null)
                {
                    _logger.LogWarning("Cache item {CacheId} not found for sync", cacheId);
                    return;
                }

                // Get the actual data
                var data = await _cacheManager.GetCachedDataAsync<object>(cacheId);
                if (data == null)
                {
                    _logger.LogWarning("No data found for cache item {CacheId}", cacheId);
                    await _cacheManager.MarkAsFailedAsync(cacheId, "No data found");
                    return;
                }

                // Send via SignalR
                var success = await _messageSender.SendMessageAsync(item.MessageType, item.TargetApp, data);

                if (success)
                {
                    await _cacheManager.MarkAsSyncedAsync(cacheId);
                    _logger.LogInformation("Successfully synced cache item {CacheId}", cacheId);
                }
                else
                {
                    await _cacheManager.MarkAsFailedAsync(cacheId, "SignalR send failed");
                    _logger.LogWarning("Failed to sync cache item {CacheId}", cacheId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing cache item {CacheId}", cacheId);
                await _cacheManager.MarkAsFailedAsync(cacheId, ex.Message);
            }
        }

        private bool ShouldUseFileStorage(string dataType)
        {
            // Use file storage for large data types
            return dataType.Contains("Image") ||
                   dataType.Contains("Photo") ||
                   dataType.Contains("Receipt") ||
                   dataType.Contains("Document");
        }

        // ===== EVENT HANDLERS =====

        private async Task HandleMessageResponse(object response)
        {
            _logger.LogDebug("Received message response from server: {Response}", response);
            // TODO: Handle specific response types if needed
        }

        private async Task HandleRegistrationError(string error)
        {
            _logger.LogError("App registration error: {Error}", error);
            _isOnline = false;
            // TODO: Implement retry logic if needed
        }

        // ===== STATUS =====

        public bool IsOnline => _isOnline && _connection.IsConnected;

        public async Task<SyncStatus> GetSyncStatusAsync()
        {
            var stats = await _cacheManager.GetCacheStatsAsync();

            return new SyncStatus
            {
                IsOnline = IsOnline,
                IsSyncing = _isSyncing,
                PendingItems = stats.PendingItems,
                FailedItems = stats.FailedItems,
                TotalCachedItems = stats.TotalItems
            };
        }
        #region Document Upload

        /// <summary>
        /// Upload a document directly (with immediate response, no caching)
        /// Use for bank slips where we want immediate confirmation
        /// </summary>
        public async Task<DocumentUploadResponse> UploadDocumentAsync(DocumentUploadRequest request)
        {
            try
            {
                // Auto-connect if not online
                if (!IsOnline)
                {
                    await _connectLock.WaitAsync();
                    try
                    {
                        // Double-check after acquiring lock
                        if (!IsOnline)
                        {
                            _logger.LogInformation("SyncCoordinator not connected - attempting to connect...");
                            var connected = await ConnectAndRegisterAsync("http://newwaysadmin.hopto.org:5080");

                            if (!connected)
                            {
                                _logger.LogWarning("Cannot upload document - failed to connect");
                                return DocumentUploadResponse.CreateError("Offline", "Not connected to server");
                            }
                        }
                    }
                    finally
                    {
                        _connectLock.Release();
                    }
                }

                _logger.LogInformation("Uploading document: {FileName} ({Size} bytes)",
                    request.FileName, request.ImageBase64?.Length ?? 0);

                // Use SignalRMessageSender to send and get response
                var response = await _messageSender.SendMessageWithResponseAsync<DocumentUploadResponse>(
                    "UploadDocument",
                    "MAUI_ExpenseTracker",
                    request);

                if (response == null)
                {
                    return DocumentUploadResponse.CreateError("NoResponse", "No response from server");
                }

                if (response.Success)
                {
                    _logger.LogInformation("Document uploaded successfully: {DocumentId}", response.DocumentId);
                }
                else
                {
                    _logger.LogWarning("Document upload failed: {Error}", response.Message);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading document");
                return DocumentUploadResponse.CreateError("Exception", ex.Message);
            }
        }

        /// <summary>
        /// Upload a document with caching fallback (for offline support)
        /// Will cache and retry if offline
        /// </summary>
        public async Task<string> QueueDocumentUploadAsync(DocumentUploadRequest request)
        {
            return await CacheAndSyncAsync(
                request,
                "BankSlipImage",
                "UploadDocument",
                CacheRetentionPolicy.DeleteAfterSync);
        }

        #endregion
    }

    /// <summary>
    /// Sync status information
    /// </summary>
    public class SyncStatus
    {
        public bool IsOnline { get; set; }
        public bool IsSyncing { get; set; }
        public int PendingItems { get; set; }
        public int FailedItems { get; set; }
        public int TotalCachedItems { get; set; }
    }
}