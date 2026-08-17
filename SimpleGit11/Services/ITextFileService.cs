using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface ITextFileService
{
    Task<TextFileDocument> ReadAsync(RepositoryInfo repository, string relativePath);

    Task WriteAsync(TextFileDocument document, string text);
}
