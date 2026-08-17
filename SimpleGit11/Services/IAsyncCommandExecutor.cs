using System;
using System.Threading.Tasks;

namespace SimpleGit11.Services;

public interface IAsyncCommandExecutor
{
    Task ExecuteAsync(Func<Task> operation);
}
