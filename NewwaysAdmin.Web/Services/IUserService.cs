namespace NewwaysAdmin.Web.Services
{
    public interface IUserService
    {
        Task<bool> ValidateUserAsync(string username, string password);
        Task CreateUserAsync(string username, string password, string role = "User");
        Task<bool> UserExistsAsync(string username);
    }
}