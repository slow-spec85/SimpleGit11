namespace SimpleGit11.Extensibility.Presentation;

/// <summary>Displays plugin operation progress and errors in the host window.</summary>
public interface IPluginOperationFeedback
{
    void Start(object source, string message, Action cancel);
    void Stop(object source);
    void ShowError(object source, string message, string details);
    void ClearError(object source);
}
