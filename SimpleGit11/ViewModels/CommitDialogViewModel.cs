using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;

namespace SimpleGit11.ViewModels;

public sealed partial class CommitDialogViewModel : ViewModelBase
{
    private readonly IGitService _gitService;
    private readonly ILocalizationService _localizationService;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IReadOnlyList<GitChangedFile> _changedFiles;
    private readonly CommitDialogMode _mode;

    public CommitDialogViewModel(
        IGitService gitService,
        ILocalizationService localizationService,
        MainWindowViewModel mainWindowViewModel,
        CommitDialogRequest request)
    {
        _gitService = gitService;
        _localizationService = localizationService;
        _mainWindowViewModel = mainWindowViewModel;
        _mode = request.Mode;
        Message = request.InitialMessage;
        EditMessage = _mode != CommitDialogMode.Amend;
        _changedFiles = request.ChangedFiles
            .GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool IsAmend => _mode == CommitDialogMode.Amend;

    public bool IsMerge => _mode == CommitDialogMode.Merge;

    public bool IsAmendOperation => _mode is CommitDialogMode.Amend or CommitDialogMode.EditMessage;

    public string Title => _mode switch
    {
        CommitDialogMode.Amend => _localizationService.GetString("AmendDialogTitle"),
        CommitDialogMode.Merge => _localizationService.GetString("MergeCommitDialogTitle"),
        CommitDialogMode.EditMessage => _localizationService.GetString("EditCommitMessageDialogTitle"),
        _ => _localizationService.GetString("CommitDialogTitle")
    };

    public string PrimaryButtonText => _mode == CommitDialogMode.EditMessage
        ? _localizationService.GetString("EditCommitMessageDialogPrimaryButton")
        : _localizationService.GetString("CommitDialogPrimaryButton");

    public string CloseButtonText =>
        _localizationService.GetString("ConfirmationDialogCancelButton");

    public string MessageHeader => _mode == CommitDialogMode.EditMessage
        ? _localizationService.GetString("EditCommitMessageDialogHeader")
        : _localizationService.GetString("CommitMessageDialogHeader");

    public string MessagePlaceholder => _mode == CommitDialogMode.EditMessage
        ? _localizationService.GetString("EditCommitMessageDialogPlaceholder")
        : _localizationService.GetString("CommitMessageDialogPlaceholder");

    public string MessageHelpText =>
        _localizationService.GetString("CommitMessageDialogHelpText");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    public partial string Message { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    public partial bool EditMessage { get; set; }

    public bool CanCommit => !(EditMessage && string.IsNullOrWhiteSpace(Message));

    public ObservableCollection<GitChangedFile> FileSuggestions { get; } = [];

    partial void OnEditMessageChanged(bool value)
    {
        if (value && _mode == CommitDialogMode.Amend)
        {
            _ = GetLastCommitAsync();
        }
        else if (!value)
        {
            Message = "";
        }
    }

    public void FilterFileSuggestions(string query)
    {
        IEnumerable<GitChangedFile> filteredFiles = _changedFiles
            .Where(file => file.FileName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(
                file => !file.FileName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase);

        FileSuggestions.Clear();
        foreach (GitChangedFile file in filteredFiles)
        {
            FileSuggestions.Add(file);
        }
    }

    private async Task GetLastCommitAsync()
    {
        if (_mainWindowViewModel.CurrentRepository != null)
        {
            GitCommit commit = await _gitService.GetLastCommitAsync(
                _mainWindowViewModel.CurrentRepository);
            Message = commit.Message;
        }
    }
}
