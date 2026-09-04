# SimpleGit11 MSI

Optional packaging project, deliberately outside the regular application solution.
WiX SDK, UI and Util extensions 7.0.0 are build-only dependencies. Read the
[WiX EULA/OSMF terms](https://docs.firegiant.com/wix/osmf/) before passing
`-AcceptWixEula`. Acceptance applies to that build, not the developer's profile.

## Build

From the repository root, for a tagged stable release:

```powershell
.\Publish-Release.cmd.bat
```

Both release BAT launchers work without arguments and prompt for WiX consent.
For noninteractive development publishing, pass consent explicitly:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\SimpleGit11\Build\Publish-Release.ps1 `
  -DevelopmentBuild -AcceptWixEula
```

PowerShell refuses to proceed while SimpleGit11 is running. The existing BAT
wrappers retain their explicit `-StopRunningApp` behavior and forward arguments.
Neither entry point installs or launches the generated package/application.

Only two MSI files and their SHA-256 sidecars are distributed under
`artifacts`: `SimpleGit11-<version>-win-x64-en-US.msi` and
`SimpleGit11-<version>-win-x64-ru-RU.msi`. Pick one language; do not install both.
Both contain the same core and optional SSH payloads, with their license notices.
The `.publish-staging-win-x64` and `.installer-staging-win-x64` directories are
internal build inputs, not separate distributions. Existing older artifacts are
not deleted automatically. The obsolete `-Installer` switch has been removed.

Packages are unsigned. Before distribution, sign with a production certificate
and trusted timestamp service, verify the signature, then regenerate SHA-256.
Never commit private keys or certificate passwords.

MSI automatically uses the application's numeric version core: for example,
`1.0.0-dev.local.20260831...` becomes `1.0.0`. Windows Installed apps displays
that numeric version; the artifact filename retains the full application version.
Stable versions match exactly and must fit `255.255.65535`. Prereleases may still
use an explicit `-InstallerVersion` override when an increasing test version is
needed, but no version argument is required by default.
Different packages with the same numeric version and downgrades remain blocked.
Uninstall the previous test package before replacing it with another package of
the same core version, including stable. Settings are preserved by default.
To modify/repair, use the **original** MSI, not another build with the same version.

BAT launchers enable interactive consent: `y`/`yes` accepts the WiX terms for
this build only; Enter or any other response cancels. Noninteractive PowerShell
still requires `-AcceptWixEula`. No consent is stored in the developer's profile.

## Installation and maintenance

- Windows 11 x64; choose current-user (default) or all-users installation.
  All-users installation requests administrator approval. The application itself
  continues to run without elevation, with separate settings for each user.
- Windows 11 remains the supported target. No manual OS build-number gate is
  applied: MSI version properties can report compatibility values. The native
  x64 package template enforces architecture compatibility.
- Default locations: `%LOCALAPPDATA%\Programs\SimpleGit11` for the current user,
  `%ProgramFiles%\SimpleGit11` for all users. The next page allows a custom folder.
- `Core` is required; `Ssh` and `Desktop` are optional, off by default.
- The wizard shows scope, folder, then feature selection; no license page or
  acceptance checkbox is shown. License files remain in the installed payload.
- The Start menu shortcut is always installed. Both shortcuts and installer
  registry entries follow the selected scope (HKCU/HKLM).
- Git is not bundled. Local Git is needed for local execution; SSH execution
  requires Git on the remote machine.
- Re-run the original MSI to change features, repair or uninstall.
- Close SimpleGit11 first; Restart Manager automatic shutdown is disabled.
- Upgrades preserve scope, the registered custom folder, feature selection and
  data. Maintenance and upgrade skip scope/folder selection. To change scope or
  move an existing installation, uninstall first. A machine installation is
  detected even from a current-user session; conflicting scopes are blocked.
  Downgrades and different
  packages with the same numeric version are blocked.
- Do not install into an existing portable copy; move it out of the installation
  directory first. Only MSI-owned files are removed from the binary directory;
  unknown third-party plugins remain.

Uninstall normally preserves `%LOCALAPPDATA%\SimpleGit11`. For a current-user
installation, a final full-UI confirmation offers
**Also delete all SimpleGit11 settings and application data**.
It removes settings, SSH profiles, history, logs and subdirectories in that fixed
folder. Back up needed data first. Repository/key paths are never read from
settings for deletion. Files **inside** the data folder are deleted; those outside
are unaffected. Other Windows users' profiles are not traversed.

WiX Util's native `RemoveFolderEx` skips junction/reparse directories rather than
following them. Such links and their ancestor folders can therefore remain.
The opt-in is not persisted and requires `Installed`, `REMOVE=ALL`,
`PURGEUSERDATA=1`, `NOT ALLUSERS`, and `NOT UPGRADINGPRODUCTCODE`. Updates, repair and SSH feature
removal cannot trigger it. Silent/default removal preserves data unless explicitly
given `PURGEUSERDATA=1` on a current-user installation. All-users uninstall never
purges profiles, including when this property is supplied. The elevated identity
may differ from the person initiating uninstall, so no profile cleanup is offered.

```powershell
# Core only
msiexec.exe /i "SimpleGit11-<version>-win-x64-en-US.msi" /qn ADDLOCAL=Core
# Core and SSH
msiexec.exe /i "SimpleGit11-<version>-win-x64-en-US.msi" /qn ADDLOCAL=Core,Ssh
# All users, optional custom folder (run an elevated console for unattended install)
msiexec.exe /i "SimpleGit11-<version>-win-x64-en-US.msi" /qn ALLUSERS=1 ADDLOCAL=Core,Ssh INSTALLFOLDER="D:\Applications\SimpleGit11"
# Destructive opt-in: application and this user's application data
msiexec.exe /x "SimpleGit11-<version>-win-x64-en-US.msi" /qn PURGEUSERDATA=1
```

## Validation

`InstallerPayload.Tests.ps1`, also run by MSTest, checks versions, stable IDs,
feature isolation, XML escaping, missing dependencies, junction rejection,
localization parity, license-free wizard navigation and cleanup guards.

Every MSI build runs ICE validation and reads its database to verify defaults,
SSH file ownership, scope-aware registry keys, removal conditions and dialog
ordering. Read-only MSI sessions additionally test default paths, switching scope,
preservation of a custom folder, upgrades and per-machine purge rejection.
No installation, application launch or user data deletion is performed by these tests.
ICE91 and ICE105 are enabled. ICE57 is rerun separately with only two specific
legacy diagnostics accepted for HKMU shortcuts in MSI-redirectable directories.
ICE03 runs separately: only language metadata diagnostics for eight named
Microsoft UI XAML runtime/resource files are accepted. Other ICE03 diagnostics
fail the build. Original DLLs are not modified.

Manual acceptance in a disposable Windows VM: install in both scopes, including
UAC credentials from a standard-user account, defaults/custom paths with spaces,
switch scope using Back, install with/without SSH,
add/remove SSH, repair, upgrade both feature states, cancel removal, uninstall
preserving data, purge uninstall, junction target preservation. Never run
destructive acceptance against real settings. Menu/live SSH checks are performed
by the user.
