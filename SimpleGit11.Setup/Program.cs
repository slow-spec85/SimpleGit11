using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WixToolset.BootstrapperApplicationApi;

namespace SimpleGit11.Setup;

internal static class Program
{
    // Burn initializes COM in MTA. WinUI runs on its separate STA thread.
    [MTAThread]
    private static void Main()
    {
        SetupBootstrapperApplication bootstrapper = new();
        ManagedBootstrapperApplication.Run(bootstrapper);
    }

    internal static void StartWinUi(SetupBootstrapperApplication bootstrapper)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App(bootstrapper);
        });
    }
}
