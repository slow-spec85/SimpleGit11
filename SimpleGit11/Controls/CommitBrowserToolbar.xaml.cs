using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Controls;

public sealed partial class CommitBrowserToolbar : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(CommitBrowserViewModelBase),
        typeof(CommitBrowserToolbar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ActionCommandsProperty = DependencyProperty.Register(
        nameof(ActionCommands),
        typeof(CommandBar),
        typeof(CommitBrowserToolbar),
        new PropertyMetadata(null));

    public CommitBrowserToolbar()
    {
        InitializeComponent();
    }

    public CommitBrowserViewModelBase? ViewModel
    {
        get => (CommitBrowserViewModelBase?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public CommandBar? ActionCommands
    {
        get => (CommandBar?)GetValue(ActionCommandsProperty);
        set => SetValue(ActionCommandsProperty, value);
    }
}
