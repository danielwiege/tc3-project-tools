using Microsoft.Extensions.DependencyInjection;

namespace Tc3ProjectTools.Abstractions;

public sealed class PluginManifest
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string EntryAssembly { get; init; } = string.Empty;
    public string EntryType { get; init; } = string.Empty;
    public string MinHostVersion { get; init; } = string.Empty;
    public string InstallationDirectory { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
}

public interface IPluginEntryPoint
{
    void Configure(IPluginRegistrationContext context);
}

public interface IPluginRegistrationContext
{
    PluginManifest Manifest { get; }

    IServiceCollection Services { get; }

    void RegisterTool(PluginToolDefinition toolDefinition, Type runnerType);
}

public sealed class RibbonContribution
{
    public string TabKey { get; init; } = string.Empty;

    public string TabTitle { get; init; } = string.Empty;

    public string GroupKey { get; init; } = string.Empty;

    public string GroupTitle { get; init; } = string.Empty;

    public int Order { get; init; }
}

public sealed class PluginToolDefinition
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool RequiresWorkspace { get; init; } = true;

    public RibbonContribution Ribbon { get; init; } = new();
}

public interface IToolRunner
{
    Task<ToolResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default);
}
