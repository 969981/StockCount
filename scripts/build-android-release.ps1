#requires -Version 7.0
<#
.SYNOPSIS
  Build an installable, signed .NET MAUI Android Release APK.
.DESCRIPTION
  Passwords come from STOCKCOUNT_ANDROID_STORE_PASS and
  STOCKCOUNT_ANDROID_KEY_PASS. MSBuild reads them via env: references, never
  plaintext CLI arguments. Requires a persistent keystore, not a debug or
  temporary key. Verifies signed APK with Android SDK apksigner.
.EXAMPLE
  pwsh -File .\scripts\build-android-release.ps1 -Version 0.2.0-local -KeyStore D:\Keys\stockcount-release.jks -KeyAlias stockcount
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$KeyStore,
  [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')][string]$KeyAlias,
  [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9]+(\.[A-Za-z0-9]+)*)?$')]
  [string]$Version = '0.2.0-local',
  [string]$OutputDirectory = 'dist\local'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
  throw 'Release APK packaging requires Windows x64 with the .NET MAUI Android toolchain.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw 'Missing .NET 10 SDK.'
}
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$keystoreFile = (Resolve-Path -LiteralPath $KeyStore -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $keystoreFile -PathType Leaf)) { throw 'Keystore must be a file.' }
if ($keystoreFile -match '["''\r\n]') { throw 'Invalid characters in keystore path.' }
if ([string]::IsNullOrWhiteSpace($env:STOCKCOUNT_ANDROID_STORE_PASS) -or
    [string]::IsNullOrWhiteSpace($env:STOCKCOUNT_ANDROID_KEY_PASS)) {
  throw 'Set both STOCKCOUNT_ANDROID_STORE_PASS and STOCKCOUNT_ANDROID_KEY_PASS; never embed signing passwords in script files or CLI arguments.'
}
$out = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
  [IO.Path]::GetFullPath($OutputDirectory)
} else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }
New-Item -ItemType Directory -Path $out -Force | Out-Null

$project = Join-Path $root 'src/StockCount.Pad/StockCount.Pad.csproj'
$buildStartedUtc = [DateTime]::UtcNow
$argsList = @(
  'publish', $project, '-f', 'net10.0-android', '-c', 'Release',
  '-p:AndroidPackageFormats=apk', '-p:AndroidKeyStore=true',
  "-p:AndroidSigningKeyStore=$keystoreFile",
  "-p:AndroidSigningKeyAlias=$KeyAlias",
  '-p:AndroidSigningStorePass=env:STOCKCOUNT_ANDROID_STORE_PASS',
  '-p:AndroidSigningKeyPass=env:STOCKCOUNT_ANDROID_KEY_PASS'
)
Write-Host 'Building Android Release APK with a persistent signing keystore...' -ForegroundColor Cyan
& dotnet @argsList
if ($LASTEXITCODE -ne 0) { throw "Android Release publish failed ($LASTEXITCODE)." }

$releaseDir = Join-Path $root 'src/StockCount.Pad/bin/Release/net10.0-android'
$publishDir = Join-Path $releaseDir 'publish'
$searchDir = if (Test-Path -LiteralPath $publishDir) { $publishDir } else { $releaseDir }
$apks = @(Get-ChildItem -LiteralPath $searchDir -Recurse -File -Filter '*-Signed.apk' |
  Where-Object { $_.LastWriteTimeUtc -ge $buildStartedUtc.AddSeconds(-5) })
if ($apks.Count -ne 1) {
  throw "Expected exactly one new signed Release APK in $searchDir; found $($apks.Count). An unsigned package is never released."
}

$sdkCandidates = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT,
    (Join-Path $env:LOCALAPPDATA 'Android\Sdk')) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
$verifier = $null
foreach ($sdk in $sdkCandidates) {
  $buildTools = Join-Path $sdk 'build-tools'
  if (-not (Test-Path -LiteralPath $buildTools)) { continue }
  $found = @(Get-ChildItem -LiteralPath $buildTools -Recurse -File -Filter 'apksigner.bat' |
    Sort-Object FullName -Descending | Select-Object -First 1)
  if ($found.Count -gt 0) { $verifier = $found[0].FullName; break }
}
if (-not $verifier) {
  throw 'Android SDK apksigner.bat not found; cannot verify Release APK signature.'
}
& $verifier verify --verbose --print-certs $apks[0].FullName
if ($LASTEXITCODE -ne 0) { throw 'Release APK signature verification failed.' }
& (Join-Path $PSScriptRoot 'verify-android-launcher-icon.ps1') -ApkPath $apks[0].FullName
& (Join-Path $PSScriptRoot 'verify-android-feedback-audio.ps1') -ApkPath $apks[0].FullName

$releaseApk = Join-Path $out "StockCount-PDA-v$Version-Android-Release-Signed.apk"
Copy-Item -LiteralPath $apks[0].FullName -Destination $releaseApk -Force
if ((Get-Item -LiteralPath $releaseApk).Length -lt 1MB) {
  throw 'Signed Release APK is unexpectedly small.'
}
$sourceHash = (Get-FileHash -LiteralPath $apks[0].FullName -Algorithm SHA256).Hash
$destHash = (Get-FileHash -LiteralPath $releaseApk -Algorithm SHA256).Hash
if ($sourceHash -ne $destHash) { throw 'Release APK staging SHA256 mismatch.' }
Write-Host "Release APK verified: $releaseApk" -ForegroundColor Green
Get-FileHash -LiteralPath $releaseApk -Algorithm SHA256 | Format-List
