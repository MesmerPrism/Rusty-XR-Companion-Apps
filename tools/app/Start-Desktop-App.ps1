<#
.SYNOPSIS
    Publishes and launches the WPF app from a stable single-file output.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputRelativePath = 'artifacts\publish\RustyXr.Companion.App'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projectPath = Join-Path $repoRoot 'src\RustyXr.Companion.App\RustyXr.Companion.App.csproj'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

dotnet restore $projectPath --runtime $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    -p:PublishSingleFile=true `
    -p:SelfContained=false `
    --output $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $outputPath 'RustyXr.Companion.App.exe'
if (-not (Test-Path $exePath)) {
    throw "Published app executable not found at $exePath"
}

Start-Process -FilePath $exePath -WorkingDirectory $outputPath
