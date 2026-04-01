using System.Text.Json;

namespace Tc3ProjectTools.Runtime;

public sealed class AppPaths
{
    public string BaseDirectory { get; init; } = string.Empty;

    public string? RepositoryRoot { get; init; }

    public string PluginRoot { get; init; } = string.Empty;

    public string ApplicationDataRoot { get; init; } = string.Empty;

    public string SettingsPath { get; init; } = string.Empty;

    public string LogRoot { get; init; } = string.Empty;

    public string SessionDirectory { get; init; } = string.Empty;

    public string SessionLogPath { get; init; } = string.Empty;

    public static AppPaths Create()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var repositoryRoot = FindRepositoryRoot(baseDirectory);
        var pluginRoot = repositoryRoot is null
            ? Path.Combine(baseDirectory, "Plugins")
            : Path.Combine(repositoryRoot, "Plugins");

        var applicationDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tc3ProjectTools");
        var logRoot = Path.Combine(applicationDataRoot, "Logs");
        var sessionDirectory = Path.Combine(logRoot, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));

        Directory.CreateDirectory(pluginRoot);
        Directory.CreateDirectory(applicationDataRoot);
        Directory.CreateDirectory(logRoot);
        Directory.CreateDirectory(sessionDirectory);

        return new AppPaths
        {
            BaseDirectory = baseDirectory,
            RepositoryRoot = repositoryRoot,
            PluginRoot = pluginRoot,
            ApplicationDataRoot = applicationDataRoot,
            SettingsPath = Path.Combine(applicationDataRoot, "settings.json"),
            LogRoot = logRoot,
            SessionDirectory = sessionDirectory,
            SessionLogPath = Path.Combine(sessionDirectory, "session.log"),
        };
    }

    private static string? FindRepositoryRoot(string startingPath)
    {
        var current = new DirectoryInfo(startingPath);
        while (current is not null)
        {
            var slnPath = Path.Combine(current.FullName, "Tc3ProjectTools.sln");
            var slnxPath = Path.Combine(current.FullName, "Tc3ProjectTools.slnx");
            if (File.Exists(slnPath) || File.Exists(slnxPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}

public sealed class AppSettings
{
    public List<string> RecentWorkspaces { get; set; } = new();

    public Dictionary<string, bool> PluginEnabledStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? LastWorkspacePath { get; set; }
}

public interface ISettingsService
{
    AppSettings Settings { get; }

    bool IsPluginEnabled(string pluginId);

    void SetPluginEnabled(string pluginId, bool isEnabled);

    void RememberWorkspace(string workspacePath);

    Task SaveAsync(CancellationToken cancellationToken = default);
}

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _settingsPath;

    private JsonSettingsService(string settingsPath, AppSettings settings)
    {
        _settingsPath = settingsPath;
        Settings = settings;
    }

    public AppSettings Settings { get; }

    public static async Task<JsonSettingsService> CreateAsync(AppPaths paths, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.SettingsPath))
        {
            return new JsonSettingsService(paths.SettingsPath, new AppSettings());
        }

        await using var stream = File.OpenRead(paths.SettingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                       ?? new AppSettings();
        return new JsonSettingsService(paths.SettingsPath, settings);
    }

    public bool IsPluginEnabled(string pluginId)
    {
        return !Settings.PluginEnabledStates.TryGetValue(pluginId, out var enabled) || enabled;
    }

    public void SetPluginEnabled(string pluginId, bool isEnabled)
    {
        Settings.PluginEnabledStates[pluginId] = isEnabled;
    }

    public void RememberWorkspace(string workspacePath)
    {
        Settings.LastWorkspacePath = workspacePath;
        Settings.RecentWorkspaces.RemoveAll(existing => string.Equals(existing, workspacePath, StringComparison.OrdinalIgnoreCase));
        Settings.RecentWorkspaces.Insert(0, workspacePath);
        if (Settings.RecentWorkspaces.Count > 10)
        {
            Settings.RecentWorkspaces.RemoveRange(10, Settings.RecentWorkspaces.Count - 10);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await using var stream = File.Create(_settingsPath);
            await JsonSerializer.SerializeAsync(stream, Settings, SerializerOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
