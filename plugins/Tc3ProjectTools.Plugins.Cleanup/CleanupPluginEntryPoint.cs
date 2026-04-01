using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using Tc3ProjectTools.Abstractions;

namespace Tc3ProjectTools.Plugins.Cleanup;

public sealed class CleanupPluginEntryPoint : IPluginEntryPoint
{
    public void Configure(IPluginRegistrationContext context)
    {
        context.Services.AddSingleton(new CleanupPluginOptions
        {
            InstallationDirectory = context.Manifest.InstallationDirectory,
        });
        context.Services.AddSingleton<CleanupRuleProfileStore>();
        context.RegisterTool(
            new PluginToolDefinition
            {
                Id = "cleanup.preview",
                DisplayName = "Clean Unnecessary Files",
                Description = "Preview and remove generated or unnecessary TwinCAT project artifacts.",
                Ribbon = new RibbonContribution
                {
                    TabKey = "twincat",
                    TabTitle = "TwinCAT",
                    GroupKey = "project-hygiene",
                    GroupTitle = "Project Hygiene",
                    Order = 10,
                },
            },
            typeof(CleanupToolRunner));
    }
}

public sealed class CleanupToolRunner : IToolRunner
{
    private readonly CleanupRuleProfileStore _ruleProfileStore;
    private readonly ILogger<CleanupToolRunner> _logger;

    public CleanupToolRunner(CleanupRuleProfileStore ruleProfileStore, ILogger<CleanupToolRunner> logger)
    {
        _ruleProfileStore = ruleProfileStore;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var rules = await _ruleProfileStore.LoadRulesAsync(request.Context.ApplicationDataDirectory, cancellationToken);
        var activeRules = rules.Where(rule => rule.EnabledByDefault).ToList();
        var candidates = DiscoverCandidates(request.Context.Workspace.RootPath, activeRules);

        return string.Equals(request.ActionId, "delete-selected", StringComparison.OrdinalIgnoreCase)
            ? DeleteSelectedCandidates(request, rules, candidates)
            : BuildPreviewResult(request.Context.ApplicationDataDirectory, rules, activeRules, candidates);
    }

    private ToolResult BuildPreviewResult(
        string applicationDataDirectory,
        IReadOnlyList<CleanupRule> allRules,
        IReadOnlyList<CleanupRule> activeRules,
        IReadOnlyList<CleanupCandidate> candidates)
    {
        var riskyRules = activeRules.Where(rule => rule.IsRisky).Select(rule => rule.Name).ToList();
        var summaryTable = new ToolTable
        {
            Title = "Rule Summary",
            Columns = ["Rule", "Matches", "Size", "Risk"],
            Rows = candidates
                .GroupBy(candidate => candidate.Rule.Name)
                .Select(group => (IReadOnlyList<string>)new[]
                {
                    group.Key,
                    group.Count().ToString(),
                    FormatSize(group.Sum(candidate => candidate.SizeInBytes)),
                    group.First().Rule.IsRisky ? "Caution" : "Safe",
                })
                .OrderBy(row => row[0])
                .ToList(),
        };

        var messages = new List<ToolMessage>
        {
            new()
            {
                Level = ToolMessageLevel.Information,
                Text = $"Using rules from {Path.Combine(applicationDataDirectory, "cleanup.rules.json")}. Edit that file to enable or disable rule groups.",
            },
        };

        if (riskyRules.Count > 0)
        {
            messages.Add(new ToolMessage
            {
                Level = ToolMessageLevel.Warning,
                Text = $"Risky rules are enabled: {string.Join(", ", riskyRules)}.",
            });
        }

        if (activeRules.Count == 0)
        {
            messages.Add(new ToolMessage
            {
                Level = ToolMessageLevel.Warning,
                Text = "No cleanup rules are currently enabled. Enable rules in cleanup.rules.json to scan for candidates.",
            });
        }

        return new ToolResult
        {
            Status = candidates.Count == 0 ? ToolResultStatus.Success : ToolResultStatus.Warning,
            Title = "Cleanup Preview",
            Summary = candidates.Count == 0
                ? "No files matched the active cleanup rules."
                : $"{candidates.Count} candidate item(s) found across {activeRules.Count} active rule(s).",
            Messages = messages,
            Tables = candidates.Count == 0 ? Array.Empty<ToolTable>() : [summaryTable],
            SelectionItems = candidates.Select(candidate => new ToolSelectionItem
            {
                Id = candidate.Id,
                Group = candidate.Rule.Name,
                DisplayName = candidate.DisplayName,
                Description = candidate.Rule.Description,
                Path = candidate.Path,
                SizeInBytes = candidate.SizeInBytes,
                IsSelected = !candidate.Rule.IsRisky,
            }).ToList(),
            Actions = candidates.Count == 0
                ? Array.Empty<ToolAction>()
                : [new ToolAction
                {
                    Id = "delete-selected",
                    Label = "Delete Selected",
                    Kind = ToolActionKind.Danger,
                    RequiresSelection = true,
                    ConfirmationMessage = "Send the selected files and folders to the Windows Recycle Bin?",
                }],
            Artifacts = [Path.Combine(applicationDataDirectory, "cleanup.rules.json")],
        };
    }

    private ToolResult DeleteSelectedCandidates(
        ToolExecutionRequest request,
        IReadOnlyList<CleanupRule> allRules,
        IReadOnlyList<CleanupCandidate> candidates)
    {
        var selectedIds = request.Selection.Where(selection => selection.IsSelected).Select(selection => selection.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedCandidates = candidates.Where(candidate => selectedIds.Contains(candidate.Id)).ToList();
        if (selectedCandidates.Count == 0)
        {
            return new ToolResult
            {
                Status = ToolResultStatus.Warning,
                Title = "Cleanup Execution",
                Summary = "No items were selected for deletion.",
                Messages =
                [
                    new ToolMessage
                    {
                        Level = ToolMessageLevel.Warning,
                        Text = "Select one or more preview items before running the delete action.",
                    },
                ],
            };
        }

        var auditLogPath = Path.Combine(request.Context.SessionDirectory, "cleanup-audit.log");
        var deletedRows = new List<IReadOnlyList<string>>();
        var failedRows = new List<IReadOnlyList<string>>();

        foreach (var candidate in selectedCandidates)
        {
            try
            {
                if (candidate.IsDirectory)
                {
                    FileSystem.DeleteDirectory(candidate.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
                }
                else
                {
                    FileSystem.DeleteFile(candidate.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }

                var deletedRow = (IReadOnlyList<string>)new[]
                {
                    candidate.Rule.Name,
                    candidate.Path,
                    FormatSize(candidate.SizeInBytes),
                };

                deletedRows.Add(deletedRow);
                File.AppendAllText(auditLogPath, $"{DateTimeOffset.Now:O} DELETED {candidate.Path}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete {CleanupPath}", candidate.Path);
                failedRows.Add(new[]
                {
                    candidate.Path,
                    ex.Message,
                });
                File.AppendAllText(auditLogPath, $"{DateTimeOffset.Now:O} FAILED {candidate.Path} {ex.Message}{Environment.NewLine}");
            }
        }

        var tables = new List<ToolTable>();
        if (deletedRows.Count > 0)
        {
            tables.Add(new ToolTable
            {
                Title = "Deleted Items",
                Columns = ["Rule", "Path", "Size"],
                Rows = deletedRows,
            });
        }

        if (failedRows.Count > 0)
        {
            tables.Add(new ToolTable
            {
                Title = "Failures",
                Columns = ["Path", "Reason"],
                Rows = failedRows,
            });
        }

        return new ToolResult
        {
            Status = failedRows.Count == 0 ? ToolResultStatus.Success : ToolResultStatus.Warning,
            Title = "Cleanup Execution",
            Summary = $"Deleted {deletedRows.Count} item(s); {failedRows.Count} item(s) failed.",
            Messages =
            [
                new ToolMessage
                {
                    Level = failedRows.Count == 0 ? ToolMessageLevel.Information : ToolMessageLevel.Warning,
                    Text = $"Audit log written to {auditLogPath}.",
                },
                new ToolMessage
                {
                    Level = ToolMessageLevel.Information,
                    Text = $"{allRules.Count(rule => rule.EnabledByDefault)} cleanup rule(s) remained active for this execution.",
                },
            ],
            Tables = tables,
            Artifacts = [auditLogPath],
        };
    }

    private static List<CleanupCandidate> DiscoverCandidates(string rootPath, IReadOnlyList<CleanupRule> activeRules)
    {
        var candidates = new List<CleanupCandidate>();
        var matchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedDirectoryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in activeRules)
        {
            foreach (var directoryName in rule.DirectoryNames)
            {
                foreach (var directory in Directory.EnumerateDirectories(rootPath, directoryName, System.IO.SearchOption.AllDirectories))
                {
                    if (!matchedPaths.Add(directory))
                    {
                        continue;
                    }

                    claimedDirectoryRoots.Add(directory);
                    candidates.Add(new CleanupCandidate
                    {
                        Id = directory,
                        Rule = rule,
                        Path = directory,
                        DisplayName = Path.GetFileName(directory),
                        SizeInBytes = GetDirectorySize(directory),
                        IsDirectory = true,
                    });
                }
            }
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", System.IO.SearchOption.AllDirectories))
        {
            if (claimedDirectoryRoots.Any(directory => file.StartsWith(directory, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var rule in activeRules)
            {
                if (!rule.FilePatterns.Any(pattern => MatchesWildcard(Path.GetFileName(file), pattern)))
                {
                    continue;
                }

                if (!matchedPaths.Add(file))
                {
                    break;
                }

                candidates.Add(new CleanupCandidate
                {
                    Id = file,
                    Rule = rule,
                    Path = file,
                    DisplayName = Path.GetFileName(file),
                    SizeInBytes = new FileInfo(file).Length,
                    IsDirectory = false,
                });
                break;
            }
        }

        return candidates
            .OrderBy(candidate => candidate.Rule.Name)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long GetDirectorySize(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.*", System.IO.SearchOption.AllDirectories)
                .Select(file => new FileInfo(file).Length)
                .Sum();
        }
        catch
        {
            return 0;
        }
    }

    private static bool MatchesWildcard(string fileName, string pattern)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            fileName,
            "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string FormatSize(long sizeInBytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = sizeInBytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }
}

public sealed class CleanupRuleProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly CleanupPluginOptions _options;

    public CleanupRuleProfileStore(CleanupPluginOptions options)
    {
        _options = options;
    }

    public async Task<IReadOnlyList<CleanupRule>> LoadRulesAsync(string applicationDataDirectory, CancellationToken cancellationToken)
    {
        var userRulesPath = Path.Combine(applicationDataDirectory, "cleanup.rules.json");
        if (!File.Exists(userRulesPath))
        {
            var defaultRulesPath = Path.Combine(_options.InstallationDirectory, "default-cleanup-rules.json");
            Directory.CreateDirectory(applicationDataDirectory);
            File.Copy(defaultRulesPath, userRulesPath, overwrite: false);
        }

        await using var stream = File.OpenRead(userRulesPath);
        return await JsonSerializer.DeserializeAsync<List<CleanupRule>>(stream, SerializerOptions, cancellationToken)
               ?? new List<CleanupRule>();
    }
}

public sealed class CleanupPluginOptions
{
    public string InstallationDirectory { get; init; } = string.Empty;
}

internal sealed class CleanupCandidate
{
    public string Id { get; init; } = string.Empty;

    public CleanupRule Rule { get; init; } = new();

    public string Path { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public long SizeInBytes { get; init; }

    public bool IsDirectory { get; init; }
}
