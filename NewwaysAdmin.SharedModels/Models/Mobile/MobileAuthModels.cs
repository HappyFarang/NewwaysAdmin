// File: NewwaysAdmin.SharedModels/Models/Mobile/MobileAuthModels.cs
namespace NewwaysAdmin.SharedModels.Models.Mobile
{
    public class MobileAuthRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class MobileAuthResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<string> Permissions { get; set; } = new();
    }

    public class AuthResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<string> Permissions { get; set; } = new();
        public bool RequiresManualLogin { get; set; }
    }
}