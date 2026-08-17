using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace SimpleGit11.Controls;

public sealed class HandCursorButton : Button
{
    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        ProtectedCursor = null;
        base.OnPointerExited(e);
    }
}
