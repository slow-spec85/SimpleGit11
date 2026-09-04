using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SimpleGit11.Plugin.Ssh.Models;
using SimpleGit11.Plugin.Ssh.Services;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using AppExecutionContext = SimpleGit11.Services.Execution.ExecutionContext;

namespace SimpleGit11.Plugin.Ssh.Presentation;

/// <summary>Connection workflow independent of the shell and concrete dialog implementation.</summary>
internal sealed class SshConnectionController(
    IExecutionContextService executionContextService,
    ISshConnectionProfileStore sshConnectionProfileStore,
    ISshConnectionDialogService dialogService,
    ISshLocalizationService localizationService) : ObservableObject
{
    private readonly IExecutionContextService _executionContextService = executionContextService;
    private readonly ISshConnectionProfileStore _sshConnectionProfileStore = sshConnectionProfileStore;
    private readonly ISshConnectionDialogService _dialogService = dialogService;
    private readonly ISshLocalizationService _localizationService = localizationService;
    private int _isBusy;

    public bool IsBusy => Volatile.Read(ref _isBusy) != 0;

    public async Task ToggleAsync()
    {
        if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            OnPropertyChanged(nameof(IsBusy));
            await ToggleCoreAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _isBusy, 0);
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private async Task ToggleCoreAsync()
    {
        AppExecutionContext initialContext = _executionContextService.Current;
        Guid initialContextId = initialContext.Id;
        if (!initialContext.IsLocal)
        {
            if (initialContext.ProviderId == SshPlugin.ProviderId)
            {
                bool disconnect = await _dialogService.ConfirmAsync(
                    _localizationService.GetString("SshDisconnectDialogTitle"),
                    string.Format(
                        _localizationService.GetString("SshDisconnectDialogMessage"),
                        initialContext.DisplayMachineName),
                    _localizationService.GetString("SshDisconnectConfirmButton"));
                if (disconnect && _executionContextService.Current.Id == initialContextId)
                {
                    await _executionContextService.UseLocalAsync();
                }
            }
            return;
        }

        while (_executionContextService.Current.Id == initialContextId)
        {
            IReadOnlyList<SshConnectionProfile> profiles = _sshConnectionProfileStore.Load();
            SshConnectionDialogResult? result = await _dialogService.ShowSshConnectionDialogAsync(
                profiles,
                profiles.FirstOrDefault()?.Id);
            if (result is null || _executionContextService.Current.Id != initialContextId)
            {
                return;
            }

            if (result.Action == SshConnectionDialogAction.DeleteProfile)
            {
                bool deleteProfile = await _dialogService.ConfirmAsync(
                    _localizationService.GetString("SshDeleteProfileDialogTitle"),
                    string.Format(
                        _localizationService.GetString("SshDeleteProfileDialogMessage"),
                        result.Username,
                        result.Host,
                        result.Port),
                    _localizationService.GetString("SshDeleteProfileConfirmButton"));
                if (deleteProfile)
                {
                    _sshConnectionProfileStore.Delete(result.ProfileId);
                }

                continue;
            }

            string? expectedHostKey = result.ExpectedHostKey;
            while (_executionContextService.Current.Id == initialContextId)
            {
                try
                {
                    await _executionContextService.ActivateAsync(
                        SshPlugin.ProviderId,
                        CreateSshConnectionRequest(result, expectedHostKey));
                    if (result.RememberProfile)
                    {
                        _sshConnectionProfileStore.Upsert(new SshConnectionProfile(
                            result.ProfileId,
                            result.Host,
                            result.Port,
                            result.Username,
                            result.PrivateKeyPath,
                            expectedHostKey,
                            DateTimeOffset.UtcNow));
                    }
                    return;
                }
                catch (SshHostKeyVerificationException exception)
                {
                    bool trust = await _dialogService.ConfirmAsync(
                        _localizationService.GetString("SshHostKeyDialogTitle"),
                        string.Format(
                            _localizationService.GetString("SshHostKeyDialogMessage"),
                            exception.Host,
                            exception.Fingerprint),
                        _localizationService.GetString("SshTrustHostKeyButton"));
                    if (!trust)
                    {
                        return;
                    }

                    expectedHostKey = exception.Fingerprint;
                }
            }
        }
    }

    private static ExecutionConnectionRequest CreateSshConnectionRequest(
        SshConnectionDialogResult result,
        string? expectedHostKey)
    {
        Dictionary<string, string> settings = new()
        {
            [SshConnectionRequestKeys.Host] = result.Host,
            [SshConnectionRequestKeys.Port] = result.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [SshConnectionRequestKeys.Username] = result.Username
        };
        if (result.PrivateKeyPath is not null)
        {
            settings[SshConnectionRequestKeys.PrivateKeyPath] = result.PrivateKeyPath;
        }

        if (expectedHostKey is not null)
        {
            settings[SshConnectionRequestKeys.ExpectedHostKey] = expectedHostKey;
        }

        Dictionary<string, string> secrets = new();
        if (result.Password is not null)
        {
            secrets[SshConnectionRequestKeys.Password] = result.Password;
        }

        if (result.PrivateKeyPassphrase is not null)
        {
            secrets[SshConnectionRequestKeys.PrivateKeyPassphrase] = result.PrivateKeyPassphrase;
        }

        return new ExecutionConnectionRequest(
            result.RememberProfile ? result.ProfileId : null,
            settings,
            secrets);
    }
}
