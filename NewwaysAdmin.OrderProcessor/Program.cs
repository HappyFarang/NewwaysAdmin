using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.OrderProcessor
{
    class Program
    {
        private static readonly CancellationTokenSource _cancellationTokenSource = new();

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        static async Task Main(string[] args)
        {
            try
            {
                // Hide console window unless in debug/test mode
                bool isTestMode = args.Length > 0 && args[0].ToLower() == "--test";
                var handle = GetConsoleWindow();
                ShowWindow(handle, isTestMode ? SW_SHOW : SW_HIDE);

                // Create host builder
                var builder = Host.CreateDefaultBuilder(args)
                    .ConfigureServices((hostContext, services) =>
                    {
                        // Add logging
                        services.AddLogging(builder => builder.AddConsole());

                        // Add all OrderProcessor services
                        services.AddOrderProcessor();
                    });

                var host = builder.Build();

                // Initialize services
                using (var scope = host.Services.CreateScope())
                {
                    await OrderProcessorSetup.InitializeServicesAsync(scope.ServiceProvider);
                    var processLogger = scope.ServiceProvider.GetRequiredService<OrderProcessorLogger>();
                    await processLogger.LogAsync("Application started. Press Ctrl+C to exit.");

                    // Handle shutdown
                    Console.CancelKeyPress += (s, e) =>
                    {
                        e.Cancel = true;
                        _cancellationTokenSource.Cancel();
                    };

                    // Start the host
                    await host.RunAsync(_cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Application shutting down...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                throw;
            }
        }
    }
}