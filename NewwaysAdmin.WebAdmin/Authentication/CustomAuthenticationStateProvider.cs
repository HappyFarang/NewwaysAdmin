using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using NewwaysAdmin.WebAdmin.Models.Auth;
using NewwaysAdmin.WebAdmin.Services.Auth;
using NewwaysAdmin.WebAdmin.Services.Circuit;

namespace NewwaysAdmin.WebAdmin.Authentication;

public class CustomAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IAuthenticationService _authService;
    private readonly ICircuitManager _circuitManager;
    private readonly ILogger<CustomAuthenticationStateProvider> _logger;
    private AuthenticationState? _cachedState;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public CustomAuthenticationStateProvider(
        IAuthenticationService authService,
        ICircuitManager circuitManager,
        ILogger<CustomAuthenticationStateProvider> logger)
    {
        _authService = authService;
        _circuitManager = circuitManager;
        _logger = logger;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_cachedState != null)
            {
                return _cachedState;
            }

            var session = await _authService.GetCurrentSessionAsync();
            return await CreateAuthenticationStateAsync(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting authentication state");
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task NotifyUserAuthenticationAsync(UserSession? session)
    {
        await _stateLock.WaitAsync();
        try
        {
            var state = await CreateAuthenticationStateAsync(session);
            _cachedState = state;
            NotifyAuthenticationStateChanged(Task.FromResult(state));

            // Log the authentication state change
            var isAuthenticated = state.User.Identity?.IsAuthenticated ?? false;
            _logger.LogInformation(
                "Authentication state changed. Authenticated: {IsAuthenticated}, User: {Username}",
                isAuthenticated,
                session?.Username ?? "none");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private Task<AuthenticationState> CreateAuthenticationStateAsync(UserSession? session)
    {
        if (session == null)
        {
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        }

        // Verify circuit matches - but log instead of immediately invalidating
        var currentCircuitId = _circuitManager.GetCurrentCircuitId();
        if (currentCircuitId != session.CircuitId)
        {
            _logger.LogWarning(
                "Circuit mismatch. Current: {CurrentCircuit}, Session: {SessionCircuit}",
                currentCircuitId,
                session.CircuitId);
            // Consider whether you want to invalidate here or just log the warning
        }

        var claims = new List<Claim>
    {
        new(ClaimTypes.Name, session.Username),
        new(ClaimTypes.NameIdentifier, session.SessionId)
    };

        if (session.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        // Add navigation permissions as claims
        foreach (var access in session.PageAccess)
        {
            claims.Add(new Claim("Navigation", access.NavigationId));
            claims.Add(new Claim($"Access_{access.NavigationId}", access.AccessLevel.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "NewwaysAuth");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    public async Task InvalidateAuthenticationStateAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            _cachedState = null;
            await NotifyUserAuthenticationAsync(null);
        }
        finally
        {
            _stateLock.Release();
        }
    }
}