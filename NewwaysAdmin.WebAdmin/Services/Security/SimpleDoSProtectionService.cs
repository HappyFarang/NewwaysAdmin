// File: NewwaysAdmin.WebAdmin/Services/Security/SimpleDoSProtectionService.cs
// SIMPLIFIED VERSION: Trust authenticated users, simple blocking logic, keeps stats

using System.Collections.Concurrent;
using System.Net;
using NewwaysAdmin.WebAdmin.Models.Security;  // Use existing PermanentBan model

namespace NewwaysAdmin.WebAdmin.Services.Security
{
    // ===== DATA MODELS (only ones not already defined elsewhere) =====

    public class RequestRecord
    {
        public DateTime Timestamp { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public int ResponseCode { get; set; }
        public bool IsAuthenticated { get; set; }
    }

    public class BlockedClient
    {
        public string IpAddress { get; set; } = string.Empty;
        public DateTime BlockedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int TotalRequests { get; set; }
        public bool IsPermanent { get; set; } = false;
        public string UserAgent { get; set; } = string.Empty;
    }

    public class DoSCheckResult
    {
        public bool IsBlocked { get; set; }
        public string Reason { get; set; } = string.Empty;
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
        public int UnauthenticatedProtectedAttempts { get; set; }
        public int SuspiciousUserAgentRequests { get; set; }
        public List<string> TopAttackers { get; set; } = new();
        public List<string> MostTargetedPaths { get; set; } = new();
        public List<string> RecentBlocks { get; set; } = new();
    }

    // ===== INTERFACE =====

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

    // ===== SERVICE IMPLEMENTATION =====

    public class SimpleDoSProtectionService : ISimpleDoSProtectionService
    {
        private readonly ILogger<SimpleDoSProtectionService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        // In-memory caches
        private readonly ConcurrentDictionary<string, List<RequestRecord>> _requestCache = new();
        private readonly ConcurrentDictionary<string, BlockedClient> _blockedCache = new();
        private readonly ConcurrentDictionary<string, PermanentBan> _permanentBanCache = new();
        private readonly ConcurrentDictionary<string, int> _protectedPageAttempts = new();

        // ===== CONFIGURATION =====

        // Public pages anyone can access without auth
        private readonly HashSet<string> _publicPages = new(StringComparer.OrdinalIgnoreCase)
        {
            "/", "/login", "/register", "/forgot-password", "/error", "/access-denied"
        };

        // Path prefixes that are always allowed (static files, framework)
        private readonly string[] _alwaysAllowedPrefixes =
        {
            "/_blazor", "/_framework", "/_content", "/css", "/js", "/lib",
            "/fonts", "/images", "/favicon", "/manifest", "/api/mobile"
        };

        // Suspicious user agents (bots, scanners)
        private readonly string[] _suspiciousUserAgents =
        {
            "bot", "crawler", "spider", "scan", "curl", "wget", "python",
            "go-http", "java", "perl", "ruby", "nmap", "masscan", "zmap",
            "exploit", "sqlmap", "nikto", "dirb", "gobuster", "wfuzz"
        };

        // Thresholds
        private const int MaxRequestsPerMinuteUnauthenticated = 150;  // Sanity check for public pages
        private const int MaxProtectedPageAttempts = 15;              // Before blocking
        private const int MaxRequestsPerMinuteSuspiciousAgent = 20;   // Bots get stricter limits

        // ===== CONSTRUCTOR =====

        public SimpleDoSProtectionService(ILogger<SimpleDoSProtectionService> logger)
        {
            _logger = logger;
            _logger.LogInformation("Simple DoS Protection Service initialized (simplified version)");

            // Start background cleanup
            _ = Task.Run(BackgroundCleanupAsync);
        }

        // ===== MAIN CHECK - THE SIMPLE LOGIC =====

        public async Task<DoSCheckResult> CheckRequestAsync(IPAddress ipAddress, string userAgent, string path, bool isAuthenticated)
        {
            var ipString = ipAddress.ToString();
            var now = DateTime.UtcNow;

            // ===== 1. CHECK PERMANENT BANS =====
            if (_permanentBanCache.TryGetValue(ipString, out var ban))
            {
                return new DoSCheckResult
                {
                    IsBlocked = true,
                    Reason = $"PERMANENTLY BANNED: {ban.Reason}",
                    RemainingBlockTime = null
                };
            }

            // ===== 2. CHECK TEMPORARY BLOCKS =====
            if (_blockedCache.TryGetValue(ipString, out var blocked))
            {
                if (blocked.ExpiresAt > now)
                {
                    return new DoSCheckResult
                    {
                        IsBlocked = true,
                        Reason = blocked.Reason,
                        RemainingBlockTime = blocked.ExpiresAt - now
                    };
                }
                else
                {
                    // Block expired, remove it
                    _blockedCache.TryRemove(ipString, out _);
                }
            }

            // ===== 3. AUTHENTICATED USERS = TRUSTED =====
            if (isAuthenticated)
            {
                // Fully trusted, no checks needed
                return new DoSCheckResult { IsBlocked = false };
            }

            // ===== 4. STATIC/FRAMEWORK FILES = ALWAYS ALLOW =====
            if (IsAlwaysAllowedPath(path))
            {
                return new DoSCheckResult { IsBlocked = false };
            }

            // ===== 5. SUSPICIOUS USER AGENT = STRICT LIMITS =====
            if (IsSuspiciousUserAgent(userAgent))
            {
                var botRequests = GetRecentRequestCount(ipString, TimeSpan.FromMinutes(1));
                if (botRequests > MaxRequestsPerMinuteSuspiciousAgent)
                {
                    await BlockAsync(ipString, TimeSpan.FromHours(4),
                        $"Suspicious bot: {botRequests} requests/min with agent containing scanner keywords", userAgent);
                    return new DoSCheckResult
                    {
                        IsBlocked = true,
                        Reason = "Bot/scanner detected and rate limit exceeded"
                    };
                }
            }

            // ===== 6. PUBLIC PAGES = ALLOW WITH SANITY CHECK =====
            if (IsPublicPage(path))
            {
                var recentCount = GetRecentRequestCount(ipString, TimeSpan.FromMinutes(1));
                if (recentCount > MaxRequestsPerMinuteUnauthenticated)
                {
                    await BlockAsync(ipString, TimeSpan.FromMinutes(15),
                        $"Extreme request rate on public pages: {recentCount}/min", userAgent);
                    return new DoSCheckResult
                    {
                        IsBlocked = true,
                        Reason = "Too many requests"
                    };
                }
                return new DoSCheckResult { IsBlocked = false };
            }

            // ===== 7. NOT AUTHENTICATED + PROTECTED PAGE = TRACK =====
            // They're trying to access pages they shouldn't know exist
            var attempts = _protectedPageAttempts.AddOrUpdate(ipString, 1, (_, count) => count + 1);

            _logger.LogDebug("Unauthenticated access to protected page {Path} from {IP} (attempt {Count})",
                path, ipString, attempts);

            if (attempts > MaxProtectedPageAttempts)
            {
                await BlockAsync(ipString, TimeSpan.FromHours(1),
                    $"Probing protected pages without authentication: {attempts} attempts", userAgent);
                return new DoSCheckResult
                {
                    IsBlocked = true,
                    Reason = "Too many attempts to access protected pages without login"
                };
            }

            // Allow but will redirect to login (handled by auth middleware)
            return new DoSCheckResult { IsBlocked = false };
        }

        // ===== LOG REQUEST (FOR STATS) =====

        public async Task LogRequestAsync(IPAddress ipAddress, string userAgent, string path, int responseCode, bool isAuthenticated)
        {
            var ipString = ipAddress.ToString();

            await _lock.WaitAsync();
            try
            {
                if (!_requestCache.TryGetValue(ipString, out var history))
                {
                    history = new List<RequestRecord>();
                    _requestCache[ipString] = history;
                }

                history.Add(new RequestRecord
                {
                    Timestamp = DateTime.UtcNow,
                    IpAddress = ipString,
                    Path = path,
                    UserAgent = userAgent,
                    ResponseCode = responseCode,
                    IsAuthenticated = isAuthenticated
                });

                // Keep only last 2 hours of history per IP
                var cutoff = DateTime.UtcNow.AddHours(-2);
                history.RemoveAll(r => r.Timestamp < cutoff);
            }
            finally
            {
                _lock.Release();
            }
        }

        // ===== BLOCKING =====

        private async Task BlockAsync(string ipAddress, TimeSpan duration, string reason, string userAgent = "")
        {
            var blocked = new BlockedClient
            {
                IpAddress = ipAddress,
                BlockedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(duration),
                Reason = reason,
                TotalRequests = GetRecentRequestCount(ipAddress, TimeSpan.FromHours(1)),
                UserAgent = userAgent
            };

            _blockedCache[ipAddress] = blocked;
            _logger.LogWarning("🚫 BLOCKED {IP} for {Duration}: {Reason}", ipAddress, duration, reason);
        }

        public async Task ManuallyBlockAsync(IPAddress ipAddress, TimeSpan duration, string reason)
        {
            await BlockAsync(ipAddress.ToString(), duration, $"Manual block: {reason}");
        }

        public async Task UnblockAsync(IPAddress ipAddress)
        {
            var ipString = ipAddress.ToString();
            if (_blockedCache.TryRemove(ipString, out _))
            {
                _logger.LogInformation("✅ Unblocked {IP}", ipString);
            }
            _protectedPageAttempts.TryRemove(ipString, out _);
        }

        public async Task PermanentlyBanAsync(IPAddress ipAddress, string reason, string bannedBy = "System")
        {
            var ipString = ipAddress.ToString();

            // Get last request info for the ban record
            var lastRequest = _requestCache.TryGetValue(ipString, out var history) && history.Any()
                ? history.Last()
                : null;

            var ban = new PermanentBan
            {
                IpAddress = ipString,
                BannedAt = DateTime.UtcNow,
                Reason = reason,
                BannedBy = bannedBy,
                TotalRequestsBeforeBan = GetRecentRequestCount(ipString, TimeSpan.FromHours(24)),
                UserAgent = lastRequest?.UserAgent ?? "",
                LastPath = lastRequest?.Path ?? ""
            };

            _permanentBanCache[ipString] = ban;
            _blockedCache.TryRemove(ipString, out _);  // Remove temporary block if exists

            _logger.LogWarning("⛔ PERMANENTLY BANNED {IP}: {Reason} (by {BannedBy})", ipString, reason, bannedBy);
        }

        public async Task UnbanPermanentlyAsync(IPAddress ipAddress)
        {
            var ipString = ipAddress.ToString();
            if (_permanentBanCache.TryRemove(ipString, out _))
            {
                _logger.LogInformation("✅ Removed permanent ban for {IP}", ipString);
            }
        }

        // ===== QUERIES =====

        public Task<List<BlockedClient>> GetBlockedClientsAsync()
        {
            var now = DateTime.UtcNow;
            var activeBlocks = _blockedCache.Values
                .Where(b => b.ExpiresAt > now)
                .OrderByDescending(b => b.BlockedAt)
                .ToList();
            return Task.FromResult(activeBlocks);
        }

        public Task<List<PermanentBan>> GetPermanentBansAsync()
        {
            return Task.FromResult(_permanentBanCache.Values.OrderByDescending(b => b.BannedAt).ToList());
        }

        public Task<SecurityMetrics> GetMetricsAsync()
        {
            var now = DateTime.UtcNow;
            var hourAgo = now.AddHours(-1);

            var allRequests = _requestCache.Values
                .SelectMany(h => h)
                .Where(r => r.Timestamp > hourAgo)
                .ToList();

            var metrics = new SecurityMetrics
            {
                GeneratedAt = now,
                TotalRequestsLastHour = allRequests.Count,
                UniqueIPsLastHour = allRequests.Select(r => r.IpAddress).Distinct().Count(),
                CurrentlyBlockedIPs = _blockedCache.Values.Count(b => b.ExpiresAt > now),
                PermanentlyBannedIPs = _permanentBanCache.Count,
                AuthenticatedPageRequests = allRequests.Count(r => r.IsAuthenticated),
                PublicPageRequests = allRequests.Count(r => !r.IsAuthenticated && IsPublicPage(r.Path)),
                UnauthenticatedProtectedAttempts = allRequests.Count(r => !r.IsAuthenticated && !IsPublicPage(r.Path) && !IsAlwaysAllowedPath(r.Path)),
                SuspiciousUserAgentRequests = allRequests.Count(r => IsSuspiciousUserAgent(r.UserAgent)),

                TopAttackers = _blockedCache.Values
                    .OrderByDescending(b => b.TotalRequests)
                    .Take(10)
                    .Select(b => $"{b.IpAddress} ({b.Reason})")
                    .ToList(),

                MostTargetedPaths = allRequests
                    .Where(r => !r.IsAuthenticated && !IsPublicPage(r.Path))
                    .GroupBy(r => r.Path)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => $"{g.Key} ({g.Count()} hits)")
                    .ToList(),

                RecentBlocks = _blockedCache.Values
                    .OrderByDescending(b => b.BlockedAt)
                    .Take(10)
                    .Select(b => $"{b.BlockedAt:HH:mm} - {b.IpAddress}: {b.Reason}")
                    .ToList()
            };

            return Task.FromResult(metrics);
        }

        // ===== HELPERS =====

        private bool IsPublicPage(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Exact match
            if (_publicPages.Contains(path))
                return true;

            // Login pages with query strings
            var pathWithoutQuery = path.Split('?')[0];
            return _publicPages.Contains(pathWithoutQuery);
        }

        private bool IsAlwaysAllowedPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var lowerPath = path.ToLowerInvariant();
            return _alwaysAllowedPrefixes.Any(prefix => lowerPath.StartsWith(prefix));
        }

        private bool IsSuspiciousUserAgent(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
                return true;  // No user agent is suspicious

            var lowerAgent = userAgent.ToLowerInvariant();
            return _suspiciousUserAgents.Any(pattern => lowerAgent.Contains(pattern));
        }

        private int GetRecentRequestCount(string ipAddress, TimeSpan window)
        {
            if (!_requestCache.TryGetValue(ipAddress, out var history))
                return 0;

            var cutoff = DateTime.UtcNow.Subtract(window);
            return history.Count(r => r.Timestamp > cutoff);
        }

        // ===== BACKGROUND CLEANUP =====

        private async Task BackgroundCleanupAsync()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(30));

                    var now = DateTime.UtcNow;
                    var cutoff = now.AddHours(-6);

                    // Clean old request history
                    foreach (var kvp in _requestCache.ToList())
                    {
                        var filtered = kvp.Value.Where(r => r.Timestamp > cutoff).ToList();
                        if (filtered.Any())
                            _requestCache[kvp.Key] = filtered;
                        else
                            _requestCache.TryRemove(kvp.Key, out _);
                    }

                    // Clean expired temporary blocks
                    var expiredBlocks = _blockedCache
                        .Where(kvp => kvp.Value.ExpiresAt < now)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var ip in expiredBlocks)
                    {
                        _blockedCache.TryRemove(ip, out _);
                    }

                    // Reset protected page attempts periodically
                    _protectedPageAttempts.Clear();

                    _logger.LogDebug("Cleanup: Removed {Expired} expired blocks, cleaned request history",
                        expiredBlocks.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during background cleanup");
                }
            }
        }
    }
}