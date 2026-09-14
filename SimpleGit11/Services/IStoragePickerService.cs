using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IStoragePickerService
{
    Task<string?> PickFolderAsync();

    Task<string?> PickFileAsync();

    Task<string?> PickSaveFileAsync(string suggestedFileName);

    Task<string?> PickArchiveFileAsync(string suggestedFileName, GitArchiveFormat format);
}
