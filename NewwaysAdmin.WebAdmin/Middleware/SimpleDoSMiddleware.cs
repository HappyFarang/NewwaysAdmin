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
                // Check if user is authenticated
                var isAuthenticated = await IsUserAuthenticated(context, authService);

                // Check for DoS patterns
                var dosCheck = await dosService.CheckRequestAsync(ipAddress, userAgent, path, isAuthenticated);

                if (dosCheck.IsBlocked)
                {
                    await HandleBlockedRequest(context, dosCheck, ipAddress, isAuthenticated);
                    return;
                }

                // Log high-risk activity
                if (dosCheck.IsHighRisk)
                {
                    var userType = isAuthenticated ? "authenticated" : "unauthenticated";
                    _logger.LogWarning("High-risk {UserType} client: {IpAddress} - {RequestsInWindow} requests",
                        userType, ipAddress, dosCheck.RequestsInWindow);
                }

                // Add security headers
                AddSecurityHeaders(context, isAuthenticated);

                // Continue to next middleware
                await _next(context);

                // Log the request
                var responseTime = DateTime.UtcNow - startTime;
                await dosService.LogRequestAsync(ipAddress, userAgent, path, context.Response.StatusCode, isAuthenticated);

                // Log errors
                if (context.Response.StatusCode >= 400)
                {
                    var logLevel = isAuthenticated ? LogLevel.Information : LogLevel.Warning;
                    _logger.Log(logLevel,
                        "Error {StatusCode} from {UserType} user {IpAddress} to {Path} in {ResponseTime}ms",
                        context.Response.StatusCode,
                        isAuthenticated ? "authenticated" : "unauthenticated",
                        ipAddress,
                        path,
                        responseTime.TotalMilliseconds);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DoS middleware for {IpAddress}", ipAddress);

                // Still log the request
                await dosService.LogRequestAsync(ipAddress, userAgent, path, 500, false);
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