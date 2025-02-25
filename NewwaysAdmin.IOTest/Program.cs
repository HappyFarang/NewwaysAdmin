using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;

namespace NewwaysAdmin.IO.Test
{
    public class TestUserData
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime Created { get; set; }
    }

    public class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting IO Manager Test...");

            try
            {
                // Setup logging
                using var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Debug);
                });

                // Initialize the IO system
                Console.WriteLine("\nInitializing IO System...");
                var logger = loggerFactory.CreateLogger<IOManager>();
                var configLoader = new IOConfigLoader(loggerFactory.CreateLogger<IOConfigLoader>());
                var config = await configLoader.LoadConfigAsync();

                Console.WriteLine($"Using configuration:");
                Console.WriteLine($"- Local Base: {config.LocalBaseFolder}");
                Console.WriteLine($"- Server Definitions: {config.ServerDefinitionsPath}");

                var ioManager = new IOManager(logger, new IOManagerOptions
                {
                    LocalBaseFolder = config.LocalBaseFolder,
                    ServerDefinitionsPath = config.ServerDefinitionsPath,
                    ApplicationName = "TestApp"
                });

                // Test data operations
                Console.WriteLine("\nTesting data operations...");

                // Test user storage
                Console.WriteLine("\nTesting User Storage:");
                var userStorage = await ioManager.GetStorageAsync<List<TestUserData>>("Users");
                var testUsers = new List<TestUserData>
                {
                    new() { Username = "test1", Email = "test1@test.com", Created = DateTime.Now },
                    new() { Username = "test2", Email = "test2@test.com", Created = DateTime.Now }
                };

                Console.WriteLine("Saving test users...");
                await userStorage.SaveAsync("testusers", testUsers);

                Console.WriteLine("Loading test users...");
                var loadedUsers = await userStorage.LoadAsync("testusers");
                Console.WriteLine($"Loaded {loadedUsers.Count} users:");
                foreach (var user in loadedUsers)
                {
                    Console.WriteLine($"- {user.Username} ({user.Email})");
                }

                // Test log storage
                Console.WriteLine("\nTesting Log Storage:");
                var logStorage = await ioManager.GetStorageAsync<List<string>>("Logs");
                var testLogs = new List<string>
                {
                    "Test log entry 1",
                    "Test log entry 2"
                };

                Console.WriteLine("Saving test logs...");
                await logStorage.SaveAsync("testlog", testLogs);

                Console.WriteLine("Loading test logs...");
                var loadedLogs = await logStorage.LoadAsync("testlog");
                Console.WriteLine("Loaded logs:");
                foreach (var log in loadedLogs)
                {
                    Console.WriteLine($"- {log}");
                }

                Console.WriteLine("\nTest completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                    Console.WriteLine($"Inner Stack trace: {ex.InnerException.StackTrace}");
                }
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}