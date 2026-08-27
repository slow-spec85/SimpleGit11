using CommunityToolkit.Mvvm.ComponentModel;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.ViewModels;

public sealed partial class GitRevisionSelectorViewModel : ViewModelBase, IDisposable
{
    private const int VisibleSuggestionCount = 30;

    private readonly RepositoryInfo _repository;
    private readonly IGitService _gitService;
    private readonly ILocalizationService _localizationService;
    private readonly Dictionary<GitRevisionKind, IReadOnlyList<GitRevisionSuggestion>> _suggestionCache = [];
    private CancellationTokenSource? _loadingCancellationTokenSource;
    private string? _preferredStartPoint;
    private GitRevisionKind _selectedKind;
    private bool _hasSelectedKind;
    private bool _includeRemoteBranches;
    private bool _isConfiguring;
    private bool _isSelectingSuggestion;

    public GitRevisionSelectorViewModel(
        RepositoryInfo repository,
        IGitService gitService,
        ILocalizationService localizationService,
        IReadOnlyList<GitRevisionKind> availableKinds,
        GitRevisionKind selectedKind = GitRevisionKind.Head,
        string initialValue = "",
        bool includeRemoteBranches = true)
    {
        _repository = repository;
        _gitService = gitService;
        _localizationService = localizationService;
        _isConfiguring = true;
        StartPoint = "";
        Description = "";
        StatusMessage = "";
        ConfigureKinds(availableKinds, selectedKind, initialValue, includeRemoteBranches);
    }

    public ObservableCollection<DisplayOption<GitRevisionKind>> SourceOptions { get; } = [];

    public ObservableCollection<GitRevisionSuggestion> FilteredSuggestions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedKind))]
    [NotifyPropertyChangedFor(nameof(IsStartPointEnabled))]
    [NotifyPropertyChangedFor(nameof(CanResolve))]
    public partial DisplayOption<GitRevisionKind>? SelectedSourceOption { get; set; }

    public GitRevisionKind SelectedKind => SelectedSourceOption?.Value ?? _selectedKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResolve))]
    public partial string StartPoint { get; set; }

    [ObservableProperty]
    public partial string Description { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string StatusMessage { get; private set; }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartPointEnabled))]
    [NotifyPropertyChangedFor(nameof(CanResolve))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartPointEnabled))]
    [NotifyPropertyChangedFor(nameof(CanResolve))]
    public partial bool IsValidating { get; private set; }

    [ObservableProperty]
    public partial GitRevisionSuggestion? SelectedSuggestion { get; private set; }

    [ObservableProperty]
    public partial GitResolvedRevision? ResolvedRevision { get; private set; }

    public bool IsStartPointEnabled => SelectedKind != GitRevisionKind.Head
        && !IsLoading
        && !IsValidating;

    public bool CanResolve => !IsLoading
        && !IsValidating
        && !string.IsNullOrWhiteSpace(StartPoint);

    public void ConfigureKinds(
        IReadOnlyList<GitRevisionKind> availableKinds,
        GitRevisionKind preferredKind,
        string preferredValue = "",
        bool includeRemoteBranches = true)
    {
        ArgumentNullException.ThrowIfNull(availableKinds);
        if (availableKinds.Count == 0)
        {
            throw new ArgumentException("At least one Git revision kind is required.", nameof(availableKinds));
        }

        GitRevisionKind? previousKind = _hasSelectedKind ? _selectedKind : null;
        string previousValue = StartPoint;
        GitRevisionKind selectedKind = previousKind is GitRevisionKind currentKind
            && availableKinds.Contains(currentKind)
                ? currentKind
                : availableKinds.Contains(preferredKind)
                    ? preferredKind
                    : availableKinds[0];
        string startPoint = selectedKind == previousKind
            ? previousValue
            : !string.IsNullOrWhiteSpace(preferredValue)
                ? preferredValue
                : GetDefaultStartPoint(selectedKind);

        _isConfiguring = true;
        try
        {
            _selectedKind = selectedKind;
            _hasSelectedKind = true;
            SourceOptions.Clear();
            foreach (GitRevisionKind kind in availableKinds.Distinct())
            {
                SourceOptions.Add(CreateSourceOption(kind));
            }

            _includeRemoteBranches = includeRemoteBranches;
            _preferredStartPoint = startPoint;
            SelectedSourceOption = SourceOptions.First(option => option.Value == selectedKind);
            StartPoint = startPoint;
            Description = selectedKind == GitRevisionKind.Head
                ? GetHeadDescription()
                : "";
            SelectedSuggestion = null;
            ResolvedRevision = null;
            StatusMessage = "";
            ReplaceFilteredSuggestions([]);
        }
        finally
        {
            _isConfiguring = false;
        }

        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(IsStartPointEnabled));
        OnPropertyChanged(nameof(CanResolve));
    }

    public async Task LoadSelectedSourceAsync()
    {
        GitRevisionKind kind = SelectedKind;
        if (kind == GitRevisionKind.Head)
        {
            return;
        }

        CancelLoading();
        CancellationTokenSource cancellationTokenSource = new();
        _loadingCancellationTokenSource = cancellationTokenSource;
        IsLoading = true;
        StatusMessage = "";

        try
        {
            if (!_suggestionCache.TryGetValue(kind, out IReadOnlyList<GitRevisionSuggestion>? suggestions))
            {
                suggestions = await _gitService.Revisions.GetSuggestionsAsync(
                    _repository,
                    kind,
                    cancellationTokenSource.Token);
                _suggestionCache[kind] = suggestions;
            }

            if (SelectedKind != kind || cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            IReadOnlyList<GitRevisionSuggestion> availableSuggestions = GetAvailableSuggestions(suggestions);
            ReplaceFilteredSuggestions(availableSuggestions);
            string preferredStartPoint = _preferredStartPoint ?? StartPoint;
            _preferredStartPoint = null;
            if (!string.IsNullOrWhiteSpace(preferredStartPoint))
            {
                GitRevisionSuggestion? matchingSuggestion = availableSuggestions.FirstOrDefault(item =>
                    item.Value.Equals(preferredStartPoint, StringComparison.Ordinal));
                if (matchingSuggestion is not null)
                {
                    SelectSuggestion(matchingSuggestion);
                }
                else
                {
                    StartPoint = preferredStartPoint;
                }

                return;
            }

            GitRevisionSuggestion? suggestedStartPoint = kind == GitRevisionKind.Branch
                ? availableSuggestions.FirstOrDefault(item => !item.IsRemote && item.Value.Equals(
                    _repository.CurrentBranch,
                    StringComparison.Ordinal))
                    ?? availableSuggestions.FirstOrDefault()
                : availableSuggestions.FirstOrDefault();
            if (suggestedStartPoint is not null)
            {
                SelectSuggestion(suggestedStartPoint);
            }
            else
            {
                StatusMessage = _localizationService.GetString("RevisionNoSuggestions");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (FileNotFoundException)
        {
            StatusMessage = _localizationService.GetString("GitExecutableNotFound");
        }
        catch (DirectoryNotFoundException)
        {
            StatusMessage = _localizationService.GetString("RepositoryFolderNotFound");
        }
        catch (GitCommandException)
        {
            StatusMessage = _localizationService.GetString("GitRevisionSuggestionsFailed");
        }
        finally
        {
            if (ReferenceEquals(_loadingCancellationTokenSource, cancellationTokenSource))
            {
                _loadingCancellationTokenSource = null;
                IsLoading = false;
            }

            cancellationTokenSource.Dispose();
        }
    }

    public void FilterSuggestions(string text)
    {
        if (!_suggestionCache.TryGetValue(SelectedKind, out IReadOnlyList<GitRevisionSuggestion>? suggestions))
        {
            return;
        }

        string filter = text.Trim();
        IEnumerable<GitRevisionSuggestion> filtered = GetAvailableSuggestions(suggestions);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            filtered = filtered.Where(item =>
                item.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceFilteredSuggestions(filtered);
    }

    public void SelectSuggestion(GitRevisionSuggestion suggestion)
    {
        _isSelectingSuggestion = true;
        try
        {
            StartPoint = suggestion.Value;
            SelectedSuggestion = suggestion;
            Description = suggestion.Description;
        }
        finally
        {
            _isSelectingSuggestion = false;
        }
    }

    public async Task<bool> ResolveAsync()
    {
        if (!CanResolve)
        {
            return false;
        }

        IsValidating = true;
        StatusMessage = "";
        try
        {
            ResolvedRevision = await _gitService.Revisions.ResolveAsync(
                _repository,
                SelectedKind,
                StartPoint,
                CancellationToken.None);
            return !string.IsNullOrWhiteSpace(ResolvedRevision.CommitHash);
        }
        catch (FileNotFoundException)
        {
            StatusMessage = _localizationService.GetString("GitExecutableNotFound");
        }
        catch (DirectoryNotFoundException)
        {
            StatusMessage = _localizationService.GetString("RepositoryFolderNotFound");
        }
        catch (GitCommandException)
        {
            StatusMessage = _localizationService.GetString("GitRevisionInvalid");
        }
        finally
        {
            IsValidating = false;
        }

        return false;
    }

    public void Dispose()
    {
        CancelLoading();
    }

    partial void OnSelectedSourceOptionChanged(DisplayOption<GitRevisionKind>? value)
    {
        if (_isConfiguring || value is null)
        {
            return;
        }

        _selectedKind = value.Value;
        _hasSelectedKind = true;
        CancelLoading();
        ReplaceFilteredSuggestions([]);
        _preferredStartPoint = GetDefaultStartPoint(value.Value);
        StartPoint = _preferredStartPoint;
        Description = value.Value == GitRevisionKind.Head
            ? GetHeadDescription()
            : "";
        StatusMessage = "";
        ResolvedRevision = null;
    }

    partial void OnStartPointChanged(string value)
    {
        ResolvedRevision = null;
        StatusMessage = "";
        if (_isConfiguring)
        {
            return;
        }

        if (!_isSelectingSuggestion)
        {
            SelectedSuggestion = null;
            Description = SelectedKind == GitRevisionKind.Head
                ? GetHeadDescription()
                : "";
        }
    }

    private DisplayOption<GitRevisionKind> CreateSourceOption(GitRevisionKind kind)
    {
        string resourceKey = kind switch
        {
            GitRevisionKind.Head => "RevisionSourceHead",
            GitRevisionKind.Branch => "RevisionSourceBranch",
            GitRevisionKind.Tag => "RevisionSourceTag",
            GitRevisionKind.Commit => "RevisionSourceCommit",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new DisplayOption<GitRevisionKind>(kind, _localizationService.GetString(resourceKey));
    }

    private string GetDefaultStartPoint(GitRevisionKind kind)
    {
        return kind switch
        {
            GitRevisionKind.Head => "HEAD",
            GitRevisionKind.Branch when !_repository.IsDetachedHead => _repository.CurrentBranch,
            _ => ""
        };
    }

    private IReadOnlyList<GitRevisionSuggestion> GetAvailableSuggestions(
        IReadOnlyList<GitRevisionSuggestion> suggestions)
    {
        return SelectedKind == GitRevisionKind.Branch && !_includeRemoteBranches
            ? suggestions.Where(item => !item.IsRemote).ToList()
            : suggestions;
    }

    private void ReplaceFilteredSuggestions(IEnumerable<GitRevisionSuggestion> suggestions)
    {
        FilteredSuggestions.Clear();
        foreach (GitRevisionSuggestion suggestion in suggestions.Take(VisibleSuggestionCount))
        {
            FilteredSuggestions.Add(suggestion);
        }
    }

    private string GetHeadDescription()
    {
        return string.Format(
            _localizationService.GetString("RevisionCurrentHeadDescription"),
            _repository.CurrentBranch);
    }

    private void CancelLoading()
    {
        _loadingCancellationTokenSource?.Cancel();
        _loadingCancellationTokenSource = null;
        IsLoading = false;
    }
}
