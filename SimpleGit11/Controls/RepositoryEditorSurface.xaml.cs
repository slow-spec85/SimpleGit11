using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleGit11.Messages;
using SimpleGit11.Services;
using TextControlBoxNS;
using TextControlBoxNS.Models;

namespace SimpleGit11.Controls;

/// <summary>
/// Keeps TextControlBox-specific APIs behind a single presentation-layer integration boundary.
/// </summary>
public sealed partial class RepositoryEditorSurface : UserControl
{
    public const int DefaultZoomFactor = 100;
    public const int MinimumZoomFactor = 4;
    public const int MaximumZoomFactor = 400;

    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly IMessenger _messenger;
    private bool _isObservingSettings;

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(RepositoryEditorSurface),
        new PropertyMetadata(false, OnEditorOptionChanged));

    public static readonly DependencyProperty ShowLineNumbersProperty = DependencyProperty.Register(
        nameof(ShowLineNumbers),
        typeof(bool),
        typeof(RepositoryEditorSurface),
        new PropertyMetadata(true, OnEditorOptionChanged));

    public static readonly DependencyProperty ShowCurrentLineProperty = DependencyProperty.Register(
        nameof(ShowCurrentLine),
        typeof(bool),
        typeof(RepositoryEditorSurface),
        new PropertyMetadata(true, OnEditorOptionChanged));

    public static readonly DependencyProperty EnableSyntaxHighlightingProperty = DependencyProperty.Register(
        nameof(EnableSyntaxHighlighting),
        typeof(bool),
        typeof(RepositoryEditorSurface),
        new PropertyMetadata(true, OnEditorOptionChanged));

    public static readonly DependencyProperty ContextFlyoutDisabledProperty = DependencyProperty.Register(
        nameof(ContextFlyoutDisabled),
        typeof(bool),
        typeof(RepositoryEditorSurface),
        new PropertyMetadata(false, OnEditorOptionChanged));

    public static readonly DependencyProperty RightGutterContentProperty = DependencyProperty.Register(
        nameof(RightGutterContent),
        typeof(object),
        typeof(RepositoryEditorSurface),
        new PropertyMetadata(null, OnEditorOptionChanged));

    public static readonly DependencyProperty ShowLineGutterProperty = DependencyProperty.Register(
        nameof(ShowLineGutter),
        typeof(bool),
        typeof(RepositoryEditorSurface),
        new PropertyMetadata(true, OnEditorOptionChanged));

    public static readonly DependencyProperty LineGutterWidthProperty = DependencyProperty.Register(
        nameof(LineGutterWidth),
        typeof(double),
        typeof(RepositoryEditorSurface),
        new PropertyMetadata(24d, OnEditorOptionChanged));

    public static readonly DependencyProperty ZoomFactorProperty = DependencyProperty.Register(
        nameof(ZoomFactor),
        typeof(int),
        typeof(RepositoryEditorSurface),
        new PropertyMetadata(DefaultZoomFactor, OnZoomFactorChanged));

    public RepositoryEditorSurface()
    {
        InitializeComponent();
        _settingsService = App.GetService<ISettingsService>();
        _localizationService = App.GetService<ILocalizationService>();
        _messenger = App.GetService<IMessenger>();
        Editor.DocumentChanged += Editor_DocumentChanged;
        Editor.ZoomChanged += Editor_ZoomChanged;
        Editor.SyntaxHighlightingRuleQuarantined += Editor_SyntaxHighlightingRuleQuarantined;
        Loaded += RepositoryEditorSurface_Loaded;
        Unloaded += RepositoryEditorSurface_Unloaded;
        ActualThemeChanged += RepositoryEditorSurface_ActualThemeChanged;
        ApplyEditorAppearance();
        ApplyEditorOptions();
    }

    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool ShowLineNumbers
    {
        get => (bool)GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    public bool ShowCurrentLine
    {
        get => (bool)GetValue(ShowCurrentLineProperty);
        set => SetValue(ShowCurrentLineProperty, value);
    }

    public bool EnableSyntaxHighlighting
    {
        get => (bool)GetValue(EnableSyntaxHighlightingProperty);
        set => SetValue(EnableSyntaxHighlightingProperty, value);
    }

    public bool ContextFlyoutDisabled
    {
        get => (bool)GetValue(ContextFlyoutDisabledProperty);
        set => SetValue(ContextFlyoutDisabledProperty, value);
    }

    public object? RightGutterContent
    {
        get => GetValue(RightGutterContentProperty);
        set => SetValue(RightGutterContentProperty, value);
    }

    public bool ShowLineGutter
    {
        get => (bool)GetValue(ShowLineGutterProperty);
        set => SetValue(ShowLineGutterProperty, value);
    }

    public double LineGutterWidth
    {
        get => (double)GetValue(LineGutterWidthProperty);
        set => SetValue(LineGutterWidthProperty, value);
    }

    public int ZoomFactor
    {
        get => (int)GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    public int NumberOfLines => Editor.NumberOfLines;

    public int CurrentLineIndex => Editor.CurrentLineIndex;

    public int GetLineIndexAt(double viewportY)
    {
        double lineHeight = Editor.ActualLineHeight;
        if (NumberOfLines == 0 || lineHeight <= 0 || viewportY < 0)
        {
            return -1;
        }

        double documentY = viewportY + Editor.VerticalScroll;
        return Math.Clamp((int)(documentY / lineHeight), 0, NumberOfLines - 1);
    }

    public bool CanUndo => Editor.CanUndo;

    public bool CanRedo => Editor.CanRedo;

    public string SelectedText => Editor.SelectedText;

    public void LoadText(string text, bool autodetectTabsSpaces = true)
    {
        Editor.LoadText(text, autodetectTabsSpaces);
    }

    public void LoadLines(
        IEnumerable<string> lines,
        bool autodetectTabsSpaces = true,
        LineEnding lineEnding = LineEnding.CRLF)
    {
        Editor.LoadLines(lines, autodetectTabsSpaces, lineEnding);
    }

    public string GetText()
    {
        return Editor.GetText();
    }

    public string GetLineText(int line)
    {
        return Editor.GetLineText(line);
    }

    public void SetSyntaxHighlighting(
        SyntaxHighlightID languageId,
        SyntaxHighlightPalette palette)
    {
        Editor.SyntaxHighlightPalette = palette;
        EnableSyntaxHighlighting = languageId != SyntaxHighlightID.None;
        Editor.SelectSyntaxHighlightingById(languageId);
    }

    public void SetSyntaxHighlightingStateBoundaries(IEnumerable<int> lineIndices)
    {
        Editor.SetSyntaxHighlightingStateBoundaries(lineIndices);
    }

    public void SetLineDecorations(string groupKey, IEnumerable<LineDecoration> decorations)
    {
        Editor.SetLineDecorations(groupKey, decorations);
    }

    public void SetLineGutterDecorations(
        string groupKey,
        IEnumerable<LineGutterDecoration> decorations)
    {
        Editor.SetLineGutterDecorations(groupKey, decorations);
    }

    public void SetLineNumberLabels(IEnumerable<string> labels)
    {
        Editor.SetLineNumberLabels(labels);
    }

    public void ClearLineNumberLabels()
    {
        Editor.ClearLineNumberLabels();
    }

    public void SetTextDecorations(string groupKey, IEnumerable<TextRangeDecoration> decorations)
    {
        Editor.SetTextDecorations(groupKey, decorations);
    }

    public void Undo()
    {
        Editor.Undo();
    }

    public void Redo()
    {
        Editor.Redo();
    }

    public void SelectAll()
    {
        Editor.SelectAll();
    }

    public void Copy()
    {
        Editor.Copy();
    }

    public void ScrollLineToCenter(int line)
    {
        Editor.ScrollLineToCenter(line);
    }

    public void BeginActionGroup()
    {
        Editor.BeginActionGroup();
    }

    public void EndActionGroup()
    {
        Editor.EndActionGroup();
    }

    public void ResetZoom()
    {
        ZoomFactor = DefaultZoomFactor;
    }

    public void SetCursorPosition(
        int lineNumber,
        int characterPosition,
        bool scrollIntoView = true,
        bool autoClamp = true)
    {
        Editor.SetCursorPosition(lineNumber, characterPosition, scrollIntoView, autoClamp);
    }

    private static void OnEditorOptionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        RepositoryEditorSurface surface = (RepositoryEditorSurface)dependencyObject;
        surface.ApplyEditorOptions();
    }

    private static void OnZoomFactorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        RepositoryEditorSurface surface = (RepositoryEditorSurface)dependencyObject;
        surface.Editor.ZoomFactor = (int)args.NewValue;
    }

    private void ApplyEditorOptions()
    {
        Editor.IsReadOnly = IsReadOnly;
        Editor.ShowLineNumbers = ShowLineNumbers;
        Editor.ShowLineHighlighter = ShowCurrentLine;
        Editor.EnableSyntaxHighlighting = EnableSyntaxHighlighting;
        Editor.ContextFlyoutDisabled = ContextFlyoutDisabled;
        Editor.RightGutterContent = RightGutterContent!;
        Editor.ShowLineGutter = ShowLineGutter;
        Editor.LineGutterWidth = LineGutterWidth;
    }

    private void Editor_DocumentChanged(object? sender, DocumentChangedEventArgs args)
    {
        DocumentChanged?.Invoke(this, args);
    }

    private void Editor_ZoomChanged(TextControlBox sender, int zoomFactor)
    {
        if (ZoomFactor != zoomFactor)
        {
            ZoomFactor = zoomFactor;
        }
    }

    private void Editor_SyntaxHighlightingRuleQuarantined(
        object? sender,
        SyntaxHighlightingRuleQuarantinedEventArgs args)
    {
        string language = string.IsNullOrWhiteSpace(args.Language.Name)
            ? _localizationService.GetString("SyntaxHighlightingUnknownLanguage")
            : args.Language.Name;
        string ruleType = args.RuleType.FullName ?? args.RuleType.Name;
        string pattern = string.IsNullOrEmpty(args.Pattern)
            ? _localizationService.GetString("SyntaxHighlightingPatternUnavailable")
            : args.Pattern;
        string exceptionType = args.Exception.GetType().FullName
            ?? args.Exception.GetType().Name;
        string details = string.Format(
            CultureInfo.CurrentCulture,
            _localizationService.GetString("SyntaxHighlightingRuleQuarantinedDetails"),
            language,
            ruleType,
            pattern,
            args.MatchTimeout.TotalMilliseconds,
            args.InputLength,
            exceptionType,
            args.Exception.Message);

        _messenger.Send(new AppNotificationMessage(
            this,
            AppNotificationSeverity.Warning,
            _localizationService.GetString("SyntaxHighlightingRuleQuarantinedTitle"),
            details));
    }

    private void ResetZoomKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ResetZoom();
        args.Handled = true;
    }

    private void RepositoryEditorSurface_Loaded(object sender, RoutedEventArgs args)
    {
        if (!_isObservingSettings)
        {
            _settingsService.EditorAppearanceChanged += SettingsService_EditorAppearanceChanged;
            _isObservingSettings = true;
        }

        ApplyEditorTheme();
        ApplyEditorAppearance();
    }

    private void RepositoryEditorSurface_Unloaded(object sender, RoutedEventArgs args)
    {
        if (_isObservingSettings)
        {
            _settingsService.EditorAppearanceChanged -= SettingsService_EditorAppearanceChanged;
            _isObservingSettings = false;
        }
    }

    private void SettingsService_EditorAppearanceChanged(object? sender, EventArgs args)
    {
        ApplyEditorAppearance();
    }

    private void RepositoryEditorSurface_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyEditorTheme();
    }

    private void ApplyEditorTheme()
    {
        Editor.RequestedTheme = ActualTheme;
    }

    private void ApplyEditorAppearance()
    {
        Editor.FontFamily = new FontFamily(_settingsService.Current.EditorFontFamily);
        Editor.FontSize = _settingsService.Current.EditorFontSize;
        Editor.LineSpacing = _settingsService.Current.EditorLineSpacing;
    }
}
