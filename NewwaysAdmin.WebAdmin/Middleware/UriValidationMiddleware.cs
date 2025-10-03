// Middleware/UriValidationMiddleware.cs
using System.Net;
using NewwaysAdmin.WebAdmin.Services.Security;

namespace NewwaysAdmin.WebAdmin.Middleware
{
    public class UriValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<UriValidationMiddleware> _logger;

        // Paths that result in INSTANT PERMANENT BAN
        private static readonly HashSet<string> InstantBanPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            // Git/Version Control
            ".git/config", ".git/head", ".git/", ".svn/", ".hg/",
            
            // PHP Admin Tools (we don't use PHP)
            "phpmyadmin", "pma", "myadmin", "adminer.php", "sql.php",
            
            // WordPress (we don't use WordPress)
            "wp-admin", "wp-login.php", "wp-content", "wp-includes", "xmlrpc.php",
            
            // Common exploit attempts
            "shell", "cmd.exe", "eval-stdin.php", "config.php", "phpinfo.php",
            
            // Environment files
            ".env", ".env.local", ".env.production",
            
            // Backup files
            "backup.sql", "database.sql", ".bak", ".old", ".backup",
            
            // Admin panels we don't have
            "administrator", "admin.php", "manager", "console", "panel"
        };

        // File extensions that don't exist in our app = instant ban
        private static readonly HashSet<string> InvalidExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".php", ".asp", ".aspx", ".jsp", ".cgi", ".pl", ".py",
            ".sql", ".bak", ".old", ".backup", ".zip", ".tar", ".gz"
        };

        // Suspicious patterns in paths
        private static readonly string[] SuspiciousPatterns = new[]
        {
            "../", "..\\", "%2e%2e", "etc/passwd", "/etc/", "cmd=", "exec=",
            "union select", "drop table", "<script", "javascript:",
            "base64", "eval(", "system(", "shell_exec"
        };

        // Valid static files (whitelist approach)
        private static readonly HashSet<string> ValidStaticFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "/favicon.ico",
            "/robots.txt",
            "/security.txt",
            "/.well-known/security.txt"  // ← ADD COMMA HERE
        };

        // Suspicious user agents = instant ban
        private static readonly string[] MaliciousUserAgents = new[]
        {
            "sqlmap", "nikto", "nmap", "masscan", "metasploit",
            "burpsuite", "acunetix", "nessus", "openvas",
            "havij", "pangolin", "wsockexpert", "brutus"
        };

        public UriValidationMiddleware(RequestDelegate next, ILogger<UriValidationMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ISimpleDoSProtectionService? dosService = null)
        {
            var ipAddress = GetClientIpAddress(context);
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            var path = context.Request.Path.ToString();
            var queryString = context.Request.QueryString.ToString();

            try
            {
                // ⭐ CRITICAL: Check if user is authenticated FIRST
                var isAuthenticated = await IsUserAuthenticatedAsync(context);

                if (isAuthenticated)
                {
                    // Authenticated users bypass most security checks
                    _logger.LogDebug("Authenticated user - bypassing security checks for {Path}", path);
                    await _next(context);
                    return;
                }

                // Continue with aggressive checks for UNAUTHENTICATED users only...

                // 1. Malicious user agents - INSTANT BAN
                if (IsMaliciousUserAgent(userAgent))
                {
                    await PermanentlyBanAndReject(context, dosService, ipAddress, userAgent, path,
                        "Malicious user agent detected");
                    return;
                }

                // 2. Instant-ban paths - PERMANENT BAN
                if (IsInstantBanPath(path))
                {
                    await PermanentlyBanAndReject(context, dosService, ipAddress, userAgent, path,
                        $"Attempted access to prohibited path: {path}");
                    return;
                }

                // 3. Invalid file extensions
                if (HasInvalidExtension(path))
                {
                    await PermanentlyBanAndReject(context, dosService, ipAddress, userAgent, path,
                        $"Invalid file extension requested: {Path.GetExtension(path)}");
                    return;
                }

                // 4. Suspicious patterns
                if (ContainsSuspiciousPatterns(path, queryString))
                {
                    await PermanentlyBanAndReject(context, dosService, ipAddress, userAgent, path,
                        "Suspicious pattern detected in request");
                    return;
                }

                // 5. Invalid static file requests
                if (IsInvalidStaticFileRequest(path))
                {
                    await PermanentlyBanAndReject(context, dosService, ipAddress, userAgent, path,
                        $"Request for non-existent static file: {path}");
                    return;
                }

                // 6. Invalid host headers
                if (!IsValidHostHeader(context))
                {
                    _logger.LogWarning("Invalid host header from {IpAddress}: '{Host}'",
                        ipAddress, context.Request.Host.ToString());
                    await LogAndReject(context, dosService, ipAddress, userAgent, path,
                        400, "Invalid host header detected");
                    return;
                }

                // Request passed all checks
                await _next(context);
            }
            catch (UriFormatException ex)
            {
                await PermanentlyBanAndReject(context, dosService, ipAddress, userAgent, path,
                    $"Malformed URI: {ex.Message}");
            }
        }

        private async Task<bool> IsUserAuthenticatedAsync(HttpContext context)
        {
            try
            {
                // Check for session cookie
                var sessionId = context.Request.Cookies["SessionId"];
                if (string.IsNullOrEmpty(sessionId))
                {
                    return false;
                }

                // If we have DoS service, we can cache this check
                // Otherwise, just return true if session cookie exists
                return true;

                // OPTIONAL: For more security, validate the session:
                // var authService = context.RequestServices.GetService<IAuthenticationService>();
                // if (authService != null)
                // {
                //     return await authService.ValidateSessionAsync(sessionId);
                // }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking authentication status");
                return false;
            }
        }

        private bool IsMaliciousUserAgent(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return false;

            var lowerAgent = userAgent.ToLowerInvariant();
            return MaliciousUserAgents.Any(mal => lowerAgent.Contains(mal));
        }

        private bool IsInstantBanPath(string path)
        {
            var lowerPath = path.ToLowerInvariant();
            return InstantBanPaths.Any(banPath => lowerPath.Contains(banPath));
        }

        private bool HasInvalidExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var lowerPath = path.ToLowerInvariant();
            return InvalidExtensions.Any(ext => lowerPath.EndsWith(ext));
        }

        private bool ContainsSuspiciousPatterns(string path, string queryString)
        {
            var combined = $"{path}{queryString}".ToLowerInvariant();
            return SuspiciousPatterns.Any(pattern => combined.Contains(pattern.ToLowerInvariant()));
        }

        private bool IsInvalidStaticFileRequest(string path)
        {
            // DON'T ban for missing CSS/JS - they're just development artifacts or cache issues
            if (path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/_blazor/", StringComparison.OrdinalIgnoreCase))
            {
                // Log but don't ban
                _logger.LogDebug("Missing static file (not banning): {Path}", path);
                return false;
            }

            // Only check extensions for OTHER paths
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
                return false; // Not a static file request

            // Ban only for truly malicious extensions
            return InvalidExtensions.Contains(extension);
        }

        private bool IsValidHostHeader(HttpContext context)
        {
            try
            {
                var host = context.Request.Host.ToString();

                // Must have a host
                if (string.IsNullOrWhiteSpace(host))
                    return false;

                // Check for obviously malicious hosts
                if (host.Contains("..") || host.Contains("/") || host.Contains("\\"))
                    return false;

                // Basic format check
                if (!host.Contains(".") && !host.StartsWith("localhost") && !host.All(char.IsDigit))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidUrl(HttpContext context)
        {
            try
            {
                var path = context.Request.Path.ToString();

                // Check for null bytes
                if (path.Contains('\0'))
                    return false;

                // Check for excessive length
                if (path.Length > 2048)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task PermanentlyBanAndReject(HttpContext context, ISimpleDoSProtectionService? dosService,
            IPAddress ipAddress, string userAgent, string path, string reason)
        {
            _logger.LogWarning("🚫 PERMANENT BAN: {IpAddress} - {Reason} - Path: {Path} - UA: {UserAgent}",
                ipAddress, reason, path, userAgent);

            // Permanently ban the IP
            if (dosService != null)
            {
                await dosService.PermanentlyBanAsync(ipAddress, reason, "AutoBan-Middleware");
            }

            // Set response
            context.Response.StatusCode = 403;
            context.Response.Headers["X-Banned-Reason"] = reason;

            // Minimal response - don't give attackers info
            await context.Response.WriteAsync("Forbidden");
        }

        private async Task LogAndReject(HttpContext context, ISimpleDoSProtectionService? dosService,
            IPAddress ipAddress, string userAgent, string path, int statusCode, string reason)
        {
            // Set response
            context.Response.StatusCode = statusCode;
            context.Response.Headers["X-Rejected-Reason"] = reason;

            // Log to DoS protection service if available
            if (dosService != null)
            {
                await dosService.LogRequestAsync(ipAddress, userAgent + $" [REJECTED: {reason}]", path, statusCode, false);
            }

            // Write minimal response
            await context.Response.WriteAsync("Bad Request");
        }

        private IPAddress GetClientIpAddress(HttpContext context)
        {
            // Handle various proxy headers
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

    public static class UriValidationMiddlewareExtensions
    {
        public static IApplicationBuilder UseUriValidation(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<UriValidationMiddleware>();
        }
    }
}