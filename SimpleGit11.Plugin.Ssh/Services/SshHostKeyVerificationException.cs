using System;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed class SshHostKeyVerificationException : Exception
{
    public SshHostKeyVerificationException(
        string host,
        string fingerprint,
        string? expectedFingerprint)
        : base(expectedFingerprint is null
            ? $"The SSH host key for '{host}' has not been trusted."
            : $"The SSH host key for '{host}' does not match the trusted key.")
    {
        Host = host;
        Fingerprint = fingerprint;
        ExpectedFingerprint = expectedFingerprint;
    }

    public string Host { get; }

    public string Fingerprint { get; }

    public string? ExpectedFingerprint { get; }
}
