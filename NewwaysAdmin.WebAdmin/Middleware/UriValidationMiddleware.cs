// NewwaysAdmin.WebAdmin/Middleware/UriValidationMiddleware.cs
using System.Net;
using NewwaysAdmin.WebAdmin.Services.Security;

namespace NewwaysAdmin.WebAdmin.Middleware
{
    public class UriValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<UriValidationMiddleware> _logger;

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

            try
            {
                // 1. Check for invalid host headers (main cause of UriFormatException)
                if (!IsValidHostHeader(context))
                {
                    _logger.LogWarning("Invalid host header from {IpAddress}: '{Host}' - UserAgent: {UserAgent}",
                        ipAddress, context.Request.Host.ToString(), userAgent);

                    await LogAndReject(context, dosService, ipAddress, userAgent, path,
                        400, "Invalid host header detected");
                    return;
                }

                // 2. Check for malformed URLs
                if (!IsValidUrl(context))
                {
                    _logger.LogWarning("Malformed URL from {IpAddress}: '{Path}' - UserAgent: {UserAgent}",
                        ipAddress, path, userAgent);

                    await LogAndReject(context, dosService, ipAddress, userAgent, path,
                        400, "Malformed URL detected");
                    return;
                }

                // 3. Check for suspicious bot patterns in the URL
                if (IsSuspiciousRequest(context))
                {
                    _logger.LogWarning("Suspicious request from {IpAddress}: '{Path}' - UserAgent: {UserAgent}",
                        ipAddress, path, userAgent);

                    await LogAndReject(context, dosService, ipAddress, userAgent, path,
                        403, "Suspicious request pattern");
                    return;
                }

                // URL is valid, continue to next middleware
                await _next(context);
            }
            catch (UriFormatException ex)
            {
                // Catch any remaining UriFormatExceptions and handle gracefully
                _logger.LogError(ex, "UriFormatException caught from {IpAddress} - Host: '{Host}', Path: '{Path}', UserAgent: '{UserAgent}'",
                    ipAddress, context.Request.Host.ToString(), path, userAgent);

                await LogAndReject(context, dosService, ipAddress, userAgent, path,
                    400, "URI format error");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in URI validation for {IpAddress}", ipAddress);
                throw;
            }
        }

        private bool IsValidHostHeader(HttpContext context)
        {
            try
            {
                var host = context.Request.Host;

                // Check if host is present
                if (!host.HasValue || string.IsNullOrWhiteSpace(host.Host))
                {
                    return false;
                }

                // Check for obviously invalid characters
                var hostString = host.Host;
                if (hostString.Contains(' ') ||
                    hostString.Contains('\t') ||
                    hostString.Contains('\n') ||
                    hostString.Contains('\r'))
                {
                    return false;
                }

                // Try to construct a URI to validate the host
                if (!Uri.IsWellFormedUriString($"https://{host.Host}", UriKind.Absolute))
                {
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsValidUrl(HttpContext context)
        {
            try
            {
                var path = context.Request.Path.ToString();
                var queryString = context.Request.QueryString.ToString();

                // Check for null bytes or other dangerous characters
                if (path.Contains('\0') || queryString.Contains('\0'))
                {
                    return false;
                }

                // Check for extremely long URLs (possible attack)
                if (path.Length > 2048 || queryString.Length > 4096)
                {
                    return false;
                }

                // Check for double encoding attacks
                if (path.Contains("%25") || queryString.Contains("%25"))
                {
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsSuspiciousRequest(HttpContext context)
        {
            var path = context.Request.Path.ToString().ToLower();
            var userAgent = context.Request.Headers["User-Agent"].ToString().ToLower();
            var queryString = context.Request.QueryString.ToString().ToLower();

            // Suspicious paths that bots commonly target
            string[] suspiciousPaths = {
                "/wp-admin", "/administrator", "/admin", "/phpmyadmin", "/myadmin",
                "/.env", "/.git", "/config", "/backup", "/database", "/db",
                "/login.php", "/wp-login", "/manager", "/console", "/panel",
                "/robots.txt", "/sitemap.xml", "/favicon.ico", "/xmlrpc.php",
                "/vendor", "/node_modules", "/.well-known", "/api/v1"
            };

            // Suspicious user agents
            string[] suspiciousAgents = {
                "python-requests", "curl", "wget", "go-http-client", "java",
                "bot", "crawler", "spider", "scanner", "masscan", "nmap",
                "sqlmap", "nikto", "dirb", "gobuster", "wfuzz", "exploit"
            };

            // Suspicious query parameters
            string[] suspiciousParams = {
                "union", "select", "insert", "delete", "drop", "exec",
                "script", "javascript", "vbscript", "onload", "onerror",
                "../", "..\\", "%2e%2e", "passwd", "/etc/", "cmd", "eval"
            };

            // Check suspicious paths
            if (suspiciousPaths.Any(sp => path.Contains(sp)))
            {
                return true;
            }

            // Check suspicious user agents
            if (string.IsNullOrEmpty(userAgent) || suspiciousAgents.Any(sa => userAgent.Contains(sa)))
            {
                return true;
            }

            // Check suspicious query parameters
            if (!string.IsNullOrEmpty(queryString) && suspiciousParams.Any(sp => queryString.Contains(sp)))
            {
                return true;
            }

            // Check for directory traversal attempts
            if (path.Contains("..") || path.Contains("%2e%2e"))
            {
                return true;
            }

            return false;
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

            // Write minimal response (don't give attackers information)
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