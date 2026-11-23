#requires -Version 5.1
<#
Build the no-installer Windows portable archive from the SAME release EXE and
.NET self-contained sidecar used for the NSIS setup.
This is an unpack-and-run build, not a Windows Service and not a single EXE.

Prerequisites:
 - Scripts package only by default; use -Build to compile Windows Setup
   and Portable ZIP automatically, or run build-local.ps1 -Target Windows.
 - Version defaults to Tauri application version in tauri.conf.json;
   optional -Version allows a suffix like 0.2.0-preview.3.
 - The target device needs WebView2 Runtime (usually installed on Win11).
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory = "release-files",
    [switch]$Build
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
# tauri.conf.json is the canonical application version. Other software
# metadata (Cargo/npm/MAUI) must match or packaging stops.
$configuredVersion = & (Join-Path $PSScriptRoot 'get-app-version.ps1')
if (-not $Version) { $Version = $configuredVersion }
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9]+(\.[A-Za-z0-9]+)*)?$') {
    throw "Invalid package version: $Version"
}
if ($Version -ne $configuredVersion -and
    -not $Version.StartsWith("$configuredVersion-", [StringComparison]::Ordinal)) {
    throw "Package version $Version does not match software version $configuredVersion. First run: pwsh -File .\scripts\set-app-version.ps1 -Version $Version (and increase Android versionCode for APK updates)."
}

# Convenience mode: build the actual Windows binaries and ZIP before packaging.
# Without -Build this script intentionally only packages existing Release EXEs.
if ($Build) {
    if (-not (Get-Command pwsh -ErrorAction SilentlyContinue)) {
        throw 'Building requires PowerShell 7 (pwsh). Install it and rerun with -Build.'
    }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-local.ps1') -Target Windows -Version $Version -OutputDirectory $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "Full Windows build failed with exit code $LASTEXITCODE" }
    return
}
$targetDir = Join-Path $root "desktop/server-console/src-tauri/target/release"
$appExe = Join-Path $targetDir "stockcount-server-console.exe"
$sidecar = Join-Path $root "desktop/server-console/src-tauri/binaries/StockCount.Api-x86_64-pc-windows-msvc.exe"
if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
    throw "Tauri Release EXE not found: $appExe. This script only packages compiled binaries. Run: pwsh -File .\scripts\build-local.ps1 -Target Windows ; or: pwsh -File .\scripts\package-windows-portable.ps1 -Build"
}
if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) {
    throw "Self-contained .NET API sidecar not found: $sidecar. Run: pwsh -File .\scripts\build-local.ps1 -Target Windows (this builds the sidecar before packaging)."
}
Write-Host "Packaging existing StockCount Windows EXEs. Software version: $configuredVersion; archive version: $Version"

if ([IO.Path]::IsPathRooted($OutputDirectory)) { $out = $OutputDirectory }
else { $out = Join-Path $root $OutputDirectory }
New-Item -ItemType Directory -Path $out -Force | Out-Null

$folderName = "StockCount-Server-v$Version-Windows-x64-Portable"
$stageRoot = Join-Path $out "portable-staging"
$stage = Join-Path $stageRoot $folderName
if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Copy-Item -LiteralPath $appExe -Destination (Join-Path $stage "stockcount-server-console.exe")
# Tauri shell.sidecar("StockCount.Api") resolves StockCount.Api.exe beside the
# desktop executable in a bundle; do NOT copy the target-triple suffix.
Copy-Item -LiteralPath $sidecar -Destination (Join-Path $stage "StockCount.Api.exe")
@'
@echo off
cd /d "%~dp0"
start "" "%~dp0stockcount-server-console.exe"
'@ | Set-Content -LiteralPath (Join-Path $stage "Start-StockCount.cmd") -Encoding ASCII

@"
StockCount Windows x64 - Portable build $Version
================================================
1. Extract the *entire* ZIP to a writable folder owned by your Windows user.
2. Run Start-StockCount.cmd or stockcount-server-console.exe.
3. Keep StockCount.Api.exe beside stockcount-server-console.exe.
4. The application creates server-logs.db in this same directory,
   possibly with -wal and -shm helper files.
5. SQL Server configuration remains in %LOCALAPPDATA%\StockCount\ServerHost\
   and secrets remain in Windows Credential Manager (NOT in this ZIP).
6. Your Windows account must have write access to this folder. Do NOT extract
   to Program Files. Never run an unknown public release as administrator.
7. Windows WebView2 Runtime must be present. If missing, install the runtime
   from Microsoft's official website (not an untrusted redistributor).
8. This is a desktop user-session application; it does not install a service.
9. For LAN PDA access, set a trusted HTTPS PFX and configure the firewall.
10. Test/Production X3 writes require independent admin approvals and
    database verification. The presence of this EXE does not enable writes.

Do not delete server-logs.db or the credential configuration while the API
is running. Back up the SQLite database before updating or relocating.
"@ | Set-Content -LiteralPath (Join-Path $stage "README-PORTABLE.txt") -Encoding UTF8

$zip = Join-Path $out "$folderName.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal
$zipInfo = Get-Item -LiteralPath $zip
if ($zipInfo.Length -lt 1048576) { throw "Unexpectedly small portable zip: $($zipInfo.Length)" }

# Reopen ZIP and check expected flat folder layout and exact sidecar name.
Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
    $expected = @(
        "$folderName/stockcount-server-console.exe",
        "$folderName/StockCount.Api.exe",
        "$folderName/Start-StockCount.cmd",
        "$folderName/README-PORTABLE.txt"
    )
    foreach ($item in $expected) {
        if (-not ($names -contains $item)) { throw "Portable ZIP is missing $item" }
    }
} finally {
    $archive.Dispose()
}

Remove-Item -LiteralPath $stageRoot -Recurse -Force
Write-Host "Portable Windows x64: $zip ($($zipInfo.Length) bytes)"
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
