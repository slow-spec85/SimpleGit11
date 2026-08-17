<p align="center">
  <a href="ARCHITECTURE.md">English</a> | <a href="ARCHITECTURE.ru.md">Русский</a>
</p>

# Архитектура SimpleGit11

Приложение использует MVVM и Dependency Injection. Архитектурные зависимости направлены от presentation-слоя к UI-независимым контрактам и прикладной логике.

## Services

`SimpleGit11.Services` содержит UI-независимые интерфейсы, прикладные сервисы и инфраструктурную логику. Типы из этого namespace не должны ссылаться на `Microsoft.UI` или конкретные окна, страницы и элементы управления.

Git-сервисы находятся в `SimpleGit11.Services.Git`. Они работают с моделями, файловой системой и Git-процессами и не зависят от presentation-слоя.

## Presentation

`SimpleGit11.Presentation` содержит адаптеры, которым необходимы WinUI или взаимодействие с пользовательским окружением: диалоги, окна редактора, выбор файлов, тема, буфер обмена и открытие проводника.

Эти адаптеры реализуют UI-независимые интерфейсы из `SimpleGit11.Services`. Их связывание выполняется в composition root — `App.xaml.cs`.

ViewModel относятся к presentation-слою. Они могут использовать типы WinUI, предназначенные непосредственно для binding, если введение дополнительной нейтральной модели не создаёт полезной архитектурной границы. ViewModel не должны передавать WinUI-типы в Git-сервисы.

Архитектурный тест `ServiceLayerArchitectureTests` запрещает зависимости типов `SimpleGit11.Services` от `Microsoft.UI`.
