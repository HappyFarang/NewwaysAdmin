// NewwaysAdmin.GoogleSheets/Extensions/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.GoogleSheets.Interfaces;
using NewwaysAdmin.GoogleSheets.Layouts;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.GoogleSheets.Services;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.GoogleSheets.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add Google Sheets services to the service collection
        /// </summary>
        public static IServiceCollection AddGoogleSheetsServices(
            this IServiceCollection services,
            GoogleSheetsConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            // Register the configuration
            services.AddSingleton(config);

            // Register the core Google Sheets service
            services.AddScoped<GoogleSheetsService>();

            // Note: UserSheetConfigService will be registered in the main application
            // where storage dependencies are available

            // Register the layout registry
            services.AddSingleton<ISheetLayoutRegistry, SheetLayoutRegistry>();

            return services;
        }

        /// <summary>
        /// Register a specific sheet layout
        /// </summary>
        public static IServiceCollection AddSheetLayout<T>(
            this IServiceCollection services,
            ISheetLayout<T> layout)
        {
            // Register the layout as a singleton - both as concrete type and interface
            services.AddSingleton(layout);
            services.AddSingleton<ISheetLayout<T>>(layout);

            // Register it with the registry (this will be called when the registry is created)
            services.Configure<SheetLayoutRegistrationOptions>(options =>
            {
                options.LayoutRegistrations.Add(serviceProvider =>
                {
                    var registry = serviceProvider.GetRequiredService<ISheetLayoutRegistry>();
                    registry.RegisterLayout(layout);
                });
            });

            return services;
        }

        /// <summary>
        /// Add Bank Slip specific services
        /// </summary>
        public static IServiceCollection AddBankSlipGoogleSheetsServices(this IServiceCollection services)
        {
            // Register the bank slip layout
            services.AddSheetLayout<BankSlipData>(new BankSlipSheetLayout());

            // Register the bank slip export service
            services.AddScoped<BankSlipExportService>();

            return services;
        }
    }

    /// <summary>
    /// Layout registry implementation
    /// </summary>
    internal class SheetLayoutRegistry : ISheetLayoutRegistry
    {
        private readonly Dictionary<Type, object> _layouts = new();
        private readonly ILogger<SheetLayoutRegistry> _logger;

        public SheetLayoutRegistry(ILogger<SheetLayoutRegistry> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;

            // Apply any registered layouts
            var options = serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<SheetLayoutRegistrationOptions>>();
            if (options?.Value?.LayoutRegistrations != null)
            {
                foreach (var registration in options.Value.LayoutRegistrations)
                {
                    try
                    {
                        registration(serviceProvider);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error registering sheet layout");
                    }
                }
            }
        }

        public void RegisterLayout<T>(ISheetLayout<T> layout)
        {
            _layouts[typeof(T)] = layout;
            _logger.LogInformation("Registered sheet layout: {LayoutName} for type {TypeName}",
                layout.LayoutName, typeof(T).Name);
        }

        public ISheetLayout<T>? GetLayout<T>()
        {
            if (_layouts.TryGetValue(typeof(T), out var layout))
            {
                return layout as ISheetLayout<T>;
            }
            return null;
        }

        public IEnumerable<string> GetRegisteredLayouts()
        {
            return _layouts.Values.OfType<ISheetLayout<object>>().Select(l => l.LayoutName);
        }
    }

    /// <summary>
    /// Options for layout registration
    /// </summary>
    internal class SheetLayoutRegistrationOptions
    {
        public List<Action<IServiceProvider>> LayoutRegistrations { get; } = new();
    }
}