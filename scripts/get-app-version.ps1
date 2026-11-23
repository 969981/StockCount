#requires -Version 5.1
<#
.SYNOPSIS
  Read the StockCount app version from Tauri configuration (source of truth).
.DESCRIPTION
  Validates synchronized version fields in package.json, Cargo.toml and MAUI.
  Prints a single version string for use by local packaging commands.
.EXAMPLE
  .\scripts\get-app-version.ps1
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tauriPath = Join-Path $root 'desktop/server-console/src-tauri/tauri.conf.json'
$packagePath = Join-Path $root 'desktop/server-console/package.json'
$cargoPath = Join-Path $root 'desktop/server-console/src-tauri/Cargo.toml'
$mauiPath = Join-Path $root 'src/StockCount.Pad/StockCount.Pad.csproj'

$tauri = Get-Content -LiteralPath $tauriPath -Raw -Encoding UTF8 | ConvertFrom-Json
$package = Get-Content -LiteralPath $packagePath -Raw -Encoding UTF8 | ConvertFrom-Json
$cargo = Get-Content -LiteralPath $cargoPath -Raw -Encoding UTF8
$maui = Get-Content -LiteralPath $mauiPath -Raw -Encoding UTF8

$version = [string]$tauri.version
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9]+(\.[A-Za-z0-9]+)*)?$') {
    throw "Invalid application version in tauri.conf.json: $version"
}
if ($cargo -notmatch '(?m)^version\s*=\s*"([^"]+)"') {
    throw 'Missing [package] version in Cargo.toml.'
}
$cargoVersion = $Matches[1]
if ($maui -notmatch '<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>') {
    throw 'Missing ApplicationDisplayVersion in StockCount.Pad.csproj.'
}
$mauiVersion = $Matches[1]
$declared = @(
    [pscustomobject]@{ Name = 'package.json'; Value = [string]$package.version },
    [pscustomobject]@{ Name = 'Cargo.toml'; Value = $cargoVersion },
    [pscustomobject]@{ Name = 'StockCount.Pad.csproj'; Value = $mauiVersion }
)
foreach ($entry in $declared) {
    if ($entry.Value -ne $version) {
        throw "App version mismatch: tauri.conf.json=$version, $($entry.Name)=$($entry.Value). Run scripts/set-app-version.ps1 -Version $version to synchronize project metadata."
    }
}
Write-Output $version
