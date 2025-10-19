// File: NewwaysAdmin.WebAdmin/Controllers/MobileController.cs
using Microsoft.AspNetCore.Mvc;
using NewwaysAdmin.WebAdmin.Services.Auth;
using NewwaysAdmin.WebAdmin.Models.Auth;
using NewwaysAdmin.SharedModels.Models.Mobile;

namespace NewwaysAdmin.WebAdmin.Controllers
{
    [ApiController]
    [Route("api/mobile")]
    public class MobileController : ControllerBase
    {
        private readonly IAuthenticationService _authService;
        private readonly ILogger<MobileController> _logger;

        public MobileController(IAuthenticationService authService, ILogger<MobileController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("auth")]
        public async Task<ActionResult<MobileAuthResponse>> Authenticate([FromBody] MobileAuthRequest request)
        {
            try
            {
                _logger.LogInformation("Mobile authentication attempt for user: {Username}", request.Username);

                var result = await _authService.LoginAsync(new LoginModel
                {
                    Username = request.Username,
                    Password = request.Password
                });

                if (result.success)
                {
                    var user = await _authService.GetUserByNameAsync(request.Username);
                    _logger.LogInformation("Mobile authentication successful for user: {Username}", request.Username);

                    return Ok(new MobileAuthResponse
                    {
                        Success = true,
                        Message = $"Welcome {user.Username}!",
                        Permissions = user.PageAccess.Select(p => p.NavigationId).ToList()
                    });
                }

                _logger.LogWarning("Mobile authentication failed for user: {Username}", request.Username);
                return Unauthorized(new MobileAuthResponse
                {
                    Success = false,
                    Message = "Invalid credentials"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during mobile authentication for user: {Username}", request.Username);
                return StatusCode(500, new MobileAuthResponse
                {
                    Success = false,
                    Message = "Server error during authentication"
                });
            }
        }

        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new { Message = "Mobile API is accessible", Timestamp = DateTime.UtcNow });
        }
    }
}