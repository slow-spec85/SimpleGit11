using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.ViewModels;

public sealed partial class FoundRepositoryViewItem
{
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly Func<string, Task> _open;
    private readonly Action<string> _copy;

    public FoundRepositoryViewItem(
        RepositoryInfo repository,
        IAsyncCommandExecutor asyncCommandExecutor,
        Func<string, Task> open,
        Action<string> copy)
    {
        Repository = repository;
        _asyncCommandExecutor = asyncCommandExecutor
            ?? throw new ArgumentNullException(nameof(asyncCommandExecutor));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
    }

    public RepositoryInfo Repository { get; }

    public string Name => Repository.Name;

    public string Path => Repository.Path;

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => _open(Path));
    }

    [RelayCommand]
    private void OnCopyPath()
    {
        _copy(Path);
    }

    [RelayCommand]
    private void OnCopyName()
    {
        _copy(Name);
    }
}
