// Middleware/SimpleDoSMiddleware.cs
using System.Net;
using NewwaysAdmin.WebAdmin.Services.Security;
using NewwaysAdmin.WebAdmin.Services.Auth;

namespace NewwaysAdmin.WebAdmin.Middleware
{
    public class SimpleDoSMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SimpleDoSMiddleware> _logger;

        private bool IsMobileApiRequest(string path)
        {
            return path.StartsWith("/api/mobile/", StringComparison.OrdinalIgnoreCase);
        }

        public SimpleDoSMiddleware(RequestDelegate next, ILogger<SimpleDoSMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context,
    ISimpleDoSProtectionService dosService,
    IAuthenticationService authService)
        {
            var ipAddress = GetClientIpAddress(context);
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            var path = context.Request.Path.ToString();
            var startTime = DateTime.UtcNow;

            try
            {
                // MOBILE API BYPASS - Skip DoS protection for mobile API calls
                if (IsMobileApiRequest(path))
                {
                    _logger.LogInformation("Mobile API request from {IpAddress} to {Path} - bypassing DoS protection",
                        ipAddress, path);

                    // Add minimal security headers for mobile API
                    context.Response.Headers["X-Protection-Level"] = "mobile-api";
                    context.Response.Headers["X-RateLimit-Limit"] = "unlimited";

                    // Continue to next middleware without DoS checks
                    await _next(context);

                    // Still log the request for monitoring
                    var responseTime = DateTime.UtcNow - startTime;
                    await dosService.LogRequestAsync(ipAddress, userAgent, path, context.Response.StatusCode, false);

                    return;
                }

                // Continue with existing DoS protection logic for non-mobile requests...
                var isAuthenticated = await IsUserAuthenticated(context, authService);

                // ... rest of your existing code stays the same
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DoS middleware for {IpAddress}", ipAddress);
                throw;
            }
        }

        private async Task<bool> IsUserAuthenticated(HttpContext context, IAuthenticationService authService)
        {
            try
            {
                // 1. Check for session cookie (primary check)
                var sessionId = context.Request.Cookies["SessionId"];
                if (!string.IsNullOrEmpty(sessionId))
                {
                    _logger.LogDebug("Found SessionId cookie: {SessionId}", sessionId);
                    return true;
                }

                // 2. Check ASP.NET Core authentication
                if (context.User?.Identity?.IsAuthenticated == true)
                {
                    return true;
                }

                // 3. Check your custom authentication service (fallback)
                var session = await authService.GetCurrentSessionAsync();
                return session != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking authentication");
                return false;
            }
        }

        private async Task HandleBlockedRequest(HttpContext context, DoSCheckResult dosCheck,
            IPAddress ipAddress, bool isAuthenticated)
        {
            var userType = isAuthenticated ? "authenticated" : "unauthenticated";
            _logger.LogWarning("Blocked {UserType} request from {IpAddress} - Reason: {Reason}",
                userType, ipAddress, dosCheck.Reason);

            context.Response.StatusCode = 429; // Too Many Requests
            var remainingSeconds = (int)(dosCheck.RemainingBlockTime?.TotalSeconds ?? 1800);
            context.Response.Headers["Retry-After"] = remainingSeconds.ToString();

            // Set rate limit headers based on user type
            if (isAuthenticated)
            {
                context.Response.Headers["X-RateLimit-Limit"] = "120";
                context.Response.Headers["X-RateLimit-Window"] = "60";
            }
            else
            {
                context.Response.Headers["X-RateLimit-Limit"] = "50";  // Updated to match new limits
                context.Response.Headers["X-RateLimit-Window"] = "60";
            }

            context.Response.Headers["X-RateLimit-Remaining"] = "0";

            var message = isAuthenticated
                ? "Rate limit exceeded. Please slow down your requests."
                : "Too many requests. Please try again later.";

            await context.Response.WriteAsync(message);
        }

        private void AddSecurityHeaders(HttpContext context, bool isAuthenticated)
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["X-XSS-Protection"] = "1; mode=block";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            if (isAuthenticated)
            {
                headers["X-RateLimit-Limit"] = "120";
                headers["X-Protection-Level"] = "authenticated";
            }
            else
            {
                headers["X-RateLimit-Limit"] = "50";  // Updated to match new limits
                headers["X-Protection-Level"] = "public";
            }

            // Remove server fingerprinting
            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            headers.Remove("X-AspNet-Version");
            headers.Remove("X-AspNetMvc-Version");
        }

        private IPAddress GetClientIpAddress(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp))
            {
                if (IPAddress.TryParse(cfIp.FirstOrDefault(), out var parsedCfIp))
                    return parsedCfIp;
            }

            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
            {
                var ips = forwardedFor.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (ips.Length > 0 && IPAddress.TryParse(ips[0].Trim(), out var parsedIp))
                    return parsedIp;
            }

            if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp))
            {
                if (IPAddress.TryParse(realIp.FirstOrDefault(), out var parsedRealIp))
                    return parsedRealIp;
            }

            return context.Connection.RemoteIpAddress ?? IPAddress.Loopback;
        }
    }

    public static class SimpleDoSMiddlewareExtensions
    {
        public static IApplicationBuilder UseSimpleDoSProtection(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SimpleDoSMiddleware>();
        }
    }
}