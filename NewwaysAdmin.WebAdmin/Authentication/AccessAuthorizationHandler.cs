using Microsoft.AspNetCore.Authorization;
using NewwaysAdmin.WebAdmin.Services.Auth;
using NewwaysAdmin.WebAdmin.Models.Auth;
using System.Security.Claims;

namespace NewwaysAdmin.WebAdmin.Authorization
{
    public class ModuleAccessHandler : AuthorizationHandler<ModuleAccessRequirement>
    {
        private readonly IAuthenticationService _authService;
        private readonly ILogger<ModuleAccessHandler> _logger;

        public ModuleAccessHandler(IAuthenticationService authService, ILogger<ModuleAccessHandler> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ModuleAccessRequirement requirement)
        {
            var username = context.User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("No username found in claims for module access check");
                context.Fail();
                return;
            }

            try
            {
                var user = await _authService.GetUserByNameAsync(username);
                if (user == null || !user.IsActive)
                {
                    _logger.LogWarning("User {Username} not found or inactive", username);
                    context.Fail();
                    return;
                }

                // Admin users have access to everything
                if (user.IsAdmin)
                {
                    _logger.LogDebug("Admin user {Username} granted access to module {ModuleId}", username, requirement.ModuleId);
                    context.Succeed(requirement);
                    return;
                }

                // Check if user has the required page access
                var pageAccess = user.PageAccess.FirstOrDefault(p => p.NavigationId == requirement.ModuleId);
                if (pageAccess != null && pageAccess.AccessLevel >= requirement.MinimumAccessLevel)
                {
                    // Check if the module requires specific configuration
                    if (user.ModuleConfigs.TryGetValue(requirement.ModuleId, out var moduleConfig))
                    {
                        if (moduleConfig.IsEnabled)
                        {
                            _logger.LogDebug("User {Username} granted access to module {ModuleId} with config", username, requirement.ModuleId);
                            context.Succeed(requirement);
                            return;
                        }
                        else
                        {
                            _logger.LogWarning("User {Username} has module {ModuleId} configured but disabled", username, requirement.ModuleId);
                        }
                    }
                    else
                    {
                        // Module doesn't require specific config, just page access
                        _logger.LogDebug("User {Username} granted access to module {ModuleId} via page access", username, requirement.ModuleId);
                        context.Succeed(requirement);
                        return;
                    }
                }

                _logger.LogWarning("User {Username} denied access to module {ModuleId}. Required: {RequiredLevel}, User has: {UserLevel}",
                    username, requirement.ModuleId, requirement.MinimumAccessLevel, pageAccess?.AccessLevel ?? AccessLevel.None);
                context.Fail();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking module access for user {Username}, module {ModuleId}", username, requirement.ModuleId);
                context.Fail();
            }
        }
    }

    public class PageAccessHandler : AuthorizationHandler<PageAccessRequirement>
    {
        private readonly IAuthenticationService _authService;
        private readonly ILogger<PageAccessHandler> _logger;

        public PageAccessHandler(IAuthenticationService authService, ILogger<PageAccessHandler> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PageAccessRequirement requirement)
        {
            var username = context.User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                context.Fail();
                return;
            }

            try
            {
                var user = await _authService.GetUserByNameAsync(username);
                if (user == null || !user.IsActive)
                {
                    context.Fail();
                    return;
                }

                // Admin users have access to everything
                if (user.IsAdmin)
                {
                    context.Succeed(requirement);
                    return;
                }

                // Check page access
                var pageAccess = user.PageAccess.FirstOrDefault(p => p.NavigationId == requirement.PageId);
                if (pageAccess != null && pageAccess.AccessLevel >= requirement.MinimumAccessLevel)
                {
                    context.Succeed(requirement);
                    return;
                }

                _logger.LogWarning("User {Username} denied access to page {PageId}", username, requirement.PageId);
                context.Fail();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking page access for user {Username}, page {PageId}", username, requirement.PageId);
                context.Fail();
            }
        }
    }
}
