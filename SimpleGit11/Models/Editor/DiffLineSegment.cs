namespace SimpleGit11.Models;

public sealed class DiffLineSegment
{
    public DiffLineSegment(int startIndex, int length)
    {
        StartIndex = startIndex;
        Length = length;
    }

    public int StartIndex { get; }

    public int Length { get; }
}
