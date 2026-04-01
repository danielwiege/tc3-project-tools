using System.Collections.ObjectModel;
using System.Data;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Tc3ProjectTools.Abstractions;
using Tc3ProjectTools.Runtime;

namespace Tc3ProjectTools.Host;

public sealed class ShellViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly PluginCatalog _pluginCatalog;
    private readonly IExplorerService _explorerService;
    private readonly IMessageDialogService _messageDialogService;
    private readonly ISettingsService _settingsService;
    private readonly ToolExecutionService _toolExecutionService;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly IWorkspaceDiscoveryService _workspaceDiscoveryService;
    private readonly IWorkspacePickerService _workspacePickerService;
    private string? _activeToolId;
    private WorkspaceSnapshot? _currentWorkspace;
    private bool _hasSelectionItems;
    private bool _hasTableResults;
    private bool _hasToolActions;
    private bool _isBusy;
    private string _resultStatus = "Ready";
    private string _resultSummary = "Open a solution or folder to start exploring TwinCAT projects.";
    private string _resultTitle = "Workspace Overview";

    public ShellViewModel(
        IWorkspacePickerService workspacePickerService,
        IWorkspaceDiscoveryService workspaceDiscoveryService,
        ToolExecutionService toolExecutionService,
        ISettingsService settingsService,
        AppPaths paths,
        UiLogStore logStore,
        PluginCatalog pluginCatalog,
        IMessageDialogService messageDialogService,
        IExplorerService explorerService,
        ILogger<ShellViewModel> logger)
    {
        _workspacePickerService = workspacePickerService;
        _workspaceDiscoveryService = workspaceDiscoveryService;
        _toolExecutionService = toolExecutionService;
        _settingsService = settingsService;
        _paths = paths;
        _pluginCatalog = pluginCatalog;
        _messageDialogService = messageDialogService;
        _explorerService = explorerService;
        _logger = logger;

        OpenWorkspaceCommand = new AsyncRelayCommand(OpenWorkspaceAsync);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync);
        RefreshWorkspaceCommand = new AsyncRelayCommand(RefreshWorkspaceAsync, () => CurrentWorkspace is not null);
        RunToolCommand = new AsyncRelayCommand<string>(RunToolAsync);
        ExecuteResultActionCommand = new AsyncRelayCommand<string>(ExecuteResultActionAsync, _ => CurrentWorkspace is not null && !string.IsNullOrWhiteSpace(_activeToolId));
        OpenRecentWorkspaceCommand = new AsyncRelayCommand<string>(OpenWorkspaceAsync);
        OpenPluginFolderCommand = new RelayCommand(() => _explorerService.OpenPath(_paths.PluginRoot));
        OpenAppDataCommand = new RelayCommand(() => _explorerService.OpenPath(_paths.ApplicationDataRoot));
        OpenLogFolderCommand = new RelayCommand(() => _explorerService.OpenPath(_paths.LogRoot));
        OpenSessionLogCommand = new RelayCommand(() => _explorerService.OpenPath(_paths.SessionLogPath));

        foreach (var recentWorkspace in settingsService.Settings.RecentWorkspaces)
        {
            RecentWorkspaces.Add(recentWorkspace);
        }

        foreach (var plugin in pluginCatalog.Plugins.OrderBy(plugin => plugin.Manifest.Name))
        {
            Plugins.Add(new PluginStatusViewModel(plugin, settingsService));
        }

        foreach (var group in RibbonGroupViewModel.Create(pluginCatalog.Tools.Values))
        {
            RibbonGroups.Add(group);
        }

        foreach (var entry in logStore.Entries)
        {
            Logs.Add(entry);
        }

        logStore.EntryAdded += (_, entry) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Logs.Add(entry);
                while (Logs.Count > 250)
                {
                    Logs.RemoveAt(0);
                }
            });
        };

        if (!string.IsNullOrWhiteSpace(settingsService.Settings.LastWorkspacePath))
        {
            _ = OpenWorkspaceAsync(settingsService.Settings.LastWorkspacePath);
        }
    }

    public ObservableCollection<string> RecentWorkspaces { get; } = new();

    public ObservableCollection<PluginStatusViewModel> Plugins { get; } = new();

    public ObservableCollection<RibbonGroupViewModel> RibbonGroups { get; } = new();

    public ObservableCollection<LogEntry> Logs { get; } = new();

    public ObservableCollection<ToolMessage> Messages { get; } = new();

    public ObservableCollection<ToolTableViewModel> Tables { get; } = new();

    public ObservableCollection<SelectionItemViewModel> SelectionItems { get; } = new();

    public ObservableCollection<ToolAction> ToolActions { get; } = new();

    public ICommand OpenWorkspaceCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand RefreshWorkspaceCommand { get; }

    public ICommand RunToolCommand { get; }

    public ICommand ExecuteResultActionCommand { get; }

    public ICommand OpenRecentWorkspaceCommand { get; }

    public ICommand OpenPluginFolderCommand { get; }

    public ICommand OpenAppDataCommand { get; }

    public ICommand OpenLogFolderCommand { get; }

    public ICommand OpenSessionLogCommand { get; }

    public WorkspaceSnapshot? CurrentWorkspace
    {
        get => _currentWorkspace;
        private set
        {
            if (SetProperty(ref _currentWorkspace, value))
            {
                RaisePropertyChanged(nameof(HasWorkspace));
                RaisePropertyChanged(nameof(WorkspaceTitle));
                RaisePropertyChanged(nameof(WorkspacePath));
                RaisePropertyChanged(nameof(ProjectCountText));
                RaisePropertyChanged(nameof(TrackedFilesText));
                ((AsyncRelayCommand)RefreshWorkspaceCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand<string>)ExecuteResultActionCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasWorkspace => CurrentWorkspace is not null;

    public string WorkspaceTitle => CurrentWorkspace?.DisplayName ?? "No workspace loaded";

    public string WorkspacePath => CurrentWorkspace?.SourcePath ?? "Choose a solution, TwinCAT project, or folder.";

    public string ProjectCountText => CurrentWorkspace is null ? "Projects: 0" : $"Projects: {CurrentWorkspace.Projects.Count}";

    public string TrackedFilesText => CurrentWorkspace is null ? "Tracked files: 0" : $"Tracked files: {CurrentWorkspace.AllFiles.Count}";

    public bool HasTableResults
    {
        get => _hasTableResults;
        private set => SetProperty(ref _hasTableResults, value);
    }

    public bool HasSelectionItems
    {
        get => _hasSelectionItems;
        private set
        {
            if (SetProperty(ref _hasSelectionItems, value))
            {
                RaisePropertyChanged(nameof(SelectionSummary));
            }
        }
    }

    public bool HasToolActions
    {
        get => _hasToolActions;
        private set => SetProperty(ref _hasToolActions, value);
    }

    public string ResultTitle
    {
        get => _resultTitle;
        private set => SetProperty(ref _resultTitle, value);
    }

    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(ref _resultSummary, value);
    }

    public string ResultStatus
    {
        get => _resultStatus;
        private set => SetProperty(ref _resultStatus, value);
    }

    public string SelectionSummary
    {
        get
        {
            if (!HasSelectionItems)
            {
                return "No selectable items in the current result.";
            }

            var selectedItems = SelectionItems.Where(item => item.IsSelected).ToList();
            var totalBytes = selectedItems.Sum(item => item.SizeInBytes);
            return $"{selectedItems.Count} item(s) selected, {FormatSize(totalBytes)} reclaimable.";
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string SessionLogPath => _paths.SessionLogPath;

    private async Task OpenWorkspaceAsync()
    {
        var path = await _workspacePickerService.PickWorkspaceFileAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenWorkspaceAsync(path);
        }
    }

    private async Task OpenFolderAsync()
    {
        var path = await _workspacePickerService.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenWorkspaceAsync(path);
        }
    }

    private async Task OpenWorkspaceAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            IsBusy = true;
            var workspace = await _workspaceDiscoveryService.DiscoverAsync(path);
            if (workspace is null)
            {
                _messageDialogService.ShowError("Workspace not found", $"Could not locate a workspace at '{path}'.");
                return;
            }

            CurrentWorkspace = workspace;
            _settingsService.RememberWorkspace(path);
            await _settingsService.SaveAsync();
            SyncRecentWorkspaces();
            ResetResultToWorkspaceOverview();
            _logger.LogInformation("Loaded workspace {WorkspacePath} with {ProjectCount} project(s).", path, workspace.Projects.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open workspace {WorkspacePath}", path);
            _messageDialogService.ShowError("Open workspace failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshWorkspaceAsync()
    {
        if (CurrentWorkspace is null)
        {
            return;
        }

        await OpenWorkspaceAsync(CurrentWorkspace.SourcePath);
    }

    private async Task RunToolAsync(string? toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId) || !_pluginCatalog.Tools.TryGetValue(toolId, out var tool))
        {
            return;
        }

        if (tool.Definition.RequiresWorkspace && CurrentWorkspace is null)
        {
            _messageDialogService.ShowInformation("Workspace required", "Open a TwinCAT workspace before running this tool.");
            return;
        }

        await ExecuteToolAsync(toolId, null);
    }

    private async Task ExecuteResultActionAsync(string? actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId) || string.IsNullOrWhiteSpace(_activeToolId) || CurrentWorkspace is null)
        {
            return;
        }

        var action = ToolActions.FirstOrDefault(candidate => string.Equals(candidate.Id, actionId, StringComparison.OrdinalIgnoreCase));
        if (action?.ConfirmationMessage is { Length: > 0 } confirmationMessage
            && !_messageDialogService.Confirm(action.Label, confirmationMessage))
        {
            return;
        }

        await ExecuteToolAsync(_activeToolId, actionId);
    }

    private async Task ExecuteToolAsync(string toolId, string? actionId)
    {
        if (CurrentWorkspace is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            _activeToolId = toolId;
            var result = await _toolExecutionService.ExecuteAsync(
                toolId,
                new ToolContext
                {
                    Workspace = CurrentWorkspace,
                    ApplicationDataDirectory = _paths.ApplicationDataRoot,
                    SessionDirectory = _paths.SessionDirectory,
                },
                actionId,
                SelectionItems.Select(item => new ToolSelectionState
                {
                    Id = item.Id,
                    IsSelected = item.IsSelected,
                }).ToList());

            ApplyResult(result);
            _logger.LogInformation("Executed tool {ToolId} action {ActionId}.", toolId, actionId ?? "<default>");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolId} failed.", toolId);
            _messageDialogService.ShowError("Tool execution failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyResult(ToolResult result)
    {
        ResultTitle = result.Title;
        ResultSummary = result.Summary;
        ResultStatus = result.Status.ToString();

        Messages.Clear();
        foreach (var message in result.Messages)
        {
            Messages.Add(message);
        }

        Tables.Clear();
        foreach (var table in result.Tables)
        {
            Tables.Add(new ToolTableViewModel(table));
        }

        SelectionItems.Clear();
        foreach (var item in result.SelectionItems.OrderBy(item => item.Group).ThenBy(item => item.Path))
        {
            var selectionItem = new SelectionItemViewModel(item);
            selectionItem.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SelectionItemViewModel.IsSelected))
                {
                    RaisePropertyChanged(nameof(SelectionSummary));
                }
            };
            SelectionItems.Add(selectionItem);
        }

        ToolActions.Clear();
        foreach (var action in result.Actions)
        {
            ToolActions.Add(action);
        }

        HasTableResults = Tables.Count > 0;
        HasSelectionItems = SelectionItems.Count > 0;
        HasToolActions = ToolActions.Count > 0;
        RaisePropertyChanged(nameof(SelectionSummary));
        ((AsyncRelayCommand<string>)ExecuteResultActionCommand).RaiseCanExecuteChanged();
    }

    private void ResetResultToWorkspaceOverview()
    {
        _activeToolId = null;
        ResultTitle = "Workspace Overview";
        ResultSummary = CurrentWorkspace is null
            ? "Open a solution or folder to start exploring TwinCAT projects."
            : $"{CurrentWorkspace.Projects.Count} TwinCAT project(s) discovered in {CurrentWorkspace.DisplayName}.";
        ResultStatus = CurrentWorkspace is null ? "Ready" : "Workspace Loaded";
        Messages.Clear();
        Tables.Clear();
        SelectionItems.Clear();
        ToolActions.Clear();

        if (CurrentWorkspace is not null)
        {
            Tables.Add(new ToolTableViewModel(new ToolTable
            {
                Title = "Discovered TwinCAT Projects",
                Columns = ["Project", "Path", "Supporting Files"],
                Rows = CurrentWorkspace.Projects
                    .Select(project => (IReadOnlyList<string>)new[]
                    {
                        project.Name,
                        project.ProjectPath,
                        project.SupportingFiles.Count.ToString(),
                    })
                    .ToList(),
            }));
        }

        HasTableResults = Tables.Count > 0;
        HasSelectionItems = false;
        HasToolActions = false;
    }

    private void SyncRecentWorkspaces()
    {
        RecentWorkspaces.Clear();
        foreach (var recentWorkspace in _settingsService.Settings.RecentWorkspaces)
        {
            RecentWorkspaces.Add(recentWorkspace);
        }
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

public sealed class RibbonGroupViewModel
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<RibbonToolViewModel> Tools { get; init; } = Array.Empty<RibbonToolViewModel>();

    public static IReadOnlyList<RibbonGroupViewModel> Create(IEnumerable<RegisteredTool> registeredTools)
    {
        return registeredTools
            .Where(tool => string.Equals(tool.Definition.Ribbon.TabKey, "twincat", StringComparison.OrdinalIgnoreCase))
            .GroupBy(tool => tool.Definition.Ribbon.GroupKey)
            .OrderBy(group => group.Min(tool => tool.Definition.Ribbon.Order))
            .Select(group => new RibbonGroupViewModel
            {
                Title = group.First().Definition.Ribbon.GroupTitle,
                Tools = group
                    .OrderBy(tool => tool.Definition.Ribbon.Order)
                    .ThenBy(tool => tool.Definition.DisplayName)
                    .Select(tool => new RibbonToolViewModel
                    {
                        Id = tool.Definition.Id,
                        DisplayName = tool.Definition.DisplayName,
                        Description = tool.Definition.Description,
                    })
                    .ToList(),
            })
            .ToList();
    }
}

public sealed class RibbonToolViewModel
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}

public sealed class PluginStatusViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private bool _isEnabled;

    public PluginStatusViewModel(PluginDescriptor descriptor, ISettingsService settingsService)
    {
        Descriptor = descriptor;
        _settingsService = settingsService;
        _isEnabled = settingsService.IsPluginEnabled(descriptor.Manifest.Id);
    }

    public PluginDescriptor Descriptor { get; }

    public string Name => string.IsNullOrWhiteSpace(Descriptor.Manifest.Name) ? Descriptor.Manifest.Id : Descriptor.Manifest.Name;

    public string Version => Descriptor.Manifest.Version;

    public string State => Descriptor.State.ToString();

    public string Message => Descriptor.Message;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _settingsService.SetPluginEnabled(Descriptor.Manifest.Id, value);
                _ = _settingsService.SaveAsync();
            }
        }
    }
}

public sealed class ToolTableViewModel
{
    public ToolTableViewModel(ToolTable table)
    {
        Title = table.Title;
        var dataTable = new DataTable();
        foreach (var column in table.Columns)
        {
            dataTable.Columns.Add(column);
        }

        foreach (var row in table.Rows)
        {
            dataTable.Rows.Add(row.ToArray());
        }

        View = dataTable.DefaultView;
    }

    public string Title { get; }

    public DataView View { get; }
}

public sealed class SelectionItemViewModel : ObservableObject
{
    private bool _isSelected;

    public SelectionItemViewModel(ToolSelectionItem item)
    {
        Id = item.Id;
        Group = item.Group;
        DisplayName = item.DisplayName;
        Description = item.Description;
        Path = item.Path;
        SizeInBytes = item.SizeInBytes;
        _isSelected = item.IsSelected;
        IsEnabled = item.IsEnabled;
    }

    public string Id { get; }

    public string Group { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string Path { get; }

    public long SizeInBytes { get; }

    public string SizeDisplay => $"{SizeInBytes / 1024d / 1024d:0.##} MB";

    public bool IsEnabled { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
