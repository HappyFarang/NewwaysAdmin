// File: Mobile/NewwaysAdmin.Mobile/Services/MobileSessionState.cs
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Mobile.Services
{
    /// <summary>
    /// Holds current user session info in memory for fast permission checks.
    /// Populated once at login/auto-login, cleared at logout.
    /// Singleton - one instance for the entire app lifetime.
    /// 
    /// Permissions are always loaded from local cache (which gets updated
    /// when server is available). This class doesn't care about online/offline.
    /// </summary>
    public class MobileSessionState
    {
        private readonly ILogger<MobileSessionState> _logger;

        // Static accessor for components that can't use constructor injection
        public static MobileSessionState? Current { get; private set; }

        public MobileSessionState(ILogger<MobileSessionState> logger)
        {
            _logger = logger;
            Current = this;
        }

        // ===== SESSION DATA =====

        public string? Username { get; private set; }
        public List<string> Permissions { get; private set; } = new();
        public DateTime? SessionStarted { get; private set; }

        // ===== STATE CHECKS =====

        public bool IsLoggedIn => !string.IsNullOrEmpty(Username);

        public bool HasPermission(string permissionId)
        {
            if (string.IsNullOrEmpty(permissionId)) return false;
            return Permissions.Contains(permissionId);
        }

        public bool HasAnyPermission(params string[] permissionIds)
        {
            if (permissionIds == null || permissionIds.Length == 0) return false;
            return permissionIds.Any(p => Permissions.Contains(p));
        }

        public bool HasAllPermissions(params string[] permissionIds)
        {
            if (permissionIds == null || permissionIds.Length == 0) return true;
            return permissionIds.All(p => Permissions.Contains(p));
        }

        // ===== SESSION MANAGEMENT =====

        public void SetSession(string username, List<string>? permissions)
        {
            Username = username;
            Permissions = permissions ?? new List<string>();
            SessionStarted = DateTime.UtcNow;

            _logger.LogInformation(
                "Session started for {Username} with {PermissionCount} permissions",
                username, Permissions.Count);
        }

        public void Clear()
        {
            var previousUser = Username;
            Username = null;
            Permissions = new List<string>();
            SessionStarted = null;

            if (!string.IsNullOrEmpty(previousUser))
            {
                _logger.LogInformation("Session cleared for {Username}", previousUser);
            }
        }
    }
}