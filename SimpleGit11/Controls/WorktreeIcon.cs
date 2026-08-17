using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace SimpleGit11.Controls;

public sealed class WorktreeIcon : PathIcon
{
    private const string GeometryResourceKey = "WorktreeIconData";

    public WorktreeIcon()
    {
        string geometryData = (string)Application.Current.Resources[GeometryResourceKey];

        Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), geometryData);
        Height = 12;
        Width = 12;
    }
}
