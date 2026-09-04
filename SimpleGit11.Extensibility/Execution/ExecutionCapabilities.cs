using System;

namespace SimpleGit11.Services.Execution;

[Flags]
public enum ExecutionCapabilities
{
    None = 0,
    LocalMachine = 1 << 0,
    Git = 1 << 1,
    ReadFiles = 1 << 2,
    WriteFiles = 1 << 3,
    TransferFiles = 1 << 4,
    OpenInLocalFileExplorer = 1 << 5
}
