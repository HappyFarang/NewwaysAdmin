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
        private readonly string[] _publicPages = {
            "/", "/login", "/register", "/forgot-password", "/error", "/access-denied",
            "/_blazor", "/_framework", "/css", "/js", "/lib", "/favicon.ico" // Added Blazor/static resources
};
        private readonly string[] _loginPages = {
            "/login", "/register", "/forgot-password" // ONLY these pages get strict protection
};
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
            var now = DateTime.UtcNow;
            var ipString = ipAddress.ToString();

            _logger.LogInformation("DoS Check for {IpAddress}: Path={Path}, IsPublicPage={IsPublic}, IsLoginPage={IsLogin}, IsAuthenticated={IsAuth}, UseStrictLimits={UseStrict}",
                ipString, path, IsPublicPage(path), IsLoginPage(path), isAuthenticated, ShouldUseStrictLimits(path, isAuthenticated));

            // Check if currently blocked
            if (_blockedCache.TryGetValue(ipString, out var blockedClient))
            {
                if (blockedClient.ExpiresAt > now) // Fixed: was BlockedUntil, now ExpiresAt
                {
                    return new DoSCheckResult
                    {
                        IsBlocked = true,
                        Reason = blockedClient.Reason,
                        RemainingBlockTime = blockedClient.ExpiresAt - now, // Fixed: ExpiresAt
                        RequestsInWindow = blockedClient.TotalRequests // Fixed: was RequestCount, now TotalRequests
                    };
                }
                else
                {
                    // Block expired, remove from cache
                    _blockedCache.TryRemove(ipString, out _);
                }
            }

            // Get request history
            var requestHistory = GetRequestHistory(ipString);

            // Add current request
            requestHistory.Add(new RequestRecord
            {
                Timestamp = now,
                IpAddress = ipString, // Fixed: added missing IpAddress
                Path = path,
                UserAgent = userAgent,
                IsAuthenticated = isAuthenticated
            });

            // Clean old requests (older than 1 hour)
            var cutoffTime = now.AddHours(-1);
            requestHistory.RemoveAll(r => r.Timestamp < cutoffTime);

            // Update cache
            _requestCache[ipString] = requestHistory;

            // **KEY CHANGE: Only apply strict limits to login pages OR unauthenticated users**
            bool shouldApplyStrictLimits = ShouldUseStrictLimits(path, isAuthenticated);

            if (shouldApplyStrictLimits)
            {
                // Apply strict rate limiting (login attempts, unauthenticated requests)
                var recentRequests = requestHistory.Where(r => r.Timestamp > now.AddMinutes(-1)).Count();
                var hourlyRequests = requestHistory.Count;

                int maxPerMinute = isAuthenticated ? _authMaxPerMinute : _publicMaxPerMinute;
                int maxPerHour = isAuthenticated ? _authMaxPerHour : _publicMaxPerHour;

                // Check for suspicious patterns
                bool isSuspicious = IsSuspiciousActivity(requestHistory, userAgent);

                if (recentRequests > maxPerMinute || hourlyRequests > maxPerHour || isSuspicious)
                {
                    var blockDuration = CalculateBlockDuration(requestHistory, ipString);
                    var blockUntil = now.Add(blockDuration);

                    var reason = isSuspicious
                        ? $"Suspicious activity detected"
                        : $"Rate limit exceeded: {recentRequests}/{maxPerMinute} per minute";

                    var blockedUser = new BlockedClient
                    {
                        IpAddress = ipString,
                        BlockedAt = now,
                        ExpiresAt = blockUntil, // Fixed: using ExpiresAt instead of BlockedUntil
                        Reason = reason,
                        TotalRequests = recentRequests // Fixed: using TotalRequests instead of RequestCount
                    };

                    _blockedCache[ipString] = blockedUser;

                    return new DoSCheckResult
                    {
                        IsBlocked = true,
                        Reason = reason,
                        RemainingBlockTime = blockDuration,
                        RequestsInWindow = recentRequests
                    };
                }

                return new DoSCheckResult
                {
                    IsBlocked = false,
                    IsHighRisk = recentRequests > (maxPerMinute * 0.8), // Warn at 80% of limit
                    RequestsInWindow = recentRequests
                };
            }
            else
            {
                // **LENIENT MODE**: For authenticated users on non-login pages
                // Only block if EXTREMELY suspicious (like 1000+ requests per minute)
                var recentRequests = requestHistory.Where(r => r.Timestamp > now.AddMinutes(-1)).Count();

                // Only block authenticated users if they're doing something really crazy
                if (recentRequests > 1000) // 1000 requests per minute is clearly a bot
                {
                    var reason = $"Extreme rate abuse: {recentRequests} requests per minute";
                    var blockedUser = new BlockedClient
                    {
                        IpAddress = ipString,
                        BlockedAt = now,
                        ExpiresAt = now.AddMinutes(5), // Short block for authenticated users
                        Reason = reason,
                        TotalRequests = recentRequests // Fixed: using TotalRequests
                    };

                    _blockedCache[ipString] = blockedUser;

                    return new DoSCheckResult
                    {
                        IsBlocked = true,
                        Reason = reason,
                        RemainingBlockTime = TimeSpan.FromMinutes(5),
                        RequestsInWindow = recentRequests
                    };
                }

                // Just log high activity but don't block
                return new DoSCheckResult
                {
                    IsBlocked = false,
                    IsHighRisk = recentRequests > 200, // Log if over 200/minute but don't block
                    RequestsInWindow = recentRequests
                };
            }
        }

        private List<RequestRecord> GetRequestHistory(string ipAddress)
        {
            return _requestCache.GetOrAdd(ipAddress, _ => new List<RequestRecord>());
        }

        private bool IsSuspiciousActivity(List<RequestRecord> requestHistory, string userAgent)
        {
            // Check for suspicious user agent
            if (IsSuspiciousUserAgent(userAgent))
                return true;

            // Check for too many different paths (scanning behavior)
            var uniquePaths = requestHistory.Where(r => r.Timestamp > DateTime.UtcNow.AddMinutes(-5))
                                          .Select(r => r.Path)
                                          .Distinct()
                                          .Count();
            if (uniquePaths > 20) // More than 20 different paths in 5 minutes
                return true;

            // Check for high error rate
            var recentRequests = requestHistory.Where(r => r.Timestamp > DateTime.UtcNow.AddMinutes(-2)).ToList();
            if (recentRequests.Count > 10)
            {
                var errorRate = recentRequests.Count(r => r.ResponseCode >= 400) / (double)recentRequests.Count;
                if (errorRate > 0.7) // More than 70% errors
                    return true;
            }

            return false;
        }

        private TimeSpan CalculateBlockDuration(List<RequestRecord> requestHistory, string ipAddress)
        {
            // Check if this IP has been blocked before (escalating penalties)
            var priorBlocks = _blockedCache.Values.Count(b => b.IpAddress == ipAddress);

            return priorBlocks switch
            {
                0 => TimeSpan.FromMinutes(5),    // First offense: 5 minutes
                1 => TimeSpan.FromMinutes(30),   // Second offense: 30 minutes  
                2 => TimeSpan.FromHours(2),      // Third offense: 2 hours
                _ => TimeSpan.FromHours(24)      // Repeat offender: 24 hours
            };
        }
        private bool ShouldUseStrictLimits(string path, bool isAuthenticated)
        {
            // Always protect login pages strictly
            if (IsLoginPage(path))
            {
                return true;
            }

            // Don't apply strict limits to authenticated users on non-login pages
            if (isAuthenticated)
            {
                return false;
            }

            // Apply moderate limits to unauthenticated users on public pages
            if (IsPublicPage(path))
            {
                return true;
            }

            // Unauthenticated users trying to access protected pages - block more aggressively
            return true;
        }

        // <summary>
        /// Check if this is a login-related page that needs strict protection
        /// </summary>
        private bool IsLoginPage(string path)
        {
            return _loginPages.Any(loginPage =>
                path.Equals(loginPage, StringComparison.OrdinalIgnoreCase));
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

        /// <summary>
        /// Check if this is a public page (including Blazor resources)
        /// </summary>
        private bool IsPublicPage(string path)
        {
            return _publicPages.Any(publicPage =>
                path.StartsWith(publicPage, StringComparison.OrdinalIgnoreCase));
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