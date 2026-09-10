using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SimpleGit11.Presentation.Commits;
using SimpleGit11.Presentation.Theming;
using Windows.Foundation;

namespace SimpleGit11.Controls;

public sealed class CommitGraphCell : Canvas
{
    private const double HorizontalPadding = 2;
    private const double LaneSpacing = 8;
    private const double MinimumWidth = 12;
    private const double NodeSize = 8;

    public static readonly DependencyProperty GraphProperty = DependencyProperty.Register(
        nameof(Graph),
        typeof(CommitGraphRow),
        typeof(CommitGraphCell),
        new PropertyMetadata(CommitGraphRow.Empty, OnGraphChanged));

    public CommitGraphCell()
    {
        IsHitTestVisible = false;
        MinHeight = 40;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Stretch;
        SizeChanged += CommitGraphCell_SizeChanged;
        Loaded += CommitGraphCell_Loaded;
        Unloaded += CommitGraphCell_Unloaded;
    }

    public CommitGraphRow Graph
    {
        get => (CommitGraphRow)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    private static void OnGraphChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is CommitGraphCell cell)
        {
            cell.UpdateWidth();
            cell.RenderGraph();
        }
    }

    private void CommitGraphCell_Loaded(object sender, RoutedEventArgs e)
    {
        ActualThemeChanged -= CommitGraphCell_ActualThemeChanged;
        ActualThemeChanged += CommitGraphCell_ActualThemeChanged;
        UpdateWidth();
        RenderGraph();
    }

    private void CommitGraphCell_Unloaded(object sender, RoutedEventArgs e)
    {
        ActualThemeChanged -= CommitGraphCell_ActualThemeChanged;
    }

    private void CommitGraphCell_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.NewSize.Equals(e.PreviousSize))
        {
            RenderGraph();
        }
    }

    private void CommitGraphCell_ActualThemeChanged(FrameworkElement sender, object args)
    {
        RenderGraph();
    }

    private void UpdateWidth()
    {
        Width = Graph.LaneCount == 0
            ? 0
            : Math.Max(MinimumWidth, HorizontalPadding * 2 + Graph.LaneCount * LaneSpacing);
    }

    private void RenderGraph()
    {
        Children.Clear();
        if (Graph.LaneCount == 0 || ActualHeight <= 0)
        {
            return;
        }

        foreach (CommitGraphSegment segment in Graph.Segments)
        {
            Children.Add(CreateSegmentPath(segment));
        }

        double nodeX = GetLaneX(Graph.NodeLane) - NodeSize / 2;
        double nodeY = ActualHeight / 2 - NodeSize / 2;
        Ellipse node = new()
        {
            Width = NodeSize,
            Height = NodeSize,
            Fill = GetLaneBrush(Graph.NodeColorIndex)
        };
        SetLeft(node, nodeX);
        SetTop(node, nodeY);
        Children.Add(node);
    }

    private Path CreateSegmentPath(CommitGraphSegment segment)
    {
        Point start = GetPoint(segment.StartLane, segment.Start);
        Point end = GetPoint(segment.EndLane, segment.End);
        PathGeometry geometry = new();
        PathFigure figure = new() { StartPoint = start };
        if (segment.StartLane == segment.EndLane)
        {
            figure.Segments.Add(new LineSegment { Point = end });
        }
        else
        {
            double middleY = (start.Y + end.Y) / 2;
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(start.X, middleY),
                Point2 = new Point(end.X, middleY),
                Point3 = end
            });
        }

        geometry.Figures.Add(figure);
        Path path = new()
        {
            Data = geometry,
            Stroke = GetLaneBrush(segment.ColorIndex),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        if (segment.IsCollapsed)
        {
            path.StrokeDashArray = [2, 2];
        }

        return path;
    }

    private Point GetPoint(int lane, CommitGraphEndpoint endpoint)
    {
        double y = endpoint switch
        {
            CommitGraphEndpoint.Top => 0,
            CommitGraphEndpoint.Node => ActualHeight / 2,
            CommitGraphEndpoint.Bottom => ActualHeight,
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
        };
        return new Point(GetLaneX(lane), y);
    }

    private static double GetLaneX(int lane) => HorizontalPadding + LaneSpacing / 2 + lane * LaneSpacing;

    private static Brush GetLaneBrush(int colorIndex)
    {
        int normalizedIndex = Math.Abs(colorIndex % 6) + 1;
        return ThemeResourceResolver.GetBrush($"CommitGraphLane{normalizedIndex}Brush");
    }
}
