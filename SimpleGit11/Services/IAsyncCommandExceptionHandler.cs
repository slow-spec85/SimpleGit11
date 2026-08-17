using System;

namespace SimpleGit11.Services;

public interface IAsyncCommandExceptionHandler
{
    void Handle(Exception exception);
}
