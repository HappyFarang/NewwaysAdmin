using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.FileSync.Client;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddLogging(configure => configure.AddConsole());

        // Configure the FileSyncClient
        services.AddSingleton<FileSyncClient>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileSyncClient>>();
            return new FileSyncClient(
                logger,
                "localhost",  // TODO: Make configurable
                5000,        // TODO: Make configurable
                "client1",   // TODO: Make configurable
                Environment.MachineName);
        });

        // Add hosted service to manage the client lifecycle
        services.AddHostedService<FileSyncClientHost>();
    });

var host = builder.Build();
await host.RunAsync();