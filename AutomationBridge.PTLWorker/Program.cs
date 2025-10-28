using AutomationBridge.PTLWorker.Data;
using AutomationBridge.PTLWorker.Logic;
using AutomationBridge.PTLWorker.Services;
using AutomationBridge.PTLWorker.Settings;
using Microsoft.Data.SqlClient;
using Serilog;
using System.Data;

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .UseSerilog((context, services, configuration) =>
    {
        var baseDir = AppContext.BaseDirectory;
        var logDir = Path.Combine(baseDir, "logs");
        Directory.CreateDirectory(logDir);

        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDir, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .Enrich.FromLogContext();
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<PTLSettings>(context.Configuration.GetSection("PTLSettings"));

        services.AddSingleton<PTLRepository>();
        services.AddSingleton<PTLFrameBuilder>();
        services.AddSingleton<PTLResponseParser>();
        services.AddHostedService<PTLWorker>();
    })
    .Build();

await host.RunAsync();