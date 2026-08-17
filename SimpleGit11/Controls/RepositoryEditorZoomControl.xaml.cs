using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SimpleGit11.Controls;

public sealed partial class RepositoryEditorZoomControl : UserControl
{
    public static readonly DependencyProperty EditorSurfaceProperty = DependencyProperty.Register(
        nameof(EditorSurface),
        typeof(RepositoryEditorSurface),
        typeof(RepositoryEditorZoomControl),
        new PropertyMetadata(null));

    public RepositoryEditorZoomControl()
    {
        InitializeComponent();
        EditorZoomSlider.Minimum = RepositoryEditorSurface.MinimumZoomFactor;
        EditorZoomSlider.Maximum = RepositoryEditorSurface.MaximumZoomFactor;
    }

    public RepositoryEditorSurface? EditorSurface
    {
        get => (RepositoryEditorSurface?)GetValue(EditorSurfaceProperty);
        set => SetValue(EditorSurfaceProperty, value);
    }

    private void ResetEditorZoomButton_Click(object sender, RoutedEventArgs args)
    {
        EditorSurface?.ResetZoom();
    }
}
