using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;

namespace SimpleGit11.Setup;

public partial class App : Application
{
    private readonly SetupBootstrapperApplication _bootstrapper;

    internal App(SetupBootstrapperApplication bootstrapper)
    {
        _bootstrapper = bootstrapper;
        ApplicationLanguages.PrimaryLanguageOverride =
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru-RU" : "en-US";
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow window = new(_bootstrapper);
        _bootstrapper.AttachWindow(window);
        if (!_bootstrapper.IsHeadless)
        {
            window.Activate();
        }
        _bootstrapper.Detect();
    }
}
