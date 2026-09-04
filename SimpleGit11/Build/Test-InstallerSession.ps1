#Requires -Version 5.1

function Assert-InstallerSessionBehavior {
    param($Installer, [string]$Path, [object[]]$Actions, [object[]]$Events,
        [object[]]$LaunchConditions, [string]$CleanupCondition)

    # Read-only MSI sessions: evaluate the compiled conditions and property actions.
    # Never run an installation sequence, file/registry mutation, or any deferred action.
    function Set-SessionProperty {
        param([string]$Name, [string]$Value)
        [void]$session.GetType().InvokeMember('Property', [Reflection.BindingFlags]::SetProperty,
            $null, $session, @($Name, $Value))
    }
    function Invoke-PropertyAction {
        param([string]$Name)
        $action = @($Actions | Where-Object { $_[0] -eq $Name })
        if ($action.Count -ne 1 -or $action[0][1] -ne '51') { throw "Expected a property-only action: $Name" }
        if ($session.DoAction($Name) -ne 1) { throw "Property action failed: $Name" }
    }
    function Assert-Property {
        param([string]$Name, [string]$Expected)
        [string]$actual = $session.Property($Name)
        if ($actual.TrimEnd('\') -ne $Expected.TrimEnd('\')) {
            throw "MSI property $Name expected '$Expected', got '$actual'."
        }
    }
    function Invoke-ScopeSelection {
        param([string]$Scope)
        Set-SessionProperty 'INSTALLSCOPE' $Scope
        # Events arrive in control ordering from the database query below.
        foreach ($event in $scopeEvents) {
            if ($session.EvaluateCondition($event[4]) -ne 1) { continue }
            if ($event[2] -notmatch '^\[(.+)\]$') { continue }
            [string]$property = $Matches[1]
            [string]$value = $event[3]
            if ($value -eq '{}') { $value = '' }
            elseif ($value -match '^\[(.+)\]$') { $value = $session.Property($Matches[1]) }
            Set-SessionProperty $property $value
        }
    }

    $scopeEvents = @($Events | Where-Object { $_[0] -eq 'SimpleGit11ScopeDlg' -and $_[1] -eq 'Next' })
    foreach ($mode in @('user', 'machine')) {
        $session = $Installer.OpenPackage($Path, 1)
        try {
            if ($session.DoAction('AppSearch') -ne 1) { throw 'Read-only MSI registry search failed.' }
            foreach ($name in @('Installed', 'EXISTINGUSERFOLDER', 'EXISTINGMACHINEFOLDER', 'INSTALLFOLDER')) {
                Set-SessionProperty $name ''
            }
            Set-SessionProperty 'ALLUSERS' '2'
            Set-SessionProperty 'MSIINSTALLPERUSER' '1'
            # These standard actions only calculate target paths and feature costs in memory.
            foreach ($action in @('CostInitialize', 'FileCost')) {
                if ($session.DoAction($action) -ne 1) { throw "MSI costing failed: $action" }
            }
            Invoke-PropertyAction 'SetMachineInstallFolder'
            Invoke-PropertyAction 'SetUserInstallFolder'
            [string]$userFolder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\SimpleGit11'
            [string]$machineFolder = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'SimpleGit11'
            Assert-Property 'MachineInstallFolder' $machineFolder
            Assert-Property 'UserInstallFolder' $userFolder
            if ($session.DoAction('CostFinalize') -ne 1) { throw 'MSI CostFinalize failed.' }
            Assert-Property 'INSTALLFOLDER' $userFolder
            # OpenPackage is not an installation sequence and does not normalize
            # ALLUSERS=2. Exercise the actual wizard's context selection instead.
            Set-SessionProperty 'PreviousInstallScope' 'user'
            Invoke-ScopeSelection $mode
            Assert-Property 'INSTALLFOLDER' $(if ($mode -eq 'user') { $userFolder } else { $machineFolder })
            Assert-Property 'ALLUSERS' $(if ($mode -eq 'user') { '' } else { '1' })
            [string]$customFolder = 'C:\Installer test\Custom SimpleGit11'
            Set-SessionProperty 'INSTALLFOLDER' $customFolder
            Invoke-ScopeSelection $mode
            Assert-Property 'INSTALLFOLDER' $customFolder
            Invoke-ScopeSelection $(if ($mode -eq 'user') { 'machine' } else { 'user' })
            Assert-Property 'INSTALLFOLDER' $(if ($mode -eq 'user') { $machineFolder } else { $userFolder })
            Assert-Property 'ALLUSERS' $(if ($mode -eq 'user') { '1' } else { '' })
            Assert-Property 'MSIINSTALLPERUSER' $(if ($mode -eq 'user') { '' } else { '1' })
            Invoke-ScopeSelection $mode
            Assert-Property 'INSTALLFOLDER' $(if ($mode -eq 'user') { $userFolder } else { $machineFolder })

            # An elevated machine uninstall must never purge the administrator's profile.
            foreach ($entry in @{ Installed = '1'; REMOVE = 'ALL'; PURGEUSERDATA = '1' }.GetEnumerator()) {
                Set-SessionProperty $entry.Key $entry.Value
            }
            if ($session.EvaluateCondition($CleanupCondition) -ne $(if ($mode -eq 'user') { 1 } else { 0 })) {
                throw "Unsafe data cleanup in $mode context."
            }
            Set-SessionProperty 'UPGRADINGPRODUCTCODE' '{11111111-1111-1111-1111-111111111111}'
            if ($session.EvaluateCondition($CleanupCondition) -ne 0) { throw 'Upgrade must never purge data.' }

            # Existing custom paths survive upgrades; the other scope is rejected.
            Set-SessionProperty 'Installed' ''
            [string]$existingProperty = $(if ($mode -eq 'user') { 'EXISTINGUSERFOLDER' } else { 'EXISTINGMACHINEFOLDER' })
            Set-SessionProperty $existingProperty $customFolder
            Invoke-PropertyAction $(if ($mode -eq 'user') { 'RestoreUserFolder' } else { 'RestoreMachineFolder' })
            Assert-Property 'INSTALLFOLDER' $customFolder
            if (@($LaunchConditions | Where-Object { $session.EvaluateCondition($_[0]) -ne 1 }).Count -ne 0) {
                throw "An existing $mode installation must allow maintenance in its original scope."
            }
            Set-SessionProperty 'ALLUSERS' $(if ($mode -eq 'user') { '1' } else { '' })
            if (@($LaunchConditions | Where-Object { $session.EvaluateCondition($_[0]) -eq 0 }).Count -eq 0) {
                throw 'Cross-scope installation must require uninstalling first.'
            }
            Set-SessionProperty 'Installed' '1'
            Set-SessionProperty 'EXISTINGUSERFOLDER' $customFolder
            Set-SessionProperty 'EXISTINGMACHINEFOLDER' $customFolder
            if (@($LaunchConditions | Where-Object { $session.EvaluateCondition($_[0]) -ne 1 }).Count -ne 0) {
                throw 'An existing installation must remain removable even if another scope was installed separately.'
            }
        }
        finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session) }
    }
    Write-Host 'MSI session tests passed: both scopes, default/custom paths, scope switching, upgrades and purge guards.'
}
