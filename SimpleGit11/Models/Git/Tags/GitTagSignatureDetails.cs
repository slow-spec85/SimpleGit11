namespace SimpleGit11.Models;

public enum GitTagSignatureStatus
{
    NotSigned,
    Valid,
    Invalid,
    UnknownKey,
    Unavailable
}

public enum GitSignatureType
{
    Unknown,
    OpenPgp,
    Ssh,
    X509
}

public sealed record GitTagSignatureDetails(
    GitTagSignatureStatus Status,
    GitSignatureType SignatureType,
    string Signer,
    string KeyId,
    string Fingerprint,
    string Diagnostic);
