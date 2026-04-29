<#
.SYNOPSIS
    Publishes the guided portable setup helper and optionally signs it.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Version = '0.1.0.0',
    [string]$OutputRelativePath = 'artifacts\windows-installer',
    [string]$FileName = 'RustyXrCompanion-Setup.exe',
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$TimestampUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-SignToolPath {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kitsRoot) {
        $match = Get-ChildItem -Path $kitsRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -ne $match) {
            return $match.FullName
        }
    }

    throw 'signtool.exe was not found. Install the Windows SDK or add signtool.exe to PATH.'
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projectPath = Join-Path $repoRoot 'src\RustyXr.Companion.PreviewInstaller\RustyXr.Companion.PreviewInstaller.csproj'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
$publishPath = Join-Path $outputPath 'setup-publish'

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
if (Test-Path $publishPath) {
    Remove-Item -Recurse -Force $publishPath
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    -p:PublishSingleFile=true `
    -p:SelfContained=true `
    -p:PublishTrimmed=false `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    "/p:Version=$Version" `
    "/p:AssemblyVersion=$Version" `
    "/p:FileVersion=$Version" `
    "/p:InformationalVersion=$Version" `
    --output $publishPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishedExe = Join-Path $publishPath 'RustyXr.Companion.PreviewInstaller.exe'
if (-not (Test-Path $publishedExe)) {
    throw "Published setup helper not found at $publishedExe"
}

$finalPath = Join-Path $outputPath $FileName
Copy-Item -LiteralPath $publishedExe -Destination $finalPath -Force

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Test-Path $CertificatePath)) {
        throw "Signing certificate not found at $CertificatePath"
    }

    if ([string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $TimestampUrl = 'http://timestamp.digicert.com'
    }

    $signArgs = @(
        'sign',
        '/fd', 'SHA256',
        '/f', [System.IO.Path]::GetFullPath($CertificatePath)
    )

    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $signArgs += @('/p', $CertificatePassword)
    }

    $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256', $finalPath)

    & (Resolve-SignToolPath) @signArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed with exit code $LASTEXITCODE"
    }
}

Remove-Item -Recurse -Force $publishPath
Write-Host "Published setup helper to $finalPath"
