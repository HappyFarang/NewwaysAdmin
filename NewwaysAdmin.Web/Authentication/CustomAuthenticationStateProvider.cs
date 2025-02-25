using Microsoft.AspNetCore.Components.Authorization;
using NewwaysAdmin.Web.Services;
using System.Security.Claims;

namespace NewwaysAdmin.Web.Authentication
{
    public class CustomAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _currentUser = new(new ClaimsIdentity());
        private readonly IUserService _userService;
        private readonly ILogger<CustomAuthenticationStateProvider> _logger;

        public CustomAuthenticationStateProvider(
            IUserService userService,
            ILogger<CustomAuthenticationStateProvider> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                _logger.LogInformation("Getting authentication state for user: {Username}",
                    _currentUser.Identity?.Name ?? "none");
                return Task.FromResult(new AuthenticationState(_currentUser));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetAuthenticationStateAsync");
                throw;
            }
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            try
            {
                _logger.LogInformation("Login attempt for user: {Username}", username);
                var isValid = await _userService.ValidateUserAsync(username, password);

                if (isValid)
                {
                    // Create claims with proper role
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, username),
                        // You might want to get the actual role from your user service
                        new Claim(ClaimTypes.Role, "Admin") // For now, hardcoded to Admin
                    };

                    var identity = new ClaimsIdentity(claims, "CustomAuth");
                    _currentUser = new ClaimsPrincipal(identity);

                    _logger.LogInformation("User {Username} logged in successfully", username);
                    NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
                }
                else
                {
                    _logger.LogWarning("Login failed for user {Username}", username);
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user {Username}", username);
                throw;
            }
        }

        public void Logout()
        {
            try
            {
                var username = _currentUser.Identity?.Name;
                _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
                _logger.LogInformation("User {Username} logged out", username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                throw;
            }
        }
    }
}