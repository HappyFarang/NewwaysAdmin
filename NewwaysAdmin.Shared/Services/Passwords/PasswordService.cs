// File: NewwaysAdmin.Shared/Services/Passwords/PasswordService.cs

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using Newtonsoft.Json;

namespace NewwaysAdmin.Shared.Services.Passwords;

/// <summary>
/// Manages encrypted password storage
/// </summary>
public class PasswordService
{
    private readonly EnhancedStorageFactory _storageFactory;
    private readonly ILogger<PasswordService> _logger;

    private const string FOLDER_NAME = "Passwords";
    private const string DATA_FILE = "password_store";
    private const string KEY_FOLDER = "Security";
    private const string KEY_FILE = "password_encryption_key";

    private IDataStorage<EncryptedDataWrapper>? _encryptedStorage;  // Changed from string
    private IDataStorage<EncryptionKeyConfig>? _keyStorage;
    private byte[]? _encryptionKey;

    public PasswordService(
        EnhancedStorageFactory storageFactory,
        ILogger<PasswordService> logger)
    {
        _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ===== PUBLIC API =====

    public async Task<List<PasswordEntry>> GetAllAsync()
    {
        await EnsureInitializedAsync();
        var store = await LoadStoreAsync();
        return store.Entries;
    }

    public async Task AddAsync(PasswordEntry entry, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(entry.Label))
            throw new ArgumentException("Label cannot be empty");

        await EnsureInitializedAsync();

        var store = await LoadStoreAsync();

        entry.Id = Guid.NewGuid().ToString();
        entry.CreatedAt = DateTime.UtcNow;
        entry.CreatedBy = createdBy;

        store.Entries.Add(entry);
        store.LastModified = DateTime.UtcNow;

        await SaveStoreAsync(store);

        _logger.LogInformation("Password entry added: {Label} by {User}", entry.Label, createdBy);
    }

    public async Task DeleteAsync(string id)
    {
        await EnsureInitializedAsync();

        var store = await LoadStoreAsync();
        var removed = store.Entries.RemoveAll(e => e.Id == id);

        if (removed > 0)
        {
            store.LastModified = DateTime.UtcNow;
            await SaveStoreAsync(store);
            _logger.LogInformation("Password entry deleted: {Id}", id);
        }
    }

    public async Task UpdateAsync(string id, string label, string password)
    {
        await EnsureInitializedAsync();

        var store = await LoadStoreAsync();
        var entry = store.Entries.FirstOrDefault(e => e.Id == id);

        if (entry != null)
        {
            entry.Label = label.Trim();
            entry.Password = password;
            store.LastModified = DateTime.UtcNow;
            await SaveStoreAsync(store);
            _logger.LogInformation("Password entry updated: {Label}", label);
        }
    }

    // ===== INITIALIZATION =====

    private async Task EnsureInitializedAsync()
    {
        if (_encryptionKey != null)
            return;

        _encryptedStorage = _storageFactory.GetStorage<EncryptedDataWrapper>(FOLDER_NAME);  // Changed
        _keyStorage = _storageFactory.GetStorage<EncryptionKeyConfig>(KEY_FOLDER);

        _encryptionKey = await LoadOrCreateKeyAsync();
    }

    private async Task<byte[]> LoadOrCreateKeyAsync()
    {
        try
        {
            var keyConfig = await _keyStorage!.LoadAsync(KEY_FILE);
            _logger.LogDebug("Loaded existing encryption key");
            return Convert.FromBase64String(keyConfig.Key);
        }
        catch
        {
            _logger.LogInformation("Generating new encryption key for password store");

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();

            var keyConfig = new EncryptionKeyConfig
            {
                Key = Convert.ToBase64String(aes.Key),
                CreatedAt = DateTime.UtcNow
            };

            await _keyStorage!.SaveAsync(KEY_FILE, keyConfig);
            _logger.LogInformation("Encryption key saved to {Folder}/{File}", KEY_FOLDER, KEY_FILE);

            return aes.Key;
        }
    }

    // ===== ENCRYPTED STORAGE =====

    private async Task<PasswordStore> LoadStoreAsync()
    {
        try
        {
            var wrapper = await _encryptedStorage!.LoadAsync(DATA_FILE);
            var decryptedJson = Decrypt(wrapper.EncryptedData);
            return JsonConvert.DeserializeObject<PasswordStore>(decryptedJson) ?? new PasswordStore();
        }
        catch
        {
            _logger.LogDebug("No existing password store found, creating new");
            return new PasswordStore();
        }
    }

    private async Task SaveStoreAsync(PasswordStore store)
    {
        var json = JsonConvert.SerializeObject(store, Formatting.None);
        var encrypted = Encrypt(json);
        var wrapper = new EncryptedDataWrapper { EncryptedData = encrypted };
        await _encryptedStorage!.SaveAsync(DATA_FILE, wrapper);
    }

    // ===== AES ENCRYPTION =====

    private string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey!;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return Convert.ToBase64String(result);
    }

    private string Decrypt(string encryptedBase64)
    {
        var fullData = Convert.FromBase64String(encryptedBase64);

        using var aes = Aes.Create();
        aes.Key = _encryptionKey!;

        var iv = new byte[aes.BlockSize / 8];
        Buffer.BlockCopy(fullData, 0, iv, 0, iv.Length);
        aes.IV = iv;

        var encryptedBytes = new byte[fullData.Length - iv.Length];
        Buffer.BlockCopy(fullData, iv.Length, encryptedBytes, 0, encryptedBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

        return Encoding.UTF8.GetString(decryptedBytes);
    }
}