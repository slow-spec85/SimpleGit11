using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using SimpleGit11.Models;
using SimpleGit11.Services;
using WinRT.Interop;

namespace SimpleGit11.Presentation.Services;

public sealed class StoragePickerService : IStoragePickerService
{
    private WindowId _windowId;
    private bool _isWindowRegistered;

    public void RegisterWindow(Window window)
    {
        IntPtr windowHandle = WindowNative.GetWindowHandle(window);
        _windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        _isWindowRegistered = true;
    }

    public async Task<string?> PickFolderAsync()
    {
        EnsureWindowRegistered();

        FolderPicker picker = new(_windowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        PickFolderResult? result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }

    public async Task<string?> PickArchiveFileAsync(
        string suggestedFileName,
        GitArchiveFormat format)
    {
        EnsureWindowRegistered();

        string extension = format switch
        {
            GitArchiveFormat.Zip => ".zip",
            GitArchiveFormat.TarGZip => ".tar.gz",
            GitArchiveFormat.Tar => ".tar",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        string formatName = format switch
        {
            GitArchiveFormat.Zip => "ZIP",
            GitArchiveFormat.TarGZip => "TAR.GZ",
            GitArchiveFormat.Tar => "TAR",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        FileSavePicker picker = new(_windowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName,
            DefaultFileExtension = extension,
            ShowOverwritePrompt = true
        };
        picker.FileTypeChoices.Add(formatName, new List<string> { extension });

        PickFileResult? result = await picker.PickSaveFileAsync();
        return result?.Path;
    }

    private void EnsureWindowRegistered()
    {
        if (!_isWindowRegistered)
        {
            throw new InvalidOperationException("The main window must be registered before showing storage pickers.");
        }
    }
}
