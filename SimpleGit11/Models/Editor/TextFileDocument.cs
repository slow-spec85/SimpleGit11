using System.Text;
using System;

namespace SimpleGit11.Models;

public sealed class TextFileDocument
{
    public TextFileDocument(
        string path,
        string text,
        Encoding encoding,
        bool emitByteOrderMark,
        string newLine,
        Guid? executionContextId = null)
    {
        Path = path;
        Text = text;
        Encoding = encoding;
        EmitByteOrderMark = emitByteOrderMark;
        NewLine = newLine;
        ExecutionContextId = executionContextId;
    }

    public string Path { get; }

    public string Text { get; }

    public Encoding Encoding { get; }

    public bool EmitByteOrderMark { get; }

    public string NewLine { get; }

    public Guid? ExecutionContextId { get; }
}
