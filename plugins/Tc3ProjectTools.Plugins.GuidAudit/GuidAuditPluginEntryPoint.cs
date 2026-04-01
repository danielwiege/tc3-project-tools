using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Tc3ProjectTools.Abstractions;

namespace Tc3ProjectTools.Plugins.GuidAudit;

public sealed class GuidAuditPluginEntryPoint : IPluginEntryPoint
{
    public void Configure(IPluginRegistrationContext context)
    {
        context.RegisterTool(
            new PluginToolDefinition
            {
                Id = "guid.audit",
                DisplayName = "Audit GUID Duplicates",
                Description = "Checks for duplicate project-level and object-level GUIDs across TwinCAT projects.",
                Ribbon = new RibbonContribution
                {
                    TabKey = "twincat",
                    TabTitle = "TwinCAT",
                    GroupKey = "integrity",
                    GroupTitle = "Integrity",
                    Order = 20,
                },
            },
            typeof(GuidAuditToolRunner));
    }
}

public sealed partial class GuidAuditToolRunner : IToolRunner
{
    private readonly ILogger<GuidAuditToolRunner> _logger;

    public GuidAuditToolRunner(ILogger<GuidAuditToolRunner> logger)
    {
        _logger = logger;
    }

    public Task<ToolResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var occurrences = new List<GuidOccurrence>();
        var skippedFiles = new List<string>();

        foreach (var project in request.Context.Workspace.Projects)
        {
            var projectFiles = project.SupportingFiles.Append(project.ProjectPath).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var file in projectFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var document = XDocument.Load(file, LoadOptions.SetLineInfo);
                    occurrences.AddRange(ExtractOccurrences(project, file, document));
                }
                catch (Exception ex)
                {
                    skippedFiles.Add(file);
                    _logger.LogDebug(ex, "Skipping non-XML or unreadable file {WorkspaceFile}", file);
                }
            }
        }

        var projectDuplicates = occurrences
            .Where(occurrence => occurrence.Kind == GuidOccurrenceKind.Project)
            .GroupBy(occurrence => occurrence.Value)
            .Where(group => group.Select(occurrence => occurrence.SourceProjectPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .ToList();

        var objectDuplicates = occurrences
            .Where(occurrence => occurrence.Kind == GuidOccurrenceKind.Object)
            .GroupBy(occurrence => occurrence.Value)
            .Where(group => group.Select(occurrence => occurrence.SourceProjectPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .ToList();

        var sameProjectRepeats = occurrences
            .GroupBy(occurrence => occurrence.Value)
            .Where(group =>
                group.Count() > 1
                && group.Select(occurrence => occurrence.SourceProjectPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .ToList();

        var tables = new List<ToolTable>();
        if (projectDuplicates.Count > 0)
        {
            tables.Add(BuildTable("Duplicate Project GUIDs", projectDuplicates));
        }

        if (objectDuplicates.Count > 0)
        {
            tables.Add(BuildTable("Duplicate Object GUIDs", objectDuplicates));
        }

        if (sameProjectRepeats.Count > 0)
        {
            tables.Add(BuildTable("Single-Project Repeats", sameProjectRepeats));
        }

        var messages = new List<ToolMessage>();
        if (skippedFiles.Count > 0)
        {
            messages.Add(new ToolMessage
            {
                Level = ToolMessageLevel.Warning,
                Text = $"Skipped {skippedFiles.Count} file(s) that could not be parsed as XML.",
            });
        }

        if (sameProjectRepeats.Count > 0)
        {
            messages.Add(new ToolMessage
            {
                Level = ToolMessageLevel.Information,
                Text = $"{sameProjectRepeats.Count} GUID value(s) repeat only inside a single project and are listed separately for review.",
            });
        }

        var duplicateCount = projectDuplicates.Count + objectDuplicates.Count;
        return Task.FromResult(new ToolResult
        {
            Status = duplicateCount == 0 ? ToolResultStatus.Success : ToolResultStatus.Warning,
            Title = "GUID Audit",
            Summary = duplicateCount == 0
                ? "No cross-project GUID duplicates were found."
                : $"{projectDuplicates.Count} project GUID collision group(s) and {objectDuplicates.Count} object GUID collision group(s) found.",
            Messages = messages,
            Tables = tables,
        });
    }

    private static IEnumerable<GuidOccurrence> ExtractOccurrences(WorkspaceProject project, string filePath, XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            if (TryParseGuid(element.Value, out var elementGuid))
            {
                yield return new GuidOccurrence
                {
                    Value = elementGuid,
                    ProjectName = project.Name,
                    SourceProjectPath = project.ProjectPath,
                    FilePath = filePath,
                    XmlPath = BuildXPath(element),
                    Kind = ClassifyOccurrence(project.ProjectPath, filePath, element.Name.LocalName, element.Ancestors().Count()),
                };
            }

            foreach (var attribute in element.Attributes())
            {
                if (!TryParseGuid(attribute.Value, out var attributeGuid))
                {
                    continue;
                }

                yield return new GuidOccurrence
                {
                    Value = attributeGuid,
                    ProjectName = project.Name,
                    SourceProjectPath = project.ProjectPath,
                    FilePath = filePath,
                    XmlPath = $"{BuildXPath(element)}/@{attribute.Name.LocalName}",
                    Kind = ClassifyOccurrence(project.ProjectPath, filePath, attribute.Name.LocalName, element.Ancestors().Count()),
                };
            }
        }
    }

    private static ToolTable BuildTable(string title, IEnumerable<IGrouping<Guid, GuidOccurrence>> groups)
    {
        return new ToolTable
        {
            Title = title,
            Columns = ["GUID", "Project", "File", "XML Path"],
            Rows = groups
                .OrderBy(group => group.Key)
                .SelectMany(group => group.OrderBy(occurrence => occurrence.ProjectName).ThenBy(occurrence => occurrence.FilePath)
                    .Select(occurrence => (IReadOnlyList<string>)new[]
                    {
                        occurrence.Value.ToString("D"),
                        occurrence.ProjectName,
                        occurrence.FilePath,
                        occurrence.XmlPath,
                    }))
                .ToList(),
        };
    }

    private static GuidOccurrenceKind ClassifyOccurrence(string projectPath, string filePath, string memberName, int depth)
    {
        if (string.Equals(filePath, projectPath, StringComparison.OrdinalIgnoreCase))
        {
            var normalizedName = memberName.ToLowerInvariant();
            if ((normalizedName.Contains("project") && normalizedName.Contains("guid")) || (depth <= 1 && normalizedName.Contains("guid")))
            {
                return GuidOccurrenceKind.Project;
            }
        }

        return GuidOccurrenceKind.Object;
    }

    private static string BuildXPath(XElement element)
    {
        var segments = element
            .AncestorsAndSelf()
            .Reverse()
            .Select(node =>
            {
                var index = node.Parent is null
                    ? 1
                    : node.Parent.Elements(node.Name).TakeWhile(candidate => candidate != node).Count() + 1;
                return $"{node.Name.LocalName}[{index}]";
            });

        return "/" + string.Join('/', segments);
    }

    private static bool TryParseGuid(string value, out Guid guid)
    {
        var trimmed = value.Trim();
        if (!GuidRegex().IsMatch(trimmed))
        {
            guid = Guid.Empty;
            return false;
        }

        return Guid.TryParse(trimmed.Trim('{', '}'), out guid);
    }

    [GeneratedRegex("^[{(]?[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}[)}]?$", RegexOptions.CultureInvariant)]
    private static partial Regex GuidRegex();
}
