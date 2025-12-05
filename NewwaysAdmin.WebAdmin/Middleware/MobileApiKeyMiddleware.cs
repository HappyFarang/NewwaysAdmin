// File: NewwaysAdmin.WebAdmin/Middleware/MobileApiKeyMiddleware.cs
namespace NewwaysAdmin.WebAdmin.Middleware
{
    public class MobileApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<MobileApiKeyMiddleware> _logger;

        // The secret - hardcoded is fine for internal app
        private const string ValidApiKey = "NwAdmin2024!Mx9$kL#pQ7zR";
        private const string ApiKeyHeader = "X-Mobile-Api-Key";

        public MobileApiKeyMiddleware(RequestDelegate next, ILogger<MobileApiKeyMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path;

            // Check mobile API routes AND SignalR hub
            if (path.StartsWithSegments("/api/mobile") || path.StartsWithSegments("/hubs/universal"))
            {
                var apiKey = context.Request.Headers[ApiKeyHeader].FirstOrDefault();

                if (string.IsNullOrEmpty(apiKey) || apiKey != ValidApiKey)
                {
                    _logger.LogWarning("🚫 BLOCKED {Path} from {IP} - Invalid/missing API key",
                        path,
                        context.Connection.RemoteIpAddress);

                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Unauthorized");
                    return;
                }

                _logger.LogDebug("✅ Valid API key for {Path} from {IP}", path, context.Connection.RemoteIpAddress);
            }

            await _next(context);
        }
    }
}