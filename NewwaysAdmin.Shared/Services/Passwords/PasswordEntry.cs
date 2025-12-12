// File: NewwaysAdmin.Shared/Services/Passwords/PasswordEntry.cs

namespace NewwaysAdmin.Shared.Services.Passwords;

/// <summary>
/// Single password entry in the company password store
/// </summary>
public class PasswordEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Label { get; set; } = string.Empty;        // What is it for (e.g., "Bank login", "Server SSH")
    public string Username { get; set; } = string.Empty;     // Username/email for the login
    public string Password { get; set; } = string.Empty;     // The actual password
    public string Note { get; set; } = string.Empty;         // Additional notes
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Container for all password entries - this gets encrypted and stored
/// </summary>
public class PasswordStore
{
    public List<PasswordEntry> Entries { get; set; } = new();
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Encryption key configuration - stored separately, never in GitHub
/// </summary>
public class EncryptionKeyConfig
{
    public string Key { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Wrapper for encrypted data storage (string can't be used directly with IDataStorage)
/// </summary>
public class EncryptedDataWrapper
{
    public string EncryptedData { get; set; } = string.Empty;
}