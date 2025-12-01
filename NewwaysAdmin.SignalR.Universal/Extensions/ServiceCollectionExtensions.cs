// File: NewwaysAdmin.SignalR.Universal/Extensions/ServiceCollectionExtensions.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SignalR.Universal.Hubs;
using NewwaysAdmin.SignalR.Universal.Services;

namespace NewwaysAdmin.SignalR.Universal.Extensions
{
    /// <summary>
    /// Extension methods for easy registration of Universal SignalR services
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add Universal SignalR services to the DI container
        /// </summary>
        public static IServiceCollection AddUniversalSignalR(this IServiceCollection services, Action<UniversalSignalROptions>? configure = null)
        {
            var options = new UniversalSignalROptions();
            configure?.Invoke(options);

            // Register core SignalR
            services.AddSignalR(hubOptions =>
            {
                hubOptions.EnableDetailedErrors = options.EnableDetailedErrors;
                hubOptions.ClientTimeoutInterval = options.ClientTimeoutInterval;
                hubOptions.HandshakeTimeout = options.HandshakeTimeout;
                hubOptions.KeepAliveInterval = options.KeepAliveInterval;
                hubOptions.MaximumReceiveMessageSize = options.MaximumReceiveMessageSize;
            });

            // Register Universal SignalR services
            services.AddSingleton<ConnectionManager>();
            services.AddSingleton<AppMessageRouter>();

            // Register cleanup service if enabled
            if (options.EnableConnectionCleanup)
            {
                services.AddHostedService<ConnectionCleanupService>();
            }

            return services;
        }

        /// <summary>
        /// Register an app-specific message handler
        /// </summary>
        public static IServiceCollection AddMessageHandler<T>(this IServiceCollection services, string appName)
                                                              where T : class, IAppMessageHandler
        {
            services.AddSingleton<T>();  // <-- Change from AddScoped to AddSingleton

            // Register handler in router during startup
            services.AddSingleton<IHostedService>(provider =>
                new MessageHandlerRegistrationService<T>(provider, appName));

            return services;
        }

        /// <summary>
        /// Map Universal SignalR hub endpoints
        /// </summary>
        public static void MapUniversalSignalR(this IEndpointRouteBuilder endpoints, string pattern = "/hubs/universal")
        {
            endpoints.MapHub<UniversalCommHub>(pattern);
        }
    }

    /// <summary>
    /// Configuration options for Universal SignalR
    /// </summary>
    public class UniversalSignalROptions
    {
        public bool EnableDetailedErrors { get; set; } = true;
        public TimeSpan ClientTimeoutInterval { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
        public long MaximumReceiveMessageSize { get; set; } = 1024 * 1024; // 1MB
        public bool EnableConnectionCleanup { get; set; } = true;
        public TimeSpan ConnectionCleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan MaxConnectionAge { get; set; } = TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Background service to register message handlers
    /// </summary>
    internal class MessageHandlerRegistrationService<T> : IHostedService where T : class, IAppMessageHandler
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly string _appName;

        public MessageHandlerRegistrationService(IServiceProvider serviceProvider, string appName)
        {
            _serviceProvider = serviceProvider;
            _appName = appName;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var router = _serviceProvider.GetRequiredService<AppMessageRouter>();
            router.RegisterHandler<T>(_appName);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Background service to cleanup stale connections
    /// </summary>
    internal class ConnectionCleanupService : BackgroundService
    {
        private readonly ConnectionManager _connectionManager;
        private readonly UniversalSignalROptions _options;
        private readonly ILogger<ConnectionCleanupService> _logger;

        public ConnectionCleanupService(
            ConnectionManager connectionManager,
            ILogger<ConnectionCleanupService> logger)
        {
            _connectionManager = connectionManager;
            _options = new UniversalSignalROptions(); // Use defaults
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cleanedCount = _connectionManager.CleanupStaleConnections(_options.MaxConnectionAge);
                    if (cleanedCount > 0)
                    {
                        _logger.LogInformation("Connection cleanup completed: {CleanedCount} stale connections removed", cleanedCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during connection cleanup");
                }

                await Task.Delay(_options.ConnectionCleanupInterval, stoppingToken);
            }
        }
    }
}