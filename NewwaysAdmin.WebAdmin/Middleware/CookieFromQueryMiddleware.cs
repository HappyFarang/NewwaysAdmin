namespace NewwaysAdmin.WebAdmin.Middleware
{
    public class CookieFromQueryMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CookieFromQueryMiddleware> _logger;

        public CookieFromQueryMiddleware(RequestDelegate next, ILogger<CookieFromQueryMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check if there's a sessionId in the query string
            if (context.Request.Query.TryGetValue("sessionId", out var sessionId) && !string.IsNullOrEmpty(sessionId))
            {
                var cookieOptions = new CookieOptions
                {
                    HttpOnly = true,
                    Secure = false,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddHours(24)
                };

                context.Response.Cookies.Append("SessionId", sessionId, cookieOptions);
                _logger.LogInformation("Set SessionId cookie from query string: {SessionId}", sessionId);

                // ⭐ NEW: Mark this request as authenticated for downstream middleware
                context.Items["AuthenticatedViaQueryString"] = true;
                context.Items["SessionId"] = sessionId.ToString();
            }

            await _next(context);
        }
    }
}