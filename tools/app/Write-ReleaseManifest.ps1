<#
.SYNOPSIS
    Writes a public release manifest for Rusty XR Companion Windows artifacts.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseRoot,

    [Parameter(Mandatory = $true)]
    [string]$AppPublishRoot,

    [Parameter(Mandatory = $true)]
    [string]$CliPublishRoot,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$TagName,

    [string]$CommitSha = '',

    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-OptionalFileRecord {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $item = Get-Item -LiteralPath $Path
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    [ordered]@{
        fileName = $item.Name
        sizeBytes = $item.Length
        sha256 = $hash
    }
}

function Read-MetadataFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $values = [ordered]@{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) {
            continue
        }

        $index = $line.IndexOf('=')
        if ($index -lt 1) {
            continue
        }

        $key = $line.Substring(0, $index).Trim()
        $value = $line.Substring($index + 1).Trim()
        if (-not [string]::IsNullOrWhiteSpace($key)) {
            $values[$key] = $value
        }
    }

    [pscustomobject]$values
}

$resolvedReleaseRoot = (Resolve-Path $ReleaseRoot).Path
$resolvedAppRoot = (Resolve-Path $AppPublishRoot).Path
$resolvedCliRoot = (Resolve-Path $CliPublishRoot).Path
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $resolvedReleaseRoot 'RELEASE_MANIFEST.json'
}

$releaseAssets = @(
    Get-OptionalFileRecord (Join-Path $resolvedReleaseRoot 'RustyXrCompanion-Setup.exe')
    Get-OptionalFileRecord (Join-Path $resolvedReleaseRoot 'RustyXrCompanion-win-x64.zip')
    Get-OptionalFileRecord (Join-Path $resolvedReleaseRoot 'rusty-xr-companion-cli-win-x64.zip')
) | Where-Object { $null -ne $_ }

$apkMetadata = @()
$apkMetadataRoot = Join-Path $resolvedAppRoot 'catalogs\apks'
if (Test-Path -LiteralPath $apkMetadataRoot -PathType Container) {
    foreach ($metadataPath in Get-ChildItem -LiteralPath $apkMetadataRoot -Filter '*.metadata.txt' -File) {
        $metadata = Read-MetadataFile $metadataPath.FullName
        $apkMetadata += [ordered]@{
            metadataFile = $metadataPath.Name
            sourceRepo = $metadata.sourceRepo
            sourceUri = $metadata.sourceUri
            apkFile = $metadata.apkFile
            sha256 = $metadata.sha256
            sizeBytes = [long]$metadata.sizeBytes
            signing = $metadata.signing
            debuggable = $metadata.debuggable
            nativeLibraries = @($metadata.nativeLibraries -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            permissions = @($metadata.permissions -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        }
    }
}

$managedTools = @(
    [ordered]@{
        id = 'android-platform-tools'
        displayName = 'Android SDK Platform-Tools'
        bundled = $false
        installMode = 'explicit managed download or user-supplied path'
        destination = '%LOCALAPPDATA%\RustyXrCompanion\tooling\platform-tools'
        sourceUri = 'https://dl.google.com/android/repository/repository2-1.xml'
        licenseSummary = 'Android Software Development Kit License Agreement'
        licenseUri = 'https://developer.android.com/studio/releases/platform-tools'
        integrity = 'checksum from Google repository metadata is verified before use'
    }
    [ordered]@{
        id = 'meta-hzdb'
        displayName = 'Meta Horizon Debug Bridge (hzdb)'
        bundled = $false
        installMode = 'explicit managed download or user-supplied path'
        destination = '%LOCALAPPDATA%\RustyXrCompanion\tooling\hzdb'
        sourceUri = 'https://registry.npmjs.org/@meta-quest/hzdb-win32-x64/latest'
        licenseSummary = 'Meta Platform Technologies SDK License Agreement'
        licenseUri = 'https://developers.meta.com/horizon/licenses/'
        integrity = 'npm sha512 integrity is verified before use'
    }
    [ordered]@{
        id = 'scrcpy'
        displayName = 'scrcpy display cast runtime'
        bundled = $false
        installMode = 'explicit managed download or user-supplied path'
        destination = '%LOCALAPPDATA%\RustyXrCompanion\tooling\scrcpy'
        sourceUri = 'https://api.github.com/repos/Genymobile/scrcpy/releases/latest'
        licenseSummary = 'Apache License 2.0'
        licenseUri = 'https://github.com/Genymobile/scrcpy/blob/master/LICENSE'
        integrity = 'GitHub release SHA-256 is verified before use'
    }
    [ordered]@{
        id = 'ffmpeg'
        displayName = 'FFmpeg media runtime'
        bundled = $false
        installMode = 'explicit managed media-runtime download or user-supplied path'
        destination = '%LOCALAPPDATA%\RustyXrCompanion\tooling\ffmpeg'
        sourceUri = 'https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest'
        licenseSummary = 'FFmpeg Windows x64 LGPL shared build'
        licenseUri = 'https://ffmpeg.org/legal.html'
        integrity = 'GitHub release SHA-256 is verified before use; ffmpeg -version is classified for --enable-gpl and --enable-nonfree'
    }
)

$manifest = [ordered]@{
    schemaVersion = 'rusty.xr.companion.release-manifest.v1'
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
    application = [ordered]@{
        name = 'Rusty XR Companion'
        repository = 'https://github.com/MesmerPrism/Rusty-XR-Companion-Apps'
        version = $Version
        tag = $TagName
        commit = $CommitSha
        license = 'MIT'
        thirdPartyNotices = 'THIRD_PARTY_NOTICES.md'
    }
    releaseAssets = $releaseAssets
    payloads = [ordered]@{
        appPublishRoot = 'RustyXrCompanion-win-x64.zip'
        cliPublishRoot = 'rusty-xr-companion-cli-win-x64.zip'
        includesLicense = (Test-Path -LiteralPath (Join-Path $resolvedAppRoot 'LICENSE') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $resolvedCliRoot 'LICENSE') -PathType Leaf)
        includesThirdPartyNotices = (Test-Path -LiteralPath (Join-Path $resolvedAppRoot 'THIRD_PARTY_NOTICES.md') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $resolvedCliRoot 'THIRD_PARTY_NOTICES.md') -PathType Leaf)
        bundledQuestApks = $apkMetadata
    }
    managedTools = $managedTools
    safety = [ordered]@{
        cameraPermissions = 'Runtime camera/headset-camera permissions remain target-APK permissions.'
        mediaProjection = 'MediaProjection consent is a headset/user step; Companion does not bypass it.'
        hazardousProfiles = 'Strobe/flicker runtime profiles are opt-in catalog profiles.'
        generatedArtifacts = 'Diagnostics, screenshots, H.264 payloads, and preview frames are written to ignored local artifact/cache folders.'
    }
}

$json = $manifest | ConvertTo-Json -Depth 12
Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8
Write-Host "Release manifest written: $OutputPath"
