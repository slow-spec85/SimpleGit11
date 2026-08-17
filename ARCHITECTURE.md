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
