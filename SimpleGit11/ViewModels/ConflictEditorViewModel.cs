using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;

namespace SimpleGit11.ViewModels;

public sealed partial class ConflictEditorViewModel : AppNotificationViewModelBase
{
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly ITextFileService _textFileService;
    private readonly IGitService _gitService;
    private readonly ILocalizationService _localizationService;
    private readonly Stack<string> _undoHistory = new();
    private readonly Stack<string> _redoHistory = new();
    private IReadOnlyList<ConflictEditorLine> _selectedLines = [];
    private RepositoryInfo? _repository;
    private GitChangedFile? _conflict;
    private TextFileDocument? _document;
    private string _originalText = "";
    private bool _suppressLineChanges;
    private int _loadVersion;

    public ConflictEditorViewModel(
        ITextFileService textFileService,
        IGitService gitService,
        ILocalizationService localizationService,
        IMessenger messenger,
        IAsyncCommandExecutor asyncCommandExecutor)
        : base(messenger)
    {
        _textFileService = textFileService;
        _gitService = gitService;
        _localizationService = localizationService;
        _asyncCommandExecutor = asyncCommandExecutor
            ?? throw new ArgumentNullException(nameof(asyncCommandExecutor));

        InitializeSyntaxHighlightingOptions();

    }

    public ObservableCollection<ConflictEditorLine> Lines { get; } = [];

    public ObservableCollection<DisplayOption<SyntaxHighlightingMode>> SyntaxHighlightingOptions { get; } = [];

    public Func<string, Task>? ConflictResolvedAsync { get; set; }

    public string FileName => _conflict?.FileName ?? "";

    public string RelativePath => _conflict?.Path ?? "";

    public bool CanEdit => _document is not null && !IsOperationRunning;

    public bool CanSave => CanEdit && IsDirty;

    public bool CanMarkResolved => CanEdit && !HasConflictMarkers;

    public bool CanAcceptAll => CanEdit && HasConflictMarkers;

    public bool CanUndo => CanEdit && _undoHistory.Count > 0;

    public bool CanRedo => CanEdit && _redoHistory.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModifiedStatus))]
    public partial bool IsDirty { get; private set; }

    public string ModifiedStatus => IsDirty
        ? _localizationService.GetString("FileEditorModifiedStatus")
        : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanMarkResolved))]
    [NotifyPropertyChangedFor(nameof(CanAcceptAll))]
    public partial bool IsOperationRunning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMarkResolved))]
    [NotifyPropertyChangedFor(nameof(CanAcceptAll))]
    [NotifyCanExecuteChangedFor(nameof(MarkResolvedCommand))]
    [NotifyCanExecuteChangedFor(nameof(AcceptAllCurrentCommand))]
    [NotifyCanExecuteChangedFor(nameof(AcceptAllIncomingCommand))]
    [NotifyCanExecuteChangedFor(nameof(AcceptAllBothCommand))]
    public partial bool HasConflictMarkers { get; private set; }

    [ObservableProperty]
    public partial DisplayOption<SyntaxHighlightingMode>? SelectedSyntaxHighlightingOption { get; set; }

    partial void OnIsDirtyChanged(bool value)
    {
        UpdateCommandStates();
    }

    partial void OnIsOperationRunningChanged(bool value)
    {
        UpdateCommandStates();
        PublishOperationState(
            value,
            _localizationService.GetString("OperationInProgressMessage"));
    }

    partial void OnSelectedSyntaxHighlightingOptionChanged(DisplayOption<SyntaxHighlightingMode>? value)
    {
        OnPropertyChanged(nameof(SelectedSyntaxHighlightingMode));
    }

    public SyntaxHighlightingMode SelectedSyntaxHighlightingMode =>
        SelectedSyntaxHighlightingOption?.Value ?? SyntaxHighlightingMode.Auto;

    public async Task LoadAsync(RepositoryInfo repository, GitChangedFile conflict)
    {
        int loadVersion = ++_loadVersion;
        ClearNotification();
        IsOperationRunning = true;
        try
        {
            TextFileDocument document = await _textFileService.ReadAsync(repository, conflict.Path);
            if (loadVersion != _loadVersion)
            {
                return;
            }

            _repository = repository;
            _conflict = conflict;
            _document = document;
            _originalText = NormalizeForEditor(document.Text);
            _undoHistory.Clear();
            _redoHistory.Clear();
            ReplaceLines(_originalText);
            IsDirty = false;
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(RelativePath));
        }
        catch (InvalidDataException exception)
        {
            ClearDocument();
            ShowNotification(
                AppNotificationSeverity.Warning,
                _localizationService.GetString("ConflictBinaryFileUnsupported"),
                exception.Message);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or DecoderFallbackException)
        {
            ClearDocument();
            ShowNotification(
                AppNotificationSeverity.Error,
                _localizationService.GetString("ConflictFileUnavailable"),
                exception.Message);
        }
        finally
        {
            if (loadVersion == _loadVersion)
            {
                IsOperationRunning = false;
            }
        }
    }

    public void Clear()
    {
        _loadVersion++;
        ClearDocument();
        ClearNotification();
        IsOperationRunning = false;
    }

    public void SetSelectedLines(IEnumerable<ConflictEditorLine> selectedLines)
    {
        _selectedLines = selectedLines.ToList();
    }

    public void BeginEditLine(ConflictEditorLine line)
    {
        if (!CanEdit || !Lines.Contains(line))
        {
            return;
        }

        foreach (ConflictEditorLine editorLine in Lines)
        {
            editorLine.IsEditing = ReferenceEquals(editorLine, line);
        }
    }

    public void EndEditLine(ConflictEditorLine line)
    {
        line.IsEditing = false;
    }

    public void ApplyEditorChanges(IReadOnlyList<ConflictEditorDocumentChange> changes)
    {
        if (!CanEdit || changes.Count == 0)
        {
            return;
        }

        _suppressLineChanges = true;
        try
        {
            foreach (ConflictEditorDocumentChange change in changes)
            {
                int start = Math.Clamp(change.StartLine, 0, Lines.Count);
                int removeCount = Math.Min(change.RemovedLineCount, Lines.Count - start);
                for (int index = 0; index < removeCount; index++)
                {
                    ConflictEditorLine removedLine = Lines[start];
                    UnsubscribeLine(removedLine);
                    Lines.RemoveAt(start);
                }

                for (int index = 0; index < change.InsertedLines.Count; index++)
                {
                    InsertLineCore(
                        start + index,
                        new ConflictEditorLine(change.InsertedLines[index]));
                }
            }
        }
        finally
        {
            _suppressLineChanges = false;
        }

        _redoHistory.Clear();
        OnLinesChanged();
        UpdateCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanSave), FlowExceptionsToTaskScheduler = true)]
    private Task OnSaveAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(SaveOperationAsync);
    }

    private async Task SaveOperationAsync()
    {
        await ExecuteFileOperationAsync(async () =>
        {
            await SaveCoreAsync();
            ShowNotification(
                AppNotificationSeverity.Success,
                _localizationService.GetString("ConflictFileSaved"));
        });
    }

    [RelayCommand(CanExecute = nameof(CanEdit), FlowExceptionsToTaskScheduler = true)]
    private Task OnReloadAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(ReloadCoreAsync);
    }

    private async Task ReloadCoreAsync()
    {
        if (_repository is not null && _conflict is not null)
        {
            await LoadAsync(_repository, _conflict);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMarkResolved), FlowExceptionsToTaskScheduler = true)]
    private Task OnMarkResolvedAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(MarkResolvedCoreAsync);
    }

    private async Task MarkResolvedCoreAsync()
    {
        if (_repository is null || _conflict is null || _document is null)
        {
            return;
        }

        string path = _conflict.Path;
        bool resolved = false;
        await _gitService.ExecuteAsync(async () =>
        {
            IsOperationRunning = true;
            try
            {
                if (HasConflictMarkers)
                {
                    ShowNotification(
                        AppNotificationSeverity.Error,
                        _localizationService.GetString("ConflictMarkersRemain"));
                    return;
                }

                if (IsDirty)
                {
                    await SaveCoreAsync();
                }

                await _gitService.Staging.StageAsync(_repository, _conflict);
                resolved = true;
            }
            catch (FileNotFoundException)
            {
                ShowNotification(
                    AppNotificationSeverity.Error,
                    _localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowNotification(
                    AppNotificationSeverity.Error,
                    _localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowNotification(
                    AppNotificationSeverity.Error,
                    _localizationService.GetString("GitStageCommandFailed"),
                    exception.Message);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or EncoderFallbackException)
            {
                ShowNotification(
                    AppNotificationSeverity.Error,
                    _localizationService.GetString("ConflictFileUnavailable"),
                    exception.Message);
            }
            finally
            {
                IsOperationRunning = false;
            }
        });

        if (resolved && ConflictResolvedAsync is not null)
        {
            await ConflictResolvedAsync(path);
        }
    }

    private async Task ExecuteFileOperationAsync(Func<Task> operation)
    {
        await _gitService.ExecuteAsync(async () =>
        {
            IsOperationRunning = true;
            ClearNotification();
            try
            {
                await operation();
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or EncoderFallbackException)
            {
                ShowNotification(
                    AppNotificationSeverity.Error,
                    _localizationService.GetString("ConflictFileUnavailable"),
                    exception.Message);
            }
            finally
            {
                IsOperationRunning = false;
            }
        });
    }

    private async Task SaveCoreAsync()
    {
        if (_document is null)
        {
            return;
        }

        string text = ComposeText();
        await _textFileService.WriteAsync(_document, text);
        _originalText = text;
        IsDirty = false;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OnDeleteLines(ConflictEditorLine? clickedLine)
    {
        IReadOnlyList<ConflictEditorLine> linesToDelete =
            clickedLine is not null && _selectedLines.Contains(clickedLine)
                ? _selectedLines
                : clickedLine is not null
                    ? [clickedLine]
                    : _selectedLines;
        if (linesToDelete.Count == 0)
        {
            return;
        }

        RecordUndoState();
        foreach (ConflictEditorLine line in linesToDelete)
        {
            Lines.Remove(line);
        }

        _selectedLines = [];
        OnLinesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OnInsertLineAbove(ConflictEditorLine? line)
    {
        InsertLine(line, insertAfter: false);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OnInsertLineBelow(ConflictEditorLine? line)
    {
        InsertLine(line, insertAfter: true);
    }

    [RelayCommand(CanExecute = nameof(CanAcceptAll))]
    private void OnAcceptAllCurrent()
    {
        ResolveAllConflicts(ConflictBlockResolution.Current);
    }

    [RelayCommand(CanExecute = nameof(CanAcceptAll))]
    private void OnAcceptAllIncoming()
    {
        ResolveAllConflicts(ConflictBlockResolution.Incoming);
    }

    [RelayCommand(CanExecute = nameof(CanAcceptAll))]
    private void OnAcceptAllBoth()
    {
        ResolveAllConflicts(ConflictBlockResolution.Both);
    }

    private void InsertLine(ConflictEditorLine? referenceLine, bool insertAfter)
    {
        int index = referenceLine is null ? Lines.Count : Lines.IndexOf(referenceLine);
        if (index < 0)
        {
            index = Lines.Count;
        }
        else if (insertAfter)
        {
            index++;
        }

        RecordUndoState();
        ConflictEditorLine newLine = new("");
        InsertLineCore(index, newLine);
        OnLinesChanged();
        BeginEditLine(newLine);
    }

    private void ResolveAllConflicts(ConflictBlockResolution resolution)
    {
        IReadOnlyList<ConflictBlockRange> blocks = FindConflictBlocks();
        if (blocks.Count == 0)
        {
            return;
        }

        RecordUndoState();
        _suppressLineChanges = true;
        try
        {
            foreach (ConflictBlockRange block in blocks.OrderByDescending(block => block.StartIndex))
            {
                ResolveBlockCore(block, resolution);
            }
        }
        finally
        {
            _suppressLineChanges = false;
        }

        OnLinesChanged();
    }

    private void ResolveBlockCore(ConflictBlockRange block, ConflictBlockResolution resolution)
    {
        List<string> currentLines = Lines
            .Skip(block.StartIndex + 1)
            .Take(block.CurrentEndIndex - block.StartIndex - 1)
            .Select(line => line.Text)
            .ToList();
        List<string> incomingLines = Lines
            .Skip(block.SeparatorIndex + 1)
            .Take(block.EndIndex - block.SeparatorIndex - 1)
            .Select(line => line.Text)
            .ToList();
        IReadOnlyList<string> replacement = resolution switch
        {
            ConflictBlockResolution.Current => currentLines,
            ConflictBlockResolution.Incoming => incomingLines,
            _ => currentLines.Concat(incomingLines).ToList()
        };

        for (int index = block.EndIndex; index >= block.StartIndex; index--)
        {
            UnsubscribeLine(Lines[index]);
            Lines.RemoveAt(index);
        }

        int insertIndex = block.StartIndex;
        foreach (string text in replacement)
        {
            InsertLineCore(insertIndex++, new ConflictEditorLine(text));
        }
    }

    private IReadOnlyList<ConflictBlockRange> FindConflictBlocks()
    {
        List<ConflictBlockRange> blocks = [];
        for (int startIndex = 0; startIndex < Lines.Count; startIndex++)
        {
            if (!Lines[startIndex].Text.StartsWith("<<<<<<<", StringComparison.Ordinal))
            {
                continue;
            }

            int baseIndex = -1;
            int separatorIndex = -1;
            int endIndex = -1;
            for (int index = startIndex + 1; index < Lines.Count; index++)
            {
                string text = Lines[index].Text;
                if (baseIndex < 0 && text.StartsWith("|||||||", StringComparison.Ordinal))
                {
                    baseIndex = index;
                }
                else if (separatorIndex < 0 && text.StartsWith("=======", StringComparison.Ordinal))
                {
                    separatorIndex = index;
                }
                else if (text.StartsWith(">>>>>>>", StringComparison.Ordinal))
                {
                    endIndex = index;
                    break;
                }
            }

            if (separatorIndex >= 0 && endIndex >= 0)
            {
                blocks.Add(new ConflictBlockRange(
                    startIndex,
                    baseIndex >= 0 ? baseIndex : separatorIndex,
                    separatorIndex,
                    endIndex));
                startIndex = endIndex;
            }
        }

        return blocks;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void OnUndo()
    {
        if (!CanUndo)
        {
            return;
        }

        _redoHistory.Push(ComposeText());
        ReplaceLines(_undoHistory.Pop());
        UpdateDirtyState();
        UpdateCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void OnRedo()
    {
        if (!CanRedo)
        {
            return;
        }

        _undoHistory.Push(ComposeText());
        ReplaceLines(_redoHistory.Pop());
        UpdateDirtyState();
        UpdateCommandStates();
    }

    private void RecordUndoState()
    {
        _undoHistory.Push(ComposeText());
        _redoHistory.Clear();
        UpdateCommandStates();
    }

    private void ReplaceLines(string text)
    {
        _suppressLineChanges = true;
        try
        {
            foreach (ConflictEditorLine line in Lines)
            {
                UnsubscribeLine(line);
            }

            Lines.Clear();
            string normalizedText = NormalizeForEditor(text);
            foreach (string lineText in normalizedText.Split('\n'))
            {
                InsertLineCore(Lines.Count, new ConflictEditorLine(lineText));
            }
        }
        finally
        {
            _suppressLineChanges = false;
        }

        OnLinesChanged();
    }

    private void InsertLineCore(int index, ConflictEditorLine line)
    {
        line.PropertyChanged += Line_PropertyChanged;
        Lines.Insert(index, line);
    }

    private void UnsubscribeLine(ConflictEditorLine line)
    {
        line.PropertyChanged -= Line_PropertyChanged;
    }

    private void Line_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_suppressLineChanges && e.PropertyName == nameof(ConflictEditorLine.Text))
        {
            OnLinesChanged();
        }
    }

    private void OnLinesChanged()
    {
        ReclassifyLines();
        UpdateLineNumbers();
        UpdateDirtyState();
    }

    private void ReclassifyLines()
    {
        ConflictEditorLineRole currentRole = ConflictEditorLineRole.Context;
        foreach (ConflictEditorLine line in Lines)
        {
            if (line.Text.StartsWith("<<<<<<<", StringComparison.Ordinal))
            {
                line.Role = ConflictEditorLineRole.Marker;
                currentRole = ConflictEditorLineRole.Current;
            }
            else if (line.Text.StartsWith("|||||||", StringComparison.Ordinal))
            {
                line.Role = ConflictEditorLineRole.Marker;
                currentRole = ConflictEditorLineRole.Base;
            }
            else if (line.Text.StartsWith("=======", StringComparison.Ordinal))
            {
                line.Role = ConflictEditorLineRole.Marker;
                currentRole = ConflictEditorLineRole.Incoming;
            }
            else if (line.Text.StartsWith(">>>>>>>", StringComparison.Ordinal))
            {
                line.Role = ConflictEditorLineRole.Marker;
                currentRole = ConflictEditorLineRole.Context;
            }
            else
            {
                line.Role = currentRole;
            }
        }

        HasConflictMarkers = Lines.Any(line => line.Role == ConflictEditorLineRole.Marker);
    }

    private void UpdateLineNumbers()
    {
        for (int index = 0; index < Lines.Count; index++)
        {
            Lines[index].LineNumber = index + 1;
        }
    }

    private string ComposeText()
    {
        return string.Join("\n", Lines.Select(line => line.Text));
    }

    private void UpdateDirtyState()
    {
        if (!_suppressLineChanges)
        {
            IsDirty = !string.Equals(ComposeText(), _originalText, StringComparison.Ordinal);
        }
    }

    private void ClearDocument()
    {
        _suppressLineChanges = true;
        foreach (ConflictEditorLine line in Lines)
        {
            UnsubscribeLine(line);
        }

        Lines.Clear();
        _suppressLineChanges = false;
        _repository = null;
        _conflict = null;
        _document = null;
        _originalText = "";
        _selectedLines = [];
        _undoHistory.Clear();
        _redoHistory.Clear();
        IsDirty = false;
        HasConflictMarkers = false;
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(RelativePath));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanMarkResolved));
        UpdateCommandStates();
    }

    private void InitializeSyntaxHighlightingOptions()
    {
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.Auto,
            _localizationService.GetString("SyntaxHighlightingAuto")));
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.None,
            _localizationService.GetString("SyntaxHighlightingNone")));
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.CStyle,
            _localizationService.GetString("SyntaxHighlightingCStyle")));
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.Hash,
            _localizationService.GetString("SyntaxHighlightingHash")));
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.Dash,
            _localizationService.GetString("SyntaxHighlightingDash")));
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.Html,
            _localizationService.GetString("SyntaxHighlightingHtml")));
        SelectedSyntaxHighlightingOption = SyntaxHighlightingOptions.FirstOrDefault();
    }

    private void UpdateCommandStates()
    {
        SaveCommand.NotifyCanExecuteChanged();
        MarkResolvedCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        DeleteLinesCommand.NotifyCanExecuteChanged();
        InsertLineAboveCommand.NotifyCanExecuteChanged();
        InsertLineBelowCommand.NotifyCanExecuteChanged();
        AcceptAllCurrentCommand.NotifyCanExecuteChanged();
        AcceptAllIncomingCommand.NotifyCanExecuteChanged();
        AcceptAllBothCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private static string NormalizeForEditor(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private readonly record struct ConflictBlockRange(
        int StartIndex,
        int CurrentEndIndex,
        int SeparatorIndex,
        int EndIndex);

    private enum ConflictBlockResolution
    {
        Current,
        Incoming,
        Both
    }
}
