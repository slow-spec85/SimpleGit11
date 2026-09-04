namespace SimpleGit11.Services.Execution;

public interface IRepositoryPathService
{
    RepositoryPathStyle Style { get; }

    string Combine(string left, string right);

    string? GetParent(string path);

    string GetFileName(string path);

    string Normalize(string path);
}
