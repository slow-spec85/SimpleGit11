using System.Windows.Input;

namespace SimpleGit11.Messages;

public sealed record AppOperationMessage(
    object Source,
    bool IsRunning,
    string Message,
    ICommand? CancelCommand = null);
