using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Windows.Input;

namespace SimpleGit11.Controls;

public sealed partial class NotificationOverlay : UserControl
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen),
        typeof(bool),
        typeof(NotificationOverlay),
        new PropertyMetadata(false, OnIsOpenChanged));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message),
        typeof(string),
        typeof(NotificationOverlay),
        new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details),
        typeof(string),
        typeof(NotificationOverlay),
        new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity),
        typeof(InfoBarSeverity),
        typeof(NotificationOverlay),
        new PropertyMetadata(InfoBarSeverity.Informational));

    public static readonly DependencyProperty CopyCommandProperty = DependencyProperty.Register(
        nameof(CopyCommand),
        typeof(ICommand),
        typeof(NotificationOverlay),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand),
        typeof(ICommand),
        typeof(NotificationOverlay),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent),
        typeof(object),
        typeof(NotificationOverlay),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ActionVisibilityProperty = DependencyProperty.Register(
        nameof(ActionVisibility),
        typeof(Visibility),
        typeof(NotificationOverlay),
        new PropertyMetadata(Visibility.Collapsed));

    public NotificationOverlay()
    {
        InitializeComponent();
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string Details
    {
        get => (string)GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public ICommand? CopyCommand
    {
        get => (ICommand?)GetValue(CopyCommandProperty);
        set => SetValue(CopyCommandProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public Visibility ActionVisibility
    {
        get => (Visibility)GetValue(ActionVisibilityProperty);
        set => SetValue(ActionVisibilityProperty, value);
    }

    private static void OnIsOpenChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is NotificationOverlay overlay && overlay.DetailsExpander is not null)
        {
            overlay.DetailsExpander.IsExpanded = false;
        }
    }

    private static void OnContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is NotificationOverlay overlay)
        {
            overlay.UpdateContentState();
        }
    }

    private void UpdateContentState()
    {
        if (DetailsExpander is null || CopyMenuFlyoutItem is null)
        {
            return;
        }

        CopyMenuFlyoutItem.CommandParameter = !string.IsNullOrWhiteSpace(Details)
            ? $"{Message.Trim()}{System.Environment.NewLine}{System.Environment.NewLine}{Details.Trim()}"
            : Message;
    }
}
