#requires -Version 7.0
<#
.SYNOPSIS
  Set StockCount's actual software version, not merely an output ZIP filename.
.DESCRIPTION
  Synchronizes Tauri configuration (canonical version), npm package, Cargo
  package and Android ApplicationDisplayVersion. Android numeric versionCode
  is independent: pass -AndroidVersionCode when making a new Android update.
.EXAMPLE
  pwsh -File .\scripts\set-app-version.ps1 -Version 1.0.1 -AndroidVersionCode 4 -WhatIf
  pwsh -File .\scripts\set-app-version.ps1 -Version 1.0.1 -AndroidVersionCode 4
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9]+(\.[A-Za-z0-9]+)*)?$')]
    [string]$Version,
    [ValidateRange(1,2100000000)][int]$AndroidVersionCode = 0
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tauri = Join-Path $root 'desktop/server-console/src-tauri/tauri.conf.json'
$package = Join-Path $root 'desktop/server-console/package.json'
$cargo = Join-Path $root 'desktop/server-console/src-tauri/Cargo.toml'
$maui = Join-Path $root 'src/StockCount.Pad/StockCount.Pad.csproj'

$files = @{}
foreach ($file in @($tauri, $package, $cargo, $maui)) {
    $files[$file] = Get-Content -LiteralPath $file -Raw -Encoding UTF8
}
# Replace ONLY the first top-level JSON version field and Cargo package version.
function Update-VersionText([string]$Text, [string]$Pattern, [string]$Value, [string]$Label) {
    $regex = [regex]::new($Pattern)
    if (-not $regex.IsMatch($Text)) { throw "Could not find expected version field in $Label" }
    return $regex.Replace($Text, { param($m) $m.Groups[1].Value + $Value + $m.Groups[2].Value }, 1)
}
$files[$tauri] = Update-VersionText $files[$tauri] '("version"\s*:\s*")[^"]+(")' $Version 'tauri.conf.json'
$files[$package] = Update-VersionText $files[$package] '("version"\s*:\s*")[^"]+(")' $Version 'package.json'
$files[$cargo] = Update-VersionText $files[$cargo] '(?m)^(version\s*=\s*")[^"]+(")' $Version 'Cargo.toml'
$files[$maui] = Update-VersionText $files[$maui] '(<ApplicationDisplayVersion>)[^<]+(</ApplicationDisplayVersion>)' $Version 'StockCount.Pad.csproj'

if ($PSBoundParameters.ContainsKey('AndroidVersionCode')) {
    $existing = [regex]::Match($files[$maui], '<ApplicationVersion>(\d+)</ApplicationVersion>')
    if (-not $existing.Success) { throw 'Missing Android ApplicationVersion (versionCode).' }
    $oldCode = [int]$existing.Groups[1].Value
    if ($AndroidVersionCode -le $oldCode) {
        throw "New Android versionCode ($AndroidVersionCode) must be greater than existing ($oldCode) for an update."
    }
    $files[$maui] = Update-VersionText $files[$maui] '(<ApplicationVersion>)\d+(</ApplicationVersion>)' ([string]$AndroidVersionCode) 'StockCount.Pad.csproj'
} else {
    Write-Warning 'Android versionCode is unchanged. Before distributing an Android update, specify -AndroidVersionCode with a value higher than the previous release.'
}

if (-not $PSCmdlet.ShouldProcess($root, "Set actual StockCount application version to $Version across four project files")) {
    return
}
$encoding = [System.Text.UTF8Encoding]::new($false)
foreach ($file in @($tauri,$package,$cargo,$maui)) {
    [System.IO.File]::WriteAllText($file, $files[$file], $encoding)
}
$checkedVersion = & (Join-Path $PSScriptRoot 'get-app-version.ps1')
if ($checkedVersion -ne $Version) {
    throw "Version update was not consistent: expected $Version, got $checkedVersion"
}
Write-Host "StockCount application version synchronized: $Version" -ForegroundColor Green
Write-Host "Android versionCode: $(if ($PSBoundParameters.ContainsKey('AndroidVersionCode')) { $AndroidVersionCode } else { 'unchanged' })"
Write-Host 'Commit the updated project metadata before producing public release binaries.'
