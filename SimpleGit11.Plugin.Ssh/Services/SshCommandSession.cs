using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed class SshCommandSession : IAsyncDisposable
{
    private readonly SshClient _client;
    private readonly SshConnectionMonitor _connectionMonitor;
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    private SshCommandSession(SshClient client, SshConnectionMonitor connectionMonitor)
    {
        _client = client;
        _connectionMonitor = connectionMonitor;
        _client.ErrorOccurred += Client_ErrorOccurred;
    }

    public static async Task<SshCommandSession> ConnectAsync(
        SshConnectionSettings settings,
        SshConnectionMonitor connectionMonitor,
        CancellationToken cancellationToken)
    {
        SshClient client = new(CreateConnectionInfo(settings));
        try
        {
            await ConnectAndVerifyHostKeyAsync(client, settings, cancellationToken);
            return new SshCommandSession(client, connectionMonitor);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<SshCommandResult> ExecuteAsync(
        string commandText,
        string? standardInput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            using SshCommand command = _client.CreateCommand(commandText, Encoding.UTF8);
            Task execution = command.ExecuteAsync(cancellationToken);
            if (standardInput is not null)
            {
                using Stream input = command.CreateInputStream();
                byte[] bytes = Encoding.UTF8.GetBytes(standardInput);
                await input.WriteAsync(bytes, cancellationToken);
                await input.FlushAsync(cancellationToken);
            }

            await execution;
            return new SshCommandResult(
                command.ExitStatus ?? -1,
                command.Result,
                command.Error);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException && !_client.IsConnected)
        {
            _connectionMonitor.Report(exception);
            throw;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private void Client_ErrorOccurred(object? sender, ExceptionEventArgs e)
    {
        _connectionMonitor.Report(e.Exception);
    }

    public async ValueTask DisposeAsync()
    {
        await _commandLock.WaitAsync();
        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }

            _client.Dispose();
        }
        finally
        {
            _commandLock.Release();
            _commandLock.Dispose();
        }
    }

    internal static ConnectionInfo CreateConnectionInfo(SshConnectionSettings settings)
    {
        AuthenticationMethod authentication;
        if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
        {
            PrivateKeyFile key = settings.PrivateKeyPassphrase is null
                ? new PrivateKeyFile(settings.PrivateKeyPath)
                : new PrivateKeyFile(settings.PrivateKeyPath, settings.PrivateKeyPassphrase);
            authentication = new PrivateKeyAuthenticationMethod(settings.Username, key);
        }
        else
        {
            authentication = new PasswordAuthenticationMethod(
                settings.Username,
                settings.Password ?? string.Empty);
        }

        return new ConnectionInfo(
            settings.Host,
            settings.Port,
            settings.Username,
            authentication);
    }

    internal static async Task ConnectAndVerifyHostKeyAsync(
        BaseClient client,
        SshConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        string? observedFingerprint = null;
        client.HostKeyReceived += (_, args) =>
        {
            observedFingerprint = $"SHA256:{args.FingerPrintSHA256}";
            args.CanTrust = string.Equals(
                NormalizeFingerprint(settings.ExpectedHostKey),
                NormalizeFingerprint(observedFingerprint),
                StringComparison.Ordinal);
        };

        try
        {
            await client.ConnectAsync(cancellationToken);
        }
        catch (SshConnectionException) when (observedFingerprint is not null)
        {
            throw new SshHostKeyVerificationException(
                settings.Host,
                observedFingerprint,
                settings.ExpectedHostKey);
        }
    }

    private static string? NormalizeFingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        return fingerprint.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase)
            ? fingerprint[7..]
            : fingerprint;
    }
}

public sealed record SshCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
