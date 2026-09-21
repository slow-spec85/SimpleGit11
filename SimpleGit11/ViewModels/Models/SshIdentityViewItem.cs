using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.ViewModels;

public sealed partial class SshIdentityViewItem
{
    private readonly Func<SshIdentityViewItem, Task> _remove;
    private readonly Func<string, Task> _showPublicKey;
    private readonly Action<string> _copy;

    public SshIdentityViewItem(
        SshIdentity identity,
        Func<SshIdentityViewItem, Task> remove,
        Action<string> copy,
        Func<string, Task> showPublicKey)
    {
        Identity = identity;
        _remove = remove;
        _copy = copy;
        _showPublicKey = showPublicKey;
    }

    public SshIdentity Identity { get; }

    public string PrivateKeyPath => Identity.PrivateKeyPath;

    public string PublicKey => Identity.PublicKey;

    public string Fingerprint => Identity.Fingerprint;

    [RelayCommand]
    private void OnCopyPath() => _copy(PrivateKeyPath);

    [RelayCommand]
    private void OnCopyFingerprint() => _copy(Fingerprint);

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnShowPublicKeyAsync() => _showPublicKey(PublicKey);

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnRemoveAsync() => _remove(this);
}
