using System;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed record SshConnectionProfile(
    string Id,
    string Host,
    int Port,
    string Username,
    string? PrivateKeyPath,
    string? ExpectedHostKey,
    DateTimeOffset LastUsedAt);
