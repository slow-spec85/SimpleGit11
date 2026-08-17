using System;

namespace SimpleGit11.Models;

public sealed class NavigationRequestedEventArgs(
    AppNavigationTarget target,
    object? parameter = null) : EventArgs
{
    public AppNavigationTarget Target { get; } = target;

    public object? Parameter { get; } = parameter;
}
