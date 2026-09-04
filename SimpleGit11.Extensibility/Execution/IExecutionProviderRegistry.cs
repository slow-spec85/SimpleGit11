namespace SimpleGit11.Services.Execution;

public interface IExecutionProviderRegistry
{
    IExecutionProvider GetRequiredProvider(string providerId);
}
