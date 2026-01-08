// File: NewwaysAdmin.WebAdmin/Security/AuthGateMiddleware.cs
// Zero-trust middleware - Lock-free implementation

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Security
{
    public class AuthGateMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuthGateMiddleware> _logger;

        private static readonly ConcurrentDictionary<string, LoginAttemptInfo> _attempts = new();
        private static long _lastCleanupTicks = DateTime.UtcNow.Ticks;

        public AuthGateMiddleware(RequestDelegate next, ILogger<AuthGateMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var ip = GetClientIp(context);
            var path = context.Request.Path.Value ?? "/";

            // Non-blocking cleanup check
            TryCleanupOldEntries();

            // 1. Check if IP is banned
            if (IsBanned(ip, out var remainingMinutes))
            {
                _logger.LogWarning("[AuthGate] Banned IP: {IP} ({Minutes}m remaining)", ip, remainingMinutes);
                context.Response.StatusCode = 403;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync($"Access denied. Try again in {remainingMinutes} minutes.");
                return;
            }

            // 2. Authenticated users → Full access
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                await _next(context);
                return;
            }

            // 2b. Check for session cookie
            if (context.Request.Cookies.ContainsKey("SessionId") ||
                context.Request.Query.ContainsKey("sessionId"))
            {
                await _next(context);
                return;
            }

            // 3. Mobile API with valid key
            if (HasValidMobileApiKey(context))
            {
                await _next(context);
                return;
            }

            // 4. Public paths
            if (IsPublicPath(path))
            {
                await _next(context);
                return;
            }

            // 5. Everything else → redirect to login
            _logger.LogDebug("[AuthGate] Unauthenticated: {Path} from {IP}", path, ip);

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

        // ===== LOCK-FREE CLEANUP =====

        private static void TryCleanupOldEntries()
        {
            var now = DateTime.UtcNow;
            var lastCleanup = new DateTime(Interlocked.Read(ref _lastCleanupTicks));

            // Only every 10 minutes
            if ((now - lastCleanup).TotalMinutes < 10)
                return;

            // Try to claim the cleanup (non-blocking)
            var originalTicks = Interlocked.CompareExchange(
                ref _lastCleanupTicks,
                now.Ticks,
                lastCleanup.Ticks);

            if (originalTicks != lastCleanup.Ticks)
                return; // Another thread is doing cleanup

            // Do cleanup without blocking other requests
            var cutoff = now.AddHours(-4);
            var keysToRemove = _attempts
                .Where(kvp => ShouldRemove(kvp.Value, cutoff))
                .Select(kvp => kvp.Key)
                .Take(100)  // Limit per cleanup cycle to avoid long iteration
                .ToList();

            foreach (var key in keysToRemove)
            {
                _attempts.TryRemove(key, out _);
            }
        }

        private static bool ShouldRemove(LoginAttemptInfo info, DateTime cutoff)
        {
            // Remove if: not banned AND old, OR ban has expired
            if (info.BannedUntil.HasValue)
                return info.BannedUntil.Value < DateTime.UtcNow;

            return info.LastAttempt < cutoff;
        }

        // ===== LOGIN TRACKING (Lock-free) =====

        public static void RecordFailedLogin(string ip, ILogger? logger = null)
        {
            var info = _attempts.AddOrUpdate(
                ip,
                _ => new LoginAttemptInfo { FailedAttempts = 1, LastAttempt = DateTime.UtcNow },
                (_, existing) =>
                {
                    existing.FailedAttempts++;
                    existing.LastAttempt = DateTime.UtcNow;

                    if (existing.FailedAttempts >= 3 && !existing.IsBanned)
                    {
                        existing.BannedUntil = DateTime.UtcNow.AddHours(4);
                    }
                    return existing;
                });

            if (info.IsBanned)
            {
                logger?.LogWarning("[AuthGate] IP BANNED after {Count} failed logins: {IP}",
                    info.FailedAttempts, ip);
            }
            else
            {
                logger?.LogInformation("[AuthGate] Failed login {Count}/3 from {IP}",
                    info.FailedAttempts, ip);
            }
        }

        public static void RecordSuccessfulLogin(string ip, ILogger? logger = null)
        {
            if (_attempts.TryRemove(ip, out _))
            {
                logger?.LogDebug("[AuthGate] Cleared attempts for {IP}", ip);
            }
        }

        public static (int failedAttempts, bool isBanned, DateTime? bannedUntil) GetStatus(string ip)
        {
            if (_attempts.TryGetValue(ip, out var info))
                return (info.FailedAttempts, info.IsBanned, info.BannedUntil);
            return (0, false, null);
        }

        public static bool UnbanIp(string ip) => _attempts.TryRemove(ip, out _);

        public static IEnumerable<(string ip, DateTime bannedUntil, int attempts)> GetBannedIps()
        {
            return _attempts
                .Where(kvp => kvp.Value.IsBanned)
                .Select(kvp => (kvp.Key, kvp.Value.BannedUntil!.Value, kvp.Value.FailedAttempts))
                .ToList();
        }

        // ===== HELPERS =====

        private bool IsBanned(string ip, out int remainingMinutes)
        {
            remainingMinutes = 0;
            if (_attempts.TryGetValue(ip, out var info) && info.IsBanned)
            {
                remainingMinutes = Math.Max(1, (int)(info.BannedUntil!.Value - DateTime.UtcNow).TotalMinutes);
                return true;
            }
            return false;
        }

        private static string GetClientIp(HttpContext context)
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwarded))
            {
                var ip = forwarded.Split(',').First().Trim();
                if (!string.IsNullOrEmpty(ip)) return ip;
            }
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private static bool IsApiRequest(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";
            return path.StartsWith("/api/") ||
                   context.Request.Headers["Accept"].FirstOrDefault()?.Contains("application/json") == true;
        }

        private bool HasValidMobileApiKey(HttpContext context)
        {
            return context.Request.Headers.TryGetValue("X-Mobile-Api-Key", out var key)
                   && key == "NwAdmin2024!Mx9$kL#pQ7zR";
        }

        private static bool IsPublicPath(string path)
        {
            var lower = path.ToLowerInvariant();

            // Login paths
            if (lower == "/" || lower == "/login" || lower.StartsWith("/login/") ||
                lower == "/api/auth/login" || lower == "/api/auth/mobile-login" ||
                lower == "/api/mobile/auth/login")
                return true;

            // SignalR hubs
            if (lower.StartsWith("/hubs/") || lower.StartsWith("/mobilehub") ||
                lower.StartsWith("/categoryhub") || lower.StartsWith("/synchub"))
                return true;

            // Static files
            if (lower.StartsWith("/_framework/") || lower.StartsWith("/_blazor") ||
                lower.StartsWith("/_content/") || lower.StartsWith("/css/") ||
                lower.StartsWith("/js/") || lower.StartsWith("/lib/") ||
                lower.StartsWith("/fonts/") ||
                lower.EndsWith(".css") || lower.EndsWith(".js") ||
                lower.EndsWith(".woff") || lower.EndsWith(".woff2") || lower.EndsWith(".ico"))
                return true;

            return false;
        }

        private class LoginAttemptInfo
        {
            public int FailedAttempts { get; set; }
            public DateTime LastAttempt { get; set; } = DateTime.UtcNow;
            public DateTime? BannedUntil { get; set; }
            public bool IsBanned => BannedUntil.HasValue && BannedUntil > DateTime.UtcNow;
        }
    }
}