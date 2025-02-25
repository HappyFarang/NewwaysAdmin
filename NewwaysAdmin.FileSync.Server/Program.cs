using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.FileSync.Server;
using NewwaysAdmin.Shared;
using NewwaysAdmin.Shared.IO.Structure;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
        });

        // Add the storage factory
        services.AddSingleton<EnhancedStorageFactory>();

        // Configure and add the FileSyncServer
        services.AddSingleton<IFileSyncServer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileSyncServer>>();
            var storageFactory = sp.GetRequiredService<EnhancedStorageFactory>();
            return new FileSyncServer(logger, storageFactory, 5000); // Port 5000
        });

        // Add hosted service to manage server lifecycle
        services.AddHostedService<FileSyncServerHost>();
    })
    .Build();

await host.RunAsync();