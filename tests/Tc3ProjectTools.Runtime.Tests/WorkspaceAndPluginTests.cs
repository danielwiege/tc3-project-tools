using Microsoft.Extensions.Logging.Abstractions;
using Tc3ProjectTools.Abstractions;
using Tc3ProjectTools.Plugins.Cleanup;
using Tc3ProjectTools.Plugins.GuidAudit;
using Tc3ProjectTools.Runtime;

namespace Tc3ProjectTools.Runtime.Tests;

public sealed class WorkspaceAndPluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Tc3ToolsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WorkspaceDiscoveryService_DiscoversTwinCatProjectsFromFolder()
    {
        Directory.CreateDirectory(_root);
        var projectDirectory = Path.Combine(_root, "LineA");
        Directory.CreateDirectory(projectDirectory);

        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "LineA.tsproj"), "<Project><ProjectGuid>{11111111-1111-1111-1111-111111111111}</ProjectGuid></Project>");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Axis.xti"), "<Axis><Guid>{22222222-2222-2222-2222-222222222222}</Guid></Axis>");

        var service = new WorkspaceDiscoveryService();
        var snapshot = await service.DiscoverAsync(_root);

        Assert.NotNull(snapshot);
        Assert.Single(snapshot.Projects);
        Assert.Equal("LineA", snapshot.Projects[0].Name);
        Assert.Contains(snapshot.AllFiles, file => file.EndsWith(".xti", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CleanupToolRunner_PreviewFindsDisposableArtifacts()
    {
        var workspaceRoot = Path.Combine(_root, "cleanup");
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "bin"));
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, "temp.user"), "data");
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, "bin", "artifact.dll"), "compiled");

        var applicationDataDirectory = Path.Combine(_root, "appdata");
        var options = new CleanupPluginOptions
        {
            InstallationDirectory = Path.GetDirectoryName(typeof(CleanupPluginEntryPoint).Assembly.Location)!,
        };

        var runner = new CleanupToolRunner(new CleanupRuleProfileStore(options), NullLogger<CleanupToolRunner>.Instance);
        var result = await runner.ExecuteAsync(new ToolExecutionRequest
        {
            Context = new ToolContext
            {
                Workspace = new WorkspaceSnapshot
                {
                    RootPath = workspaceRoot,
                    SourcePath = workspaceRoot,
                },
                ApplicationDataDirectory = applicationDataDirectory,
                SessionDirectory = Path.Combine(_root, "session"),
            },
        });

        Assert.Equal(ToolResultStatus.Warning, result.Status);
        Assert.Contains(result.SelectionItems, item => item.Path.EndsWith("temp.user", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.SelectionItems, item => item.Path.EndsWith("bin", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Actions, action => action.Id == "delete-selected");
        Assert.True(File.Exists(Path.Combine(applicationDataDirectory, "cleanup.rules.json")));
    }

    [Fact]
    public async Task GuidAuditToolRunner_FindsCrossProjectDuplicateGuids()
    {
        var workspaceRoot = Path.Combine(_root, "guids");
        var projectA = Path.Combine(workspaceRoot, "MachineA");
        var projectB = Path.Combine(workspaceRoot, "MachineB");
        Directory.CreateDirectory(projectA);
        Directory.CreateDirectory(projectB);

        const string projectGuid = "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}";
        const string objectGuid = "{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}";

        await File.WriteAllTextAsync(Path.Combine(projectA, "MachineA.tsproj"), $"<Project><ProjectGuid>{projectGuid}</ProjectGuid><Node Guid=\"{objectGuid}\" /></Project>");
        await File.WriteAllTextAsync(Path.Combine(projectB, "MachineB.tsproj"), $"<Project><ProjectGuid>{projectGuid}</ProjectGuid><Node Guid=\"{objectGuid}\" /></Project>");

        var runner = new GuidAuditToolRunner(NullLogger<GuidAuditToolRunner>.Instance);
        var result = await runner.ExecuteAsync(new ToolExecutionRequest
        {
            Context = new ToolContext
            {
                Workspace = new WorkspaceSnapshot
                {
                    RootPath = workspaceRoot,
                    SourcePath = workspaceRoot,
                    Projects =
                    [
                        new WorkspaceProject
                        {
                            Name = "MachineA",
                            ProjectPath = Path.Combine(projectA, "MachineA.tsproj"),
                            DirectoryPath = projectA,
                        },
                        new WorkspaceProject
                        {
                            Name = "MachineB",
                            ProjectPath = Path.Combine(projectB, "MachineB.tsproj"),
                            DirectoryPath = projectB,
                        },
                    ],
                },
                ApplicationDataDirectory = Path.Combine(_root, "appdata"),
                SessionDirectory = Path.Combine(_root, "session"),
            },
        });

        Assert.Equal(ToolResultStatus.Warning, result.Status);
        Assert.Contains(result.Tables, table => table.Title == "Duplicate Project GUIDs");
        Assert.Contains(result.Tables, table => table.Title == "Duplicate Object GUIDs");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
