# Internal Windows Installer package

`SimpleGit11.Setup/Msi` builds the MSI payload embedded in the WinUI setup EXE. The
MSI is not a separate release artifact. Users run
`SimpleGit11-<version>-win-x64-setup.exe`; the WinUI 3 interface in
`SimpleGit11.Setup` controls installation, maintenance, language and system theme.
`SimpleGit11.Setup/Bundle` uses WiX Burn to carry the MSI and the self-contained
WinUI bootstrapper in one EXE.

The MSI has no wizard or dialog resources. Windows Installer still owns the
files, shortcuts, installed-app registration, upgrades and removal. The core
feature is required; SSH support and the desktop shortcut are optional. Burn
passes the selected scope, folder, features and optional data-removal choice to
the MSI. The MSI retains the historical upgrade code and ignores the previous
MSI UI language, so the setup EXE can upgrade older English and Russian MSI
installations.

The default per-user folder is `%LOCALAPPDATA%\Programs\SimpleGit11`; the
all-users default is `%ProgramFiles%\SimpleGit11` and requires elevation.
Upgrades preserve the selected scope, folder and components. Changing scope or
moving an existing installation requires uninstalling first. Settings are kept
by default. Complete removal of a per-user installation can delete
`%LOCALAPPDATA%\SimpleGit11` only when the user explicitly selects that option
in the WinUI interface. Machine uninstall, upgrades, repairs and component
changes never delete profile data.

`SimpleGit11/Build/Publish-Release.ps1` builds the internal MSI, embeds it in
the setup EXE, removes the standalone MSI staging artifact and returns only the
EXE and its SHA-256 file. `Build-Installer.ps1` remains an internal build step.
The MSI project uses WiX SDK and Util extension 7.0.0; the bundle uses the
WiX BootstrapperApplication API. Read the
[WiX terms](https://docs.firegiant.com/wix/osmf/) before passing
`-AcceptWixEula` for a build.

Every MSI build runs database checks and ICE03/ICE57 validation. The release
script validates required application files and license notices before building
the package. These checks do not install or launch the application.
