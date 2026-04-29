<#
.SYNOPSIS
    Creates or exports a self-signed preview code-signing certificate bundle.
#>
[CmdletBinding()]
param(
    [string]$Subject = 'CN=Rusty XR Companion Preview',
    [string]$ExistingThumbprint,
    [string]$Password = 'ChangeThisPreviewPassword',
    [string]$OutputRelativePath = 'artifacts\preview-signing'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

if (-not [string]::IsNullOrWhiteSpace($ExistingThumbprint)) {
    $normalized = $ExistingThumbprint.Replace(' ', '').ToUpperInvariant()
    $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
        Where-Object { $_.Thumbprint -eq $normalized } |
        Select-Object -First 1
    if ($null -eq $certificate) {
        throw "Certificate $normalized was not found."
    }
}
else {
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyExportPolicy Exportable `
        -KeyUsage DigitalSignature `
        -NotAfter (Get-Date).AddYears(5)
}

$securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
$pfxPath = Join-Path $outputPath 'RustyXrCompanion-preview-signing.pfx'
$cerPath = Join-Path $outputPath 'RustyXrCompanion-preview-signing.cer'
$base64Path = Join-Path $outputPath 'WINDOWS_PREVIEW_SETUP_CERTIFICATE_BASE64.txt'

Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null
[Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath)) |
    Set-Content -Path $base64Path -Encoding ascii

Write-Host "Certificate thumbprint: $($certificate.Thumbprint)"
Write-Host "PFX: $pfxPath"
Write-Host "CER: $cerPath"
Write-Host "Base64 secret file: $base64Path"
Write-Host "Set WINDOWS_PREVIEW_SETUP_CERTIFICATE_PASSWORD to the password used for export."
