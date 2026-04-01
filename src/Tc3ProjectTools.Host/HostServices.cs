using System.IO;
using System.Diagnostics;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Forms = System.Windows.Forms;
using WinMessageBox = System.Windows.MessageBox;

namespace Tc3ProjectTools.Host;

public interface IWorkspacePickerService
{
    Task<string?> PickWorkspaceFileAsync();

    Task<string?> PickFolderAsync();
}

public sealed class WorkspacePickerService : IWorkspacePickerService
{
    public Task<string?> PickWorkspaceFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Solutions and TwinCAT projects|*.sln;*.tsproj;*.plcproj|All files|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> PickFolderAsync()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select a TwinCAT workspace folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        return Task.FromResult(dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null);
    }
}

public interface IExplorerService
{
    void OpenPath(string path);
}

public sealed class ExplorerService : IExplorerService
{
    public void OpenPath(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}

public interface IMessageDialogService
{
    bool Confirm(string title, string message);

    void ShowInformation(string title, string message);

    void ShowError(string title, string message);
}

public sealed class MessageDialogService : IMessageDialogService
{
    public bool Confirm(string title, string message)
    {
        return WinMessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
    }

    public void ShowInformation(string title, string message)
    {
        WinMessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    public void ShowError(string title, string message)
    {
        WinMessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
}
