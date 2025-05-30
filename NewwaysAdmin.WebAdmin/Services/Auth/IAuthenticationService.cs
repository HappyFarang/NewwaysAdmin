/*using NewwaysAdmin.WebAdmin.Models.Auth;

public interface IAuthenticationService
{
    Task<User?> GetUserByNameAsync(string username);
    Task<(bool success, string? error)> LoginAsync(LoginModel model);
    Task<bool> ValidateSessionAsync(string sessionId);
    Task LogoutAsync();
    Task<UserSession?> GetCurrentSessionAsync();
    Task<List<User>> GetAllUsersAsync();
    Task UpdateUserAsync(User user);  // Already exists
    Task InitializeDefaultAdminAsync();
    Task<bool> CreateUserAsync(User user);  // New method
    Task<bool> DeleteUserAsync(string username);  // New method
}
*/