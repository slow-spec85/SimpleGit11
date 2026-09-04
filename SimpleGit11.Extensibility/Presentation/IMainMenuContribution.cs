using System.ComponentModel;
using System.Windows.Input;

namespace SimpleGit11.Extensibility.Presentation;

public interface IMainMenuContribution : INotifyPropertyChanged
{
    string Id { get; }

    string Label { get; }

    string IconGlyph { get; }

    MainMenuPlacement Placement { get; }

    MainMenuIndicator Indicator { get; }

    ICommand Command { get; }
}
