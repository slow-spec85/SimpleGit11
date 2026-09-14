namespace SimpleGit11.Extensibility.Presentation;

/// <summary>Shows system file pickers for plugin-owned workflows.</summary>
public interface IPluginStoragePicker
{
    Task<string?> PickFileAsync();
    Task<string?> PickSaveFileAsync(string suggestedFileName);
}
