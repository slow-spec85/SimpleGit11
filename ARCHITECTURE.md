<p align="center">
  <a href="ARCHITECTURE.md">English</a> | <a href="ARCHITECTURE.ru.md">Русский</a>
</p>

# SimpleGit11 architecture

The application uses MVVM and Dependency Injection. Architectural dependencies
flow from the presentation layer toward UI-neutral contracts and application
logic.

## Services

`SimpleGit11.Services` contains UI-neutral interfaces, application services,
and infrastructure logic. Types in this namespace must not reference
`Microsoft.UI` or concrete windows, pages, and controls.

Git services live in `SimpleGit11.Services.Git`. They work with models, the
file system, and Git processes without depending on the presentation layer.

## Presentation

`SimpleGit11.Presentation` contains adapters that require WinUI or interaction
with the user environment: dialogs, editor windows, file pickers, themes, the
clipboard, and File Explorer integration.

These adapters implement UI-neutral interfaces from `SimpleGit11.Services`.
They are wired together in the composition root, `App.xaml.cs`.

ViewModels belong to the presentation layer. They may use WinUI types intended
specifically for binding when introducing an additional neutral model would
not create a useful architectural boundary. ViewModels must not pass WinUI
types to Git services.

The `ServiceLayerArchitectureTests` architecture test prevents types in
`SimpleGit11.Services` from depending on `Microsoft.UI`.

## Plugin loading (API 1.0)

`SimpleGit11.Extensibility` contains the shared execution and plugin contracts.
Plugins reference that assembly, not the application executable. The host scans
only immediate subdirectories of `Plugins` next to the executable at startup,
after registering core services and before building the DI container.

Each plugin directory contains an entry DLL, its `.deps.json`, private
dependencies, and `plugin.json`:

```json
{
  "id": "simplegit11.example",
  "name": "Example plugin",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "minimumHostVersion": "1.0.0",
  "entryAssembly": "SimpleGit11.Plugin.Example.dll",
  "entryType": "SimpleGit11.Plugin.Example.ExamplePlugin"
}
```

The entry type must be a public, non-abstract `ISimpleGitPlugin` implementation
with a public parameterless constructor. Its metadata must match the manifest.
API versions match exactly; minimum host versions are numeric .NET versions
with two to four components (missing components are treated as zero).

`ConfigureServices` receives an isolated collection of new registrations, not
the host's service collection. The host appends these registrations only after
configuration succeeds. Modules must not construct a service provider during
registration. The loader isolates manifest, activation, and registration errors
per plugin; it cannot isolate arbitrary exceptions in services used later.
Loaded metadata and failures are available through `IPluginCatalog`; startup
failures are logged to `%LocalAppData%\SimpleGit11\Logs\SimpleGit11-plugins.log`.

Each plugin has a separate `AssemblyLoadContext`. .NET framework assemblies,
Extensibility, DI contracts, CommunityToolkit.Mvvm, and WinUI framework
assemblies retain their host identities. Resolved private
managed/native dependencies must stay within the plugin directory. Manifest
paths cannot contain directory components, and reparse points are rejected.

Plugins execute trusted code inside the application process: this is not a
security sandbox or a signature-verification mechanism. Install/remove/update
operations require an application restart; live unloading is not exposed.
SSH is supplied by the optional `SimpleGit11.Plugin.Ssh` module. The application
has no reference to that project or to SSH.NET. Without the module, no SSH
execution provider, connection dialog, menu command or SSH-specific resources
are registered. Git's own SSH remotes, `core.sshCommand` setting and signature
parsing are independent of remote-machine execution and remain in the core.

### Main menu contributions

A module registers one or more `IMainMenuContribution` services. The shell
renders them as native `NavigationViewItem` commands without changing the
current page or its selection. Primary contributions follow built-in pages;
footer contributions precede built-in footer commands. Without contributions,
no extra menu items or separators are created.

IDs must be unique (case-insensitive); `Id` and `Placement` remain fixed for the
window lifetime. `Label`, `IconGlyph`, `Indicator`, and `Command` can change via
`INotifyPropertyChanged`. Notifications are dispatched to the UI thread, and
`CanExecuteChanged` controls enabled state. Subscriptions and menu items are
removed when the window closes.

Use `IAsyncRelayCommand` from the shared CommunityToolkit.Mvvm assembly for
asynchronous actions. The host awaits it, prevents repeated execution, and
passes failures to the existing command error handler. Plain `ICommand` is
supported for synchronous actions; `async void` commands cannot be observed.

Non-empty indicators require a localized accessible description. Native
`InfoBadge` styles distinguish information, success, warning, error, and
progress (a sync icon); the item's tooltip and accessible name also include
the status, so state is not conveyed by color alone. The contribution owns
localization of its label and status descriptions.

### Execution context coordination

`MainWindow` has no SSH-specific dependencies or commands. Its
`ExecutionContextShellCoordinator` observes context changes from any provider,
closes the previous repository, reloads recent repositories for the active
machine, and refreshes the page on the UI thread. A connection-loss notification
for the current remote context restores local mode and reports a warning;
stale events and callbacks queued before window disposal are ignored.

`SshConnectionController` owns connection/disconnection, saved profiles and host
key confirmation through the existing dialog service. It does not know the
window or page view models. Concurrent invocations are ignored, failed or
cancelled connections do not change the shell, and secrets are passed separately
from saved profile settings. `SshMainMenuContribution` exposes the stable
"SSH connection" command with a localized status indicator. While connected,
the same command disconnects; its tooltip explains that action. Another remote
provider's connection is not disconnected by the SSH command.

### SSH plugin boundary and distribution

The single `SimpleGit11.Plugin.Ssh` project owns transport services, SSH.NET,
profiles, the connection controller, menu contribution and the existing WinUI
dialog. Its only project reference is `SimpleGit11.Extensibility`; services do
not depend on WinUI. The shared assembly now also exposes `ILocalSettingsStore`,
`ILocalizationService`, `AppLanguage`, `GitCommandException` and the WinUI
presentation contract `IPluginDialogHost`. The host's existing `DialogService`
implements the latter, applying the active window's XamlRoot and theme and
reusing the existing confirmation dialog. No concrete host view is exposed.

Plugin strings live in its own `Strings/en-US` and `Strings/ru-RU` RESW files,
embedded into the module. They are selected using the host language, with
English fallback for other system languages. They do not require merging a
plugin PRI into the host at installation time. The original `ssh` provider ID,
`SshConnectionProfiles` settings key and profile JSON fields are preserved;
saved connections and their recent-repository identities remain compatible.

Building the plugin creates a `Plugin` subdirectory in its output containing
only the entry assembly, `.deps.json`, manifest and private runtime libraries.
Copy this directory's contents to `<application>/Plugins/Ssh` while the
application is closed. Do not copy the full build output or replace the shared
host assemblies. The normal application build does not install the plugin.
See [SSH plugin build and installation](SimpleGit11.Plugin.Ssh/README.md).
The component-selecting installer remains a later stage.

`SimpleGit11.Plugin.Ssh.Tests` tests transport logic, the controller, resources
and profile compatibility without referencing the host. Core tests use an
independent fake remote runtime; real-plugin loader tests consume the staged
plugin as files (not a CLR project reference) and verify isolated dependencies,
DI resolution, the menu command and the no-plugin case.
