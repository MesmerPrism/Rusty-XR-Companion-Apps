<#
.SYNOPSIS
    Validates the guided setup helper signature when signing is expected.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SetupPath,
    [switch]$RequireSignature,
    [switch]$AllowSelfSigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPath = (Resolve-Path $SetupPath).Path
$signature = Get-AuthenticodeSignature -FilePath $resolvedPath

if ($null -eq $signature.SignerCertificate) {
    if ($RequireSignature) {
        throw "No signer certificate was found on $resolvedPath."
    }

    Write-Warning "No signer certificate was found on $resolvedPath. This is acceptable only for unsigned preview artifacts."
    return
}

$selfIssued = [string]::Equals(
    $signature.SignerCertificate.Subject,
    $signature.SignerCertificate.Issuer,
    [System.StringComparison]::OrdinalIgnoreCase)

$allowedSelfSignedTrustFailure =
    $AllowSelfSigned -and
    $selfIssued -and
    $signature.Status -eq [System.Management.Automation.SignatureStatus]::UnknownError -and
    $signature.StatusMessage -match 'not trusted by the trust provider'

if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -and -not $allowedSelfSignedTrustFailure) {
    throw "Authenticode validation failed for ${resolvedPath}: $($signature.Status) $($signature.StatusMessage)"
}

if ($null -eq $signature.TimeStamperCertificate) {
    throw "The signed setup helper is missing an RFC3161 timestamp."
}

if ($selfIssued) {
    Write-Warning "Setup helper is signed with a self-issued certificate. Windows reputation policy can still block first-run downloads."
}

[pscustomobject]@{
    Path = $resolvedPath
    Subject = $signature.SignerCertificate.Subject
    Issuer = $signature.SignerCertificate.Issuer
    Thumbprint = $signature.SignerCertificate.Thumbprint
    TimestampBy = $signature.TimeStamperCertificate.Subject
    SelfIssued = $selfIssued
} | Format-List | Out-Host
