using System.Windows.Input;

namespace SimpleGit11.Messages;

public enum AppNotificationSeverity
{
    Informational,
    Success,
    Warning,
    Error
}

public sealed record AppNotificationMessage(
    object Source,
    AppNotificationSeverity Severity,
    string Message,
    string Details,
    ICommand? ActionCommand = null,
    string? ActionText = null);

public sealed record ClearAppNotificationMessage(object Source);
