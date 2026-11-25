#requires -Version 7.0
<#
.SYNOPSIS
  Package the Avalonia Windows single-file build and API as a Portable ZIP.
.DESCRIPTION
  Requires exactly two single-file EXEs, refuses to ship
  adjacent user settings, logs or signing secrets, and verifies the ZIP.
  Does not replace the existing Tauri Setup / Portable release.
.EXAMPLE
  pwsh -File .\scripts\package-avalonia-portable.ps1 -Build
#>
[CmdletBinding()]
param(
  [string]$Version,
  [string]$InputDirectory = 'publish/avalonia-desktop',
  [string]$OutputDirectory = 'dist/local',
  [switch]$Build
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Avalonia Portable packing requires Windows.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configuredVersion = (& (Join-Path $PSScriptRoot 'get-app-version.ps1')).Trim()
if ([string]::IsNullOrWhiteSpace($configuredVersion)) { throw 'Cannot resolve application version.' }
if (!$Version) { $Version = $configuredVersion }
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9]+(\.[A-Za-z0-9]+)*)?$') {
  throw "Invalid Avalonia archive version: $Version"
}
if ($Version -ne $configuredVersion -and
    -not $Version.StartsWith($configuredVersion + '-', [StringComparison]::Ordinal)) {
  throw "Avalonia archive version $Version does not match software version $configuredVersion. Run set-app-version.ps1 first."
}

$inputPath = if ([IO.Path]::IsPathFullyQualified($InputDirectory)) {
  [IO.Path]::GetFullPath($InputDirectory)
} else { [IO.Path]::GetFullPath((Join-Path $root $InputDirectory)) }
$out = if ([IO.Path]::IsPathFullyQualified($OutputDirectory)) {
  [IO.Path]::GetFullPath($OutputDirectory)
} else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }

if ($Build) {
  & (Join-Path $PSScriptRoot 'build-avalonia-desktop.ps1') -Configuration Release -OutputDirectory $inputPath
  if (-not $?) { throw 'Avalonia Release build failed.' }
}
if (!(Test-Path -LiteralPath $inputPath -PathType Container)) {
  throw "Avalonia publish output missing: $inputPath. Run build-avalonia-desktop.ps1 or use -Build."
}
$files = @(Get-ChildItem -LiteralPath $inputPath -Recurse -File -Force)
if ($files.Count -eq 0) { throw "Avalonia publish output is empty: $inputPath" }
# Both executables must be application bundles (not tiny apphosts).
$required = @{
  'StockCount.Server.Desktop.exe' = 1048576
  'StockCount.Api.exe' = 1048576
}
foreach ($name in $required.Keys) {
  $exe = Join-Path $inputPath $name
  if (!(Test-Path -LiteralPath $exe -PathType Leaf) -or
      (Get-Item -LiteralPath $exe).Length -lt $required[$name]) {
    throw "Required Windows EXE missing or suspicious: $exe"
  }
}


# User data, credentials and signing material never belong in a distributable
# ZIP. Fail closed rather than silently copying a developer's live settings.
$prohibited = '(^|[\\/])(server-settings\.json|server-logs\.db(-wal|-shm)?|.*\.jks|.*\.pfx|.*\.pem|.*\.key|\.env(\..*)?|.*secrets.*)$'
foreach ($file in $files) {
  $relative = [IO.Path]::GetRelativePath($inputPath, $file.FullName)
  if ($relative -match $prohibited) {
    throw "Refusing to distribute user data / signing material: $relative"
  }
  if ($file.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
    throw "Refusing to package symbolic link: $relative"
  }
}

# Validate the full tree; do not accept stale loose dependencies or subdirectories.
$unexpected = @(Get-ChildItem -LiteralPath $inputPath -Force | Where-Object {
    $_.PSIsContainer -or $_.Name -notin $required.Keys
})
if ($files.Count -ne 2 -or $unexpected.Count -ne 0) {
    throw 'Expected exactly two bundled EXEs. Rebuild into a clean publish directory; do not package loose files.'
}

$folder = "StockCount-Avalonia-v$Version-Windows-x64-Portable"
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ("stockcount-avalonia-stage-" + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $stageRoot $folder
$zip = Join-Path $out "$folder.zip"
New-Item -ItemType Directory -Path $stage,$out -Force | Out-Null
try {
  foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($inputPath, $file.FullName)
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
  }
  @'
@echo off
cd /d "%~dp0"
start "" "%~dp0StockCount.Server.Desktop.exe"
'@ | Set-Content -LiteralPath (Join-Path $stage 'Start-StockCount-Avalonia.cmd') -Encoding ascii
  @"
StockCount Avalonia Windows Portable v$Version
===============================================
1. Extract ALL files to a user-writable folder, not Program Files.
2. Launch Start-StockCount-Avalonia.cmd or StockCount.Server.Desktop.exe.
3. Keep the two single-file EXEs together. The default build requires x64 .NET 10 and ASP.NET Core 10 shared runtimes.
   Install the x64 ASP.NET Core Runtime 10 (includes .NET Runtime), or the x64 .NET 10 SDK, and check dotnet --list-runtimes.
   https://dotnet.microsoft.com/download/dotnet/10.0
   Optional self-contained builds do not require these shared runtimes.
   Native runtime files may be extracted to the per-user temporary .NET cache on first run.
4. server-settings.json and server-logs.db live BESIDE the UI EXE.
5. Old settings migrate from LocalAppData only if the adjacent JSON is absent.
6. Passwords and PDA device keys remain in Windows Credential Manager;
   they cannot be moved to another Windows user account with this ZIP.
7. Stop the UI and API before upgrades; preserve SQLite WAL/SHM files.
   Upgrading an old loose-file install does not automatically delete legacy DLLs. Use a fresh folder and migrate settings/logs safely if desired.
8. To upgrade safely use scripts/deploy-local.ps1 -Target AvaloniaPortable.
9. XTEST/COB write permissions remain default-deny until independently verified.
10. HTTP LAN access is only for trusted isolated PDA subnets, not the internet.
This is a Windows desktop user-session app, not a Windows Service.
"@ | Set-Content -LiteralPath (Join-Path $stage 'README-AVALONIA-PORTABLE.txt') -Encoding UTF8

  if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
  Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
  Add-Type -AssemblyName System.IO.Compression
  $archive = [IO.Compression.ZipFile]::OpenRead($zip)
  try {
    $actual = @($archive.Entries | Where-Object { $_.Name.Length -gt 0 } |
      ForEach-Object { $_.FullName.Replace('\','/') })
    $expected = @($files | ForEach-Object {
      "$folder/$([IO.Path]::GetRelativePath($inputPath, $_.FullName).Replace('\','/'))"
    }) + @("$folder/Start-StockCount-Avalonia.cmd","$folder/README-AVALONIA-PORTABLE.txt")
    foreach ($item in $expected) {
      if ($actual -notcontains $item) { throw "Avalonia archive is missing: $item" }
    }
    $unexpected = @($actual | Where-Object { $expected -notcontains $_ })
    if ($unexpected.Count -gt 0) { throw "Avalonia ZIP contains unexpected file: $($unexpected[0])" }
  } finally { $archive.Dispose() }
  $sha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
  Write-Host "Avalonia Portable created: $zip"
  Write-Host "SHA256: $sha"
} finally {
  Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
}
