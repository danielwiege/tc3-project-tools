using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tc3ProjectTools.Runtime;

namespace Tc3ProjectTools.Host;

internal static class HostFactory
{
    public static async Task<IHost> CreateAsync()
    {
        var paths = AppPaths.Create();
        var settingsService = await JsonSettingsService.CreateAsync(paths);
        var logStore = new UiLogStore();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<ISettingsService>(settingsService);
        builder.Services.AddSingleton(logStore);
        builder.Services.AddSingleton<IWorkspaceDiscoveryService, WorkspaceDiscoveryService>();
        builder.Services.AddSingleton<ToolExecutionService>();
        builder.Services.AddSingleton<IWorkspacePickerService, WorkspacePickerService>();
        builder.Services.AddSingleton<IExplorerService, ExplorerService>();
        builder.Services.AddSingleton<IMessageDialogService, MessageDialogService>();

        var pluginCatalog = new PluginCatalogBootstrapper().Build(builder.Services, paths, settingsService);
        builder.Services.AddSingleton(pluginCatalog);
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.AddProvider(new UiLogStoreLoggerProvider(logStore, paths.SessionLogPath));

        var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogInformation("TwinCAT3 Project Tools started.");
        logger.LogInformation("Plugin root: {PluginRoot}", paths.PluginRoot);

        foreach (var plugin in pluginCatalog.Plugins)
        {
            logger.LogInformation("Plugin {PluginId} state: {State} ({Message})", plugin.Manifest.Id, plugin.State, plugin.Message);
        }

        return host;
    }
}
