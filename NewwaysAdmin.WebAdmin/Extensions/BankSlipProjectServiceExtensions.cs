// NewwaysAdmin.WebAdmin/Extensions/BankSlipProjectServiceExtensions.cs

using Microsoft.Extensions.DependencyInjection;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;

namespace NewwaysAdmin.WebAdmin.Extensions;

/// <summary>
/// DI registration for bank slip project processing services
/// </summary>
public static class BankSlipProjectServiceExtensions
{
    /// <summary>
    /// Add bank slip project processing services
    /// </summary>
    public static IServiceCollection AddBankSlipProjectServices(this IServiceCollection services)
    {
        // Parsers (stateless, can be singleton)
        services.AddSingleton<BankSlipFilenameParser>();
        services.AddSingleton<MemoParserService>();

        // Main service (scoped - uses other scoped services)
        services.AddScoped<BankSlipProjectService>();

        // Startup scanner (singleton - uses IServiceProvider for scoped access)
        services.AddSingleton<BankSlipStartupScanner>();

        return services;
    }

    /// <summary>
    /// Run startup scan for unprocessed bank slips.
    /// Call this after app.Build() in Program.cs
    /// </summary>
    public static async Task ScanUnprocessedBankSlipsAsync(this IServiceProvider serviceProvider)
    {
        var scanner = serviceProvider.GetRequiredService<BankSlipStartupScanner>();
        await scanner.ScanAndProcessAsync();
    }
}