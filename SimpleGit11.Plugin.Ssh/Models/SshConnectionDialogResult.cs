namespace SimpleGit11.Plugin.Ssh.Models;

public enum SshConnectionDialogAction
{
    Connect,
    DeleteProfile
}

public sealed record SshConnectionDialogResult(
    SshConnectionDialogAction Action,
    string ProfileId,
    string Host,
    int Port,
    string Username,
    string? Password,
    string? PrivateKeyPath,
    string? PrivateKeyPassphrase,
    string? ExpectedHostKey,
    bool RememberProfile);
