// File: NewwaysAdmin.WebAdmin/Middleware/HostValidationMiddleware.cs
namespace NewwaysAdmin.WebAdmin.Middleware;

/// <summary>
/// Blocks requests with invalid/malicious Host headers before they reach Blazor.
/// Prevents UriFormatException from bots sending garbage like "../../etc/passwd"
/// </summary>
public class HostValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _allowedHosts;

    public HostValidationMiddleware(RequestDelegate next)
    {
        _next = next;
        _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "localhost",
            "newwaysadmin.hopto.org",
            "127.0.0.1"
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;

        if (string.IsNullOrEmpty(host) || !_allowedHosts.Contains(host))
        {
            context.Response.StatusCode = 400;
            return;
        }

        await _next(context);
    }
}