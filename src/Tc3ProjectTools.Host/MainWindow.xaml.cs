using System.Windows;
using System.Windows.Controls;
using Fluent;

namespace Tc3ProjectTools.Host;

public partial class MainWindow : RibbonWindow
{
    private readonly ShellViewModel _viewModel;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        BuildDynamicRibbon();
    }

    private void BuildDynamicRibbon()
    {
        TwinCatTab.Groups.Clear();
        foreach (var group in _viewModel.RibbonGroups)
        {
            var ribbonGroup = new RibbonGroupBox
            {
                Header = group.Title,
            };

            foreach (var tool in group.Tools)
            {
                ribbonGroup.Items.Add(new Fluent.Button
                {
                    Header = tool.DisplayName,
                    ToolTip = tool.Description,
                    Command = _viewModel.RunToolCommand,
                    CommandParameter = tool.Id,
                    LargeIcon = new TextBlock
                    {
                        Text = GetAbbreviation(tool.DisplayName),
                        FontSize = 22,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(8),
                    },
                });
            }

            TwinCatTab.Groups.Add(ribbonGroup);
        }
    }

    private static string GetAbbreviation(string displayName)
    {
        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return "T";
        }

        if (words.Length == 1)
        {
            return words[0][..1].ToUpperInvariant();
        }

        return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }
}
