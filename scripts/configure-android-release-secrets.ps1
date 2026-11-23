#requires -Version 7.0
<#
.SYNOPSIS
  Upload the long-lived StockCount Android JKS and signing passwords to
  the four repository-level GitHub Actions Secrets using GitHub CLI.
.DESCRIPTION
  No passwords are accepted on command line, written to disk or printed.
  Values go to gh secret set over standard input. Refuses to overwrite existing
  secret names unless -Force is explicitly provided.
.EXAMPLE
  pwsh -File .\scripts\configure-android-release-secrets.ps1 -KeyStore D:\StockCountSecrets\stockcount-release.jks
.EXAMPLE
  pwsh -File .\scripts\configure-android-release-secrets.ps1 -KeyStore D:\StockCountSecrets\stockcount-release.jks -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [Parameter(Mandatory)][string]$KeyStore,
  [ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')][string]$KeyAlias = 'stockcount',
  [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
  [string]$Repository = '969981/StockCount',
  [switch]$Force
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$file = (Resolve-Path -LiteralPath $KeyStore -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Keystore must be an existing file.' }
if ((Get-Item -LiteralPath $file).Length -lt 1024) { throw 'Keystore is too small; refusing to upload.' }
if ([IO.Path]::GetExtension($file) -ne '.jks') { throw 'This script expects a JKS keystore.' }

$required = @(
  'STOCKCOUNT_ANDROID_KEYSTORE_BASE64',
  'STOCKCOUNT_ANDROID_KEY_ALIAS',
  'STOCKCOUNT_ANDROID_STORE_PASS',
  'STOCKCOUNT_ANDROID_KEY_PASS'
)
Write-Host "Target repository: $Repository"
Write-Host "Signing key alias: $KeyAlias"
Write-Host 'Secrets to set:'
$required | ForEach-Object { Write-Host "  $_" }

if (-not $PSCmdlet.ShouldProcess($Repository, 'Upload four Android Release signing secrets')) {
  return
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
  throw 'GitHub CLI missing. Install gh, then run: gh auth login.'
}
& gh auth status
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI is not authenticated. Run gh auth login.' }
$json = & gh secret list --repo $Repository --json name
if ($LASTEXITCODE -ne 0) { throw "Could not read Actions secrets metadata for $Repository." }
$present = @($json | ConvertFrom-Json | ForEach-Object { $_.name })
$overlap = @($required | Where-Object { $_ -in $present })
if ($overlap.Count -gt 0 -and -not $Force) {
  throw "Signing Secrets already exist ($($overlap -join ', ')). Refusing to silently replace an update identity. Use -Force ONLY if this is the SAME original JKS and passwords."
}
if ($overlap.Count -gt 0) {
  Write-Warning 'Replacing existing secrets. If the JKS is different, existing PDA Release APKs may no longer update in place.'
}

$storeSecure = Read-Host 'Existing JKS STORE password (hidden)' -AsSecureString
$keySecure = Read-Host 'Existing JKS ALIAS/KEY password (hidden)' -AsSecureString
$storePass = [Net.NetworkCredential]::new('', $storeSecure).Password
$keyPass = [Net.NetworkCredential]::new('', $keySecure).Password
if ([string]::IsNullOrWhiteSpace($storePass) -or [string]::IsNullOrWhiteSpace($keyPass)) {
  throw 'Both passwords must be non-empty.'
}

function Set-GitHubSecret([string]$Name, [string]$Value) {
  if ([string]::IsNullOrWhiteSpace($Value)) { throw "Refusing empty secret $Name." }
  $Value | & gh secret set $Name --repo $Repository
  if ($LASTEXITCODE -ne 0) { throw "GitHub rejected secret $Name." }
  Write-Host "Configured: $Name" -ForegroundColor Green
}
try {
  $encoded = [Convert]::ToBase64String([IO.File]::ReadAllBytes($file))
  Set-GitHubSecret 'STOCKCOUNT_ANDROID_KEYSTORE_BASE64' $encoded
  Set-GitHubSecret 'STOCKCOUNT_ANDROID_KEY_ALIAS' $KeyAlias
  Set-GitHubSecret 'STOCKCOUNT_ANDROID_STORE_PASS' $storePass
  Set-GitHubSecret 'STOCKCOUNT_ANDROID_KEY_PASS' $keyPass
} finally {
  $storePass = $null
  $keyPass = $null
  $encoded = $null
}

Write-Host 'All four GitHub Actions secrets configured.' -ForegroundColor Green
Write-Host 'Verify secret NAMES (never values):'
& gh secret list --repo $Repository
Write-Host 'Next: scripts/publish-stockcount-release.ps1 -Version 0.2.0-preview.3'
