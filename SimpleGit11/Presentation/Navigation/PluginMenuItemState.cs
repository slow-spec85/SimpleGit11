using SimpleGit11.Extensibility.Presentation;

namespace SimpleGit11.Presentation.Navigation;

internal sealed record PluginMenuItemState(
    string Label,
    string IconGlyph,
    MainMenuIndicator Indicator,
    bool IsEnabled)
{
    public string ToolTipText => string.IsNullOrWhiteSpace(Indicator.AccessibleText)
        ? Label
        : Indicator.AccessibleText;

    public string AccessibleName => string.IsNullOrWhiteSpace(Indicator.AccessibleText)
        ? Label
        : $"{Label} — {Indicator.AccessibleText}";
}
