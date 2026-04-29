<#
.SYNOPSIS
    Scans the public repo for common private-data and artifact leaks.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [string[]]$ExtraDenyTerms = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Join-Path $PSScriptRoot '..\..'
}

$resolvedRoot = (Resolve-Path $RepoRoot).Path
$deniedTerms = @() + $ExtraDenyTerms
$localUserPathPattern = '[A-Za-z]:\\Users\\'

if (-not [string]::IsNullOrWhiteSpace($env:RUSTY_XR_COMPANION_DENY_TERMS)) {
    $deniedTerms += $env:RUSTY_XR_COMPANION_DENY_TERMS.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -gt 0 }
}

$textExtensions = @(
    '.cs', '.xaml', '.csproj', '.props', '.ps1', '.md', '.json', '.yml', '.yaml', '.mjs', '.js',
    '.html', '.css', '.slnx', '.gitattributes', '.gitignore', '.editorconfig'
)
$blockedArtifacts = @('.apk', '.aab', '.pfx', '.p12', '.keystore', '.jks', '.rgba', '.raw', '.u16le')
$excludedDirectories = @('\.git\', '\bin\', '\obj\', '\artifacts\', '\site\', '\node_modules\')

$failures = New-Object System.Collections.Generic.List[string]

Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File | ForEach-Object {
    $path = $_.FullName
    $relative = $path.Substring($resolvedRoot.Length).TrimStart('\')

    foreach ($excluded in $excludedDirectories) {
        if ($path -like "*$excluded*") {
            return
        }
    }

    if ($_.Extension -in $blockedArtifacts) {
        $failures.Add("Blocked artifact file: $relative")
        return
    }

    if ($_.Extension -notin $textExtensions -and $_.Name -notin @('LICENSE', 'README.md', 'AGENTS.md')) {
        return
    }

    $content = Get-Content -LiteralPath $path -Raw -ErrorAction SilentlyContinue
    if ($content -match $localUserPathPattern) {
        $failures.Add("Local Windows user path in $relative")
    }

    foreach ($term in $deniedTerms) {
        if (-not [string]::IsNullOrWhiteSpace($term) -and
            $content.IndexOf($term, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $failures.Add("Denied term '$term' in $relative")
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | Sort-Object -Unique | ForEach-Object { Write-Error $_ }
    throw "Public boundary scan failed with $($failures.Count) issue(s)."
}

Write-Host "Public boundary scan passed for $resolvedRoot"
