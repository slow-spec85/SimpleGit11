namespace SimpleGit11.Models;

public enum BranchCreationMode
{
    FromCommit,
    CheckoutFromCommit,
    CheckoutEmptyOrphan,
    EmptyOrphanWithInitialCommit,
    CheckoutOrphanFromCommit,
    OrphanFromCommit
}

public enum OrphanBranchContentMode
{
    Empty,
    StartPointSnapshot
}

public sealed class BranchCreationRequest
{
    public BranchCreationRequest(
        string branchName,
        string? startPointHash,
        BranchCreationMode mode)
    {
        BranchName = branchName;
        StartPointHash = startPointHash;
        Mode = mode;
    }

    public string BranchName { get; }

    public string? StartPointHash { get; }

    public BranchCreationMode Mode { get; }
}
