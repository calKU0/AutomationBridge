using AutomationBridge.PLCWorker.Data;
using AutomationBridge.PLCWorker.Logic;
using AutomationBridge.PLCWorker.Services;
using AutomationBridge.PLCWorker.Settings;
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
        // Settings
        services.Configure<PLCSettings>(context.Configuration.GetSection("PLCSettings"));

        // Data
        services.AddTransient<IDbConnection>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connectionString = config.GetConnectionString("DefaultConnectionString");
            return new SqlConnection(connectionString);
        });

        // Logic and Services
        services.AddHostedService<PlcServerService>();
        services.AddSingleton<FrameProcessor>();
        services.AddSingleton<DestinationResolver>();
    })
    .Build();

await host.RunAsync();