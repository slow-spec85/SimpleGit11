using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SimpleGit11.Controls;

public sealed partial class DiffStatPresenter : UserControl
{
    public static readonly DependencyProperty AddedTextProperty = DependencyProperty.Register(
        nameof(AddedText),
        typeof(string),
        typeof(DiffStatPresenter),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RemovedTextProperty = DependencyProperty.Register(
        nameof(RemovedText),
        typeof(string),
        typeof(DiffStatPresenter),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ColumnMetricsProperty = DependencyProperty.Register(
        nameof(ColumnMetrics),
        typeof(DiffStatColumnMetrics),
        typeof(DiffStatPresenter),
        new PropertyMetadata(null, OnColumnMetricsChanged));

    public static readonly DependencyProperty IsColumnWidthSourceProperty = DependencyProperty.Register(
        nameof(IsColumnWidthSource),
        typeof(bool),
        typeof(DiffStatPresenter),
        new PropertyMetadata(false, OnLayoutPropertyChanged));

    private bool _isLoaded;
    private bool _isSubscribed;

    public DiffStatPresenter()
    {
        InitializeComponent();
        Loaded += DiffStatPresenter_Loaded;
        Unloaded += DiffStatPresenter_Unloaded;
    }

    public string AddedText
    {
        get => (string)GetValue(AddedTextProperty);
        set => SetValue(AddedTextProperty, value);
    }

    public string RemovedText
    {
        get => (string)GetValue(RemovedTextProperty);
        set => SetValue(RemovedTextProperty, value);
    }

    public DiffStatColumnMetrics? ColumnMetrics
    {
        get => (DiffStatColumnMetrics?)GetValue(ColumnMetricsProperty);
        set => SetValue(ColumnMetricsProperty, value);
    }

    public bool IsColumnWidthSource
    {
        get => (bool)GetValue(IsColumnWidthSourceProperty);
        set => SetValue(IsColumnWidthSourceProperty, value);
    }

    private static void OnLayoutPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        DiffStatPresenter presenter = (DiffStatPresenter)dependencyObject;
        if (presenter._isLoaded)
        {
            presenter.ConfigureColumns();
        }
    }

    private static void OnColumnMetricsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        DiffStatPresenter presenter = (DiffStatPresenter)dependencyObject;
        if (presenter._isSubscribed
            && args.OldValue is DiffStatColumnMetrics oldMetrics)
        {
            oldMetrics.PropertyChanged -= presenter.ColumnMetrics_PropertyChanged;
            presenter._isSubscribed = false;
        }

        if (presenter._isLoaded)
        {
            presenter.ConfigureColumns();
        }
    }

    private void DiffStatPresenter_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ConfigureColumns();
    }

    private void DiffStatPresenter_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        UnsubscribeFromMetrics();
    }

    private void ConfigureColumns()
    {
        UnsubscribeFromMetrics();

        if (IsColumnWidthSource)
        {
            AddedColumn.Width = GridLength.Auto;
            RemovedColumn.Width = GridLength.Auto;
            PublishMeasuredWidths();
            return;
        }

        SubscribeToMetrics();
        ApplyColumnMetrics();
    }

    private void SubscribeToMetrics()
    {
        if (ColumnMetrics is null || _isSubscribed)
        {
            return;
        }

        ColumnMetrics.PropertyChanged += ColumnMetrics_PropertyChanged;
        _isSubscribed = true;
    }

    private void UnsubscribeFromMetrics()
    {
        if (ColumnMetrics is null || !_isSubscribed)
        {
            return;
        }

        ColumnMetrics.PropertyChanged -= ColumnMetrics_PropertyChanged;
        _isSubscribed = false;
    }

    private void ColumnMetrics_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiffStatColumnMetrics.AddedWidth)
            or nameof(DiffStatColumnMetrics.RemovedWidth))
        {
            ApplyColumnMetrics();
        }
    }

    private void ApplyColumnMetrics()
    {
        if (ColumnMetrics is null)
        {
            AddedColumn.Width = GridLength.Auto;
            RemovedColumn.Width = GridLength.Auto;
            return;
        }

        AddedColumn.Width = new GridLength(ColumnMetrics.AddedWidth);
        RemovedColumn.Width = new GridLength(ColumnMetrics.RemovedWidth);
    }

    private void StatText_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsColumnWidthSource)
        {
            PublishMeasuredWidths();
        }
    }

    private void PublishMeasuredWidths()
    {
        ColumnMetrics?.Update(AddedTextElement.ActualWidth, RemovedTextElement.ActualWidth);
    }
}
