using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
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
using SimpleGit11.ViewModels;
using TextControlBoxNS.Models;

namespace SimpleGit11.Controls;

public sealed partial class ConflictEditor : UserControl
{
    private const string ConflictDecorationGroup = "conflicts";
    private const string ConflictMarkerTextDecorationGroup = "conflict-marker-text";

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(ConflictEditorViewModel),
        typeof(ConflictEditor),
        new PropertyMetadata(null, OnViewModelChanged));

    private readonly List<DiffLineKind> _overviewKinds = [];
    private INotifyCollectionChanged? _observedLines;
    private INotifyPropertyChanged? _observedViewModel;
    private bool _isApplyingDocument;
    private bool _isApplyingEditorChange;
    private bool _isDocumentRenderQueued;
    private bool _isOverviewDragging;
    private bool _hasSearchMatches;

    public ConflictEditor()
    {
        InitializeComponent();
        EditorSurface.DocumentChanged += EditorSurface_DocumentChanged;
        Loaded += ConflictEditor_Loaded;
        Unloaded += ConflictEditor_Unloaded;
    }

    public ConflictEditorViewModel? ViewModel
    {
        get => (ConflictEditorViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ConflictEditor editor = (ConflictEditor)sender;
        editor.ObserveViewModel(args.NewValue as ConflictEditorViewModel);
        editor.QueueDocumentRender();
    }

    private void ConflictEditor_Loaded(object sender, RoutedEventArgs args)
    {
        ActualThemeChanged -= ConflictEditor_ActualThemeChanged;
        ActualThemeChanged += ConflictEditor_ActualThemeChanged;
        ObserveViewModel(ViewModel);
        RenderDocument();
    }

    private void ConflictEditor_Unloaded(object sender, RoutedEventArgs args)
    {
        ActualThemeChanged -= ConflictEditor_ActualThemeChanged;
        ObserveViewModel(null);
    }

    private void ObserveViewModel(ConflictEditorViewModel? viewModel)
    {
        if (!ReferenceEquals(_observedViewModel, viewModel))
        {
            if (_observedViewModel is not null)
            {
                _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _observedViewModel = viewModel;
            if (_observedViewModel is not null)
            {
                _observedViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        INotifyCollectionChanged? lines = viewModel?.Lines;
        if (ReferenceEquals(_observedLines, lines))
        {
            return;
        }

        if (_observedLines is not null)
        {
            _observedLines.CollectionChanged -= Lines_CollectionChanged;
        }

        _observedLines = lines;
        if (_observedLines is not null)
        {
            _observedLines.CollectionChanged += Lines_CollectionChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ConflictEditorViewModel.SelectedSyntaxHighlightingOption)
            or nameof(ConflictEditorViewModel.RelativePath))
        {
            ApplySyntaxHighlighting();
        }

        if (args.PropertyName is nameof(ConflictEditorViewModel.IsOperationRunning)
            or nameof(ConflictEditorViewModel.IsDirty)
            or nameof(ConflictEditorViewModel.HasConflictMarkers))
        {
            UpdateEditorState();
        }
    }

    private void Lines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!_isApplyingEditorChange)
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
        IReadOnlyList<ConflictEditorLine> lines = ViewModel?.Lines.ToArray() ?? [];
        _isApplyingDocument = true;
        try
        {
            EditorSurface.IsReadOnly = ViewModel?.CanEdit != true;
            EditorSurface.LoadLines(
                lines.Select(line => line.Text),
                false,
                TextControlBoxNS.LineEnding.LF);
        }
        finally
        {
            _isApplyingDocument = false;
        }

        ApplySyntaxHighlighting();
        ApplyDecorations();
        UpdateEditorState();
        ApplyCurrentSearch();
    }

    private void ApplySyntaxHighlighting()
    {
        SyntaxHighlightingMode mode =
            ViewModel?.SelectedSyntaxHighlightingMode ?? SyntaxHighlightingMode.Auto;
        TextControlBoxNS.SyntaxHighlightID languageId = TextControlBoxSyntaxMapper.Resolve(
            mode,
            ViewModel?.RelativePath);
        EditorSurface.SetSyntaxHighlighting(
            languageId,
            RepositorySyntaxHighlightPalette.Create());
    }

    private void ApplyDecorations()
    {
        IReadOnlyList<ConflictEditorLine> lines = ViewModel?.Lines.ToArray() ?? [];
        List<LineDecoration> decorations = [];
        _overviewKinds.Clear();
        _overviewKinds.AddRange(lines.Select(line => line.BackgroundKind));

        int blockStart = -1;
        DiffLineKind blockKind = DiffLineKind.Context;
        for (int index = 0; index <= lines.Count; index++)
        {
            DiffLineKind kind = index < lines.Count
                ? lines[index].BackgroundKind
                : DiffLineKind.Context;
            bool hasBackground = kind != DiffLineKind.Context;
            if (blockStart >= 0 && (!hasBackground || kind != blockKind))
            {
                Brush? background = ThemeResourceResolver.GetDiffLineBackgroundBrush(blockKind);
                if (background is SolidColorBrush solidBackground)
                {
                    decorations.Add(new LineDecoration(
                        blockStart,
                        index - 1,
                        solidBackground.Color));
                }

                blockStart = -1;
            }

            if (blockStart < 0 && hasBackground)
            {
                blockStart = index;
                blockKind = kind;
            }
        }

        EditorSurface.SetLineDecorations(ConflictDecorationGroup, decorations);

        List<TextRangeDecoration> markerTextDecorations = [];
        for (int index = 0; index < lines.Count; index++)
        {
            ConflictEditorLine line = lines[index];
            if (line.Role != ConflictEditorLineRole.Marker
                || line.Text.Length == 0
                || ThemeResourceResolver.GetDiffLineAccentBrush(line.BackgroundKind)
                    is not SolidColorBrush accent)
            {
                continue;
            }

            markerTextDecorations.Add(new TextRangeDecoration(
                index,
                0,
                line.Text.Length,
                foregroundColor: accent.Color,
                priority: 100));
        }

        EditorSurface.SetTextDecorations(
            ConflictMarkerTextDecorationGroup,
            markerTextDecorations);
        UpdateConflictOverview();
    }

    private void EditorSurface_DocumentChanged(object? sender, DocumentChangedEventArgs args)
    {
        if (_isApplyingDocument ||
            ViewModel is not ConflictEditorViewModel viewModel ||
            args.Reason == DocumentChangeReason.Load)
        {
            return;
        }

        List<ConflictEditorDocumentChange> changes = [];
        foreach (DocumentChange change in args.Changes)
        {
            List<string> insertedLines = [];
            for (int index = 0; index < change.InsertedLineCount; index++)
            {
                int line = change.StartLine + index;
                if (line >= 0 && line < EditorSurface.NumberOfLines)
                {
                    insertedLines.Add(EditorSurface.GetLineText(line));
                }
            }

            changes.Add(new ConflictEditorDocumentChange(
                change.StartLine,
                change.RemovedLineCount,
                insertedLines));
        }

        _isApplyingEditorChange = true;
        try
        {
            viewModel.ApplyEditorChanges(changes);
        }
        finally
        {
            _isApplyingEditorChange = false;
        }

        ApplyDecorations();
        UpdateEditorState();
    }

    private void UpdateEditorState()
    {
        EditorSurface.IsReadOnly = ViewModel?.CanEdit != true;
        UndoEditButton.IsEnabled = EditorSurface.CanUndo || ViewModel?.CanUndo == true;
        RedoEditButton.IsEnabled = EditorSurface.CanRedo || ViewModel?.CanRedo == true;
    }

    private void ConflictSearchToggleButton_Click(object sender, RoutedEventArgs args)
    {
        ConflictSearchToggleButton.IsChecked = !string.IsNullOrEmpty(ConflictSearchTextBox.Text);
        FlyoutBase.ShowAttachedFlyout(ConflictSearchToggleButton);
    }

    private void ConflictSearchFlyout_Opening(object sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ConflictSearchTextBox.Focus(FocusState.Programmatic);
            ConflictSearchTextBox.SelectAll();
        });
    }

    private void ConflictSearchTextBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        ApplyCurrentSearch();
    }

    private void PreviousConflictSearchMatchButton_Click(object sender, RoutedEventArgs args)
    {
        EditorSurface.SelectPreviousSearchMatch();
    }

    private void NextConflictSearchMatchButton_Click(object sender, RoutedEventArgs args)
    {
        EditorSurface.SelectNextSearchMatch();
    }

    private void ApplyCurrentSearch()
    {
        if (EditorSurface is null || ConflictSearchTextBox is null || ConflictSearchToggleButton is null)
        {
            return;
        }

        string query = ConflictSearchTextBox.Text;
        ConflictSearchToggleButton.IsChecked = !string.IsNullOrEmpty(query);
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
        if (PreviousConflictSearchMatchButton is null || NextConflictSearchMatchButton is null)
        {
            return;
        }

        PreviousConflictSearchMatchButton.IsEnabled = _hasSearchMatches;
        NextConflictSearchMatchButton.IsEnabled = _hasSearchMatches;
    }

    private void UndoEditButton_Click(object sender, RoutedEventArgs args)
    {
        Undo();
    }

    private void RedoEditButton_Click(object sender, RoutedEventArgs args)
    {
        Redo();
    }

    private void UndoKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Undo();
        args.Handled = true;
    }

    private void RedoKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Redo();
        args.Handled = true;
    }

    private void Undo()
    {
        if (EditorSurface.CanUndo)
        {
            EditorSurface.Undo();
        }
        else if (ViewModel?.UndoCommand is ICommand command)
        {
            command.TryExecute(null);
        }

        UpdateEditorState();
    }

    private void Redo()
    {
        if (EditorSurface.CanRedo)
        {
            EditorSurface.Redo();
        }
        else if (ViewModel?.RedoCommand is ICommand command)
        {
            command.TryExecute(null);
        }

        UpdateEditorState();
    }

    private void AcceptAllCurrentMenuFlyoutItem_Click(object sender, RoutedEventArgs args)
    {
        ExecuteAcceptCommand(viewModel => viewModel.AcceptAllCurrentCommand);
    }

    private void AcceptAllIncomingMenuFlyoutItem_Click(object sender, RoutedEventArgs args)
    {
        ExecuteAcceptCommand(viewModel => viewModel.AcceptAllIncomingCommand);
    }

    private void AcceptAllBothMenuFlyoutItem_Click(object sender, RoutedEventArgs args)
    {
        ExecuteAcceptCommand(viewModel => viewModel.AcceptAllBothCommand);
    }

    private void ExecuteAcceptCommand(Func<ConflictEditorViewModel, ICommand> commandSelector)
    {
        if (ViewModel is ConflictEditorViewModel viewModel)
        {
            commandSelector(viewModel).TryExecute(null);
        }
    }

    private void ConflictEditor_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplySyntaxHighlighting();
        ApplyDecorations();
    }

    private void ConflictOverviewBar_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateConflictOverview();
    }

    private void ConflictOverviewBar_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(ConflictOverviewBar).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isOverviewDragging = ConflictOverviewBar.CapturePointer(args.Pointer);
        ScrollFromConflictOverview(args);
        args.Handled = true;
    }

    private void ConflictOverviewBar_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!_isOverviewDragging ||
            !args.GetCurrentPoint(ConflictOverviewBar).Properties.IsLeftButtonPressed)
        {
            return;
        }

        ScrollFromConflictOverview(args);
        args.Handled = true;
    }

    private void ConflictOverviewBar_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_isOverviewDragging)
        {
            return;
        }

        ConflictOverviewBar.ReleasePointerCapture(args.Pointer);
        _isOverviewDragging = false;
        args.Handled = true;
    }

    private void ScrollFromConflictOverview(PointerRoutedEventArgs args)
    {
        int lineCount = Math.Max(EditorSurface.NumberOfLines, 1);
        if (ConflictOverviewBar.ActualHeight <= 0)
        {
            return;
        }

        double ratio = Math.Clamp(
            args.GetCurrentPoint(ConflictOverviewBar).Position.Y / ConflictOverviewBar.ActualHeight,
            0,
            1);
        int line = Math.Clamp((int)Math.Round(ratio * (lineCount - 1)), 0, lineCount - 1);
        EditorSurface.ScrollLineToCenter(line);
    }

    private void UpdateConflictOverview()
    {
        ConflictMarkersCanvas.Children.Clear();
        int lineCount = _overviewKinds.Count;
        double height = ConflictOverviewBar.ActualHeight;
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
            bool isChange = kind != DiffLineKind.Context;

            if (blockStart >= 0 && (!isChange || kind != blockKind))
            {
                AddOverviewMarker(blockStart, index - blockStart, blockKind, lineCount, height);
                blockStart = -1;
            }

            if (blockStart < 0 && isChange)
            {
                blockStart = index;
                blockKind = kind;
            }
        }
    }

    private void AddOverviewMarker(
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
            Width = ConflictOverviewBar.ActualWidth,
            Height = Math.Max(2, bottom - top),
            Fill = ThemeResourceResolver.GetDiffLineBackgroundBrush(kind),
            IsHitTestVisible = false
        };

        Canvas.SetTop(marker, Math.Min(top, Math.Max(0, overviewHeight - marker.Height)));
        ConflictMarkersCanvas.Children.Add(marker);
    }
}
