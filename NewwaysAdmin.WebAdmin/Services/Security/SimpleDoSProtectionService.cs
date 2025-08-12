// Services/Security/SimpleDoSProtectionService.cs
using System.Net;
using System.Collections.Concurrent;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.WebAdmin.Services.Security
{
    public class RequestRecord
    {
        public DateTime Timestamp { get; set; }
        public string IpAddress { get; set; } = "";
        public string Path { get; set; } = "";
        public int ResponseCode { get; set; }
        public string UserAgent { get; set; } = "";
        public bool IsAuthenticated { get; set; }
    }

    public class BlockedClient
    {
        public string IpAddress { get; set; } = "";
        public DateTime BlockedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string Reason { get; set; } = "";
        public int TotalRequests { get; set; }
        public string UserAgent { get; set; } = "";
        public bool IsPermanent { get; set; }
    }

    public class DoSCheckResult
    {
        public bool IsBlocked { get; set; }
        public string Reason { get; set; } = "";
        public TimeSpan? RemainingBlockTime { get; set; }
        public int RequestsInWindow { get; set; }
        public bool IsHighRisk { get; set; }
    }

    public class SecurityMetrics
    {
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public int TotalRequestsLastHour { get; set; }
        public int UniqueIPsLastHour { get; set; }
        public int CurrentlyBlockedIPs { get; set; }
        public int AutoBlocksToday { get; set; }
        public int PublicPageRequests { get; set; }
        public int AuthenticatedPageRequests { get; set; }
        public List<string> TopAttackers { get; set; } = new();
        public List<string> MostTargetedPaths { get; set; } = new();
    }

    public interface ISimpleDoSProtectionService
    {
        Task<DoSCheckResult> CheckRequestAsync(IPAddress ipAddress, string userAgent, string path, bool isAuthenticated);
        Task LogRequestAsync(IPAddress ipAddress, string userAgent, string path, int responseCode, bool isAuthenticated);
        Task<List<BlockedClient>> GetBlockedClientsAsync();
        Task ManuallyBlockAsync(IPAddress ipAddress, TimeSpan duration, string reason);
        Task PermanentlyBanAsync(IPAddress ipAddress, string reason);
        Task UnblockAsync(IPAddress ipAddress);
        Task<SecurityMetrics> GetMetricsAsync();
    }

    public class SimpleDoSProtectionService : ISimpleDoSProtectionService
    {
        private readonly StorageManager _storageManager;
        private readonly ILogger<SimpleDoSProtectionService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        // Storage instances
        private IDataStorage<List<RequestRecord>>? _requestStorage;
        private IDataStorage<List<BlockedClient>>? _blockedStorage;

        // In-memory cache for performance (with storage backup)
        private readonly ConcurrentDictionary<string, List<RequestRecord>> _requestCache = new();
        private readonly ConcurrentDictionary<string, BlockedClient> _blockedCache = new();
        private DateTime _lastStorageSync = DateTime.MinValue;
        private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(5);

        // Protection settings - VERY RELAXED for debugging
        private readonly int _publicMaxPerMinute = 50;      // Very high for debugging
        private readonly int _publicMaxPerHour = 200;       // Very high for debugging
        private readonly int _authMaxPerMinute = 120;
        private readonly int _authMaxPerHour = 3600;

        // Suspicious patterns
        private readonly string[] _publicPages = { "/", "/login", "/register", "/forgot-password", "/error", "/access-denied" };
        private readonly string[] _suspiciousUserAgents = {
            "bot", "crawler", "spider", "scan", "curl", "wget", "python",
            "go-http", "java", "perl", "ruby", "nmap", "masscan", "zmap",
            "exploit", "sqlmap", "nikto", "dirb", "gobuster", "wfuzz"
        };

        public SimpleDoSProtectionService(
            StorageManager storageManager,
            ILogger<SimpleDoSProtectionService> logger)
        {
            _storageManager = storageManager;
            _logger = logger;

            // Initialize storage and start background tasks
            _ = Task.Run(InitializeStorageAsync);
            _ = Task.Run(BackgroundCleanupAsync);

            _logger.LogInformation("Simple DoS Protection Service initialized with storage persistence");
        }

        private async Task InitializeStorageAsync()
        {
            try
            {
                _requestStorage = await _storageManager.GetStorage<List<RequestRecord>>("Security");
                _blockedStorage = await _storageManager.GetStorage<List<BlockedClient>>("Security");

                // Load existing blocked clients from storage
                await LoadBlockedClientsFromStorageAsync();

                _logger.LogInformation("Security storage initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize security storage - continuing with in-memory only");
            }
        }

        public async Task<DoSCheckResult> CheckRequestAsync(IPAddress ipAddress, string userAgent, string path, bool isAuthenticated)
        {
            var ipString = ipAddress.ToString();

            // Check if already blocked
            if (_blockedCache.TryGetValue(ipString, out var blocked))
            {
                if (DateTime.UtcNow < blocked.ExpiresAt)
                {
                    return new DoSCheckResult
                    {
                        IsBlocked = true,
                        Reason = blocked.Reason,
                        RemainingBlockTime = blocked.ExpiresAt - DateTime.UtcNow
                    };
                }
                else if (!blocked.IsPermanent)
                {
                    _blockedCache.TryRemove(ipString, out _);
                }
            }

            await _lock.WaitAsync();
            try
            {
                // Get request history for this IP
                if (!_requestCache.ContainsKey(ipString))
                    _requestCache[ipString] = new List<RequestRecord>();

                var history = _requestCache[ipString];
                var now = DateTime.UtcNow;

                // Clean old requests (older than 24 hours)
                history.RemoveAll(r => r.Timestamp < now.AddHours(-24));

                // Determine if this is a public page or login attempt
                var isPublicPage = IsPublicPage(path);
                var isLoginPage = path.ToLower().Contains("/login");

                // Be very lenient with login pages and authenticated users
                var useStrictLimits = isPublicPage && !isLoginPage && !isAuthenticated;

                // Log for debugging
                _logger.LogInformation("DoS Check for {IpAddress}: Path={Path}, IsPublicPage={IsPublicPage}, IsLoginPage={IsLoginPage}, IsAuthenticated={IsAuthenticated}, UseStrictLimits={UseStrictLimits}",
                    ipString, path, isPublicPage, isLoginPage, isAuthenticated, useStrictLimits);

                // Check rate limits
                var lastMinute = history.Where(r => r.Timestamp > now.AddMinutes(-1)).Count();
                var lastHour = history.Where(r => r.Timestamp > now.AddHours(-1)).Count();

                var maxPerMinute = useStrictLimits ? _publicMaxPerMinute : _authMaxPerMinute;
                var maxPerHour = useStrictLimits ? _publicMaxPerHour : _authMaxPerHour;

                var result = new DoSCheckResult
                {
                    RequestsInWindow = lastMinute,
                    IsHighRisk = lastMinute >= maxPerMinute * 0.7
                };

                // Check minute limit
                if (lastMinute >= maxPerMinute)
                {
                    _logger.LogWarning("BLOCKING {IpAddress}: {LastMinute}/{MaxPerMinute} requests in last minute. Path: {Path}, UseStrictLimits: {UseStrictLimits}",
                        ipString, lastMinute, maxPerMinute, path, useStrictLimits);

                    await BlockClientAsync(ipString, TimeSpan.FromMinutes(30),
                        $"Rate limit exceeded: {lastMinute}/{maxPerMinute} per minute");
                    result.IsBlocked = true;
                    result.Reason = "Rate limit exceeded";
                    return result;
                }

                // Check hour limit
                if (lastHour >= maxPerHour)
                {
                    _logger.LogWarning("BLOCKING {IpAddress}: {LastHour}/{MaxPerHour} requests in last hour. Path: {Path}",
                        ipString, lastHour, maxPerHour, path);

                    await BlockClientAsync(ipString, TimeSpan.FromHours(2),
                        $"Hourly limit exceeded: {lastHour}/{maxPerHour} per hour");
                    result.IsBlocked = true;
                    result.Reason = "Hourly limit exceeded";
                    return result;
                }

                // Check for suspicious user agents (only for public pages)
                if (useStrictLimits && IsSuspiciousUserAgent(userAgent))
                {
                    await BlockClientAsync(ipString, TimeSpan.FromHours(8),
                        $"Suspicious user agent: {userAgent}");
                    result.IsBlocked = true;
                    result.Reason = "Suspicious user agent";
                    return result;
                }

                return result;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task LogRequestAsync(IPAddress ipAddress, string userAgent, string path, int responseCode, bool isAuthenticated)
        {
            var ipString = ipAddress.ToString();

            await _lock.WaitAsync();
            try
            {
                if (!_requestCache.ContainsKey(ipString))
                    _requestCache[ipString] = new List<RequestRecord>();

                var record = new RequestRecord
                {
                    Timestamp = DateTime.UtcNow,
                    IpAddress = ipString,
                    Path = path,
                    ResponseCode = responseCode,
                    UserAgent = userAgent,
                    IsAuthenticated = isAuthenticated
                };

                _requestCache[ipString].Add(record);

                // Periodic sync to storage
                if (ShouldSyncToStorage())
                {
                    await SyncToStorageAsync();
                }

                // Check for patterns that warrant immediate blocking
                await CheckForSuspiciousPatterns(ipString, userAgent, _requestCache[ipString]);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task BlockClientAsync(string ipAddress, TimeSpan duration, string reason)
        {
            var blockedClient = new BlockedClient
            {
                IpAddress = ipAddress,
                BlockedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(duration),
                Reason = reason,
                TotalRequests = _requestCache.TryGetValue(ipAddress, out var history) ? history.Count : 0,
                UserAgent = history?.LastOrDefault()?.UserAgent ?? "Unknown",
                IsPermanent = false
            };

            _blockedCache[ipAddress] = blockedClient;

            // Persist to storage immediately for blocks
            await PersistBlockedClientAsync(blockedClient);

            _logger.LogWarning("IP {IpAddress} blocked for {Duration} - Reason: {Reason}",
                ipAddress, duration, reason);
        }

        public async Task PermanentlyBanAsync(IPAddress ipAddress, string reason)
        {
            var ipString = ipAddress.ToString();

            var blockedClient = new BlockedClient
            {
                IpAddress = ipString,
                BlockedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddYears(10),
                Reason = $"PERMANENT BAN: {reason}",
                TotalRequests = _requestCache.TryGetValue(ipString, out var history) ? history.Count : 0,
                UserAgent = history?.LastOrDefault()?.UserAgent ?? "Unknown",
                IsPermanent = true
            };

            _blockedCache[ipString] = blockedClient;

            // Persist permanent ban immediately
            await PersistBlockedClientAsync(blockedClient);

            _logger.LogWarning("IP {IpAddress} PERMANENTLY BANNED - Reason: {Reason}", ipString, reason);
        }

        public async Task UnblockAsync(IPAddress ipAddress)
        {
            var ipString = ipAddress.ToString();
            _blockedCache.TryRemove(ipString, out _);

            // Remove from storage too
            await RemoveBlockedClientFromStorageAsync(ipString);

            _logger.LogInformation("IP {IpAddress} manually unblocked", ipString);
        }

        public async Task<List<BlockedClient>> GetBlockedClientsAsync()
        {
            return _blockedCache.Values.OrderByDescending(b => b.BlockedAt).ToList();
        }

        public async Task<SecurityMetrics> GetMetricsAsync()
        {
            var now = DateTime.UtcNow;
            var metrics = new SecurityMetrics();

            var allRequests = _requestCache.Values.SelectMany(h => h)
                .Where(r => r.Timestamp > now.AddHours(-1))
                .ToList();

            metrics.TotalRequestsLastHour = allRequests.Count;
            metrics.UniqueIPsLastHour = allRequests.Select(r => r.IpAddress).Distinct().Count();
            metrics.CurrentlyBlockedIPs = _blockedCache.Count;
            metrics.PublicPageRequests = allRequests.Count(r => !r.IsAuthenticated);
            metrics.AuthenticatedPageRequests = allRequests.Count(r => r.IsAuthenticated);

            metrics.AutoBlocksToday = _blockedCache.Values.Count(b => b.BlockedAt > now.Date);

            // Top attackers (simple string format)
            metrics.TopAttackers = _requestCache
                .OrderByDescending(kvp => kvp.Value.Count)
                .Take(10)
                .Select(kvp => $"{kvp.Key} ({kvp.Value.Count} requests)")
                .ToList();

            // Most targeted paths
            metrics.MostTargetedPaths = allRequests
                .GroupBy(r => r.Path)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();

            return metrics;
        }

        public async Task ManuallyBlockAsync(IPAddress ipAddress, TimeSpan duration, string reason)
        {
            await BlockClientAsync(ipAddress.ToString(), duration, $"Manual block: {reason}");
        }

        private bool IsPublicPage(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            var lowerPath = path.ToLower();
            return _publicPages.Any(publicPage => lowerPath.StartsWith(publicPage.ToLower()));
        }

        private bool IsSuspiciousUserAgent(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return true;
            var lowerAgent = userAgent.ToLower();
            return _suspiciousUserAgents.Any(pattern => lowerAgent.Contains(pattern));
        }

        private async Task CheckForSuspiciousPatterns(string ipAddress, string userAgent, List<RequestRecord> history)
        {
            // Check for too many consecutive 404s (path scanning)
            var recent404s = history.TakeLast(10).Where(r => r.ResponseCode == 404).Count();
            if (recent404s >= 5)
            {
                await BlockClientAsync(ipAddress, TimeSpan.FromHours(8),
                    $"Path scanning detected: {recent404s} 404s");
            }

            // Check for high error rate
            if (history.Count >= 10)
            {
                var recent20 = history.TakeLast(20);
                var errorRate = recent20.Count(r => r.ResponseCode >= 400) / (double)recent20.Count();

                if (errorRate >= 0.8)
                {
                    await BlockClientAsync(ipAddress, TimeSpan.FromHours(2),
                        $"High error rate: {errorRate:P0}");
                }
            }
        }

        private async Task BackgroundCleanupAsync()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromHours(6));
                    await CleanupExpiredDataAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during cleanup");
                }
            }
        }

        private async Task CleanupExpiredDataAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;
                var cutoff = now.AddHours(-48);

                // Clean request cache
                foreach (var kvp in _requestCache.ToList())
                {
                    var filteredHistory = kvp.Value.Where(r => r.Timestamp > cutoff).ToList();
                    if (filteredHistory.Any())
                        _requestCache[kvp.Key] = filteredHistory;
                    else
                        _requestCache.TryRemove(kvp.Key, out _);
                }

                // Clean expired blocks
                var expiredBlocks = _blockedCache.Where(kvp =>
                    !kvp.Value.IsPermanent && now >= kvp.Value.ExpiresAt).ToList();

                foreach (var expired in expiredBlocks)
                {
                    _blockedCache.TryRemove(expired.Key, out _);
                }

                _logger.LogDebug("Cleanup completed - Removed {ExpiredBlocks} expired blocks",
                    expiredBlocks.Count);

                // Sync to storage after cleanup
                await SyncToStorageAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        // Storage helper methods
        private bool ShouldSyncToStorage()
        {
            return DateTime.UtcNow - _lastStorageSync > _syncInterval;
        }

        private async Task SyncToStorageAsync()
        {
            try
            {
                if (_requestStorage != null && _blockedStorage != null)
                {
                    // Save recent request history (last 24 hours)
                    var cutoff = DateTime.UtcNow.AddHours(-24);
                    var recentRequests = _requestCache.Values
                        .SelectMany(h => h)
                        .Where(r => r.Timestamp > cutoff)
                        .ToList();

                    await _requestStorage.SaveAsync("recent-requests", recentRequests);

                    // Save all blocked clients
                    var allBlocked = _blockedCache.Values.ToList();
                    await _blockedStorage.SaveAsync("blocked-clients", allBlocked);

                    _lastStorageSync = DateTime.UtcNow;
                    _logger.LogDebug("Synced security data to storage");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync security data to storage");
            }
        }

        private async Task LoadBlockedClientsFromStorageAsync()
        {
            try
            {
                if (_blockedStorage != null)
                {
                    var stored = await _blockedStorage.LoadAsync("blocked-clients");
                    if (stored != null)
                    {
                        var activeBlocks = stored.Where(b =>
                            b.IsPermanent || DateTime.UtcNow < b.ExpiresAt).ToList();

                        foreach (var block in activeBlocks)
                        {
                            _blockedCache[block.IpAddress] = block;
                        }

                        _logger.LogInformation("Loaded {Count} active blocks from storage", activeBlocks.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load blocked clients from storage");
            }
        }

        private async Task PersistBlockedClientAsync(BlockedClient blockedClient)
        {
            try
            {
                if (_blockedStorage != null)
                {
                    var allBlocked = await _blockedStorage.LoadAsync("blocked-clients") ?? new List<BlockedClient>();

                    // Remove existing entry for this IP
                    allBlocked.RemoveAll(b => b.IpAddress == blockedClient.IpAddress);

                    // Add new entry
                    allBlocked.Add(blockedClient);

                    await _blockedStorage.SaveAsync("blocked-clients", allBlocked);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist blocked client: {IpAddress}", blockedClient.IpAddress);
            }
        }

        private async Task RemoveBlockedClientFromStorageAsync(string ipAddress)
        {
            try
            {
                if (_blockedStorage != null)
                {
                    var allBlocked = await _blockedStorage.LoadAsync("blocked-clients") ?? new List<BlockedClient>();
                    allBlocked.RemoveAll(b => b.IpAddress == ipAddress);
                    await _blockedStorage.SaveAsync("blocked-clients", allBlocked);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove blocked client from storage: {IpAddress}", ipAddress);
            }
        }
    }
}