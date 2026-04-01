using System.Text.RegularExpressions;
using Tc3ProjectTools.Abstractions;

namespace Tc3ProjectTools.Runtime;

public interface IWorkspaceDiscoveryService
{
    Task<WorkspaceSnapshot?> DiscoverAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceDiscoveryService : IWorkspaceDiscoveryService
{
    private static readonly string[] PrimaryProjectExtensions = [".tsproj", ".plcproj"];
    private static readonly HashSet<string> RelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tsproj",
        ".plcproj",
        ".xti",
        ".tctto",
        ".xml",
        ".tmc",
        ".tpy",
        ".TcTTO",
        ".TcPOU",
    };

    public Task<WorkspaceSnapshot?> DiscoverAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<WorkspaceSnapshot?>(null);
        }

        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            return Task.FromResult<WorkspaceSnapshot?>(BuildSnapshot(fullPath, fullPath, null, [fullPath]));
        }

        if (!File.Exists(fullPath))
        {
            return Task.FromResult<WorkspaceSnapshot?>(null);
        }

        var extension = Path.GetExtension(fullPath);
        if (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetDirectoryName(fullPath)!;
            var solutionRoots = SolutionFileParser.ParseProjectDirectories(fullPath, root);
            if (solutionRoots.Count == 0)
            {
                solutionRoots.Add(root);
            }

            return Task.FromResult<WorkspaceSnapshot?>(BuildSnapshot(fullPath, root, fullPath, solutionRoots));
        }

        if (PrimaryProjectExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            var root = Path.GetDirectoryName(fullPath)!;
            return Task.FromResult<WorkspaceSnapshot?>(BuildSnapshot(fullPath, root, null, [root]));
        }

        return Task.FromResult<WorkspaceSnapshot?>(BuildSnapshot(fullPath, Path.GetDirectoryName(fullPath)!, null, [Path.GetDirectoryName(fullPath)!]));
    }

    private static WorkspaceSnapshot BuildSnapshot(string sourcePath, string rootPath, string? solutionPath, IEnumerable<string> roots)
    {
        var allFiles = roots
            .SelectMany(EnumerateRelevantFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projects = allFiles
            .Where(path => PrimaryProjectExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(projectPath =>
            {
                var projectDirectory = Path.GetDirectoryName(projectPath)!;
                var supportingFiles = allFiles
                    .Where(candidate =>
                        !string.Equals(candidate, projectPath, StringComparison.OrdinalIgnoreCase)
                        && candidate.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return new WorkspaceProject
                {
                    Name = Path.GetFileNameWithoutExtension(projectPath),
                    ProjectPath = projectPath,
                    DirectoryPath = projectDirectory,
                    SupportingFiles = supportingFiles,
                };
            })
            .ToList();

        return new WorkspaceSnapshot
        {
            SourcePath = sourcePath,
            RootPath = rootPath,
            SolutionPath = solutionPath,
            Projects = projects,
            AllFiles = allFiles,
        };
    }

    private static IEnumerable<string> EnumerateRelevantFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (RelevantExtensions.Contains(Path.GetExtension(file)))
            {
                yield return Path.GetFullPath(file);
            }
        }
    }
}

internal static class SolutionFileParser
{
    private static readonly Regex ProjectPathRegex = new(
        "Project\\(\"[^\"]+\"\\)\\s*=\\s*\"[^\"]+\",\\s*\"(?<path>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<string> ParseProjectDirectories(string solutionPath, string rootPath)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(solutionPath))
        {
            var match = ProjectPathRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var relativePath = match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
            var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                directories.Add(directory);
            }
        }

        return directories.ToList();
    }
}
