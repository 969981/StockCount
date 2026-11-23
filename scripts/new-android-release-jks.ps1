#requires -Version 7.0
<#
.SYNOPSIS
  Create the ONE long-lived Android Release signing keystore for StockCount.
.DESCRIPTION
  Uses the JDK keytool in interactive mode: no password in PowerShell arguments,
  source code, environment variables, or saved command history.
  Refuses to overwrite an existing key and refuses to put the key in the repo.
.EXAMPLE
  pwsh -File .\scripts\new-android-release-jks.ps1 -OutputPath D:\StockCountSecrets\stockcount-release.jks
.EXAMPLE
  pwsh -File .\scripts\new-android-release-jks.ps1 -OutputPath D:\StockCountSecrets\stockcount-release.jks -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [string]$OutputPath = (Join-Path $HOME 'StockCountSecrets\stockcount-release.jks'),
  [ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')]
  [string]$Alias = 'stockcount',
  [ValidateRange(2048,8192)][int]$KeySize = 3072,
  [ValidateRange(365,20000)][int]$ValidityDays = 10000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath($OutputPath)
$rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($destination.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $destination.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
  throw 'JKS must be created OUTSIDE the Git checkout. Choose a private location such as D:\StockCountSecrets.'
}
if ([IO.Path]::GetExtension($destination) -ne '.jks') {
  throw 'Expected a .jks filename; StockCount explicitly uses JKS keystore format.'
}
if (Test-Path -LiteralPath $destination) {
  throw "Refusing to overwrite an existing signing identity: $destination. Reuse and back up the existing key instead."
}
if (-not $PSCmdlet.ShouldProcess($destination, 'Generate a new persistent JKS private key and certificate')) {
  return
}
$keytool = Get-Command keytool -ErrorAction SilentlyContinue
if (-not $keytool) {
  throw 'JDK keytool not found. Install a supported JDK and ensure keytool is on PATH.'
}

$parent = Split-Path -Path $destination -Parent
New-Item -Path $parent -ItemType Directory -Force | Out-Null
Write-Host 'Creating the long-term Android Release signing identity...' -ForegroundColor Cyan
Write-Host 'Choose strong passwords interactively when keytool prompts; never paste them into the script.'
Write-Host 'Do not generate another JKS for future releases; BACK UP this file and both passwords securely.'
$argsList = @('-genkeypair','-v','-storetype','JKS','-keystore',$destination,
  '-alias',$Alias,'-keyalg','RSA','-keysize',"$KeySize",'-validity',"$ValidityDays")
& $keytool.Source @argsList
if ($LASTEXITCODE -ne 0) {
  throw "keytool failed with exit code $LASTEXITCODE. Existing key material will not be overwritten."
}
if (-not (Test-Path -LiteralPath $destination -PathType Leaf) -or
    (Get-Item -LiteralPath $destination).Length -lt 1024) {
  throw 'Generated JKS file is missing or unexpectedly small.'
}
Write-Host 'Signing keystore generated successfully.' -ForegroundColor Green
Write-Host "Path: $destination"
Write-Host "Alias: $Alias"
Write-Host 'JKS FILE checksum (NOT the Android certificate fingerprint):'
Get-FileHash -LiteralPath $destination -Algorithm SHA256 | Format-List
Write-Host 'Record certificate SHA256 using this interactive command:'
Write-Host ('keytool -list -v -keystore "' + $destination + '" -alias ' + $Alias)
Write-Host 'Next: scripts/configure-android-release-secrets.ps1 and scripts/publish-stockcount-release.ps1.'
