using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Windows.Input;

namespace SimpleGit11.Controls;

public sealed partial class OperationProgressOverlay : UserControl
{
    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(
        nameof(IsRunning),
        typeof(bool),
        typeof(OperationProgressOverlay),
        new PropertyMetadata(false));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message),
        typeof(string),
        typeof(OperationProgressOverlay),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(
        nameof(CancelCommand),
        typeof(ICommand),
        typeof(OperationProgressOverlay),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CancelButtonVisibilityProperty = DependencyProperty.Register(
        nameof(CancelButtonVisibility),
        typeof(Visibility),
        typeof(OperationProgressOverlay),
        new PropertyMetadata(Visibility.Collapsed));

    public OperationProgressOverlay()
    {
        InitializeComponent();
    }

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public Visibility CancelButtonVisibility
    {
        get => (Visibility)GetValue(CancelButtonVisibilityProperty);
        set => SetValue(CancelButtonVisibilityProperty, value);
    }
}
