<p align="center">
  <a href="PUBLISHING.md">English</a> | <a href="PUBLISHING.ru.md">Русский</a>
</p>

# Сборка, установка и обновление SimpleGit11

## Готовый дистрибутив

SimpleGit11 распространяется как **unpackaged, self-contained, win-x64** приложение:

- MSIX и Microsoft Store не используются;
- .NET и Windows App SDK включены в дистрибутив;
- приложение не требует установки сертификата или прав администратора;
- для работы нужна вся папка приложения, а не только `SimpleGit11.exe`.

Для каждой опубликованной версии в разделе Releases доступны:

```text
SimpleGit11-<version>-win-x64.zip
SimpleGit11-<version>-win-x64.zip.sha256
```

Автоматически создаваемые GitHub архивы `Source code (zip)` и `Source code (tar.gz)` содержат исходный код, но не готовое приложение.

## Системные требования

Для готового приложения:

- Windows 11 x64;
- Git for Windows.

Для сборки из исходного кода дополнительно потребуются:

- .NET SDK 10 (минимум 10.0.100; более новые feature band и patch-версии 10.0 разрешены);
- Windows SDK с поддержкой Windows 10, версия 19041 или новее;
- доступ к NuGet для восстановления зависимостей.

## Установка

1. Откройте нужную опубликованную версию в разделе Releases.
2. Скачайте `SimpleGit11-<version>-win-x64.zip`.
3. При необходимости проверьте SHA-256 по инструкции ниже.
4. Создайте постоянную папку, например:

```text
%LOCALAPPDATA%\Programs\SimpleGit11
```

5. Полностью распакуйте содержимое ZIP в эту папку.
6. Запустите `SimpleGit11.exe`.
7. При желании создайте ярлык вручную.

Не запускайте приложение непосредственно из ZIP и не переносите отдельно только EXE-файл.

## Проверка SHA-256

Поместите ZIP и соответствующий `.sha256` в один каталог, затем выполните в PowerShell:

```powershell
$archive = ".\SimpleGit11-1.0.0-win-x64.zip"
$checksum = "$archive.sha256"

$expected = (Get-Content $checksum).Split(
    " ",
    [System.StringSplitOptions]::RemoveEmptyEntries)[0]
$actual = (Get-FileHash $archive -Algorithm SHA256).Hash

if ($actual -ne $expected) {
    throw "SHA-256 checksum mismatch."
}

"SHA-256 checksum is valid."
```

Замените `1.0.0` на номер скачанной версии.

## Обновление

Пока автоматическое обновление не реализовано.

Чтобы обновить приложение вручную:

1. Закройте все экземпляры SimpleGit11.
2. Скачайте ZIP новой версии.
3. При необходимости проверьте SHA-256.
4. Полностью замените файлы в папке приложения содержимым нового ZIP.
5. Запустите `SimpleGit11.exe`.

Настройки и список недавних репозиториев хранятся отдельно:

```text
%LOCALAPPDATA%\SimpleGit11\settings.json
```

Замена папки приложения не удаляет пользовательские настройки.

## Сборка из исходного кода

Клонирование Git-репозитория предпочтительнее скачивания автоматически сформированного архива исходного кода: MinVer использует Git-теги для вычисления версии.

Восстановите зависимости:

```powershell
dotnet restore .\SimpleGit11.slnx
```

Соберите Debug-версию только для `x64`:

```powershell
dotnet build .\SimpleGit11.slnx `
  -c Debug `
  -p:Platform=x64
```

Результат сборки:

```text
SimpleGit11\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\
```

Запуск собранного приложения:

```powershell
& ".\SimpleGit11\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\SimpleGit11.exe"
```

## Подготовка ZIP и SHA-256

Репозиторий содержит скрипты, которые выполняют self-contained публикацию,
проверяют обязательные WinUI-файлы и создают готовые артефакты в каталоге
`artifacts`.

Для релизной сборки (стабильной или предварительной) запустите:

```powershell
.\Publish-Release.cmd.bat
```

Релизный режим требует:

- чистое рабочее дерево;
- ровно один тег `vMAJOR.MINOR.PATCH[-PRERELEASE]`, указывающий на `HEAD`;
- совпадение версии собранного EXE с версией из тега.

Примеры допустимых релизных тегов:

```text
v1.0.0-preview.1
v1.0.0-rc.1
v1.0.0
```

Публичный release workflow принимает стабильные теги, а также prerelease-теги
с каналами `preview.N` и `rc.N`.

Числовые prerelease-идентификаторы не должны содержать ведущие нули:
`preview.1` допустим, а `preview.01` — нет.

Для тестовой сборки из любой ветки запустите:

```powershell
.\Publish-Release-dev.cmd.bat
```

Development-режим не требует чистого рабочего дерева или тега на `HEAD`.
Артефакт получает уникальную prerelease-версию вида
`<next-patch>-dev.local.<timestamp>`.

Оба BAT-файла закрывают запущенный SimpleGit11 перед публикацией и создают:

```text
artifacts\SimpleGit11-<version>-win-x64\
artifacts\SimpleGit11-<version>-win-x64.zip
artifacts\SimpleGit11-<version>-win-x64.zip.sha256
```

Каталог приложения и ZIP также содержат `LICENSE`,
`THIRD-PARTY-NOTICES.txt` и каталог `Licenses` с точными версиями пакетов,
ревизиями исходных компонентов и оригинальными лицензионными файлами фактически
распространяемых компонентов. Публикация завершается ошибкой, если необходимую
лицензию не удаётся собрать автоматически.

Прямой запуск без BAT-обёртки:

```powershell
# Релизная сборка (stable или prerelease)
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\SimpleGit11\Build\Publish-Release.ps1 `
  -StopRunningApp

# Development-сборка
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\SimpleGit11\Build\Publish-Release.ps1 `
  -DevelopmentBuild `
  -StopRunningApp
```

Скрипты только готовят локальные артефакты. Они не создают коммиты и теги,
не выполняют `push` и не создают GitHub Release.

## Локальная self-contained публикация

Если ZIP и checksum не требуются, публикацию можно выполнить напрямую:

```powershell
dotnet publish .\SimpleGit11\SimpleGit11.csproj `
  -c Release `
  -p:Platform=x64 `
  -p:PublishProfile=win-x64
```

Готовая папка:

```text
SimpleGit11\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\
```

Профиль публикации добавляет необходимые WinUI-ресурсы:

- XBF-файлы;
- `SimpleGit11.pri`;
- каталог `Assets`;
- .NET и Windows App SDK runtime.

Распространять нужно всё содержимое папки `publish`.

## Версионирование

Версия вычисляется пакетом [MinVer](https://github.com/adamralph/minver) из тегов формата:

```text
vMAJOR.MINOR.PATCH[-PRERELEASE]
```

Проект следует [Semantic Versioning 2.0.0](https://semver.org/):

- patch, например `1.0.1`, — исправления;
- minor, например `1.1.0`, — новая обратно совместимая функциональность;
- major, например `2.0.0`, — несовместимые изменения.
- prerelease, например `1.0.0-preview.1`, — предварительная версия, которая предшествует стабильной `1.0.0`.

Сборка коммита, отмеченного тегом `v1.0.0-preview.1`, получает версию
`1.0.0-preview.1`, а тег `v1.0.0` создаёт стабильную версию `1.0.0`.
Нетегированные сборки получают prerelease-версию с информацией о коммите.

## Подпись и предупреждения Windows

Unpackaged-приложение может запускаться без сертификата. Однако неподписанный или недавно опубликованный бинарный файл может вызвать предупреждение Microsoft Defender SmartScreen.

Скачивайте дистрибутив только из официального раздела Releases и проверяйте опубликованный SHA-256.

## Дополнительные материалы

- [MinVer](https://github.com/adamralph/minver)
- [Self-contained deployment overview](https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/self-contained-deploy-overview)
- [Deploy unpackaged apps](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app)
- [Semantic Versioning 2.0.0](https://semver.org/)
