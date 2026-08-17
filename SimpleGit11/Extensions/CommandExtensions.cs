using System.Windows.Input;

namespace SimpleGit11.Extensions;

public static class CommandExtensions
{
    public static bool TryExecute(this ICommand command, object? parameter = null)
    {
        if (!command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }
}
