using System;

namespace SimpleGit11.Services;

public sealed class AsyncCommandExceptionHandler : IAsyncCommandExceptionHandler
{
    private const string LogFileName = "SimpleGit11-command-error.log";

    public void Handle(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ExceptionLogWriter.Write(LogFileName, exception);
    }
}
