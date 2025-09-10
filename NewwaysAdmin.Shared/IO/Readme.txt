# NewwaysAdmin IO Manager - Complete System Manual

## Table of Contents
1. [System Overview](#system-overview)
2. [Core Components](#core-components)
3. [Storage Types & File Management](#storage-types--file-management)
4. [File Indexing System](#file-indexing-system)
5. [Encryption & Security](#encryption--security)
6. [Backup System](#backup-system)
7. [External Collections](#external-collections)
8. [Configuration Examples](#configuration-examples)
9. [Integration Guide](#integration-guide)
10. [Troubleshooting](#troubleshooting)

---

## System Overview

The NewwaysAdmin IO Manager is a comprehensive file management system that provides:
- **Type-safe data storage** with automatic serialization
- **File indexing and tracking** for processing pipelines
- **Encryption capabilities** for sensitive data
- **Automatic backup management**
- **External file monitoring** (NAS, network drives)
- **Processing status tracking** with enum-based stages

### Architecture Principles
- **Component-based design** - Small, focused classes
- **Type safety** - Compile-time safety with enums
- **Backwards compatibility** - New features are opt-in
- **Separation of concerns** - Storage, indexing, and processing are separate

---

## Core Components

### EnhancedStorageFactory
Central factory for creating and managing storage instances.
```csharp
var factory = new EnhancedStorageFactory(logger);
factory.RegisterFolder(folder);
var storage = factory.GetStorage<MyDataType>("FolderName");
```

### StorageFolder
Defines storage folder configuration and behavior.
```csharp
public class StorageFolder
{
    // Basic properties
    public string Name { get; set; }              // Unique folder identifier
    public StorageType Type { get; set; }         // Json or Binary
    public string Path { get; set; }              // Optional nesting path
    public bool IsShared { get; set; }            // Multi-module access
    
    // Backup properties  
    public bool CreateBackups { get; set; }       // Enable/disable backups
    public int MaxBackupCount { get; set; }       // Backup retention limit
    
    // Indexing properties
    public bool IndexFiles { get; set; }          // Enable file indexing
    public string[]? IndexedExtensions { get; set; } // File types to index
    public bool IndexContent { get; set; }        // Full-text indexing (future)
}
```

### File Indexing Components
- **FileIndexEngine** - Low-level file scanning and hash calculation
- **FileIndexManager** - High-level indexing operations for internal folders
- **FileIndexProcessingManager** - Processing status tracking and updates
- **ExternalIndexManager** - Dynamic external collection management

---

## Storage Types & File Management

### What Files Does Each Folder Type Manage?

#### 1. Application Data Files
Files created and managed by the application itself:
- **StorageType.Json** → `.json` files (configs, user data, structured data)
- **StorageType.Binary** → `.bin` files (serialized objects, processed data, performance-critical data)

#### 2. Index Files (Automatic)
When `IndexFiles = true`, automatically creates:
- **{FolderName}_Index/file-index.json** - File metadata and processing status
- Contains: file paths, SHA256 hashes, sizes, timestamps, processing stages

#### 3. External Files (Monitored)
Files that exist outside the application:
- **Any extensions** via `IndexedExtensions = [".pdf", ".jpg", ".png"]`
- Only indexed/tracked, not stored locally
- Used for processing pipeline management

### Storage Type Selection Guide

| Use Case | StorageType | Reasoning |
|----------|-------------|-----------|
| User settings, configs | Json | Human-readable, easy debugging |
| Large datasets, performance-critical | Binary | Faster serialization, smaller files |
| Processing pipeline tracking | Json | Easy to inspect processing status |
| Cached calculations | Binary | Performance optimization |

---

## File Indexing System

### Processing Stages
File processing is tracked using type-safe enums:

```csharp
public enum ProcessingStage
{
    Detected = 0,           // File found and indexed
    ProcessingStarted = 1,  // Processing begun
    Processing = 2,         // Currently being processed  
    ProcessingCompleted = 3,// Successfully finished
    ProcessingFailed = 4    // Failed - needs retry/attention
}
```

### Internal vs External Indexing

#### Internal Indexing (IO Manager Folders)
For folders managed by the application:
```csharp
new StorageFolder
{
    Name = "ProcessedOcrData",
    Type = StorageType.Binary,      // Stores .bin files
    IndexFiles = true,              // Track the .bin files
    IndexedExtensions = null,       // Auto-detect .bin (internal)
    CreateBackups = true
}
```
- `IndexedExtensions = null` → Auto-detects based on StorageType
- `StorageType.Json` → indexes `.json` files
- `StorageType.Binary` → indexes `.bin` files

#### External Indexing (NAS, Network Drives)
For monitoring external files:
```csharp
await externalIndexManager.RegisterExternalFolderAsync(
    "BankSlips2024_Q1",           // Collection name
    @"\\NAS\BankSlips\2024\Q1",  // External path
    [".pdf", ".jpg", ".png"]);   // Explicit extensions
```
- Used to track files you don't store locally
- Perfect for "have we processed this file already?" scenarios

### Index Data Structure
Each indexed file contains:
```csharp
public class FileIndexEntry
{
    public string FilePath { get; set; }           // Relative path
    public string FileHash { get; set; }           // SHA256 for duplicates
    public DateTime Created { get; set; }          // File creation time
    public DateTime LastModified { get; set; }     // File modification time
    public long FileSize { get; set; }             // Size in bytes
    public DateTime IndexedAt { get; set; }        // When indexed
    
    // Processing tracking
    public bool IsProcessed { get; set; }          // Processing complete?
    public DateTime? ProcessedAt { get; set; }     // When completed
    public ProcessingStage ProcessingStage { get; set; } // Current stage
    public Dictionary<string, object>? ProcessingMetadata { get; set; } // Custom data
}
```

---

## Encryption & Security

### Encrypted Storage
For sensitive data, enable encryption:
```csharp
var encryptedStorage = new EncryptedStorage<SensitiveData>(
    baseStorage,
    encryptionKey,
    logger);

await encryptedStorage.SaveAsync("sensitive-data", secretData);
```

### Security Features
- **AES encryption** for data at rest
- **SHA256 hashing** for file integrity verification
- **Access control** via AllowedMachines and AllowedApps
- **Audit trails** through comprehensive logging

### Best Practices
- Use encryption for PII, financial data, authentication tokens
- Regular key rotation for production systems
- Separate encryption keys per data classification
- Never log sensitive data, even in debug mode

---

## Backup System

### Automatic Backups
When `CreateBackups = true`:
1. **Before overwrite** - Original file backed up before saving new version
2. **Timestamp naming** - Backups named with ISO timestamp
3. **Automatic cleanup** - Old backups removed when `MaxBackupCount` exceeded
4. **Cross-storage-type** - Works with both Json and Binary storage

### Backup Configuration
```csharp
new StorageFolder
{
    Name = "CriticalData",
    CreateBackups = true,        // Enable backups
    MaxBackupCount = 10,         // Keep 10 previous versions
    Type = StorageType.Json
}
```

### Backup Location
Backups stored in: `Data/{FolderPath}/Backups/{Timestamp}_{FileName}`

Example:
```
Data/
├── Users/
│   ├── user-data.json
│   └── Backups/
│       ├── 20250905_143022_user-data.json
│       └── 20250904_091533_user-data.json
```

---

## External Collections

### Dynamic Collection Management
External collections allow monitoring of files outside your application:

```csharp
// Register a new external collection
await externalIndexManager.RegisterExternalFolderAsync(
    "BankSlips2024_Q1",                    // Unique collection name
    @"\\NAS\BankSlips\2024\Q1",          // External path to monitor
    [".pdf", ".jpg", ".png"]);           // File types to track

// Scan for new/changed files
await externalIndexManager.ScanExternalFolderAsync("BankSlips2024_Q1");

// Get the index
var externalFiles = await externalIndexManager.GetExternalIndexAsync("BankSlips2024_Q1");
```

### Collection Storage
External collections create two types of files:
1. **Collection Config**: `ExternalIndexes/collection_{name}.json`
2. **Index Data**: `External_{name}_Index/file-index.json`

### Use Cases
- **NAS monitoring** - Track files on network storage
- **Input folder processing** - Monitor for new documents
- **Duplicate detection** - Avoid reprocessing the same files
- **Audit trails** - Track what external files have been processed

---

## Configuration Examples

### Bank Slip Processing System
Complete example for a bank slip OCR processing pipeline:

```csharp
// 1. Input monitoring folder
RegisterFolderIfNotExists(new StorageFolder
{
    Name = "BankSlipInput",
    Description = "Monitors incoming bank slip PDFs",
    Type = StorageType.Json,         // For any config files
    IndexFiles = true,               // Track incoming PDFs
    IndexedExtensions = [".pdf"],    // Monitor PDF files
    CreateBackups = false,           // Input folder, don't backup
    Path = "BankSlips/Input"
});

// 2. Processed OCR data storage
RegisterFolderIfNotExists(new StorageFolder
{
    Name = "BankSlipOcrData", 
    Description = "Stores processed OCR results",
    Type = StorageType.Binary,       // Efficient storage for OCR data
    IndexFiles = true,               // Track processed files
    IndexedExtensions = null,        // Auto-detect .bin files
    CreateBackups = true,
    MaxBackupCount = 10,
    Path = "BankSlips/Processed"
});

// 3. External NAS monitoring
await externalIndexManager.RegisterExternalFolderAsync(
    "BankSlipsArchive2024", 
    @"\\NAS\BankSlips\Archive\2024",
    [".pdf", ".tiff", ".jpg"]);
```

### User Management System
```csharp
// User accounts
RegisterFolderIfNotExists(new StorageFolder
{
    Name = "Users",
    Description = "User account data",
    Type = StorageType.Json,         // Human-readable for debugging
    IndexFiles = false,              // No indexing needed
    CreateBackups = true,
    MaxBackupCount = 5,
    IsShared = false                 // Sensitive data, not shared
});

// Session data
RegisterFolderIfNotExists(new StorageFolder
{
    Name = "Sessions", 
    Description = "Active user sessions",
    Type = StorageType.Binary,       // Performance-critical
    IndexFiles = false,              // No indexing needed
    CreateBackups = false,           // Temporary data
    IsShared = true                  // Multiple modules access sessions
});
```

---

## Integration Guide

### Service Registration (DI Container)
```csharp
// In Program.cs or Startup.cs
services.AddSingleton<EnhancedStorageFactory>();
services.AddScoped<FileIndexEngine>();
services.AddScoped<FileIndexManager>();
services.AddScoped<FileIndexProcessingManager>();
services.AddScoped<ExternalIndexManager>();
```

### Processing Pipeline Integration
```csharp
public class BankSlipProcessor
{
    private readonly FileIndexProcessingManager _processingManager;
    
    public async Task ProcessNewFilesAsync()
    {
        // 1. Find unprocessed files
        var unprocessedFiles = await _processingManager
            .GetUnprocessedFilesAsync("BankSlipInput");
            
        foreach (var file in unprocessedFiles)
        {
            // 2. Mark as started
            await _processingManager.UpdateProcessingStageAsync(
                "BankSlipInput", 
                file.FilePath, 
                ProcessingStage.ProcessingStarted);
                
            try
            {
                // 3. Process the file
                var ocrResult = await PerformOcrAsync(file.FilePath);
                
                // 4. Mark as complete
                await _processingManager.MarkFileAsProcessedAsync(
                    "BankSlipInput",
                    file.FilePath,
                    ProcessingStage.ProcessingCompleted,
                    new Dictionary<string, object>
                    {
                        ["ocr_confidence"] = ocrResult.Confidence,
                        ["extracted_amount"] = ocrResult.Amount
                    });
            }
            catch (Exception ex)
            {
                // 5. Mark as failed
                await _processingManager.UpdateProcessingStageAsync(
                    "BankSlipInput",
                    file.FilePath, 
                    ProcessingStage.ProcessingFailed);
            }
        }
    }
}
```

### FileSystemWatcher Integration
```csharp
public class IndexingIntegrationService
{
    public async Task OnFileCreatedAsync(string filePath, StorageFolder folder)
    {
        if (folder.IsIndexingEnabled())
        {
            await _fileIndexManager.AddFileToIndexAsync(folder, folderPath, filePath);
            // File automatically indexed with ProcessingStage.Detected
        }
    }
}
```

---

## Troubleshooting

### Common Issues

#### Files Not Being Indexed
**Symptoms**: New files appear but aren't tracked in index
**Causes**:
- `IndexFiles = false` in folder configuration
- File extension not in `IndexedExtensions` array
- FileSystemWatcher not properly integrated

**Solutions**:
```csharp
// Check folder configuration
var folder = GetStorageFolder("MyFolder");
if (!folder.IsIndexingEnabled())
{
    // Enable indexing
    folder.IndexFiles = true;
}

// Check extension configuration
if (!folder.ShouldIndexExtension(".pdf"))
{
    // Add PDF to indexed extensions
    folder.IndexedExtensions = [".pdf", ".json"];
}
```

#### Processing Status Not Updating
**Symptoms**: Files stuck in `ProcessingStarted` stage
**Causes**:
- Exception in processing pipeline not handled
- Missing call to `MarkFileAsProcessedAsync()`
- Incorrect folder name in processing calls

**Solutions**:
```csharp
// Always wrap processing in try-catch
try
{
    await ProcessFileAsync(filePath);
    await _processingManager.MarkFileAsProcessedAsync(folderName, filePath);
}
catch (Exception ex)
{
    await _processingManager.UpdateProcessingStageAsync(
        folderName, filePath, ProcessingStage.ProcessingFailed);
    _logger.LogError(ex, "Processing failed for {FilePath}", filePath);
}
```

#### External Collections Not Found
**Symptoms**: `ExternalIndexManager` can't find registered collections
**Causes**:
- `ExternalIndexes` folder not registered in startup
- Network path not accessible
- Permissions issues

**Solutions**:
```csharp
// Ensure folder is registered
RegisterFolderIfNotExists(new StorageFolder
{
    Name = "ExternalIndexes",
    Type = StorageType.Json,
    Path = "FileIndexing",
    IsShared = true
});

// Check network connectivity
if (!Directory.Exists(@"\\NAS\BankSlips"))
{
    _logger.LogError("Network path not accessible: {Path}", @"\\NAS\BankSlips");
}
```

### Debug Information

#### View Storage Structure
```csharp
var structure = factory.GetDirectoryStructure();
Console.WriteLine(structure);
```

#### Check Index Contents
```csharp
var entries = await _fileIndexManager.GetIndexAsync("FolderName");
foreach (var entry in entries)
{
    Console.WriteLine($"{entry.FilePath}: {entry.ProcessingStage}");
}
```

#### Monitor External Collections
```csharp
var collections = await _externalIndexManager.GetAllCollectionsAsync();
foreach (var collection in collections)
{
    Console.WriteLine($"Collection: {collection.Name}");
    Console.WriteLine($"Path: {collection.ExternalPath}"); 
    Console.WriteLine($"Last Scanned: {collection.LastScanned}");
}
```

---

## Performance Considerations

### File Indexing Performance
- **Large folders**: Consider `IndexCacheLifetime` for performance tuning
- **Network drives**: Use external indexing, scan periodically vs real-time
- **Hash calculation**: SHA256 is CPU-intensive for large files

### Storage Performance
- **Binary vs JSON**: Binary is ~3-5x faster for large objects
- **Backup overhead**: More backups = slower saves, tune `MaxBackupCount`
- **Shared folders**: Consider locking contention with multiple writers

### Memory Usage
- **Index caching**: Indexes loaded into memory, monitor large collections
- **Storage caching**: `EnhancedStorageFactory` caches storage instances
- **External scanning**: Scanning large network folders can use significant memory

---

## Future Enhancements

### Planned Features
- **Full-text indexing** via `IndexContent = true`
- **Search APIs** with `SearchCriteria` class
- **Real-time sync** between machines
- **Compression** for large datasets
- **Distributed indexing** for very large external collections

### Version Compatibility
- All new features are **opt-in** with sensible defaults
- Existing storage continues to work without modification
- Index format versioning for future upgrades
- Migration tools for major version changes

---

*This manual covers IO Manager version 2.x. For questions or issues, check the troubleshooting section or contact the development team.*