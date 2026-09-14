namespace SimpleGit11.Services;

public sealed record SshIdentityCreationRequest(
    string Host,
    string Passphrase,
    string PassphraseConfirmation);
