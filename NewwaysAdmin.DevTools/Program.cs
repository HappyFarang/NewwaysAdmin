using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NewwaysAdmin.DevTools
{
    class Program
    {
        private const string BaseDirectory = @"C:\NewwaysData"; // Change this to match your environment
        private const string NetworkBaseDirectory = @"X:\NewwaysAdmin"; // Change this to match your server path
        private const string ConfigFolder = "Config";
        private const string DefinitionsFolder = "Definitions";
        private const string RegistryFile = "storage-registry.json";

        static async Task Main(string[] args)
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("IO Manager Developer Utility");
                Console.WriteLine("===========================");
                Console.WriteLine("1. List registered folders");
                Console.WriteLine("2. Clean up duplicated folder definitions");
                Console.WriteLine("3. Reset all IO configurations");
                Console.WriteLine("4. Find running IO processes");
                Console.WriteLine("5. View folder structure on disk");
                Console.WriteLine("6. Compare local/server configurations");
                Console.WriteLine("Q. Quit");
                Console.WriteLine();
                Console.Write("Enter your choice: ");

                var choice = Console.ReadKey().KeyChar;
                Console.WriteLine("\n");

                try
                {
                    switch (choice)
                    {
                        case '1':
                            await ListRegisteredFolders();
                            break;
                        case '2':
                            await CleanupDuplicateFolders();
                            break;
                        case '3':
                            await ResetIOConfigurations();
                            break;
                        case '4':
                            FindRunningIOProcesses();
                            break;
                        case '5':
                            ViewFolderStructure();
                            break;
                        case '6':
                            await CompareConfigurations();
                            break;
                        case 'q':
                        case 'Q':
                            return;
                        default:
                            Console.WriteLine("Invalid choice");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.ResetColor();
                }

                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
        }

        private static async Task ListRegisteredFolders()
        {
            Console.WriteLine("Registered Folders:");
            Console.WriteLine("===================");

            var registryPath = Path.Combine(BaseDirectory, ConfigFolder, RegistryFile);
            if (!File.Exists(registryPath))
            {
                Console.WriteLine("No registry file found. No folders are registered.");
                return;
            }

            var json = await File.ReadAllTextAsync(registryPath);
            using var document = JsonDocument.Parse(json);

            var root = document.RootElement;
            if (root.TryGetProperty("RegisteredFolders", out var foldersElement))
            {
                int count = 0;
                foreach (var folder in foldersElement.EnumerateArray())
                {
                    count++;
                    string name = folder.GetProperty("Name").GetString() ?? "(unknown)";
                    string path = folder.GetProperty("Path").GetString() ?? "(no path)";
                    string type = folder.GetProperty("Type").GetInt32() == 1 ? "Json" : "Binary";
                    bool isShared = folder.GetProperty("IsShared").GetBoolean();

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"{count}. {name}");
                    Console.ResetColor();
                    Console.WriteLine($"   Path: {path}");
                    Console.WriteLine($"   Type: {type}");
                    Console.WriteLine($"   Shared: {isShared}");

                    if (folder.TryGetProperty("CreatedBy", out var createdBy))
                        Console.WriteLine($"   Created by: {createdBy.GetString()}");

                    Console.WriteLine();

                    // Check for potential duplicates
                    if (name.Contains("_") && name.Split('_').Length > 2)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"   ⚠️ Possible duplicate naming pattern detected");
                        Console.ResetColor();
                    }
                }

                Console.WriteLine($"Total folders registered: {count}");
            }
            else
            {
                Console.WriteLine("Invalid registry format. No folders found.");
            }

            // Also list definition files on disk
            Console.WriteLine("\nFolder definition files on disk:");
            Console.WriteLine("==============================");

            string definitionsPath = Path.Combine(BaseDirectory, ConfigFolder, DefinitionsFolder);
            if (Directory.Exists(definitionsPath))
            {
                var apps = Directory.GetDirectories(definitionsPath);
                if (apps.Length == 0)
                {
                    Console.WriteLine("No application folders found in definitions directory.");
                }

                foreach (var appDir in apps)
                {
                    var appName = Path.GetFileName(appDir);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Application: {appName}");
                    Console.ResetColor();

                    var definitionFiles = Directory.GetFiles(appDir, "*.json");
                    if (definitionFiles.Length == 0)
                    {
                        Console.WriteLine("  No definition files found");
                        continue;
                    }

                    foreach (var file in definitionFiles)
                    {
                        var fileName = Path.GetFileName(file);
                        var fileContent = await File.ReadAllTextAsync(file);
                        using var fileDoc = JsonDocument.Parse(fileContent);

                        var folderName = fileDoc.RootElement.GetProperty("Name").GetString();
                        Console.WriteLine($"  {fileName} => {folderName}");

                        // Check for potential issues
                        if (folderName.Contains($"{appName}_{appName}"))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"    ⚠️ Double application name prefix detected: {folderName}");
                            Console.ResetColor();
                        }
                    }
                    Console.WriteLine();
                }
            }
            else
            {
                Console.WriteLine("Definitions directory not found.");
            }
        }

        private static async Task CleanupDuplicateFolders()
        {
            Console.WriteLine("Cleaning up duplicated folder definitions");
            Console.WriteLine("=========================================");

            // Parse the registry file
            var registryPath = Path.Combine(BaseDirectory, ConfigFolder, RegistryFile);
            if (!File.Exists(registryPath))
            {
                Console.WriteLine("No registry file found. Nothing to clean up.");
                return;
            }

            // Create a backup
            string backupPath = $"{registryPath}.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            File.Copy(registryPath, backupPath);
            Console.WriteLine($"Created backup at: {backupPath}");

            // Read and parse the registry
            var json = await File.ReadAllTextAsync(registryPath);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var registry = JsonSerializer.Deserialize<StorageRegistry>(json, options);

            if (registry == null || registry.RegisteredFolders == null)
            {
                Console.WriteLine("Invalid registry format. Cannot process.");
                return;
            }

            var originalCount = registry.RegisteredFolders.Count;
            var duplicatePatterns = new List<(string Original, string Duplicate)>();

            // Find duplicates with pattern AppName_AppName_Xyz
            foreach (var folder in registry.RegisteredFolders.ToList())
            {
                var parts = folder.Name.Split('_');
                if (parts.Length >= 3)
                {
                    // Check if we have a pattern like "App_App_Xyz"
                    if (parts[0] == parts[1])
                    {
                        // Construct what the original name should have been
                        var originalName = parts[0] + "_" + string.Join("_", parts.Skip(2));

                        duplicatePatterns.Add((originalName, folder.Name));
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Found duplicate pattern: {folder.Name}");
                        Console.WriteLine($"  Should be: {originalName}");
                        Console.ResetColor();
                    }
                }
            }

            if (!duplicatePatterns.Any())
            {
                Console.WriteLine("No duplicate folder patterns found in registry.");
                return;
            }

            Console.WriteLine($"Found {duplicatePatterns.Count} potential duplicates.");
            Console.Write("Do you want to proceed with cleanup? (y/n): ");
            if (Console.ReadKey().KeyChar.ToString().ToLower() != "y")
            {
                Console.WriteLine("\nCleanup canceled.");
                return;
            }
            Console.WriteLine();

            // Perform the cleanup
            int removedCount = 0;
            foreach (var (original, duplicate) in duplicatePatterns)
            {
                // Find and remove the duplicate from the registry
                var dupFolder = registry.RegisteredFolders.FirstOrDefault(f => f.Name == duplicate);
                if (dupFolder != null)
                {
                    registry.RegisteredFolders.Remove(dupFolder);
                    removedCount++;
                    Console.WriteLine($"Removed: {duplicate}");

                    // Check if we need to rename file definitions
                    string appName = duplicate.Split('_')[0];
                    var definitionFile = Path.Combine(BaseDirectory, ConfigFolder, DefinitionsFolder, appName, $"{duplicate}.json");
                    if (File.Exists(definitionFile))
                    {
                        Console.WriteLine($"Found definition file: {definitionFile}");
                        File.Delete(definitionFile);
                        Console.WriteLine($"Deleted duplicate definition file.");
                    }
                }
            }

            // Save the updated registry
            var updatedJson = JsonSerializer.Serialize(registry, options);
            await File.WriteAllTextAsync(registryPath, updatedJson);

            Console.WriteLine($"\nCompleted cleanup. Removed {removedCount} duplicate entries.");
            Console.WriteLine($"Original count: {originalCount}, New count: {registry.RegisteredFolders.Count}");
            Console.WriteLine("Please restart any applications to ensure they use the updated configuration.");
        }

        private static async Task 
            ResetIOConfigurations()
        {
            Console.WriteLine("Reset IO Configurations");
            Console.WriteLine("======================");
            Console.WriteLine("This will delete the storage registry file and all folder definitions.");
            Console.WriteLine("This is a destructive operation and will require reconfiguring all applications.");
            Console.WriteLine();

            Console.Write("Are you sure you want to proceed? (y/n): ");
            if (Console.ReadKey().KeyChar.ToString().ToLower() != "y")
            {
                Console.WriteLine("\nReset canceled.");
                return;
            }
            Console.WriteLine();

            // Create backup directory
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupDir = Path.Combine(BaseDirectory, "ConfigBackup", timestamp);
            Directory.CreateDirectory(backupDir);

            // Backup the registry file
            var registryPath = Path.Combine(BaseDirectory, ConfigFolder, RegistryFile);
            if (File.Exists(registryPath))
            {
                string backupFile = Path.Combine(backupDir, RegistryFile);
                File.Copy(registryPath, backupFile);
                File.Delete(registryPath);
                Console.WriteLine($"Deleted registry file: {registryPath}");
                Console.WriteLine($"Created backup at: {backupFile}");
            }
            else
            {
                Console.WriteLine("Registry file not found. Nothing to delete.");
            }

            // Backup and delete definition files
            string definitionsPath = Path.Combine(BaseDirectory, ConfigFolder, DefinitionsFolder);
            if (Directory.Exists(definitionsPath))
            {
                // Copy entire directory structure
                CopyDirectory(definitionsPath, Path.Combine(backupDir, DefinitionsFolder));

                // Now delete the folders
                var folders = Directory.GetDirectories(definitionsPath);
                foreach (var folder in folders)
                {
                    Directory.Delete(folder, true);
                    Console.WriteLine($"Deleted definitions folder: {folder}");
                }

                Console.WriteLine($"Backed up all definition files to: {Path.Combine(backupDir, DefinitionsFolder)}");
            }
            else
            {
                Console.WriteLine("Definitions directory not found. Nothing to delete.");
            }

            // Cleanup sync tracking files
            var syncTrackingPath = Path.Combine(BaseDirectory, "Config", ".sync-tracking");
            if (File.Exists(syncTrackingPath))
            {
                File.Copy(syncTrackingPath, Path.Combine(backupDir, ".sync-tracking"));
                File.Delete(syncTrackingPath);
                Console.WriteLine($"Deleted sync tracking file: {syncTrackingPath}");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nIO Configuration reset complete!");
            Console.WriteLine($"All configuration backed up to: {backupDir}");
            Console.ResetColor();
        }

        private static void FindRunningIOProcesses()
        {
            Console.WriteLine("Finding Running IO Processes");
            Console.WriteLine("===========================");

            // Get all running processes
            var processes = Process.GetProcesses();
            var neways = processes.Where(p => p.ProcessName.Contains("Newways") ||
                                           p.ProcessName.Contains("IO") ||
                                           p.ProcessName.Contains("FileSync")).ToList();

            if (!neways.Any())
            {
                Console.WriteLine("No relevant processes found running.");
                return;
            }

            Console.WriteLine($"Found {neways.Count} potentially relevant processes:");
            Console.WriteLine();

            foreach (var process in neways)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Process: {process.ProcessName} (ID: {process.Id})");
                Console.ResetColor();

                try
                {
                    Console.WriteLine($"  Window Title: {process.MainWindowTitle}");
                    Console.WriteLine($"  Start Time: {process.StartTime}");
                    Console.WriteLine($"  Memory: {process.WorkingSet64 / 1024 / 1024:N2} MB");

                    string filePath = process.MainModule?.FileName ?? "Unknown";
                    Console.WriteLine($"  Path: {filePath}");

                    Console.Write("  Do you want to terminate this process? (y/n): ");
                    if (Console.ReadKey().KeyChar.ToString().ToLower() == "y")
                    {
                        process.Kill();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\n  Process {process.Id} terminated successfully.");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine();
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Error accessing process details: {ex.Message}");
                    Console.ResetColor();
                }

                Console.WriteLine();
            }
        }

        private static void ViewFolderStructure()
        {
            Console.WriteLine("IO Manager Folder Structure");
            Console.WriteLine("==========================");

            Console.WriteLine("1. View Local Structure");
            Console.WriteLine("2. View Server Structure");
            Console.WriteLine("3. View Both (Compare)");
            Console.Write("Choose an option: ");

            var choice = Console.ReadKey().KeyChar;
            Console.WriteLine("\n");

            switch (choice)
            {
                case '1':
                    if (!Directory.Exists(BaseDirectory))
                    {
                        Console.WriteLine($"Local base directory not found: {BaseDirectory}");
                        return;
                    }
                    Console.WriteLine("LOCAL FOLDER STRUCTURE:");
                    Console.WriteLine("======================");
                    DisplayFolderStructure(BaseDirectory, 0);
                    break;

                case '2':
                    var networkBaseFolder = NetworkBaseDirectory;
                    if (!Directory.Exists(networkBaseFolder))
                    {
                        Console.WriteLine($"Server base directory not found: {networkBaseFolder}");
                        return;
                    }
                    Console.WriteLine("SERVER FOLDER STRUCTURE:");
                    Console.WriteLine("=======================");
                    DisplayFolderStructure(networkBaseFolder, 0);
                    break;

                case '3':
                    var networkFolder = NetworkBaseDirectory;
                    Console.WriteLine("COMPARING LOCAL AND SERVER STRUCTURES:");
                    Console.WriteLine("===================================");

                    if (!Directory.Exists(BaseDirectory))
                    {
                        Console.WriteLine($"Local base directory not found: {BaseDirectory}");
                    }
                    else
                    {
                        Console.WriteLine("LOCAL FOLDER STRUCTURE:");
                        Console.WriteLine("======================");
                        DisplayFolderStructure(BaseDirectory, 0);
                    }

                    Console.WriteLine("\n");

                    if (!Directory.Exists(networkFolder))
                    {
                        Console.WriteLine($"Server base directory not found: {networkFolder}");
                    }
                    else
                    {
                        Console.WriteLine("SERVER FOLDER STRUCTURE:");
                        Console.WriteLine("=======================");
                        DisplayFolderStructure(networkFolder, 0);
                    }
                    break;

                default:
                    Console.WriteLine("Invalid choice. Showing local structure:");
                    if (Directory.Exists(BaseDirectory))
                    {
                        DisplayFolderStructure(BaseDirectory, 0);
                    }
                    break;
            }
        }

        private static void DisplayFolderStructure(string path, int level)
        {
            // Print the current directory
            string indent = new string(' ', level * 2);
            string dirName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(dirName)) // For the root directory
                dirName = path;

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"{indent}📂 {dirName}");
            Console.ResetColor();

            try
            {
                // Get subdirectories
                foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                {
                    DisplayFolderStructure(dir, level + 1);
                }

                // Get files in the current directory
                var files = Directory.GetFiles(path).OrderBy(f => f);

                // Always show all files
                foreach (var file in files)
                {
                    // Display file size
                    var fileInfo = new FileInfo(file);
                    string fileSize = FormatFileSize(fileInfo.Length);

                    // Display last modified time
                    var lastModified = File.GetLastWriteTime(file);
                    string modifiedTime = lastModified.ToString("yyyy-MM-dd HH:mm:ss");

                    // Set color based on file type
                    if (Path.GetExtension(file).ToLower() == ".json")
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    }
                    else if (Path.GetExtension(file).ToLower() == ".bak")
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                    }

                    Console.WriteLine($"{indent}  📄 {Path.GetFileName(file)} [{fileSize}] - {modifiedTime}");

                    // If it's a JSON file, try to show a summary of contents
                    if (Path.GetExtension(file).ToLower() == ".json" && fileInfo.Length < 100000) // Skip large files
                    {
                        try
                        {
                            var jsonContent = File.ReadAllText(file);
                            using var document = JsonDocument.Parse(jsonContent);

                            // Show basic info for folder definitions
                            if (jsonContent.Contains("\"Name\"") && jsonContent.Contains("\"Path\""))
                            {
                                var root = document.RootElement;
                                if (root.TryGetProperty("Name", out var nameElement))
                                {
                                    string folderName = nameElement.GetString() ?? "(unknown)";
                                    string folderPath = "(no path)";

                                    if (root.TryGetProperty("Path", out var pathElement))
                                    {
                                        folderPath = pathElement.GetString() ?? "(no path)";
                                    }

                                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                                    Console.WriteLine($"{indent}    → Folder: {folderName}, Path: {folderPath}");
                                    Console.ResetColor();
                                }
                            }
                        }
                        catch
                        {
                            // Silently ignore JSON parsing errors
                        }
                    }

                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{indent}  Error: {ex.Message}");
                Console.ResetColor();
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;

            while (number > 1024 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }

            return $"{number:N2} {suffixes[counter]}";
        }

        private static async Task CompareConfigurations()
        {
            Console.WriteLine("Comparing Local and Server Configurations");
            Console.WriteLine("========================================");

            // Check if directories exist
            if (!Directory.Exists(BaseDirectory))
            {
                Console.WriteLine($"Local base directory not found: {BaseDirectory}");
                return;
            }

            if (!Directory.Exists(NetworkBaseDirectory))
            {
                Console.WriteLine($"Server base directory not found: {NetworkBaseDirectory}");
                return;
            }

            // Compare registry files
            var localRegistryPath = Path.Combine(BaseDirectory, ConfigFolder, RegistryFile);
            var serverRegistryPath = Path.Combine(NetworkBaseDirectory, ConfigFolder, RegistryFile);

            if (File.Exists(localRegistryPath) && File.Exists(serverRegistryPath))
            {
                Console.WriteLine("Comparing registry files...");

                var localRegistry = await LoadRegistry(localRegistryPath);
                var serverRegistry = await LoadRegistry(serverRegistryPath);

                if (localRegistry != null && serverRegistry != null)
                {
                    Console.WriteLine($"Local registry has {localRegistry.RegisteredFolders.Count} folders");
                    Console.WriteLine($"Server registry has {serverRegistry.RegisteredFolders.Count} folders");

                    // Compare folder entries
                    var localFolders = localRegistry.RegisteredFolders.ToDictionary(f => f.Name);
                    var serverFolders = serverRegistry.RegisteredFolders.ToDictionary(f => f.Name);

                    Console.WriteLine("\nFolders in server but not in local:");
                    var serverOnly = serverFolders.Keys.Except(localFolders.Keys).ToList();
                    if (serverOnly.Any())
                    {
                        foreach (var folder in serverOnly)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"  - {folder}");
                            Console.ResetColor();
                        }
                    }
                    else
                    {
                        Console.WriteLine("  None");
                    }

                    Console.WriteLine("\nFolders in local but not in server:");
                    var localOnly = localFolders.Keys.Except(serverFolders.Keys).ToList();
                    if (localOnly.Any())
                    {
                        foreach (var folder in localOnly)
                        {
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            Console.WriteLine($"  - {folder}");
                            Console.ResetColor();
                        }
                    }
                    else
                    {
                        Console.WriteLine("  None");
                    }

                    // Check for duplicated folder patterns
                    Console.WriteLine("\nChecking for duplicated folder patterns...");
                    int count = 0;
                    foreach (var folder in localRegistry.RegisteredFolders)
                    {
                        var parts = folder.Name.Split('_');
                        if (parts.Length >= 3 && parts[0] == parts[1])
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  - Found duplicate pattern: {folder.Name}");
                            Console.ResetColor();
                            count++;
                        }
                    }

                    if (count == 0)
                    {
                        Console.WriteLine("  No duplicate patterns found");
                    }
                }
            }
            else
            {
                Console.WriteLine("Registry files not found for comparison");
            }

            // Compare folder definition files
            Console.WriteLine("\nComparing folder definition files...");

            var localDefsPath = Path.Combine(BaseDirectory, ConfigFolder, DefinitionsFolder);
            var serverDefsPath = Path.Combine(NetworkBaseDirectory, DefinitionsFolder);

            if (Directory.Exists(localDefsPath) && Directory.Exists(serverDefsPath))
            {
                var localApps = Directory.GetDirectories(localDefsPath)
                    .Select(d => Path.GetFileName(d)).ToList();

                var serverApps = Directory.GetDirectories(serverDefsPath)
                    .Select(d => Path.GetFileName(d)).ToList();

                Console.WriteLine("\nApplication folders in server but not in local:");
                var serverAppOnly = serverApps.Except(localApps).ToList();
                if (serverAppOnly.Any())
                {
                    foreach (var app in serverAppOnly)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"  - {app}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.WriteLine("  None");
                }

                Console.WriteLine("\nApplication folders in local but not in server:");
                var localAppOnly = localApps.Except(serverApps).ToList();
                if (localAppOnly.Any())
                {
                    foreach (var app in localAppOnly)
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"  - {app}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.WriteLine("  None");
                }

                // For common apps, compare definitions
                var commonApps = localApps.Intersect(serverApps).ToList();
                if (commonApps.Any())
                {
                    Console.WriteLine("\nComparing definitions for common applications:");

                    foreach (var app in commonApps)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\nApplication: {app}");
                        Console.ResetColor();

                        var localAppPath = Path.Combine(localDefsPath, app);
                        var serverAppPath = Path.Combine(serverDefsPath, app);

                        var localDefs = Directory.GetFiles(localAppPath, "*.json")
                            .Select(f => Path.GetFileName(f)).ToList();

                        var serverDefs = Directory.GetFiles(serverAppPath, "*.json")
                            .Select(f => Path.GetFileName(f)).ToList();

                        Console.WriteLine("  Definitions in server but not in local:");
                        var serverDefOnly = serverDefs.Except(localDefs).ToList();
                        if (serverDefOnly.Any())
                        {
                            foreach (var def in serverDefOnly)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"    - {def}");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.WriteLine("    None");
                        }

                        Console.WriteLine("  Definitions in local but not in server:");
                        var localDefOnly = localDefs.Except(serverDefs).ToList();
                        if (localDefOnly.Any())
                        {
                            foreach (var def in localDefOnly)
                            {
                                Console.ForegroundColor = ConsoleColor.Magenta;
                                Console.WriteLine($"    - {def}");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.WriteLine("    None");
                        }

                        // For common definitions, compare last modified times
                        var commonDefs = localDefs.Intersect(serverDefs).ToList();
                        if (commonDefs.Any())
                        {
                            Console.WriteLine("  Common definitions with different timestamps:");
                            int diffCount = 0;

                            foreach (var def in commonDefs)
                            {
                                var localFile = Path.Combine(localAppPath, def);
                                var serverFile = Path.Combine(serverAppPath, def);

                                var localTime = File.GetLastWriteTimeUtc(localFile);
                                var serverTime = File.GetLastWriteTimeUtc(serverFile);

                                if (localTime != serverTime)
                                {
                                    Console.Write($"    - {def}: ");

                                    if (localTime > serverTime)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Blue;
                                        Console.WriteLine($"Local is newer by {(localTime - serverTime).TotalMinutes:N1} minutes");
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                        Console.WriteLine($"Server is newer by {(serverTime - localTime).TotalMinutes:N1} minutes");
                                    }
                                    Console.ResetColor();

                                    diffCount++;
                                }
                            }

                            if (diffCount == 0)
                            {
                                Console.WriteLine("    None - All common definitions are in sync");
                            }
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("Definitions directories not found for comparison");
            }
        }

        private static async Task<StorageRegistry?> LoadRegistry(string path)
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<StorageRegistry>(json, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading registry from {path}: {ex.Message}");
                return null;
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            // Create the destination directory if it doesn't exist
            Directory.CreateDirectory(destinationDir);

            // Copy all the files
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            // Copy all subdirectories recursively
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destDir = Path.Combine(destinationDir, Path.GetFileName(dir));
                CopyDirectory(dir, destDir);
            }
        }
    }

    // Class to deserialize the storage registry
    public class StorageRegistry
    {
        public List<StorageFolder> RegisteredFolders { get; set; } = new List<StorageFolder>();
    }

    public class StorageFolder
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Type { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool IsShared { get; set; }
        public bool CreateBackups { get; set; } = true;
        public int MaxBackupCount { get; set; } = 5;
        public DateTime Created { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
    }
}