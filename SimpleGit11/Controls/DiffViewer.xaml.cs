using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SimpleGit11.Extensions;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Editor;
using SimpleGit11.Presentation.Theming;
using TextControlBoxNS.Models;
using Windows.UI;

namespace SimpleGit11.Controls;

public sealed partial class DiffViewer : UserControl
{
    private const string DiffLineDecorationGroup = "diff-lines";
    private const string InlineDiffDecorationGroup = "word-diff";
    private const string GitServiceLineDecorationGroup = "git-service-lines";
    private const string DiffLineGutterDecorationGroup = "diff-line-markers";
    private const float InlineDiffCornerRadius = 2;
    private const float InlineDiffHorizontalPadding = 1;
    private bool _hasSearchMatches;

    public static readonly DependencyProperty CopyTextCommandProperty = RegisterCommand(
        nameof(CopyTextCommand));

    public static readonly DependencyProperty RevertLineCommandProperty = RegisterCommand(
        nameof(RevertLineCommand));

    public static readonly DependencyProperty EditFileCommandProperty = RegisterCommand(
        nameof(EditFileCommand));

    public static readonly DependencyProperty SaveEditCommandProperty = RegisterCommand(
        nameof(SaveEditCommand));

    public static readonly DependencyProperty CancelEditCommandProperty = RegisterCommand(
        nameof(CancelEditCommand));

    public static readonly DependencyProperty EditContentChangedCommandProperty = RegisterCommand(
        nameof(EditContentChangedCommand));

    public static readonly DependencyProperty RevertActionVisibilityProperty = DependencyProperty.Register(
        nameof(RevertActionVisibility),
        typeof(Visibility),
        typeof(DiffViewer),
        new PropertyMetadata(Visibility.Collapsed, OnChromePropertyChanged));

    public static readonly DependencyProperty EditActionVisibilityProperty = DependencyProperty.Register(
        nameof(EditActionVisibility),
        typeof(Visibility),
        typeof(DiffViewer),
        new PropertyMetadata(Visibility.Collapsed, OnChromePropertyChanged));

    public static readonly DependencyProperty DiffLinesSourceProperty = DependencyProperty.Register(
        nameof(DiffLinesSource),
        typeof(object),
        typeof(DiffViewer),
        new PropertyMetadata(null, OnDiffLinesSourceChanged));

    public static readonly DependencyProperty EditableDiffLinesSourceProperty = DependencyProperty.Register(
        nameof(EditableDiffLinesSource),
        typeof(object),
        typeof(DiffViewer),
        new PropertyMetadata(null, OnEditableDiffLinesSourceChanged));

    public static readonly DependencyProperty EditableTextProperty = DependencyProperty.Register(
        nameof(EditableText),
        typeof(string),
        typeof(DiffViewer),
        new PropertyMetadata("", OnEditableTextChanged));

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing),
        typeof(bool),
        typeof(DiffViewer),
        new PropertyMetadata(false, OnEditorModeChanged));

    public static readonly DependencyProperty IsFullFileModeProperty = DependencyProperty.Register(
        nameof(IsFullFileMode),
        typeof(bool),
        typeof(DiffViewer),
        new PropertyMetadata(false, OnLineNumberModeChanged));

    public static readonly DependencyProperty IsEditDirtyProperty = DependencyProperty.Register(
        nameof(IsEditDirty),
        typeof(bool),
        typeof(DiffViewer),
        new PropertyMetadata(false, OnChromePropertyChanged));

    public static readonly DependencyProperty SyntaxHighlightingModeProperty = DependencyProperty.Register(
        nameof(SyntaxHighlightingMode),
        typeof(SyntaxHighlightingMode),
        typeof(DiffViewer),
        new PropertyMetadata(SyntaxHighlightingMode.Auto, OnSyntaxPropertyChanged));

    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath),
        typeof(string),
        typeof(DiffViewer),
        new PropertyMetadata("", OnSyntaxPropertyChanged));

    public static readonly DependencyProperty HasEmptyStateProperty = DependencyProperty.Register(
        nameof(HasEmptyState),
        typeof(bool),
        typeof(DiffViewer),
        new PropertyMetadata(true, OnChromePropertyChanged));

    public static readonly DependencyProperty EmptyMessageProperty = DependencyProperty.Register(
        nameof(EmptyMessage),
        typeof(string),
        typeof(DiffViewer),
        new PropertyMetadata("", OnChromePropertyChanged));

    private DiffEditorProjection _projection = DiffEditorProjection.Create(null);
    private readonly List<DiffLineKind> _overviewKinds = [];
    private INotifyCollectionChanged? _observedDiffLines;
    private INotifyCollectionChanged? _observedEditableDiffLines;
    private bool _isChangeOverviewDragging;
    private bool _isDocumentRenderQueued;
    private bool _isApplyingDocument;
    private bool _editingDocumentLoaded;
    private int? _contextLineIndex;

    public DiffViewer()
    {
        InitializeComponent();
        EditorSurface.DocumentChanged += EditorSurface_DocumentChanged;
        Loaded += DiffViewer_Loaded;
        Unloaded += DiffViewer_Unloaded;
        UpdateModeChrome();
    }

    public ICommand? CopyTextCommand
    {
        get => (ICommand?)GetValue(CopyTextCommandProperty);
        set => SetValue(CopyTextCommandProperty, value);
    }

    public ICommand? RevertLineCommand
    {
        get => (ICommand?)GetValue(RevertLineCommandProperty);
        set => SetValue(RevertLineCommandProperty, value);
    }

    public ICommand? EditFileCommand
    {
        get => (ICommand?)GetValue(EditFileCommandProperty);
        set => SetValue(EditFileCommandProperty, value);
    }

    public ICommand? SaveEditCommand
    {
        get => (ICommand?)GetValue(SaveEditCommandProperty);
        set => SetValue(SaveEditCommandProperty, value);
    }

    public ICommand? CancelEditCommand
    {
        get => (ICommand?)GetValue(CancelEditCommandProperty);
        set => SetValue(CancelEditCommandProperty, value);
    }

    public ICommand? EditContentChangedCommand
    {
        get => (ICommand?)GetValue(EditContentChangedCommandProperty);
        set => SetValue(EditContentChangedCommandProperty, value);
    }

    public Visibility RevertActionVisibility
    {
        get => (Visibility)GetValue(RevertActionVisibilityProperty);
        set => SetValue(RevertActionVisibilityProperty, value);
    }

    public Visibility EditActionVisibility
    {
        get => (Visibility)GetValue(EditActionVisibilityProperty);
        set => SetValue(EditActionVisibilityProperty, value);
    }

    public object? DiffLinesSource
    {
        get => GetValue(DiffLinesSourceProperty);
        set => SetValue(DiffLinesSourceProperty, value);
    }

    public object? EditableDiffLinesSource
    {
        get => GetValue(EditableDiffLinesSourceProperty);
        set => SetValue(EditableDiffLinesSourceProperty, value);
    }

    public string EditableText
    {
        get => (string)GetValue(EditableTextProperty);
        set => SetValue(EditableTextProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public bool IsFullFileMode
    {
        get => (bool)GetValue(IsFullFileModeProperty);
        set => SetValue(IsFullFileModeProperty, value);
    }

    public bool IsEditDirty
    {
        get => (bool)GetValue(IsEditDirtyProperty);
        set => SetValue(IsEditDirtyProperty, value);
    }

    public SyntaxHighlightingMode SyntaxHighlightingMode
    {
        get => (SyntaxHighlightingMode)GetValue(SyntaxHighlightingModeProperty);
        set => SetValue(SyntaxHighlightingModeProperty, value);
    }

    public string FilePath
    {
        get => (string)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public bool HasEmptyState
    {
        get => (bool)GetValue(HasEmptyStateProperty);
        set => SetValue(HasEmptyStateProperty, value);
    }

    public string EmptyMessage
    {
        get => (string)GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    private static DependencyProperty RegisterCommand(string propertyName)
    {
        return DependencyProperty.Register(
            propertyName,
            typeof(ICommand),
            typeof(DiffViewer),
            new PropertyMetadata(null));
    }

    private static void OnDiffLinesSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        DiffViewer viewer = (DiffViewer)sender;
        viewer.ObserveDiffLines(args.NewValue as INotifyCollectionChanged);
        viewer.QueueDocumentRender();
    }

    private static void OnEditableDiffLinesSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        DiffViewer viewer = (DiffViewer)sender;
        viewer.ObserveEditableDiffLines(args.NewValue as INotifyCollectionChanged);
        viewer.QueueDocumentRender();
    }

    private static void OnEditableTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        DiffViewer viewer = (DiffViewer)sender;
        viewer._editingDocumentLoaded = false;
        viewer.QueueDocumentRender();
    }

    private static void OnEditorModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        DiffViewer viewer = (DiffViewer)sender;
        viewer._editingDocumentLoaded = false;
        viewer.UpdateModeChrome();
        viewer.ApplyLineNumberPresentation();
        viewer.QueueDocumentRender();
    }

    private static void OnLineNumberModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((DiffViewer)sender).ApplyLineNumberPresentation();
    }

    private static void OnChromePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((DiffViewer)sender).UpdateModeChrome();
    }

    private static void OnSyntaxPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((DiffViewer)sender).ApplySyntaxHighlighting();
    }

    private void DiffViewer_Loaded(object sender, RoutedEventArgs args)
    {
        ActualThemeChanged -= DiffViewer_ActualThemeChanged;
        ActualThemeChanged += DiffViewer_ActualThemeChanged;
        ObserveDiffLines(DiffLinesSource as INotifyCollectionChanged);
        ObserveEditableDiffLines(EditableDiffLinesSource as INotifyCollectionChanged);
        RenderDocument();
    }

    private void DiffViewer_Unloaded(object sender, RoutedEventArgs args)
    {
        ActualThemeChanged -= DiffViewer_ActualThemeChanged;
        ObserveDiffLines(null);
        ObserveEditableDiffLines(null);
    }

    private void ObserveDiffLines(INotifyCollectionChanged? source)
    {
        if (ReferenceEquals(_observedDiffLines, source))
        {
            return;
        }

        if (_observedDiffLines is not null)
        {
            _observedDiffLines.CollectionChanged -= Lines_CollectionChanged;
        }

        _observedDiffLines = source;
        if (_observedDiffLines is not null)
        {
            _observedDiffLines.CollectionChanged += Lines_CollectionChanged;
        }
    }

    private void ObserveEditableDiffLines(INotifyCollectionChanged? source)
    {
        if (ReferenceEquals(_observedEditableDiffLines, source))
        {
            return;
        }

        if (_observedEditableDiffLines is not null)
        {
            _observedEditableDiffLines.CollectionChanged -= Lines_CollectionChanged;
        }

        _observedEditableDiffLines = source;
        if (_observedEditableDiffLines is not null)
        {
            _observedEditableDiffLines.CollectionChanged += Lines_CollectionChanged;
        }
    }

    private void Lines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!IsEditing || ReferenceEquals(sender, _observedEditableDiffLines))
        {
            QueueDocumentRender();
        }
    }

    private void QueueDocumentRender()
    {
        if (_isDocumentRenderQueued)
        {
            return;
        }

        _isDocumentRenderQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _isDocumentRenderQueued = false;
            RenderDocument();
        }))
        {
            _isDocumentRenderQueued = false;
        }
    }

    private void RenderDocument()
    {
        IEnumerable<DiffLine>? source = IsEditing
            ? EditableDiffLinesSource as IEnumerable<DiffLine>
            : DiffLinesSource as IEnumerable<DiffLine>;
        _projection = DiffEditorProjection.Create(source);
        _contextLineIndex = null;
        _overviewKinds.Clear();
        _overviewKinds.AddRange(_projection.SourceLines.Select(line => line.Kind));

        EditorSurface.IsReadOnly = !IsEditing;
        EditorSurface.ShowCurrentLine = IsEditing;

        bool documentLoaded = false;
        _isApplyingDocument = true;
        try
        {
            if (IsEditing)
            {
                if (!_editingDocumentLoaded)
                {
                    EditorSurface.LoadText(EditableText, true);
                    _editingDocumentLoaded = true;
                    documentLoaded = true;
                }
            }
            else
            {
                EditorSurface.LoadLines(_projection.Lines, false, TextControlBoxNS.LineEnding.LF);
                documentLoaded = true;
            }
        }
        finally
        {
            _isApplyingDocument = false;
        }

        ApplyLineNumberPresentation();
        ApplySyntaxHighlighting();
        ApplyDecorations();
        UpdateChangeOverview();
        UpdateModeChrome();
        if (documentLoaded)
        {
            ApplyCurrentSearch();
        }
    }

    private void ApplyLineNumberPresentation()
    {
        if (EditorSurface is null)
        {
            return;
        }

        if (IsEditing)
        {
            EditorSurface.ClearLineNumberLabels();
            EditorSurface.ShowLineNumbers = true;
            return;
        }

        if (!IsFullFileMode)
        {
            EditorSurface.ClearLineNumberLabels();
            EditorSurface.ShowLineNumbers = false;
            return;
        }

        EditorSurface.SetLineNumberLabels(
            _projection.SourceLines.Select(line => line.LineNumberText));
        EditorSurface.ShowLineNumbers = true;
    }

    private void ApplySyntaxHighlighting()
    {
        if (EditorSurface is null)
        {
            return;
        }

        TextControlBoxNS.SyntaxHighlightID languageId = TextControlBoxSyntaxMapper.Resolve(
            SyntaxHighlightingMode,
            FilePath);
        EditorSurface.SetSyntaxHighlighting(
            languageId,
            RepositorySyntaxHighlightPalette.Create());
        EditorSurface.SetSyntaxHighlightingStateBoundaries(
            IsEditing ? [] : _projection.SyntaxStateBoundaryLines);
    }

    private void ApplyDecorations()
    {
        List<LineDecoration> lineDecorations = [];
        foreach (DiffEditorLineBlock block in _projection.LineBlocks)
        {
            Brush? background = ThemeResourceResolver.GetDiffLineBackgroundBrush(block.Kind);
            if (background is SolidColorBrush solidBackground)
            {
                lineDecorations.Add(new LineDecoration(
                    block.StartLine,
                    block.EndLine,
                    solidBackground.Color));
            }
        }

        List<TextRangeDecoration> textDecorations = [];
        foreach (DiffEditorTextRange range in _projection.TextRanges)
        {
            string resourceKey = range.Kind == DiffLineKind.Removed
                ? "DiffInlineRemovedTextBorderBrush"
                : "DiffInlineAddedTextBorderBrush";
            textDecorations.Add(new TextRangeDecoration(
                range.Line,
                range.StartColumn,
                range.Length,
                borderColor: ThemeResourceResolver.GetColor(resourceKey))
            {
                CornerRadius = InlineDiffCornerRadius,
                HorizontalPadding = InlineDiffHorizontalPadding,
            });
        }

        EditorSurface.SetLineDecorations(DiffLineDecorationGroup, lineDecorations);
        EditorSurface.SetTextDecorations(InlineDiffDecorationGroup, textDecorations);

        List<TextRangeDecoration> serviceLineDecorations = [];
        for (int lineIndex = 0; lineIndex < _projection.SourceLines.Count; lineIndex++)
        {
            DiffLine sourceLine = _projection.SourceLines[lineIndex];
            if (sourceLine.Kind is not (DiffLineKind.Header or DiffLineKind.Hunk or DiffLineKind.ConflictMarker)
                || sourceLine.Text.Length == 0
                || ThemeResourceResolver.GetDiffLineAccentBrush(sourceLine.Kind)
                    is not SolidColorBrush accent)
            {
                continue;
            }

            serviceLineDecorations.Add(new TextRangeDecoration(
                lineIndex,
                0,
                sourceLine.Text.Length,
                foregroundColor: accent.Color,
                priority: 100));
        }

        EditorSurface.SetTextDecorations(
            GitServiceLineDecorationGroup,
            serviceLineDecorations);
        ApplyLineGutterDecorations();
    }

    private void ApplyLineGutterDecorations()
    {
        List<LineGutterDecoration> gutterDecorations = [];
        Color markerColor = ThemeResourceResolver.GetColor("DiffHunkLineAccentBrush");
        int lineCount = IsEditing
            ? Math.Min(EditorSurface.NumberOfLines, _overviewKinds.Count)
            : _projection.SourceLines.Count;
        for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            DiffLineKind kind = IsEditing
                ? _overviewKinds[lineIndex]
                : _projection.SourceLines[lineIndex].Kind;
            string marker = IsEditing
                ? kind switch
                {
                    DiffLineKind.Added => "+",
                    DiffLineKind.Removed => "-",
                    _ => ""
                }
                : _projection.SourceLines[lineIndex].Marker;
            Color? backgroundColor = ThemeResourceResolver.GetDiffLineBackgroundBrush(kind)
                is SolidColorBrush background
                    ? background.Color
                    : null;
            if (marker.Length == 0 && !backgroundColor.HasValue)
            {
                continue;
            }

            gutterDecorations.Add(new LineGutterDecoration(
                lineIndex,
                marker,
                markerColor,
                backgroundColor));
        }

        EditorSurface.SetLineGutterDecorations(
            DiffLineGutterDecorationGroup,
            gutterDecorations);
    }

    private void UpdateModeChrome()
    {
        if (IgnoreWhitespaceToggleSwitch is null)
        {
            return;
        }

        Visibility viewModeVisibility = IsEditing ? Visibility.Collapsed : Visibility.Visible;
        SaveEditButton.Visibility = IsEditing ? Visibility.Visible : Visibility.Collapsed;
        SaveEditButton.IsEnabled = IsEditDirty;
        CancelEditButton.Visibility = IsEditing ? Visibility.Visible : Visibility.Collapsed;
        DiffEditorModifiedStatusTextBlock.Visibility = IsEditing && IsEditDirty
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateRevertActionVisibility();

        bool showEmptyState = HasEmptyState && !IsEditing;
        DiffSearchToggleButton.IsEnabled = !showEmptyState;
        DiffEmptyInfoBar.Message = EmptyMessage;
        DiffEmptyInfoBar.IsOpen = showEmptyState;
        DiffEmptyInfoBar.Visibility = showEmptyState ? Visibility.Visible : Visibility.Collapsed;
        EditorHostGrid.Visibility = showEmptyState ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DiffSearchToggleButton_Click(object sender, RoutedEventArgs args)
    {
        DiffSearchToggleButton.IsChecked = !string.IsNullOrEmpty(DiffSearchTextBox.Text);
        FlyoutBase.ShowAttachedFlyout(DiffSearchToggleButton);
    }

    private void DiffSearchFlyout_Opening(object sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            DiffSearchTextBox.Focus(FocusState.Programmatic);
            DiffSearchTextBox.SelectAll();
        });
    }

    private void DiffSearchTextBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        ApplyCurrentSearch();
    }

    private void PreviousDiffSearchMatchButton_Click(object sender, RoutedEventArgs args)
    {
        EditorSurface.SelectPreviousSearchMatch();
    }

    private void NextDiffSearchMatchButton_Click(object sender, RoutedEventArgs args)
    {
        EditorSurface.SelectNextSearchMatch();
    }

    private void ApplyCurrentSearch()
    {
        if (EditorSurface is null || DiffSearchTextBox is null || DiffSearchToggleButton is null)
        {
            return;
        }

        string query = DiffSearchTextBox.Text;
        DiffSearchToggleButton.IsChecked = !string.IsNullOrEmpty(query);
        if (string.IsNullOrEmpty(query))
        {
            _hasSearchMatches = false;
            EditorSurface.ClearSearch();
            UpdateSearchNavigationState();
            return;
        }

        _hasSearchMatches = EditorSurface.SearchAndSelectFirst(query);
        UpdateSearchNavigationState();
    }

    private void UpdateSearchNavigationState()
    {
        if (PreviousDiffSearchMatchButton is null || NextDiffSearchMatchButton is null)
        {
            return;
        }

        PreviousDiffSearchMatchButton.IsEnabled = _hasSearchMatches;
        NextDiffSearchMatchButton.IsEnabled = _hasSearchMatches;
    }

    private void EditorSurface_DocumentChanged(object? sender, DocumentChangedEventArgs args)
    {
        if (_isApplyingDocument || !IsEditing || args.Reason == DocumentChangeReason.Load)
        {
            return;
        }

        UpdateOverviewKinds(args.Changes);
        ApplyLineGutterDecorations();
        if (EditContentChangedCommand is ICommand command)
        {
            command.TryExecute(null);
        }
        UpdateChangeOverview();
    }

    private void UpdateOverviewKinds(IReadOnlyList<DocumentChange> changes)
    {
        foreach (DocumentChange change in changes)
        {
            int start = Math.Clamp(change.StartLine, 0, _overviewKinds.Count);
            DiffLineKind inheritedKind = start < _overviewKinds.Count
                ? _overviewKinds[start]
                : start > 0
                    ? _overviewKinds[start - 1]
                    : DiffLineKind.Context;
            int removeCount = Math.Min(change.RemovedLineCount, _overviewKinds.Count - start);
            if (removeCount > 0)
            {
                _overviewKinds.RemoveRange(start, removeCount);
            }

            if (change.InsertedLineCount > 0)
            {
                _overviewKinds.InsertRange(
                    start,
                    Enumerable.Repeat(inheritedKind, change.InsertedLineCount));
            }
        }
    }

    private void SaveEditButton_Click(object sender, RoutedEventArgs args)
    {
        string text = EditorSurface.GetText();
        if (SaveEditCommand?.CanExecute(text) == true)
        {
            SaveEditCommand.Execute(text);
        }
    }

    private void CancelEditButton_Click(object sender, RoutedEventArgs args)
    {
        if (CancelEditCommand is ICommand command)
        {
            command.TryExecute(null);
        }
    }

    private void CopyDiffSelectionMenuFlyoutItem_Click(object sender, RoutedEventArgs args)
    {
        string selectedText = EditorSurface.SelectedText;
        if (!string.IsNullOrEmpty(selectedText))
        {
            if (CopyTextCommand is ICommand command)
            {
                command.TryExecute(selectedText);
            }
        }
    }

    private void CopyDiffAllMenuFlyoutItem_Click(object sender, RoutedEventArgs args)
    {
        if (CopyTextCommand is ICommand command)
        {
            command.TryExecute(EditorSurface.GetText());
        }
    }

    private void RevertChangeMenuFlyoutItem_Click(object sender, RoutedEventArgs args)
    {
        if (TryGetCurrentChangedLine(out DiffLine? line))
        {
            if (RevertLineCommand is ICommand command)
            {
                command.TryExecute(line);
            }
        }
    }

    private void DiffContextFlyout_Opening(object sender, object args)
    {
        UpdateRevertActionVisibility();
    }

    private void EditorSurface_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        _contextLineIndex = args.TryGetPosition(EditorSurface, out Windows.Foundation.Point position)
            ? EditorSurface.GetLineIndexAt(position.Y)
            : EditorSurface.CurrentLineIndex;
        UpdateRevertActionVisibility();
    }

    private void UpdateRevertActionVisibility()
    {
        if (RevertChangeSeparator is null)
        {
            return;
        }

        Visibility visibility = !IsEditing
            && RevertActionVisibility == Visibility.Visible
            && TryGetCurrentChangedLine(out _)
                ? Visibility.Visible
                : Visibility.Collapsed;
        RevertChangeSeparator.Visibility = visibility;
        RevertChangeMenuItem.Visibility = visibility;
    }

    private bool TryGetCurrentChangedLine(out DiffLine? line)
    {
        int index = _contextLineIndex ?? EditorSurface.CurrentLineIndex;
        if (index >= 0 && index < _projection.SourceLines.Count)
        {
            DiffLine candidate = _projection.SourceLines[index];
            if (candidate.Kind is DiffLineKind.Added or DiffLineKind.Removed
                && candidate.SourceLineNumber.HasValue)
            {
                line = candidate;
                return true;
            }
        }

        line = null;
        return false;
    }

    private void DiffViewer_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplySyntaxHighlighting();
        ApplyDecorations();
        UpdateChangeOverview();
    }

    private void ChangeOverviewBar_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateChangeOverview();
    }

    private void ChangeOverviewBar_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(ChangeOverviewBar).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isChangeOverviewDragging = ChangeOverviewBar.CapturePointer(args.Pointer);
        ScrollFromChangeOverview(args);
        args.Handled = true;
    }

    private void ChangeOverviewBar_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!_isChangeOverviewDragging ||
            !args.GetCurrentPoint(ChangeOverviewBar).Properties.IsLeftButtonPressed)
        {
            return;
        }

        ScrollFromChangeOverview(args);
        args.Handled = true;
    }

    private void ChangeOverviewBar_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_isChangeOverviewDragging)
        {
            return;
        }

        ChangeOverviewBar.ReleasePointerCapture(args.Pointer);
        _isChangeOverviewDragging = false;
        args.Handled = true;
    }

    private void ScrollFromChangeOverview(PointerRoutedEventArgs args)
    {
        int lineCount = Math.Max(EditorSurface.NumberOfLines, 1);
        if (ChangeOverviewBar.ActualHeight <= 0)
        {
            return;
        }

        double ratio = Math.Clamp(
            args.GetCurrentPoint(ChangeOverviewBar).Position.Y / ChangeOverviewBar.ActualHeight,
            0,
            1);
        int line = Math.Clamp((int)Math.Round(ratio * (lineCount - 1)), 0, lineCount - 1);
        EditorSurface.ScrollLineToCenter(line);
    }

    private void UpdateChangeOverview()
    {
        ChangeMarkersCanvas.Children.Clear();
        int lineCount = _overviewKinds.Count;
        double height = ChangeOverviewBar.ActualHeight;
        if (lineCount == 0 || height <= 0)
        {
            return;
        }

        int blockStart = -1;
        DiffLineKind blockKind = DiffLineKind.Context;
        for (int index = 0; index <= lineCount; index++)
        {
            DiffLineKind kind = index < lineCount
                ? _overviewKinds[index]
                : DiffLineKind.Context;
            bool isChange = IsOverviewChange(kind);

            if (blockStart >= 0 && (!isChange || kind != blockKind))
            {
                AddChangeOverviewMarker(blockStart, index - blockStart, blockKind, lineCount, height);
                blockStart = -1;
            }

            if (blockStart < 0 && isChange)
            {
                blockStart = index;
                blockKind = kind;
            }
        }
    }

    private void AddChangeOverviewMarker(
        int startIndex,
        int lineLength,
        DiffLineKind kind,
        int lineCount,
        double overviewHeight)
    {
        double top = startIndex * overviewHeight / lineCount;
        double bottom = (startIndex + lineLength) * overviewHeight / lineCount;
        Rectangle marker = new()
        {
            Width = ChangeOverviewBar.ActualWidth,
            Height = Math.Max(2, bottom - top),
            Fill = ThemeResourceResolver.GetDiffLineBackgroundBrush(kind),
            IsHitTestVisible = false
        };

        Canvas.SetTop(marker, Math.Min(top, Math.Max(0, overviewHeight - marker.Height)));
        ChangeMarkersCanvas.Children.Add(marker);
    }

    private static bool IsOverviewChange(DiffLineKind kind)
    {
        return kind is DiffLineKind.Added or DiffLineKind.Removed or DiffLineKind.ConflictMarker;
    }
}
