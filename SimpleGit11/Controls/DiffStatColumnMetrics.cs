using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimpleGit11.Controls;

public sealed class DiffStatColumnMetrics : INotifyPropertyChanged
{
    private double _addedWidth;
    private double _removedWidth;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double AddedWidth
    {
        get => _addedWidth;
        private set => SetProperty(ref _addedWidth, value);
    }

    public double RemovedWidth
    {
        get => _removedWidth;
        private set => SetProperty(ref _removedWidth, value);
    }

    internal void Update(double addedWidth, double removedWidth)
    {
        AddedWidth = Math.Max(0, addedWidth);
        RemovedWidth = Math.Max(0, removedWidth);
    }

    private void SetProperty(
        ref double field,
        double value,
        [CallerMemberName] string? propertyName = null)
    {
        if (Math.Abs(field - value) < 0.01)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
