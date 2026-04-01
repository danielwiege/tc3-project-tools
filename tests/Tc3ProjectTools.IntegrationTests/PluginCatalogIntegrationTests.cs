using Microsoft.Extensions.DependencyInjection;
using Tc3ProjectTools.Runtime;

namespace Tc3ProjectTools.IntegrationTests;

public sealed class PluginCatalogIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Tc3ToolsIntegration", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PluginCatalogBootstrapper_LoadsValidPlugins_AndKeepsBrokenPluginsNonFatal()
    {
        var pluginRoot = Path.Combine(_root, "Plugins");
        var repositoryRoot = GetRepositoryRoot();
        var cleanupDirectory = CreatePluginInstall(
            "tc3.cleanup",
            Path.Combine(repositoryRoot, "plugins", "Tc3ProjectTools.Plugins.Cleanup", "bin", "Debug", "net8.0"));
        var guidDirectory = CreatePluginInstall(
            "tc3.guid-audit",
            Path.Combine(repositoryRoot, "plugins", "Tc3ProjectTools.Plugins.GuidAudit", "bin", "Debug", "net8.0"));
        CreateBrokenPluginInstall(pluginRoot);

        var paths = new AppPaths
        {
            BaseDirectory = _root,
            PluginRoot = pluginRoot,
            ApplicationDataRoot = Path.Combine(_root, "AppData"),
            SettingsPath = Path.Combine(_root, "AppData", "settings.json"),
            LogRoot = Path.Combine(_root, "Logs"),
            SessionDirectory = Path.Combine(_root, "Logs", "session"),
            SessionLogPath = Path.Combine(_root, "Logs", "session", "session.log"),
        };

        Directory.CreateDirectory(paths.ApplicationDataRoot);
        Directory.CreateDirectory(paths.LogRoot);
        Directory.CreateDirectory(paths.SessionDirectory);

        var settingsService = await JsonSettingsService.CreateAsync(paths);
        var services = new ServiceCollection();
        services.AddLogging();

        var catalog = new PluginCatalogBootstrapper().Build(services, paths, settingsService);

        Assert.True(
            catalog.Tools.Count == 2,
            $"Expected 2 tools, got {catalog.Tools.Count}. States: {string.Join(" | ", catalog.Plugins.Select(plugin => $"{plugin.Manifest.Id}:{plugin.State}:{plugin.Message}"))}");
        Assert.Contains(catalog.Plugins, plugin => plugin.Manifest.Id == "tc3.cleanup" && plugin.State == PluginLoadState.Loaded);
        Assert.Contains(catalog.Plugins, plugin => plugin.Manifest.Id == "tc3.guid-audit" && plugin.State == PluginLoadState.Loaded);
        Assert.Contains(catalog.Plugins, plugin => plugin.Manifest.Id == "broken" && plugin.State == PluginLoadState.Failed);
        Assert.True(Directory.Exists(cleanupDirectory));
        Assert.True(Directory.Exists(guidDirectory));
    }

    private string CreatePluginInstall(string pluginId, string sourceDirectory)
    {
        var pluginRoot = Path.Combine(_root, "Plugins");
        var installDirectory = Path.Combine(pluginRoot, pluginId);
        Directory.CreateDirectory(installDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(installDirectory, name), overwrite: true);
        }

        return installDirectory;
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Tc3ProjectTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root for integration tests.");
    }

    private static void CreateBrokenPluginInstall(string pluginRoot)
    {
        var brokenDirectory = Path.Combine(pluginRoot, "broken");
        Directory.CreateDirectory(brokenDirectory);
        File.WriteAllText(
            Path.Combine(brokenDirectory, "plugin.json"),
            """
            {
              "id": "broken",
              "name": "Broken Plugin",
              "version": "1.0.0",
              "description": "Invalid plugin for error-path validation.",
              "entryAssembly": "missing.dll",
              "entryType": "Broken.Missing",
              "minHostVersion": "1.0.0"
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
