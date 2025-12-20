// File: NewwaysAdmin.WebAdmin/Services/Security/SimpleDoSProtectionService.cs
// FIXED: Added null safety checks to all LINQ queries to prevent NullReferenceException
// in concurrent dictionary operations

using System.Net;
using System.Collections.Concurrent;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WebAdmin.Models.Security;
using NewwaysAdmin.WebAdmin.Models.Auth;

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
        public int PermanentlyBannedIPs { get; set; }
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
        Task<List<PermanentBan>> GetPermanentBansAsync();
        Task ManuallyBlockAsync(IPAddress ipAddress, TimeSpan duration, string reason);
        Task PermanentlyBanAsync(IPAddress ipAddress, string reason, string bannedBy = "System");
        Task UnblockAsync(IPAddress ipAddress);
        Task UnbanPermanentlyAsync(IPAddress ipAddress);
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
        private IDataStorage<List<PermanentBan>>? _permanentBanStorage;

        // In-memory cache for performance (with storage backup)
        private readonly ConcurrentDictionary<string, List<RequestRecord>> _requestCache = new();
        private readonly ConcurrentDictionary<string, BlockedClient> _blockedCache = new();
        private DateTime _lastStorageSync = DateTime.MinValue;
        private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(5);
        private readonly ConcurrentDictionary<string, PermanentBan> _permanentBanCache = new();

        // Protection settings - VERY RELAXED for development
        private readonly int _publicMaxPerMinute = 80;
        private readonly int _publicMaxPerHour = 250;
        private readonly int _authMaxPerMinute = 180;
        private readonly int _authMaxPerHour = 5000;

        // Suspicious patterns
        private readonly string[] _publicPages = {
            "/", "/login", "/register", "/forgot-password", "/error", "/access-denied",
            "/_blazor", "/_framework", "/css", "/js", "/lib", "/favicon.ico"
        };
        private readonly string[] _loginPages = {
            "/login", "/register", "/forgot-password"
        };
        private readonly string[] _suspiciousUserAgents = {
            "bot", "crawler", "spider", "scan", "curl", "wget", "python",
            "go-http", "java", "perl", "ruby", "nmap", "masscan", "zmap",
            "exploit", "sqlmap", "nikto", "dirb", "gobuster", "wfuzz"
        };

        // ===== CONSTRUCTOR =====

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

        // ===== INITIALIZATION =====

        private async Task InitializeStorageAsync()
        {
            try
            {
                _requestStorage = await _storageManager.GetStorage<List<RequestRecord>>("SecurityRequests");
                _blockedStorage = await _storageManager.GetStorage<List<BlockedClient>>("SecurityBlocked");
                _permanentBanStorage = await _storageManager.GetStorage<List<PermanentBan>>("SecurityBans");

                // Load existing blocked clients from storage
                await LoadBlockedClientsFromStorageAsync();

                // Load permanent bans from storage
                await LoadPermanentBansFromStorageAsync();

                _logger.LogInformation("Security storage initialized - Loaded {BanCount} permanent bans", _permanentBanCache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize security storage - continuing with in-memory only");
            }
        }

        // ===== MAIN CHECK REQUEST =====

        public async Task<DoSCheckResult> CheckRequestAsync(IPAddress ipAddress, string userAgent, string path, bool isAuthenticated)
        {
            var ipString = ipAddress.ToString();
            var now = DateTime.UtcNow;

            _logger.LogInformation("DoS Check for {IpAddress}: Path={Path}, IsPublicPage={IsPublic}, IsLoginPage={IsLogin}, IsAuthenticated={IsAuth}, UseStrictLimits={UseStrict}",
                ipString, path, IsPublicPage(path), IsLoginPage(path), isAuthenticated, ShouldUseStrictLimits(path, isAuthenticated));

            // Check permanent bans first
            if (_permanentBanCache.TryGetValue(ipString, out var ban))
            {
                return new DoSCheckResult
                {
                    IsBlocked = true,
                    Reason = $"PERMANENTLY BANNED: {ban.Reason}",
                    RemainingBlockTime = null,
                    RequestsInWindow = ban.TotalRequestsBeforeBan
                };
            }

            // Check temporary blocks
            if (_blockedCache.TryGetValue(ipString, out var blockedClient))
            {
                if (blockedClient.ExpiresAt > now)
                {
                    return new DoSCheckResult
                    {
                        IsBlocked = true,
                        Reason = blockedClient.Reason,
                        RemainingBlockTime = blockedClient.ExpiresAt - now,
                        RequestsInWindow = blockedClient.TotalRequests
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
                IpAddress = ipString,
                Path = path,
                UserAgent = userAgent,
                IsAuthenticated = isAuthenticated
            });

            // Clean old requests (older than 1 hour)
            var cutoffTime = now.AddHours(-1);
            requestHistory.RemoveAll(r => r == null || r.Timestamp < cutoffTime);

            // Update cache
            _requestCache[ipString] = requestHistory;

            // Only apply strict limits to login pages OR unauthenticated users
            bool shouldApplyStrictLimits = ShouldUseStrictLimits(path, isAuthenticated);

            if (shouldApplyStrictLimits)
            {
                // Apply strict rate limiting (login attempts, unauthenticated requests)
                // FIXED: Added null check
                var recentRequests = requestHistory.Where(r => r != null && r.Timestamp > now.AddMinutes(-1)).Count();
                var hourlyRequests = requestHistory.Count(r => r != null);

                int maxPerMinute = isAuthenticated ? _authMaxPerMinute : _publicMaxPerMinute;
                int maxPerHour = isAuthenticated ? _authMaxPerHour : _publicMaxPerHour;

                // Check for suspicious patterns
                bool isSuspicious = IsSuspiciousActivity(requestHistory, userAgent);

                if (recentRequests > maxPerMinute || hourlyRequests > maxPerHour || isSuspicious)
                {
                    var blockDuration = CalculateBlockDuration(requestHistory, ipString);
                    var blockUntil = now.Add(blockDuration);

                    var reason = isSuspicious
                        ? "Suspicious activity detected"
                        : $"Rate limit exceeded ({recentRequests}/min, {hourlyRequests}/hour)";

                    var blockedUser = new BlockedClient
                    {
                        IpAddress = ipString,
                        BlockedAt = now,
                        ExpiresAt = blockUntil,
                        Reason = reason,
                        TotalRequests = recentRequests,
                        IsPermanent = false
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
            }
            else if (isAuthenticated)
            {
                // Authenticated users get more lenient limits
                // FIXED: Added null check
                var recentRequests = requestHistory.Where(r => r != null && r.Timestamp > now.AddMinutes(-1)).Count();

                // Extreme rate for authenticated users (e.g. > 500/minute)
                if (recentRequests > 500)
                {
                    var reason = $"Authenticated rate limit exceeded ({recentRequests}/min)";
                    var blockedUser = new BlockedClient
                    {
                        IpAddress = ipString,
                        BlockedAt = now,
                        ExpiresAt = now.AddMinutes(5),
                        Reason = reason,
                        TotalRequests = recentRequests
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
                    IsHighRisk = recentRequests > 200,
                    RequestsInWindow = recentRequests
                };
            }

            return new DoSCheckResult
            {
                IsBlocked = false,
                RequestsInWindow = requestHistory.Count(r => r != null)
            };
        }

        // ===== LOG REQUEST =====

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

        // ===== METRICS =====

        public async Task<SecurityMetrics> GetMetricsAsync()
        {
            var now = DateTime.UtcNow;
            var metrics = new SecurityMetrics();

            // FIXED: Added null checks to prevent NullReferenceException
            var allRequests = _requestCache.Values
                .Where(h => h != null)
                .SelectMany(h => h)
                .Where(r => r != null && r.Timestamp > now.AddHours(-1))
                .ToList();

            metrics.TotalRequestsLastHour = allRequests.Count;
            metrics.UniqueIPsLastHour = allRequests
                .Where(r => r?.IpAddress != null)
                .Select(r => r.IpAddress)
                .Distinct()
                .Count();
            metrics.CurrentlyBlockedIPs = _blockedCache.Count;
            metrics.PermanentlyBannedIPs = _permanentBanCache.Count;
            metrics.PublicPageRequests = allRequests.Count(r => r != null && !r.IsAuthenticated);
            metrics.AuthenticatedPageRequests = allRequests.Count(r => r != null && r.IsAuthenticated);

            // FIXED: Added null check for blocked cache values
            metrics.AutoBlocksToday = _blockedCache.Values
                .Where(b => b != null)
                .Count(b => b.BlockedAt > now.Date);

            // FIXED: Added null check for TopAttackers
            metrics.TopAttackers = _requestCache
                .Where(kvp => kvp.Value != null)
                .OrderByDescending(kvp => kvp.Value.Count)
                .Take(10)
                .Select(kvp => $"{kvp.Key} ({kvp.Value.Count} requests)")
                .ToList();

            // FIXED: Added null checks for MostTargetedPaths
            metrics.MostTargetedPaths = allRequests
                .Where(r => r?.Path != null)
                .GroupBy(r => r.Path)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => $"{g.Key ?? "unknown"} ({g.Count()})")
                .ToList();

            return metrics;
        }

        // ===== BLOCKING METHODS =====

        public async Task ManuallyBlockAsync(IPAddress ipAddress, TimeSpan duration, string reason)
        {
            await BlockClientAsync(ipAddress.ToString(), duration, $"Manual block: {reason}");
        }

        private async Task BlockClientAsync(string ipAddress, TimeSpan duration, string reason)
        {
            // FIXED: Added null safety when accessing history
            _requestCache.TryGetValue(ipAddress, out var history);

            var blockedClient = new BlockedClient
            {
                IpAddress = ipAddress,
                BlockedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(duration),
                Reason = reason,
                TotalRequests = history?.Count ?? 0,
                UserAgent = history?.LastOrDefault(r => r != null)?.UserAgent ?? "Unknown",
                IsPermanent = false
            };

            _blockedCache[ipAddress] = blockedClient;

            // Persist to storage immediately for blocks
            await PersistBlockedClientAsync(blockedClient);

            _logger.LogWarning("IP {IpAddress} blocked for {Duration} - Reason: {Reason}",
                ipAddress, duration, reason);
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
            // FIXED: Added null check
            return _blockedCache.Values
                .Where(b => b != null)
                .OrderByDescending(b => b.BlockedAt)
                .ToList();
        }

        // ===== PERMANENT BAN METHODS =====

        public async Task<List<PermanentBan>> GetPermanentBansAsync()
        {
            // FIXED: Added null check
            return _permanentBanCache.Values
                .Where(b => b != null)
                .OrderByDescending(b => b.BannedAt)
                .ToList();
        }

        public async Task PermanentlyBanAsync(IPAddress ipAddress, string reason, string bannedBy = "System")
        {
            var ipString = ipAddress.ToString();

            // FIXED: Added null safety when accessing history
            _requestCache.TryGetValue(ipString, out var history);

            var ban = new PermanentBan
            {
                IpAddress = ipString,
                BannedAt = DateTime.UtcNow,
                Reason = reason,
                UserAgent = history?.LastOrDefault(r => r != null)?.UserAgent ?? "Unknown",
                LastPath = history?.LastOrDefault(r => r != null)?.Path ?? "",
                TotalRequestsBeforeBan = history?.Count ?? 0,
                BannedBy = bannedBy
            };

            _permanentBanCache[ipString] = ban;

            // Remove from temporary blocks if present
            _blockedCache.TryRemove(ipString, out _);

            // Persist permanent ban immediately
            await PersistPermanentBanAsync(ban);

            _logger.LogWarning("IP {IpAddress} PERMANENTLY BANNED by {BannedBy} - Reason: {Reason}",
                ipString, bannedBy, reason);
        }

        public async Task UnbanPermanentlyAsync(IPAddress ipAddress)
        {
            var ipString = ipAddress.ToString();

            if (_permanentBanCache.TryRemove(ipString, out var ban))
            {
                // Remove from storage
                await RemovePermanentBanFromStorageAsync(ipString);

                _logger.LogWarning("IP {IpAddress} UNBANNED from permanent ban list - Was banned for: {Reason}",
                    ipString, ban.Reason);
            }
        }

        // ===== HELPER METHODS =====

        private List<RequestRecord> GetRequestHistory(string ipAddress)
        {
            return _requestCache.GetOrAdd(ipAddress, _ => new List<RequestRecord>());
        }

        private bool IsPublicPage(string path)
        {
            return _publicPages.Any(publicPage =>
                path.StartsWith(publicPage, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsLoginPage(string path)
        {
            return _loginPages.Any(loginPage =>
                path.Equals(loginPage, StringComparison.OrdinalIgnoreCase));
        }

        private bool ShouldUseStrictLimits(string path, bool isAuthenticated)
        {
            // DON'T apply strict limits to login pages - allow page loads
            if (IsLoginPage(path))
            {
                return false;
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

        private bool IsSuspiciousUserAgent(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return true;
            var lowerAgent = userAgent.ToLower();
            return _suspiciousUserAgents.Any(pattern => lowerAgent.Contains(pattern));
        }

        private bool IsSuspiciousActivity(List<RequestRecord> requestHistory, string userAgent)
        {
            // Check for suspicious user agent
            if (IsSuspiciousUserAgent(userAgent))
                return true;

            // FIXED: Added null checks
            // Check for too many different paths (scanning behavior)
            var uniquePaths = requestHistory
                .Where(r => r != null && r.Timestamp > DateTime.UtcNow.AddMinutes(-5))
                .Select(r => r.Path)
                .Where(p => p != null)
                .Distinct()
                .Count();

            if (uniquePaths > 20)
                return true;

            // FIXED: Added null checks and division by zero protection
            // Check for high error rate
            var recentRequests = requestHistory
                .Where(r => r != null && r.Timestamp > DateTime.UtcNow.AddMinutes(-2))
                .ToList();

            if (recentRequests.Count >= 10)
            {
                var recent20 = recentRequests.TakeLast(20).ToList();
                if (recent20.Count > 0)
                {
                    var errorRate = recent20.Count(r => r.ResponseCode >= 400) / (double)recent20.Count;
                    if (errorRate > 0.7)
                        return true;
                }
            }

            return false;
        }

        private TimeSpan CalculateBlockDuration(List<RequestRecord> requestHistory, string ipAddress)
        {
            // FIXED: Added null check
            var priorBlocks = _blockedCache.Values
                .Where(b => b != null)
                .Count(b => b.IpAddress == ipAddress);

            return priorBlocks switch
            {
                0 => TimeSpan.FromMinutes(5),
                1 => TimeSpan.FromMinutes(30),
                2 => TimeSpan.FromHours(2),
                _ => TimeSpan.FromHours(24)
            };
        }

        private async Task CheckForSuspiciousPatterns(string ipAddress, string userAgent, List<RequestRecord> history)
        {
            // FIXED: Added null check
            var safeHistory = history?.Where(r => r != null).ToList() ?? new List<RequestRecord>();

            // Check for too many consecutive 404s (path scanning)
            var recent404s = safeHistory
                .TakeLast(10)
                .Count(r => r.ResponseCode == 404);

            if (recent404s >= 5)
            {
                await BlockClientAsync(ipAddress, TimeSpan.FromHours(8),
                    $"Path scanning detected: {recent404s} 404s");
            }

            // Check for high error rate
            if (safeHistory.Count >= 10)
            {
                var recent20 = safeHistory.TakeLast(20).ToList();
                if (recent20.Count > 0)
                {
                    var errorRate = recent20.Count(r => r.ResponseCode >= 400) / (double)recent20.Count;

                    if (errorRate >= 0.8)
                    {
                        await BlockClientAsync(ipAddress, TimeSpan.FromHours(2),
                            $"High error rate: {errorRate:P0}");
                    }
                }
            }
        }

        // ===== BACKGROUND TASKS =====

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
                    // FIXED: Added null checks
                    var filteredHistory = kvp.Value?
                        .Where(r => r != null && r.Timestamp > cutoff)
                        .ToList() ?? new List<RequestRecord>();

                    if (filteredHistory.Any())
                        _requestCache[kvp.Key] = filteredHistory;
                    else
                        _requestCache.TryRemove(kvp.Key, out _);
                }

                // FIXED: Added null check for blocked cache
                // Clean expired TEMPORARY blocks only (don't touch permanent bans!)
                var expiredBlocks = _blockedCache
                    .Where(kvp => kvp.Value != null && !kvp.Value.IsPermanent && now >= kvp.Value.ExpiresAt)
                    .ToList();

                foreach (var expired in expiredBlocks)
                {
                    _blockedCache.TryRemove(expired.Key, out _);
                }

                _logger.LogDebug("Cleanup completed - Removed {ExpiredBlocks} expired blocks. " +
                    "Permanent bans: {PermanentBans}", expiredBlocks.Count, _permanentBanCache.Count);

                // Sync to storage after cleanup
                await SyncToStorageAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        // ===== STORAGE METHODS =====

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
                    // FIXED: Added null checks to prevent NullReferenceException
                    // Save recent request history (last 24 hours)
                    var cutoff = DateTime.UtcNow.AddHours(-24);
                    var recentRequests = _requestCache.Values
                        .Where(h => h != null)
                        .SelectMany(h => h)
                        .Where(r => r != null && r.Timestamp > cutoff)
                        .ToList();

                    await _requestStorage.SaveAsync("recent-requests", recentRequests);

                    // FIXED: Added null check
                    // Save all blocked clients
                    var allBlocked = _blockedCache.Values
                        .Where(b => b != null)
                        .ToList();
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
                        // FIXED: Added null check
                        var activeBlocks = stored
                            .Where(b => b != null && (b.IsPermanent || DateTime.UtcNow < b.ExpiresAt))
                            .ToList();

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

        private async Task LoadPermanentBansFromStorageAsync()
        {
            try
            {
                if (_permanentBanStorage != null)
                {
                    var stored = await _permanentBanStorage.LoadAsync("permanent-bans");
                    if (stored != null && stored.Any())
                    {
                        // FIXED: Added null check
                        foreach (var ban in stored.Where(b => b != null))
                        {
                            _permanentBanCache[ban.IpAddress] = ban;
                        }
                        _logger.LogInformation("Loaded {Count} permanent bans from storage", stored.Count);
                    }
                }
            }
            catch (StorageException ex) when (ex.Message.Contains("Data not found"))
            {
                // File doesn't exist yet - this is normal on first run
                _logger.LogInformation("No existing permanent bans file found (first run)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load permanent bans from storage");
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
                    allBlocked.RemoveAll(b => b?.IpAddress == blockedClient.IpAddress);

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
                    allBlocked.RemoveAll(b => b?.IpAddress == ipAddress);
                    await _blockedStorage.SaveAsync("blocked-clients", allBlocked);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove blocked client from storage: {IpAddress}", ipAddress);
            }
        }

        private async Task PersistPermanentBanAsync(PermanentBan ban)
        {
            try
            {
                if (_permanentBanStorage != null)
                {
                    var allBans = await _permanentBanStorage.LoadAsync("permanent-bans") ?? new List<PermanentBan>();
                    allBans.RemoveAll(b => b?.IpAddress == ban.IpAddress);
                    allBans.Add(ban);
                    await _permanentBanStorage.SaveAsync("permanent-bans", allBans);

                    _logger.LogInformation("Persisted permanent ban for {IpAddress} to storage", ban.IpAddress);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist permanent ban: {IpAddress}", ban.IpAddress);
            }
        }

        private async Task RemovePermanentBanFromStorageAsync(string ipAddress)
        {
            try
            {
                if (_permanentBanStorage != null)
                {
                    var allBans = await _permanentBanStorage.LoadAsync("permanent-bans") ?? new List<PermanentBan>();
                    allBans.RemoveAll(b => b?.IpAddress == ipAddress);
                    await _permanentBanStorage.SaveAsync("permanent-bans", allBans);

                    _logger.LogInformation("Removed permanent ban for {IpAddress} from storage", ipAddress);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove permanent ban from storage: {IpAddress}", ipAddress);
            }
        }
    }
}