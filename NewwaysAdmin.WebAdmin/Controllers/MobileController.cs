using Microsoft.AspNetCore.Mvc;
using NewwaysAdmin.SharedModels.Models.Mobile;
using NewwaysAdmin.WebAdmin.Services.Auth;

namespace NewwaysAdmin.WebAdmin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MobileController : ControllerBase
    {
        private readonly IAuthenticationService _authService;
        private readonly ILogger<MobileController> _logger;

        public MobileController(IAuthenticationService authService, ILogger<MobileController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new { status = "alive", timestamp = DateTime.UtcNow });
        }

        [HttpPost("auth")]
        public async Task<IActionResult> Authenticate([FromBody] MobileAuthRequest request)
        {
            try
            {
                // Use your existing authentication service
                var result = await _authService.AuthenticateAsync(request.Username, request.Password);

                if (result.Success)
                {
                    return Ok(new MobileAuthResponse
                    {
                        Success = true,
                        Message = "Authentication successful",
                        Permissions = result.Permissions // Pass through the user's permissions
                    });
                }
                else
                {
                    return Ok(new MobileAuthResponse
                    {
                        Success = false,
                        Message = result.Message ?? "Authentication failed"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mobile authentication error");
                return Ok(new MobileAuthResponse
                {
                    Success = false,
                    Message = "Server error"
                });
            }
        }
    }
}