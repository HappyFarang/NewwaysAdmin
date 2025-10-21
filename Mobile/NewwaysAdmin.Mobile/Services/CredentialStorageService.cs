// File: Mobile/NewwaysAdmin.Mobile/Services/CredentialStorageService.cs
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Mobile.Services
{
    public class CredentialStorageService
    {
        private readonly EnhancedStorageFactory _storageFactory;

        public CredentialStorageService(EnhancedStorageFactory storageFactory)
        {
            _storageFactory = storageFactory;
        }

        public async Task<SavedCredentials?> GetSavedCredentialsAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<SavedCredentials>("MobileAuth");
                return await storage.LoadAsync("credentials");
            }
            catch (Exception)
            {
                return null; // First time or corrupted file
            }
        }

        public async Task SaveCredentialsAsync(string username, string password)
        {
            var credentials = new SavedCredentials
            {
                Username = username,
                Password = password,
                SavedAt = DateTime.UtcNow
            };

            var storage = _storageFactory.GetStorage<SavedCredentials>("MobileAuth");
            await storage.SaveAsync("credentials", credentials);
        }

        public async Task ClearCredentialsAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<SavedCredentials>("MobileAuth");
                await storage.DeleteAsync("credentials");
            }
            catch (Exception)
            {
                // File might not exist, ignore
            }
        }
    }

    public class SavedCredentials
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public DateTime SavedAt { get; set; }
    }
}