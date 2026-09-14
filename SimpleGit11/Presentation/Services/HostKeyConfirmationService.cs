using System;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Presentation.Services;

public sealed class HostKeyConfirmationService : IHostKeyConfirmationService
{
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;
    private readonly IExecutionContextService _executionContextService;

    public HostKeyConfirmationService(
        IDialogService dialogService,
        ILocalizationService localizationService,
        IExecutionContextService executionContextService)
    {
        _dialogService = dialogService;
        _localizationService = localizationService;
        _executionContextService = executionContextService;
    }

    public async Task<bool> ConfirmAsync(
        HostKeyConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string endpoint = confirmation.Port == 22
            ? confirmation.Host
            : $"{confirmation.Host}:{confirmation.Port}";
        string fingerprints = string.Join(Environment.NewLine, confirmation.Fingerprints);
        string message = _executionContextService.Current.IsLocal
            ? string.Format(
                _localizationService.GetString("SshHostKeyDialogMessage"),
                endpoint,
                fingerprints)
            : string.Format(
                _localizationService.GetString("RemoteExecutionSshHostKeyDialogMessage"),
                endpoint,
                _executionContextService.Current.DisplayMachineName,
                fingerprints);
        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("SshHostKeyDialogTitle"),
            message,
            _localizationService.GetString("SshHostKeyDialogPrimaryButton"));
        cancellationToken.ThrowIfCancellationRequested();
        return confirmed;
    }
}
