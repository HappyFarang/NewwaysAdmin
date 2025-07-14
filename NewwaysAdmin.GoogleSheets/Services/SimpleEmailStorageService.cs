using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace NewwaysAdmin.GoogleSheets.Services
{
    public class SimpleEmailStorageService
    {
        private readonly ILogger<SimpleEmailStorageService> _logger;
        private readonly string _filePath;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SimpleEmailStorageService(ILogger<SimpleEmailStorageService> logger)
        {
            _logger = logger;
            _filePath = Path.Combine("C:", "NewwaysAdmin", "GoogleSheets", "user-emails.json");
        }

        /// <summary>
        /// Get user's email address for Google Sheets ownership transfer
        /// </summary>
        public async Task<string?> GetUserEmailAsync(string username)
        {
            try
            {
                await _lock.WaitAsync();

                if (!File.Exists(_filePath))
                {
                    _logger.LogDebug("Email file does not exist for user {Username}", username);
                    return null;
                }

                var json = await File.ReadAllTextAsync(_filePath);
                var emails = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();

                var email = emails.TryGetValue(username, out var userEmail) ? userEmail : null;
                _logger.LogDebug("Retrieved email for user {Username}: {Found}", username, email != null ? "Found" : "Not Found");

                return email;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading user email for {Username}", username);
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Save user's email address for Google Sheets ownership transfer
        /// </summary>
        public async Task<bool> SetUserEmailAsync(string username, string email)
        {
            try
            {
                await _lock.WaitAsync();

                var emails = new Dictionary<string, string>();

                // Load existing emails if file exists
                if (File.Exists(_filePath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(_filePath);
                        emails = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                    }
                    catch (Exception readEx)
                    {
                        _logger.LogWarning(readEx, "Could not read existing email file, creating new one");
                        emails = new Dictionary<string, string>();
                    }
                }

                // Update user's email
                emails[username] = email;

                // Ensure directory exists
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogInformation("Created directory: {Directory}", directory);
                }

                // Save updated emails
                var newJson = JsonSerializer.Serialize(emails, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_filePath, newJson);

                _logger.LogInformation("✅ Saved email for user {Username} to {FilePath}", username, _filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving user email for {Username}", username);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Remove user's email address
        /// </summary>
        public async Task<bool> RemoveUserEmailAsync(string username)
        {
            try
            {
                await _lock.WaitAsync();

                if (!File.Exists(_filePath))
                {
                    return true; // Already doesn't exist
                }

                var json = await File.ReadAllTextAsync(_filePath);
                var emails = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();

                if (emails.Remove(username))
                {
                    var newJson = JsonSerializer.Serialize(emails, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    await File.WriteAllTextAsync(_filePath, newJson);
                    _logger.LogInformation("Removed email for user {Username}", username);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing user email for {Username}", username);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Get all user emails (for admin purposes)
        /// </summary>
        public async Task<Dictionary<string, string>> GetAllUserEmailsAsync()
        {
            try
            {
                await _lock.WaitAsync();

                if (!File.Exists(_filePath))
                {
                    return new Dictionary<string, string>();
                }

                var json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading all user emails");
                return new Dictionary<string, string>();
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}