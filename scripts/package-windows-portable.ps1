#requires -Version 5.1
<#
Build the no-installer Windows portable archive from the SAME release EXE and
.NET self-contained sidecar used for the NSIS setup.
This is an unpack-and-run build, not a Windows Service and not a single EXE.

Prerequisites:
 - scripts/build-tauri-sidecar.ps1 was run successfully.
 - Tauri 2 Release binary was compiled on Windows x64.
 - The device has the WebView2 Runtime (Windows 11 typically includes it).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$OutputDirectory = "release-files"
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$targetDir = Join-Path $root "desktop/server-console/src-tauri/target/release"
$appExe = Join-Path $targetDir "stockcount-server-console.exe"
$sidecar = Join-Path $root "desktop/server-console/src-tauri/binaries/StockCount.Api-x86_64-pc-windows-msvc.exe"
if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) { throw "Tauri exe was not built: $appExe" }
if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) { throw "Self-contained API sidecar missing: $sidecar" }

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
