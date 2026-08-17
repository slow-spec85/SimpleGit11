using System.Text;

namespace SimpleGit11.Models;

public sealed class TextFileDocument
{
    public TextFileDocument(
        string path,
        string text,
        Encoding encoding,
        bool emitByteOrderMark,
        string newLine)
    {
        Path = path;
        Text = text;
        Encoding = encoding;
        EmitByteOrderMark = emitByteOrderMark;
        NewLine = newLine;
    }

    public string Path { get; }

    public string Text { get; }

    public Encoding Encoding { get; }

    public bool EmitByteOrderMark { get; }

    public string NewLine { get; }
}
