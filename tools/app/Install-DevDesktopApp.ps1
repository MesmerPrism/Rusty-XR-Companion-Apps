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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repoRoot 'src\RustyXr.Companion.App\RustyXr.Companion.App.csproj'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\RustyXrCompanionDev'
$shortcutDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)) 'Programs\Rusty XR Companion'
$shortcutPath = Join-Path $shortcutDirectory 'Rusty XR Companion Dev.url'

Assert-SafeInstallRoot -Path $installRoot

if (Test-Path -LiteralPath $installRoot) {
    Remove-Item -LiteralPath $installRoot -Recurse -Force
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
    -p:Version=0.1.0-dev `
    -p:AssemblyVersion=0.1.0.0 `
    -p:FileVersion=0.1.0.0 `
    -p:InformationalVersion=0.1.0-dev `
    --output $installRoot

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $installRoot 'RustyXr.Companion.App.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Published dev app executable not found at $exePath"
}

New-Item -ItemType Directory -Force -Path $shortcutDirectory | Out-Null
$shortcutExePath = $exePath.Replace('\', '/')
Set-Content -Path $shortcutPath -Encoding ASCII -Value "[InternetShortcut]`r`nURL=file:///$shortcutExePath`r`n"

Write-Host "Installed Rusty XR Companion Dev to $installRoot"
Write-Host "Created Start Menu shortcut at $shortcutPath"

if ($Launch) {
    Start-Process -FilePath $exePath -WorkingDirectory $installRoot
}
