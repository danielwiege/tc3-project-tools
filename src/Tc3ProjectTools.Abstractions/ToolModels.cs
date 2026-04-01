namespace Tc3ProjectTools.Abstractions;

public sealed class ToolContext
{
    public required WorkspaceSnapshot Workspace { get; init; }

    public required string ApplicationDataDirectory { get; init; }

    public required string SessionDirectory { get; init; }
}

public sealed class ToolExecutionRequest
{
    public required ToolContext Context { get; init; }

    public string? ActionId { get; init; }

    public IReadOnlyList<ToolSelectionState> Selection { get; init; } = Array.Empty<ToolSelectionState>();
}

public sealed class ToolSelectionState
{
    public required string Id { get; init; }

    public bool IsSelected { get; init; }
}

public sealed class ToolAction
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public ToolActionKind Kind { get; init; } = ToolActionKind.Secondary;

    public bool RequiresSelection { get; init; }

    public string? ConfirmationMessage { get; init; }
}

public enum ToolActionKind
{
    Secondary,
    Primary,
    Danger,
}

public sealed class ToolResult
{
    public ToolResultStatus Status { get; init; } = ToolResultStatus.Success;

    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<ToolMessage> Messages { get; init; } = Array.Empty<ToolMessage>();

    public IReadOnlyList<ToolTable> Tables { get; init; } = Array.Empty<ToolTable>();

    public IReadOnlyList<ToolSelectionItem> SelectionItems { get; init; } = Array.Empty<ToolSelectionItem>();

    public IReadOnlyList<ToolAction> Actions { get; init; } = Array.Empty<ToolAction>();

    public IReadOnlyList<string> Artifacts { get; init; } = Array.Empty<string>();

    public static ToolResult Create(ToolResultStatus status, string title, string summary)
    {
        return new ToolResult
        {
            Status = status,
            Title = title,
            Summary = summary,
        };
    }
}

public enum ToolResultStatus
{
    Success,
    Warning,
    Error,
}

public sealed class ToolMessage
{
    public ToolMessageLevel Level { get; init; } = ToolMessageLevel.Information;

    public string Text { get; init; } = string.Empty;
}

public enum ToolMessageLevel
{
    Information,
    Warning,
    Error,
}

public sealed class ToolTable
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();
}

public sealed class ToolSelectionItem
{
    public string Id { get; init; } = string.Empty;

    public string Group { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public long SizeInBytes { get; init; }

    public bool IsSelected { get; init; }

    public bool IsEnabled { get; init; } = true;
}
