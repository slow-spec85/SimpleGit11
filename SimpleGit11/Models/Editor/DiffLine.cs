using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimpleGit11.Models;

public sealed class DiffLine : INotifyPropertyChanged
{
    private bool _isSelected;
    private double _displayWidth;

    public DiffLine(
        string text,
        DiffLineKind kind,
        string marker = "",
        int? sourceLineNumber = null,
        int? displayLineNumber = null,
        IReadOnlyList<DiffLineSegment>? inlineSegments = null)
    {
        Text = text;
        Kind = kind;
        Marker = marker;
        SourceLineNumber = sourceLineNumber;
        LineNumberText = displayLineNumber.HasValue ? displayLineNumber.Value.ToString() : "";
        InlineSegments = inlineSegments ?? [];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text { get; }

    public string Marker { get; }

    public string LineNumberText { get; }

    public DiffLineKind Kind { get; }

    public IReadOnlyList<DiffLineSegment> InlineSegments { get; private set; }

    public int? SourceLineNumber { get; }

    public double DisplayWidth
    {
        get => _displayWidth;
        set
        {
            if (_displayWidth == value)
            {
                return;
            }

            _displayWidth = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public void SetInlineSegments(IReadOnlyList<DiffLineSegment> inlineSegments)
    {
        InlineSegments = inlineSegments;
        OnPropertyChanged(nameof(InlineSegments));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
