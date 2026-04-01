namespace Tc3ProjectTools.Abstractions;

public sealed class WorkspaceSnapshot
{
    public string SourcePath { get; init; } = string.Empty;

    public string RootPath { get; init; } = string.Empty;

    public string? SolutionPath { get; init; }

    public IReadOnlyList<WorkspaceProject> Projects { get; init; } = Array.Empty<WorkspaceProject>();

    public IReadOnlyList<string> AllFiles { get; init; } = Array.Empty<string>();

    public string DisplayName =>
        string.IsNullOrWhiteSpace(SolutionPath)
            ? new DirectoryInfo(RootPath).Name
            : System.IO.Path.GetFileNameWithoutExtension(SolutionPath);
}

public sealed class WorkspaceProject
{
    public string Name { get; init; } = string.Empty;

    public string ProjectPath { get; init; } = string.Empty;

    public string DirectoryPath { get; init; } = string.Empty;

    public IReadOnlyList<string> SupportingFiles { get; init; } = Array.Empty<string>();
}

public sealed class CleanupRule
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool EnabledByDefault { get; init; }

    public bool IsRisky { get; init; }

    public List<string> FilePatterns { get; init; } = new();

    public List<string> DirectoryNames { get; init; } = new();
}

public sealed class GuidOccurrence
{
    public Guid Value { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string SourceProjectPath { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public string XmlPath { get; init; } = string.Empty;

    public GuidOccurrenceKind Kind { get; init; }
}

public enum GuidOccurrenceKind
{
    Project,
    Object,
}
