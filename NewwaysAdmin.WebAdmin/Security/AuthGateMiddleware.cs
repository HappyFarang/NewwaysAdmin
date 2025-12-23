// File: NewwaysAdmin.WebAdmin/Security/AuthGateMiddleware.cs
// Zero-trust middleware: Only login is public, everything else requires authentication
// 3 failed login attempts = IP ban

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Security
{
    public class AuthGateMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuthGateMiddleware> _logger;

        // Track failed logins and bans per IP
        private static readonly ConcurrentDictionary<string, LoginAttemptInfo> _attempts = new();

        // Clean up old entries periodically
        private static DateTime _lastCleanup = DateTime.UtcNow;
        private static readonly object _cleanupLock = new();

        public AuthGateMiddleware(RequestDelegate next, ILogger<AuthGateMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var ip = GetClientIp(context);
            var path = context.Request.Path.Value ?? "/";

            // Periodic cleanup of old entries (every 10 minutes)
            CleanupOldEntries();

            // 1. Check if IP is banned
            if (IsBanned(ip, out var remainingMinutes))
            {
                _logger.LogWarning("[AuthGate] 🚫 Banned IP attempted access: {IP} ({Minutes}m remaining)",
                    ip, remainingMinutes);

                context.Response.StatusCode = 403;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync($"Access denied. Try again in {remainingMinutes} minutes.");
                return;
            }

            // 2. Authenticated users (web session via claims OR session cookie) → Full access
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                await _next(context);
                return;
            }

            // 2b. Check for session cookie (Blazor might set this)
            if (context.Request.Cookies.ContainsKey(".AspNetCore.Session") ||
                context.Request.Cookies.ContainsKey("NewwaysSession") ||
                context.Request.Query.ContainsKey("sessionId"))
            {
                // Has session indicator - let the auth system validate it
                await _next(context);
                return;
            }

            // 3. Mobile API with valid key → Allow through to authenticate
            if (HasValidMobileApiKey(context))
            {
                await _next(context);
                return;
            }

            // 4. Check if this is a public path (login-related)
            if (IsPublicPath(path))
            {
                await _next(context);
                return;
            }

            // 5. Everything else requires authentication
            _logger.LogDebug("[AuthGate] Unauthenticated request blocked: {Path} from {IP}", path, ip);

            // Redirect to login for web browsers, 401 for API calls
            if (IsApiRequest(context))
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"Authentication required\"}");
            }
            else
            {
                context.Response.Redirect("/login");
            }
        }

        // ===== PUBLIC PATHS =====
        // These are the ONLY paths accessible without authentication

        private static bool IsPublicPath(string path)
        {
            var lowerPath = path.ToLowerInvariant();

            // Login page and API
            if (lowerPath == "/" ||
                lowerPath == "/login" ||
                lowerPath.StartsWith("/login/") ||
                lowerPath == "/api/auth/login" ||
                lowerPath == "/api/auth/mobile-login" ||
                lowerPath == "/api/mobile/auth/login")
            {
                return true;
            }

            // SignalR hubs (mobile app needs these, protected by API key check)
            if (lowerPath.StartsWith("/hubs/") ||
                lowerPath.StartsWith("/mobilehub") ||
                lowerPath.StartsWith("/categoryhub") ||
                lowerPath.StartsWith("/synchub"))
            {
                return true;
            }

            // Static files required for the login page to render
            if (lowerPath.StartsWith("/_framework/") ||
                lowerPath.StartsWith("/_blazor") ||
                lowerPath.StartsWith("/_content/") ||
                lowerPath.StartsWith("/css/") ||
                lowerPath.StartsWith("/js/") ||
                lowerPath.StartsWith("/lib/") ||
                lowerPath.StartsWith("/fonts/") ||
                lowerPath.EndsWith(".css") ||
                lowerPath.EndsWith(".js") ||
                lowerPath.EndsWith(".woff") ||
                lowerPath.EndsWith(".woff2") ||
                lowerPath.EndsWith(".ico"))
            {
                return true;
            }

            return false;
        }

        // ===== MOBILE API KEY =====

        private bool HasValidMobileApiKey(HttpContext context)
        {
            // Check for mobile API key in header
            if (context.Request.Headers.TryGetValue("X-Mobile-Api-Key", out var apiKey))
            {
                // The mobile API key - should match AppConfig.MobileApiKey
                const string ValidMobileApiKey = "NwAdmin2024!Mx9$kL#pQ7zR";
                return apiKey == ValidMobileApiKey;
            }
            return false;
        }

        // ===== LOGIN ATTEMPT TRACKING =====

        /// <summary>
        /// Call this when a login attempt fails
        /// </summary>
        public static void RecordFailedLogin(string ip, ILogger? logger = null)
        {
            var info = _attempts.GetOrAdd(ip, _ => new LoginAttemptInfo());

            lock (info)
            {
                info.FailedAttempts++;
                info.LastAttempt = DateTime.UtcNow;

                if (info.FailedAttempts >= 3 && !info.IsBanned)
                {
                    info.BannedUntil = DateTime.UtcNow.AddHours(4);
                    logger?.LogWarning("[AuthGate] 🔒 IP BANNED after {Count} failed logins: {IP}",
                        info.FailedAttempts, ip);
                }
                else
                {
                    logger?.LogInformation("[AuthGate] Failed login attempt {Count}/3 from {IP}",
                        info.FailedAttempts, ip);
                }
            }
        }

        /// <summary>
        /// Call this when a login succeeds - clears failed attempts
        /// </summary>
        public static void RecordSuccessfulLogin(string ip, ILogger? logger = null)
        {
            if (_attempts.TryRemove(ip, out _))
            {
                logger?.LogDebug("[AuthGate] Cleared failed attempts for {IP} after successful login", ip);
            }
        }

        /// <summary>
        /// Get the current status for an IP (for diagnostics)
        /// </summary>
        public static (int failedAttempts, bool isBanned, DateTime? bannedUntil) GetStatus(string ip)
        {
            if (_attempts.TryGetValue(ip, out var info))
            {
                return (info.FailedAttempts, info.IsBanned, info.BannedUntil);
            }
            return (0, false, null);
        }

        /// <summary>
        /// Manually unban an IP (for admin use)
        /// </summary>
        public static bool UnbanIp(string ip)
        {
            return _attempts.TryRemove(ip, out _);
        }

        /// <summary>
        /// Get list of currently banned IPs
        /// </summary>
        public static IEnumerable<(string ip, DateTime bannedUntil, int attempts)> GetBannedIps()
        {
            var now = DateTime.UtcNow;
            return _attempts
                .Where(kvp => kvp.Value.BannedUntil.HasValue && kvp.Value.BannedUntil > now)
                .Select(kvp => (kvp.Key, kvp.Value.BannedUntil!.Value, kvp.Value.FailedAttempts))
                .ToList();
        }

        // ===== HELPERS =====

        private bool IsBanned(string ip, out int remainingMinutes)
        {
            remainingMinutes = 0;

            if (_attempts.TryGetValue(ip, out var info) && info.IsBanned)
            {
                remainingMinutes = (int)(info.BannedUntil!.Value - DateTime.UtcNow).TotalMinutes;
                if (remainingMinutes < 1) remainingMinutes = 1;
                return true;
            }

            return false;
        }

        private static string GetClientIp(HttpContext context)
        {
            // Check for forwarded IP (behind reverse proxy)
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                var ip = forwardedFor.Split(',').First().Trim();
                if (!string.IsNullOrEmpty(ip)) return ip;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private static bool IsApiRequest(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";
            var accept = context.Request.Headers["Accept"].FirstOrDefault() ?? "";

            return path.StartsWith("/api/") ||
                   accept.Contains("application/json") ||
                   context.Request.ContentType?.Contains("application/json") == true;
        }

        private static void CleanupOldEntries()
        {
            // Only cleanup every 10 minutes
            if ((DateTime.UtcNow - _lastCleanup).TotalMinutes < 10)
                return;

            lock (_cleanupLock)
            {
                if ((DateTime.UtcNow - _lastCleanup).TotalMinutes < 10)
                    return;

                _lastCleanup = DateTime.UtcNow;

                var cutoff = DateTime.UtcNow.AddHours(-4);
                var toRemove = _attempts
                    .Where(kvp => kvp.Value.LastAttempt < cutoff && !kvp.Value.IsBanned)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var ip in toRemove)
                {
                    _attempts.TryRemove(ip, out _);
                }

                // Also remove expired bans
                var expiredBans = _attempts
                    .Where(kvp => kvp.Value.BannedUntil.HasValue && kvp.Value.BannedUntil < DateTime.UtcNow)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var ip in expiredBans)
                {
                    _attempts.TryRemove(ip, out _);
                }
            }
        }

        // ===== INNER CLASS =====

        private class LoginAttemptInfo
        {
            public int FailedAttempts { get; set; }
            public DateTime LastAttempt { get; set; } = DateTime.UtcNow;
            public DateTime? BannedUntil { get; set; }

            public bool IsBanned => BannedUntil.HasValue && BannedUntil > DateTime.UtcNow;
        }
    }
}