using System.Collections.Generic;

namespace SimpleGit11.Models;

public sealed record ConflictEditorDocumentChange(
    int StartLine,
    int RemovedLineCount,
    IReadOnlyList<string> InsertedLines);
