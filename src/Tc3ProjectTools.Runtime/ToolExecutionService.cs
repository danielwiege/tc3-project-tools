using Microsoft.Extensions.DependencyInjection;
using Tc3ProjectTools.Abstractions;

namespace Tc3ProjectTools.Runtime;

public sealed class ToolExecutionService
{
    private readonly PluginCatalog _catalog;
    private readonly IServiceProvider _serviceProvider;

    public ToolExecutionService(PluginCatalog catalog, IServiceProvider serviceProvider)
    {
        _catalog = catalog;
        _serviceProvider = serviceProvider;
    }

    public async Task<ToolResult> ExecuteAsync(
        string toolId,
        ToolContext context,
        string? actionId = null,
        IReadOnlyList<ToolSelectionState>? selection = null,
        CancellationToken cancellationToken = default)
    {
        if (!_catalog.Tools.TryGetValue(toolId, out var tool))
        {
            return ToolResult.Create(ToolResultStatus.Error, "Tool not found", $"The tool '{toolId}' is not registered.");
        }

        var runner = (IToolRunner)_serviceProvider.GetRequiredService(tool.RunnerType);
        return await runner.ExecuteAsync(
            new ToolExecutionRequest
            {
                Context = context,
                ActionId = actionId,
                Selection = selection ?? Array.Empty<ToolSelectionState>(),
            },
            cancellationToken);
    }
}
