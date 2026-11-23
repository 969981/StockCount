#requires -Version 7.0
<#[.SYNOPSIS] Windows x64 local build of the native PDA APK, NSIS Setup and Portable ZIP. #>
[CmdletBinding()]
param(
  [ValidateSet('All','Windows','Android')][string]$Target = 'All',
  [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9]+(\.[A-Za-z0-9]+)*)?$')][string]$Version,
  [string]$OutputDirectory = 'dist\local',
  [switch]$SkipTests,
  [switch]$InstallAndroidWorkload,
  [ValidateSet('Debug','Release','Both')][string]$AndroidVariant = 'Debug',
  [string]$ReleaseKeyStore,
  [string]$ReleaseKeyAlias
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require-Tool([string]$Name, [string]$Hint) {
  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "Missing $Name. $Hint" }
}
function Invoke-Checked([string]$Exe, [string[]]$ArgsList, [string]$Directory) {
  Push-Location -LiteralPath $Directory
  try {
    Write-Host "RUN: $Exe $($ArgsList -join ' ')" -ForegroundColor Cyan
    & $Exe @ArgsList
    if ($LASTEXITCODE -ne 0) { throw "$Exe exited with code $LASTEXITCODE" }
  } finally { Pop-Location }
}
function Assert-Artifact([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing output: $Path" }
  if ((Get-Item -LiteralPath $Path).Length -lt 1MB) { throw "Output too small: $Path" }
}

if (-not $IsWindows -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
  throw 'This build script requires Windows x64.'
}
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configuredVersion = & (Join-Path $PSScriptRoot 'get-app-version.ps1')
if (-not $Version) { $Version = $configuredVersion }
if ($Version -ne $configuredVersion -and
    -not $Version.StartsWith("$configuredVersion-", [StringComparison]::Ordinal)) {
  throw "Packaging version $Version does not match the application version $configuredVersion. Use scripts/set-app-version.ps1 first to change the actual software version."
}
Write-Host "StockCount application version: $configuredVersion. Build artifact version: $Version"
$out = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
  [IO.Path]::GetFullPath($OutputDirectory)
} else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }
New-Item -ItemType Directory -Path $out -Force | Out-Null
Require-Tool dotnet 'Install .NET 10 SDK (not only the Runtime).'
$dotnetVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $dotnetVersion.StartsWith('10.')) {
  throw "The .NET SDK must be 10.x (found: $dotnetVersion)."
}

$artifacts = [System.Collections.Generic.List[string]]::new()
if (-not $SkipTests) {
  Invoke-Checked dotnet @('test','tests/StockCount.Core.Tests/StockCount.Core.Tests.csproj','-c','Release') $root
}

if ($Target -in @('All','Windows')) {
  Require-Tool node 'Install Node.js 22 or newer.'
  Require-Tool npm 'Install Node.js 22 or newer.'
  Require-Tool cargo 'Install Rust x86_64-pc-windows-msvc.'
  Require-Tool rustc 'Install Rust x86_64-pc-windows-msvc.'
  $nodeVersion = (& node --version).Trim()
  if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v(2[2-9]|[3-9][0-9])\.') {
    throw "Node.js 22+ required (found: $nodeVersion)"
  }
  $rustInfo = (& rustc -vV) | Out-String
  if ($LASTEXITCODE -ne 0 -or $rustInfo -notmatch 'host: x86_64-pc-windows-msvc') {
    throw 'Rust host must be x86_64-pc-windows-msvc.'
  }
  Invoke-Checked dotnet @('build','src/StockCount.Api/StockCount.Api.csproj','-c','Release') $root
  & (Join-Path $PSScriptRoot 'build-tauri-sidecar.ps1')
  if ($LASTEXITCODE -ne 0) { throw 'Self-contained API Sidecar build failed.' }
  $desktop = Join-Path $root 'desktop/server-console'
  Invoke-Checked npm @('install') $desktop
  Invoke-Checked npm @('run','tauri','--','icon','./assets/app-icon.svg') $desktop
  Invoke-Checked npm @('run','build:desktop') $desktop
  $nsisDir = Join-Path $desktop 'src-tauri/target/release/bundle/nsis'
  $setups = @(Get-ChildItem -LiteralPath $nsisDir -File -Filter '*.exe')
  if ($setups.Count -ne 1) { throw "Expected one NSIS setup, got $($setups.Count)." }
  $setupDest = Join-Path $out "StockCount-Server-v$Version-Windows-x64-Setup.exe"
  Copy-Item -LiteralPath $setups[0].FullName -Destination $setupDest -Force
  Assert-Artifact $setupDest
  $artifacts.Add($setupDest)
  & (Join-Path $PSScriptRoot 'package-windows-portable.ps1') -Version $Version -OutputDirectory $out
  $portable = Join-Path $out "StockCount-Server-v$Version-Windows-x64-Portable.zip"
  Assert-Artifact $portable
  $artifacts.Add($portable)
}

if ($Target -in @('All','Android')) {
  if ($InstallAndroidWorkload) {
    Invoke-Checked dotnet @('workload','install','maui-android') $root
  } else {
    $workloads = (& dotnet workload list) | Out-String
    if ($LASTEXITCODE -ne 0 -or $workloads -notmatch 'maui-android') {
      throw 'Install MAUI Android workload or rerun with -InstallAndroidWorkload.'
    }
  }
  if ($AndroidVariant -in @('Debug','Both')) {
    Invoke-Checked dotnet @('build','src/StockCount.Pad/StockCount.Pad.csproj','-f','net10.0-android','-c','Debug','-p:AndroidPackageFormat=apk','-p:EmbedAssembliesIntoApk=true') $root
    $apkDir = Join-Path $root 'src/StockCount.Pad/bin/Debug/net10.0-android'
    $signed = @(Get-ChildItem -LiteralPath $apkDir -File -Filter '*-Signed.apk')
    if ($signed.Count -ne 1) { throw "Expected one signed debug APK, found $($signed.Count); do not ship unsigned APK." }
    $apk = Join-Path $out "StockCount-PDA-v$Version-Android-Debug-Signed.apk"
    Copy-Item -LiteralPath $signed[0].FullName -Destination $apk -Force
    Assert-Artifact $apk
    $artifacts.Add($apk)
  }

  if ($AndroidVariant -in @('Release','Both')) {
    if ([string]::IsNullOrWhiteSpace($ReleaseKeyStore) -or
        [string]::IsNullOrWhiteSpace($ReleaseKeyAlias)) {
      throw 'Android Release requires -ReleaseKeyStore, -ReleaseKeyAlias and environment signing passwords.'
    }
    & (Join-Path $PSScriptRoot 'build-android-release.ps1') -Version $Version -KeyStore $ReleaseKeyStore -KeyAlias $ReleaseKeyAlias -OutputDirectory $out
    $releaseApk = Join-Path $out "StockCount-PDA-v$Version-Android-Release-Signed.apk"
    Assert-Artifact $releaseApk
    $artifacts.Add($releaseApk)
  }
}

$hashLines = @($artifacts | ForEach-Object {
  $sum = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
  "$sum  $([IO.Path]::GetFileName($_))"
})
if ($hashLines.Count -eq 0) { throw 'No artifacts built.' }
$hashLines | Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
$commit = 'unknown'
if (Get-Command git -ErrorAction SilentlyContinue) {
  $commit = (& git -C $root rev-parse HEAD 2>$null | Select-Object -First 1)
  if ($LASTEXITCODE -ne 0) { $commit = 'unknown' }
}
[ordered]@{
  version=$Version; target=$Target; androidVariant=$AndroidVariant; utc=(Get-Date).ToUniversalTime().ToString('o');
  sourceCommit=[string]$commit; testsSkipped=[bool]$SkipTests;
  artifacts=@($artifacts | ForEach-Object { [IO.Path]::GetFileName($_) })
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'build-manifest.json') -Encoding utf8
Write-Host "SUCCESS: $Target artifacts saved to $out" -ForegroundColor Green
$hashLines | ForEach-Object { Write-Host $_ }
