using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Dialogs;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Presentation.Services;

public sealed class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILocalizationService _localizationService;
    private bool _isAboutDialogOpen;
    private Window? _window;

    public DialogService(
        IServiceProvider serviceProvider,
        ILocalizationService localizationService)
    {
        _serviceProvider = serviceProvider;
        _localizationService = localizationService;
    }

    public void RegisterWindow(Window window)
    {
        _window = window;
    }

    public async Task ShowAboutAsync()
    {
        EnsureWindowRegistered();
        if (_isAboutDialogOpen)
        {
            return;
        }

        _isAboutDialogOpen = true;
        try
        {
            AboutDialogViewModel viewModel = _serviceProvider.GetRequiredService<AboutDialogViewModel>();
            AboutDialog dialog = new(viewModel)
            {
                XamlRoot = _window!.Content.XamlRoot
            };
            ApplyTheme(dialog);
            await dialog.ShowAsync();
        }
        finally
        {
            _isAboutDialogOpen = false;
        }
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText)
    {
        EnsureWindowRegistered();

        TextBlock content = new()
        {
            Text = message,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        ContentDialog dialog = new()
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = _localizationService.GetString("ConfirmationDialogCancelButton"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<int?> ShowCherryPickMainlineDialogAsync(GitCommit commit)
    {
        EnsureWindowRegistered();
        RadioButtons parentOptions = new()
        {
            Header = _localizationService.GetString("CherryPickMainlineDialogHeader"),
            SelectedIndex = 0
        };
        for (int index = 0; index < commit.ParentHashes.Count; index++)
        {
            string parentHash = commit.ParentHashes[index];
            string shortHash = parentHash.Length > 8 ? parentHash[..8] : parentHash;
            string relationship = _localizationService.GetString(index == 0
                ? "CommitParentMainline"
                : "CommitParentMergedHistory");
            parentOptions.Items.Add(string.Format(
                _localizationService.GetString("CherryPickMainlineParentOption"),
                index + 1,
                shortHash,
                relationship));
        }

        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = string.Format(
                _localizationService.GetString("CherryPickMainlineDialogMessage"),
                commit.ShortHash,
                commit.Title),
            TextWrapping = TextWrapping.WrapWholeWords
        });
        content.Children.Add(parentOptions);

        ContentDialog dialog = new()
        {
            Title = _localizationService.GetString("CherryPickMainlineDialogTitle"),
            Content = content,
            PrimaryButtonText = _localizationService.GetString("CherryPickMainlineDialogPrimaryButton"),
            CloseButtonText = _localizationService.GetString("ConfirmationDialogCancelButton"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? parentOptions.SelectedIndex + 1
            : null;
    }

    public async Task<bool> ConfirmCherryPickAsync(
        string branchName,
        IReadOnlyList<GitCommit> commits,
        GitCherryPickOptions options)
    {
        EnsureWindowRegistered();
        string messageKey = commits.Count == 1
            ? "CherryPickConfirmationDialogSingleMessage"
            : "CherryPickConfirmationDialogMultipleMessage";
        StackPanel content = new() { MinWidth = 440, Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = string.Format(
                _localizationService.GetString(messageKey),
                commits.Count,
                branchName),
            TextWrapping = TextWrapping.WrapWholeWords
        });
        content.Children.Add(new ListView
        {
            MaxHeight = 280,
            ItemsSource = commits
                .Select(commit => $"{commit.ShortHash}  {commit.Title}")
                .ToArray(),
            SelectionMode = ListViewSelectionMode.None
        });
        string optionMessageKey = options switch
        {
            { NoCommit: true } => "CherryPickNoCommitConfirmationNotice",
            { AppendSourceReference: true } => "CherryPickSourceConfirmationNotice",
            { AddSignOff: true } => "CherryPickSignOffConfirmationNotice",
            _ => ""
        };
        if (!string.IsNullOrEmpty(optionMessageKey))
        {
            content.Children.Add(new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    "TextFillColorSecondaryBrush"],
                Text = _localizationService.GetString(optionMessageKey),
                TextWrapping = TextWrapping.WrapWholeWords
            });
        }

        if (options.MainlineParentNumber is int mainlineParentNumber)
        {
            content.Children.Add(new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    "TextFillColorSecondaryBrush"],
                Text = string.Format(
                    _localizationService.GetString("CherryPickMainlineConfirmationNotice"),
                    mainlineParentNumber),
                TextWrapping = TextWrapping.WrapWholeWords
            });
        }

        ContentDialog dialog = new()
        {
            Title = _localizationService.GetString("CherryPickConfirmationDialogTitle"),
            Content = content,
            PrimaryButtonText = _localizationService.GetString("CherryPickConfirmationDialogPrimaryButton"),
            CloseButtonText = _localizationService.GetString("ConfirmationDialogCancelButton"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<TagConflictResolution> ShowTagConflictDialogAsync(
        string tagName,
        string localHash,
        string remoteHash,
        string remoteName)
    {
        EnsureWindowRegistered();
        TextBlock content = new()
        {
            Text = string.Format(
                _localizationService.GetString("RemoteTagConflictDialogMessage"),
                tagName,
                localHash,
                remoteName,
                remoteHash),
            TextWrapping = TextWrapping.WrapWholeWords
        };
        ContentDialog dialog = new()
        {
            Title = _localizationService.GetString("RemoteTagConflictDialogTitle"),
            Content = content,
            PrimaryButtonText = _localizationService.GetString("RemoteTagConflictReplaceButton"),
            SecondaryButtonText = _localizationService.GetString("RemoteTagConflictTemporaryButton"),
            CloseButtonText = _localizationService.GetString("ConfirmationDialogCancelButton"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => TagConflictResolution.ReplaceLocal,
            ContentDialogResult.Secondary => TagConflictResolution.OpenRemoteTemporarily,
            _ => TagConflictResolution.Cancel
        };
    }

    public async Task<string?> ShowTextInputAsync(TextInputDialogRequest request)
    {
        EnsureWindowRegistered();

        TextInputDialogViewModel viewModel = new(request, CreateValidationMessages());
        TextInputDialog dialog = new(viewModel)
        {
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        string text = viewModel.Text.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public async Task<CommitDialogResult?> ShowCommitDialogAsync(CommitDialogRequest request)
    {
        EnsureWindowRegistered();

        CommitDialogViewModel viewModel = ActivatorUtilities.CreateInstance<CommitDialogViewModel>(
            _serviceProvider,
            request);
        CommitDialog dialog = new(viewModel)
        {
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? new CommitDialogResult(viewModel.Message, viewModel.IsAmendOperation)
            : null;
    }

    public async Task<BranchCreationRequest?> ShowCreateBranchDialogAsync(RepositoryInfo repository)
    {
        EnsureWindowRegistered();

        IReadOnlyList<OrphanBranchContentOption> orphanContentOptions =
        [
            new(
                OrphanBranchContentMode.Empty,
                _localizationService.GetString("BranchCreateOrphanEmptyContent")),
            new(
                OrphanBranchContentMode.StartPointSnapshot,
                _localizationService.GetString("BranchCreateOrphanSnapshotContent"))
        ];
        GitRevisionSelectorViewModel revisionSelector = CreateRevisionSelector(
            repository,
            [
                GitRevisionKind.Head,
                GitRevisionKind.Branch,
                GitRevisionKind.Tag,
                GitRevisionKind.Commit
            ],
            GitRevisionKind.Head,
            "HEAD");
        BranchCreateDialogViewModel viewModel = new(
            revisionSelector,
            orphanContentOptions,
            CreateValidationMessages());
        BranchCreateDialog dialog = new(viewModel)
        {
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? viewModel.CreateRequest()
            : null;
    }

    public async Task<TagCreationRequest?> ShowCreateTagDialogAsync(IReadOnlyList<GitCommit> commits)
    {
        EnsureWindowRegistered();

        TagCreateDialogViewModel viewModel = new(commits, CreateValidationMessages());
        TagCreateDialog dialog = new(viewModel)
        {
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || viewModel.SelectedCommit is null)
        {
            return null;
        }

        return new TagCreationRequest(
            viewModel.TagName.Trim(),
            viewModel.SelectedCommit.Hash,
            viewModel.IsAnnotated,
            viewModel.Message.Trim());
    }

    public async Task<string?> ShowRenameBranchDialogAsync(GitBranch branch)
    {
        string? branchName = await ShowTextInputAsync(new TextInputDialogRequest(
            _localizationService.GetString("BranchNameDialogTitle"),
            _localizationService.GetString("BranchNameDialogTextBoxHeader"),
            branch.Name,
            _localizationService.GetString("BranchNameDialogPrimaryButton"),
            _localizationService.GetString("BranchNameDialogCloseButton"),
            _localizationService.GetString("BranchNameDialogTextBoxPlaceholder")));

        return string.IsNullOrWhiteSpace(branchName) || branchName == branch.Name
            ? null
            : branchName;
    }

    public async Task<string?> ShowBranchDescriptionDialogAsync(GitBranch branch)
    {
        EnsureWindowRegistered();
        TextInputDialogViewModel viewModel = new(
            new TextInputDialogRequest(
                _localizationService.GetString("BranchDescriptionDialogTitle"),
                _localizationService.GetString("BranchDescriptionDialogTextBoxHeader"),
                branch.ConfigDescription,
                _localizationService.GetString("BranchDescriptionDialogPrimaryButton"),
                _localizationService.GetString("BranchDescriptionDialogCloseButton"),
                _localizationService.GetString("BranchDescriptionDialogPlaceholder"),
                isMultiline: true,
                allowEmpty: true),
            CreateValidationMessages());
        TextInputDialog dialog = new(viewModel)
        {
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? viewModel.Text.Trim()
            : null;
    }

    public async Task<WorktreeCreationRequest?> ShowCreateWorktreeDialogAsync(
        RepositoryInfo repository,
        string path,
        string startPoint,
        string newBranchName = "",
        WorktreeCreationMode creationMode = WorktreeCreationMode.ExistingBranch,
        bool canUseExistingBranch = true,
        GitRevisionKind startPointKind = GitRevisionKind.Branch)
    {
        EnsureWindowRegistered();
        WorktreeCreationMode effectiveMode = !canUseExistingBranch
            && creationMode == WorktreeCreationMode.ExistingBranch
                ? WorktreeCreationMode.NewBranch
                : creationMode;
        IReadOnlyList<GitRevisionKind> availableKinds = effectiveMode == WorktreeCreationMode.ExistingBranch
            ? [GitRevisionKind.Branch]
            : [
                GitRevisionKind.Head,
                GitRevisionKind.Branch,
                GitRevisionKind.Tag,
                GitRevisionKind.Commit
            ];
        GitRevisionSelectorViewModel revisionSelector = CreateRevisionSelector(
            repository,
            availableKinds,
            startPointKind,
            startPoint,
            includeRemoteBranches: effectiveMode != WorktreeCreationMode.ExistingBranch);
        WorktreeCreateDialogViewModel viewModel = new(
            revisionSelector,
            path,
            newBranchName,
            creationMode,
            canUseExistingBranch,
            CreateValidationMessages());
        WorktreeCreateDialog dialog = new(viewModel)
        {
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? viewModel.CreateRequest()
            : null;
    }

    public async Task<SubmoduleAddRequest?> ShowAddSubmoduleDialogAsync(string defaultPath)
    {
        EnsureWindowRegistered();
        SubmoduleAddDialogViewModel viewModel = new(defaultPath);
        SubmoduleAddDialog dialog = new(viewModel)
        {
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? viewModel.CreateRequest()
            : null;
    }

    public async Task<GitUrlRewrite?> ShowGitUrlRewriteDialogAsync(GitUrlRewrite? rewrite = null)
    {
        EnsureWindowRegistered();
        GitUrlRewriteDialogViewModel viewModel = new(rewrite);
        GitUrlRewriteDialog dialog = new(viewModel)
        {
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? viewModel.CreateRewrite()
            : null;
    }

    public async Task<GitArchiveDialogResult?> ShowArchiveDialogAsync(RepositoryInfo repository)
    {
        EnsureWindowRegistered();
        GitRevisionSelectorViewModel revisionSelector = CreateRevisionSelector(
            repository,
            [
                GitRevisionKind.Head,
                GitRevisionKind.Branch,
                GitRevisionKind.Tag,
                GitRevisionKind.Commit
            ],
            GitRevisionKind.Head,
            "HEAD");
        ArchiveDialogViewModel viewModel = new(revisionSelector);
        ArchiveDialog dialog = new(viewModel)
        {
            XamlRoot = _window!.Content.XamlRoot
        };
        ApplyTheme(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            && viewModel.RevisionSelector.ResolvedRevision is not null
                ? viewModel.CreateResult()
                : null;
    }

    private GitRevisionSelectorViewModel CreateRevisionSelector(
        RepositoryInfo repository,
        IReadOnlyList<GitRevisionKind> availableKinds,
        GitRevisionKind selectedKind,
        string initialValue,
        bool includeRemoteBranches = true)
    {
        return new GitRevisionSelectorViewModel(
            repository,
            _serviceProvider.GetRequiredService<IGitService>(),
            _localizationService,
            availableKinds,
            selectedKind,
            initialValue,
            includeRemoteBranches);
    }

    private DialogValidationMessages CreateValidationMessages()
    {
        return new DialogValidationMessages(
            _localizationService.GetString("ValidationRequiredField"),
            _localizationService.GetString("ValidationSelectionRequired"));
    }

    private void EnsureWindowRegistered()
    {
        if (_window?.Content?.XamlRoot is null)
        {
            throw new InvalidOperationException("The main window must be registered before showing dialogs.");
        }
    }

    private void ApplyTheme(ContentDialog dialog)
    {
        if (_window?.Content is FrameworkElement rootElement)
        {
            dialog.RequestedTheme = rootElement.ActualTheme;
        }
    }
}
