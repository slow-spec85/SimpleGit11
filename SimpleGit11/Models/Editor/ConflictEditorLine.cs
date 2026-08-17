using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimpleGit11.Models;

public enum ConflictEditorLineRole
{
    Context,
    Current,
    Base,
    Incoming,
    Marker
}

public sealed class ConflictEditorLine : INotifyPropertyChanged
{
    private string _text;
    private ConflictEditorLineRole _role;
    private bool _isEditing;
    private bool _isSelected;
    private int _lineNumber;

    public ConflictEditorLine(string text)
    {
        _text = text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            OnPropertyChanged();
        }
    }

    public ConflictEditorLineRole Role
    {
        get => _role;
        internal set
        {
            if (_role == value)
            {
                return;
            }

            _role = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundKind));
        }
    }

    public int LineNumber
    {
        get => _lineNumber;
        internal set
        {
            if (_lineNumber == value)
            {
                return;
            }

            _lineNumber = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LineNumberText));
        }
    }

    public string LineNumberText => LineNumber.ToString();

    public DiffLineKind BackgroundKind => Role switch
    {
        ConflictEditorLineRole.Current => DiffLineKind.Added,
        ConflictEditorLineRole.Base => DiffLineKind.Hunk,
        ConflictEditorLineRole.Incoming => DiffLineKind.Removed,
        ConflictEditorLineRole.Marker => DiffLineKind.ConflictMarker,
        _ => DiffLineKind.Context
    };

    public bool IsEditing
    {
        get => _isEditing;
        internal set
        {
            if (_isEditing == value)
            {
                return;
            }

            _isEditing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotEditing));
        }
    }

    public bool IsNotEditing => !IsEditing;

    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
