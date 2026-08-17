namespace SimpleGit11.Models;

public sealed class DisplayOption<T>(T value, string displayName)
    where T : struct, System.Enum
{
    public T Value { get; } = value;

    public string DisplayName { get; } = displayName;
}
