using Windows.ApplicationModel.DataTransfer;

namespace SimpleGit11.Presentation.Services;

public sealed class ClipboardService : SimpleGit11.Services.IClipboardService
{
    public void SetText(string text)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
    }
}
