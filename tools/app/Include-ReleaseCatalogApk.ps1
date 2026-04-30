<#
.SYNOPSIS
    Copies the public Rusty XR composite-layer APK into a published Companion app payload.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppPublishRoot,

    [Parameter(Mandatory = $true)]
    [string]$ApkPath,

    [string]$CatalogFileName = 'rusty-xr-quest-composite-layer.catalog.json',

    [string]$ApkFileName = 'rusty-xr-quest-composite-layer-debug.apk'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPublishRoot = (Resolve-Path $AppPublishRoot).Path
$resolvedApkPath = (Resolve-Path $ApkPath).Path
if ([IO.Path]::GetExtension($resolvedApkPath) -ne '.apk') {
    throw "Expected an .apk file, got: $resolvedApkPath"
}

$catalogPath = Join-Path $resolvedPublishRoot "catalogs\$CatalogFileName"
if (-not (Test-Path -LiteralPath $catalogPath)) {
    throw "The published app payload does not include the default catalog: $catalogPath"
}

$apkInfo = Get-Item -LiteralPath $resolvedApkPath
if ($apkInfo.Length -le 0) {
    throw "The bundled APK is empty: $resolvedApkPath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedApkPath)
try {
    $hasManifest = $archive.Entries | Where-Object { $_.FullName -eq 'AndroidManifest.xml' } | Select-Object -First 1
    if ($null -eq $hasManifest) {
        throw "The bundled APK does not look valid; AndroidManifest.xml was not found: $resolvedApkPath"
    }
}
finally {
    $archive.Dispose()
}

$targetDirectory = Join-Path $resolvedPublishRoot 'catalogs\apks'
New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
$targetPath = Join-Path $targetDirectory $ApkFileName
Copy-Item -LiteralPath $resolvedApkPath -Destination $targetPath -Force

Write-Host "Bundled Quest APK: $targetPath"
