<#
.SYNOPSIS
    Publishes the current source tree into a separate per-user dev install.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$Launch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SafeInstallRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $expected = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\RustyXrCompanionDev')).TrimEnd('\')
    $actual = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (-not [string]::Equals($expected, $actual, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write unexpected dev install root: $actual"
    }
}

function Set-ShellShortcut {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Shell,
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [Parameter(Mandatory = $true)]
        [string]$IconLocation,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $shortcut = $Shell.CreateShortcut($Path)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $IconLocation
    $shortcut.Description = $Description
    $shortcut.Save()
}

function Repair-DevTaskbarPin {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Shell,
        [Parameter(Mandatory = $true)]
        [string]$InstallRoot,
        [Parameter(Mandatory = $true)]
        [string]$ExePath,
        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    $taskbarDirectory = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'
    if (-not (Test-Path -LiteralPath $taskbarDirectory)) {
        return @()
    }

    $normalizedInstallRoot = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
    $desiredShortcutPath = Join-Path $taskbarDirectory "$DisplayName.lnk"
    $updated = @()

    foreach ($shortcutFile in Get-ChildItem -LiteralPath $taskbarDirectory -Filter '*.lnk' -Force) {
        try {
            $shortcut = $Shell.CreateShortcut($shortcutFile.FullName)
            if ([string]::IsNullOrWhiteSpace($shortcut.TargetPath)) {
                continue
            }

            $normalizedTarget = [System.IO.Path]::GetFullPath($shortcut.TargetPath)
            $isDevPin = $normalizedTarget.StartsWith(
                $normalizedInstallRoot + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)
            if (-not $isDevPin) {
                continue
            }

            if (-not [string]::Equals($shortcutFile.FullName, $desiredShortcutPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                if (-not (Test-Path -LiteralPath $desiredShortcutPath)) {
                    Move-Item -LiteralPath $shortcutFile.FullName -Destination $desiredShortcutPath -Force
                }
                else {
                    Remove-Item -LiteralPath $shortcutFile.FullName -Force
                }
            }

            Set-ShellShortcut `
                -Shell $Shell `
                -Path $desiredShortcutPath `
                -TargetPath $ExePath `
                -WorkingDirectory $InstallRoot `
                -IconLocation "$ExePath,0" `
                -Description $DisplayName
            $updated += $shortcutFile.Name
        }
        catch {
            Write-Warning "Could not repair taskbar pin $($shortcutFile.FullName): $($_.Exception.Message)"
        }
    }

    return $updated
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repoRoot 'src\RustyXr.Companion.App\RustyXr.Companion.App.csproj'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\RustyXrCompanionDev'
$shortcutDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)) 'Programs\Rusty XR Companion'
$shortcutPath = Join-Path $shortcutDirectory 'Rusty XR Companion Dev.lnk'
$legacyShortcutPath = Join-Path $shortcutDirectory 'Rusty XR Companion Dev.url'
$devVersion = '0.1.7-dev'
$devExeName = 'RustyXr.Companion.Dev.exe'
$devDisplayName = 'Rusty XR Companion Dev'

Assert-SafeInstallRoot -Path $installRoot

if (Test-Path -LiteralPath $installRoot) {
    Get-ChildItem -LiteralPath $installRoot -Force | Remove-Item -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
dotnet restore $projectPath --runtime $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    -p:PublishSingleFile=true `
    -p:SelfContained=false `
    "-p:RustyXrCompanionApplicationIcon=Assets\RustyXrCompanionDev.ico" `
    "-p:RustyXrCompanionAssemblyName=RustyXr.Companion.Dev" `
    "-p:Product=Rusty XR Companion Dev" `
    "-p:AssemblyTitle=Rusty XR Companion Dev" `
    "-p:Version=$devVersion" `
    -p:AssemblyVersion=0.1.7.0 `
    -p:FileVersion=0.1.7.0 `
    "-p:InformationalVersion=$devVersion" `
    --output $installRoot

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $installRoot $devExeName
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Published dev app executable not found at $exePath"
}

New-Item -ItemType Directory -Force -Path $shortcutDirectory | Out-Null
Remove-Item -LiteralPath $legacyShortcutPath -Force -ErrorAction SilentlyContinue
$shell = New-Object -ComObject WScript.Shell
Set-ShellShortcut `
    -Shell $shell `
    -Path $shortcutPath `
    -TargetPath $exePath `
    -WorkingDirectory $installRoot `
    -IconLocation "$exePath,0" `
    -Description $devDisplayName
$repairedTaskbarPins = @(Repair-DevTaskbarPin `
    -Shell $shell `
    -InstallRoot $installRoot `
    -ExePath $exePath `
    -DisplayName $devDisplayName)

Write-Host "Installed $devDisplayName to $installRoot"
Write-Host "Created Start Menu shortcut at $shortcutPath"
if ($repairedTaskbarPins.Count -gt 0) {
    Write-Host "Repaired dev taskbar pin(s): $($repairedTaskbarPins -join ', ')"
}

if ($Launch) {
    Start-Process -FilePath $exePath -WorkingDirectory $installRoot
}
