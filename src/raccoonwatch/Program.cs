using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Raccoonwatch;
using Raccoonwatch.Api;
using Raccoonwatch.Infrastructure;
using Raccoonwatch.Services;
using Raccoonwatch.UI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 9F));

        using var instanceGuard = new SingleInstanceGuard();
        if (!instanceGuard.IsFirstInstance)
        {
            instanceGuard.SignalFirstInstance();
            return;
        }

        var services = new ServiceCollection();
        ConfigureServices(services, instanceGuard);
        using var provider = services.BuildServiceProvider();

        var startupLogger = provider.GetRequiredService<ILogger<ProgramLog>>();
        Application.ThreadException += (_, e) =>
            startupLogger.LogError(e.Exception, "Unhandled UI exception.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                startupLogger.LogCritical(exception, "Unhandled domain exception.");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            startupLogger.LogError(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        };

        var loggingOptions = provider.GetRequiredService<LoggingOptions>();
        var settings = provider.GetRequiredService<ISettingsService>();
        settings.Load();

        if (Enum.TryParse<LogLevel>(settings.Settings.LogLevel, ignoreCase: true, out var level))
        {
            loggingOptions.MinLevel = level;
        }

        provider.GetRequiredService<IOutputStore>().HistoryLimit = settings.Settings.OutputHistoryLines;

        var context = provider.GetRequiredService<TrayApplicationContext>();
        instanceGuard.ShowManageRequested += context.ShowManageProcesses;

        provider.GetRequiredService<IAppController>().StartupAsync().GetAwaiter().GetResult();

        var apiServer = provider.GetRequiredService<NamedPipeApiServer>();
        apiServer.StartAsync().GetAwaiter().GetResult();

        try
        {
            Application.Run(context);
        }
        finally
        {
            apiServer.StopAsync().GetAwaiter().GetResult();
        }
    }

    private static void ConfigureServices(IServiceCollection services, SingleInstanceGuard instanceGuard)
    {
        var loggingOptions = new LoggingOptions();
        services.AddSingleton(loggingOptions);
        services.AddSingleton(instanceGuard);

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddRaccoonwatchFileLogger(AppPaths.LogDirectory, loggingOptions);
        });

        services.AddSingleton<JsonSettingsStore>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IPollingIntervalProvider>(provider =>
            new PollingIntervalProvider(provider.GetRequiredService<ISettingsService>()));
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<IOutputStore, OutputStore>();
        services.AddSingleton<IProcessManager, ProcessManager>();
        services.AddSingleton<IAutostartService, WindowsRunRegistry>();
        services.AddSingleton<IAppController, AppController>();
        services.AddSingleton<IUpdateCommandRunner, UpdateCommandRunner>();
        services.AddSingleton<IApiRequestHandler, ApiRequestHandler>();
        services.AddSingleton(provider => new NamedPipeApiServer(
            ApiProtocol.DefaultPipeName,
            provider.GetRequiredService<IApiRequestHandler>(),
            error => provider.GetRequiredService<ILogger<NamedPipeApiServer>>()
                .LogError(error, "Local control API connection failed.")));
        services.AddSingleton<TrayApplicationContext>();
        services.AddTransient<SettingsForm>();
    }
}

internal sealed class ProgramLog;
