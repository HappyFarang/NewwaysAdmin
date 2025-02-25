# Enhanced Storage System Manual

## Table of Contents
1. [Overview](#overview)
2. [Getting Started](#getting-started)
3. [Core Concepts](#core-concepts)
4. [Basic Usage](#basic-usage)
5. [Advanced Features](#advanced-features)
6. [Best Practices](#best-practices)
7. [API Reference](#api-reference)

## Overview

The Enhanced Storage System provides a structured, type-safe way to manage file storage in your application. It features:
- Automatic folder management
- Type-safe data storage
- JSON and Binary storage support
- Shared folder capabilities
- Built-in backup system
- Directory structure tracking

## Getting Started

### Installation

Add the following packages to your project:
```xml
<PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

### Basic Setup

Here's a minimal setup example:

```csharp
// Create a logger
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
});
var logger = loggerFactory.CreateLogger<EnhancedStorageFactory>();

// Create the storage factory
var factory = new EnhancedStorageFactory(logger);

// Register a storage folder
var usersFolder = new StorageFolder
{
    Name = "Users",
    Description = "User data storage",
    Type = StorageType.Json,
    IsShared = false,
    CreatedBy = "MyApp"
};

factory.RegisterFolder(usersFolder);
```

## Core Concepts

### Storage Folders

Storage folders are the basic unit of organization. Each folder has:
- A unique name
- A storage type (JSON/Binary)
- Optional path for nesting
- Sharing settings
- Backup configuration

```csharp
public class StorageFolder
{
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public required StorageType Type { get; set; }
    public string Path { get; set; } = string.Empty;
    public bool IsShared { get; set; }
    public bool CreateBackups { get; set; } = true;
    public int MaxBackupCount { get; set; } = 5;
}
```

### Storage Types

Two storage types are available:
- `StorageType.Json`: For human-readable data storage
- `StorageType.Binary`: For efficient binary data storage

### Shared Folders

Folders can be marked as shared, allowing multiple modules to access them:
```csharp
var sharedFolder = new StorageFolder
{
    Name = "SharedData",
    Description = "Shared data storage",
    Type = StorageType.Json,
    IsShared = true
};
```

## Basic Usage

### Creating Data Storage

```csharp
// Define your data class
public class UserData
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

// Get storage for the data
var userStorage = factory.GetStorage<List<UserData>>("Users");
```

### Saving Data

```csharp
var users = new List<UserData>
{
    new() { Username = "user1", Email = "user1@example.com" }
};

await userStorage.SaveAsync("users-list", users);
```

### Loading Data

```csharp
var loadedUsers = await userStorage.LoadAsync("users-list");
```

## Advanced Features

### Nested Folders

Create nested folder structures using the Path property:
```csharp
var nestedFolder = new StorageFolder
{
    Name = "Reports",
    Path = "Data/Monthly",  // Creates Data/Monthly/Reports
    Type = StorageType.Json
};
```

### Directory Structure

View the complete storage structure:
```csharp
var structure = factory.GetDirectoryStructure();
Console.WriteLine(structure);
```

### Automatic Backups

Backups are created automatically when:
- A file is being overwritten
- The folder has backups enabled
- The backup count is within limits

```csharp
var folderWithBackups = new StorageFolder
{
    Name = "Important",
    CreateBackups = true,
    MaxBackupCount = 5
};
```

## Best Practices

1. **Folder Organization**
   - Group related data in folders
   - Use meaningful folder names and descriptions
   - Keep folder structure shallow when possible

2. **Storage Types**
   - Use JSON for configuration and human-readable data
   - Use Binary for large data sets or performance-critical storage

3. **Shared Folders**
   - Use shared folders for common resources
   - Document shared folder usage
   - Be cautious with shared folder modifications

4. **Error Handling**
   - Always handle potential storage exceptions
   - Implement retry logic for transient failures
   - Log storage operations for debugging

## API Reference

### EnhancedStorageFactory

```csharp
public class EnhancedStorageFactory
{
    public EnhancedStorageFactory(ILogger logger);
    public void RegisterFolder(StorageFolder folder);
    public IDataStorage<T> GetStorage<T>(string folderName) where T : class, new();
    public string GetDirectoryStructure();
}
```

### StorageFolder

```csharp
public class StorageFolder
{
    public required string Name { get; set; }
    public string Description { get; set; }
    public required StorageType Type { get; set; }
    public string Path { get; set; }
    public bool IsShared { get; set; }
    public bool CreateBackups { get; set; }
    public int MaxBackupCount { get; set; }
}
```

### IDataStorage<T>

```csharp
public interface IDataStorage<T> where T : class, new()
{
    Task<T> LoadAsync(string identifier);
    Task SaveAsync(string identifier, T data);
    Task<bool> ExistsAsync(string identifier);
    Task<IEnumerable<string>> ListIdentifiersAsync();
    Task DeleteAsync(string identifier);
}
```

---

This manual will be updated as new features are added to the system.
