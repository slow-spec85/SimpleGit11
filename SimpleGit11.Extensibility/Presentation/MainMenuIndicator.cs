namespace SimpleGit11.Extensibility.Presentation;

public sealed record MainMenuIndicator(
    MainMenuIndicatorKind Kind,
    string AccessibleText)
{
    public static MainMenuIndicator None { get; } = new(MainMenuIndicatorKind.None, string.Empty);
}
