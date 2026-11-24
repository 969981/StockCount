#requires -Version 7.0
<#
.SYNOPSIS
  Install a locally built StockCount release: Android APK via adb,
  Windows NSIS Setup interactively, or an unpack-and-run Windows Portable ZIP.
.EXAMPLE
  pwsh -File .\scripts\deploy-local.ps1 -Target WindowsPortable -PackagePath .\dist\local\StockCount-Server-v0.2.0-local-Windows-x64-Portable.zip -Destination D:\Apps\StockCount-Server
.EXAMPLE
  pwsh -File .\scripts\deploy-local.ps1 -Target Android -PackagePath .\dist\local\StockCount-PDA-v0.2.0-local-Android-Debug-Signed.apk
.NOTES
  Stops if an API/desktop process is running before replacing portable EXEs.
  Does not overwrite server-logs.db, database credentials, or local settings.
  Never auto-uninstalls an Android app (which could destroy SecureStorage).
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][ValidateSet('Android','WindowsSetup','WindowsPortable','AvaloniaPortable')]
  [string]$Target,
  [Parameter(Mandatory)][string]$PackagePath,
  [string]$Destination,
  [string]$DeviceSerial,
  [switch]$Launch
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Use this deployment script on a Windows machine.' }
$package = (Resolve-Path -LiteralPath $PackagePath).Path
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
  throw "Package not found: $package"
}

switch ($Target) {
  'AvaloniaPortable' {
    & (Join-Path $PSScriptRoot 'deploy-avalonia-portable.ps1') -PackagePath $package -Destination $Destination -Launch:$Launch
    if (-not $?) { throw 'Avalonia Portable upgrade failed.' }
  }
  'WindowsSetup' {
    if ([IO.Path]::GetExtension($package) -ne '.exe') { throw 'Setup must be an .exe file.' }
    Write-Host "Starting the NSIS installer: $package" -ForegroundColor Cyan
    Write-Host 'Follow the Windows installation wizard. No silent setup or elevation is forced.'
    $process = Start-Process -FilePath $package -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Setup finished with code $($process.ExitCode)" }
    Write-Host 'Installer completed. Configure API/TLS/database in the management UI.' -ForegroundColor Green
  }
  'Android' {
    if ([IO.Path]::GetExtension($package) -ne '.apk') { throw 'Android package must be .apk.' }
    if (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
      throw 'adb not found. Install Android SDK Platform-Tools and enable PDA USB debugging.'
    }
    $devices = @(& adb devices | Where-Object { $_ -match '^\S+\s+device$' } |
      ForEach-Object { ($_ -split '\s+')[0] })
    if ($LASTEXITCODE -ne 0) { throw 'adb devices failed.' }
    if ([string]::IsNullOrWhiteSpace($DeviceSerial)) {
      if ($devices.Count -ne 1) {
        throw "Expected one authorized Android device; found $($devices.Count). Specify -DeviceSerial and check adb devices."
      }
      $DeviceSerial = $devices[0]
    } elseif ($devices -notcontains $DeviceSerial) {
      throw "Device $DeviceSerial is not authorized/connected according to adb devices."
    }
    Write-Host "Installing Android APK to $DeviceSerial" -ForegroundColor Cyan
    & adb -s $DeviceSerial install -r -t $package
    if ($LASTEXITCODE -ne 0) {
      throw 'ADB install failed. Do not automatically uninstall an existing app; a signature mismatch requires a planned backup/migration of its encrypted pending drafts.'
    }
    if ($Launch) {
      & adb -s $DeviceSerial shell monkey -p com.dreamer.stockcount.pad -c android.intent.category.LAUNCHER 1
      if ($LASTEXITCODE -ne 0) { throw 'APK installed, but app launch failed.' }
    }
    Write-Host "Android install SUCCESS: $DeviceSerial" -ForegroundColor Green
  }
  'WindowsPortable' {
    if ([IO.Path]::GetExtension($package) -ne '.zip') { throw 'Portable package must be a .zip file.' }
    if (-not $Destination) {
      if (-not $env:LOCALAPPDATA) { throw 'Missing LOCALAPPDATA; specify -Destination.' }
      $Destination = Join-Path $env:LOCALAPPDATA 'StockCount\ServerPortable'
    }
    $dest = [IO.Path]::GetFullPath($Destination)
    if ($dest -eq [IO.Path]::GetPathRoot($dest)) {
      throw 'Refusing to deploy directly into the root of a drive.'
    }
    if ($dest.StartsWith([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')), [StringComparison]::OrdinalIgnoreCase)) {
      Write-Warning 'Destination is inside the Git checkout; prefer a separate user-writable application folder.'
    }
    # The sidecar writes SQLite logs adjacent to the desktop EXE. Never swap
    # binaries while a live API holds a .db-wal or .db-shm database open.
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
      $_.ProcessName -in @('stockcount-server-console','StockCount.Api')
    })
    if ($running.Count -gt 0) {
      throw 'StockCount desktop/API is still running. Stop the service, close the manager, and rerun before updating. SQLite logs must not be touched while running.'
    }
    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
      foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if ($name.StartsWith('/') -or $name -match '^[A-Za-z]:' -or
            @($name.Split('/')) -contains '..') {
          throw "Unsafe path in portable archive: $name"
        }
      }
    } finally { $archive.Dispose() }

    $stage = Join-Path ([IO.Path]::GetTempPath()) ("stockcount-deploy-" + [guid]::NewGuid().ToString('N'))
    New-Item -Path $stage -ItemType Directory -Force | Out-Null
    try {
      Expand-Archive -LiteralPath $package -DestinationPath $stage -Force
      $exe = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Filter 'stockcount-server-console.exe')
      if ($exe.Count -ne 1) { throw 'Portable ZIP must contain exactly one StockCount desktop EXE.' }
      $sourceDir = $exe[0].DirectoryName
      $must = @('stockcount-server-console.exe','StockCount.Api.exe',
        'Start-StockCount.cmd','README-PORTABLE.txt')
      foreach ($name in $must) {
        if (-not (Test-Path -LiteralPath (Join-Path $sourceDir $name) -PathType Leaf)) {
          throw "Portable archive is missing $name"
        }
      }

      New-Item -Path $dest -ItemType Directory -Force | Out-Null
      $existing = @($must | Where-Object { Test-Path -LiteralPath (Join-Path $dest $_) })
      $backup = if ($existing.Count -gt 0) {
        Join-Path $dest ("backup-binaries-" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
      } else { $null }
      if ($backup) {
        New-Item -Path $backup -ItemType Directory -Force | Out-Null
        foreach ($name in $existing) {
          Copy-Item -LiteralPath (Join-Path $dest $name) -Destination (Join-Path $backup $name)
        }
      }
      $updated = [Collections.Generic.List[string]]::new()
      try {
        foreach ($name in $must) {
          $source = Join-Path $sourceDir $name
          $targetFile = Join-Path $dest $name
          Copy-Item -LiteralPath $source -Destination $targetFile -Force
          $updated.Add($name)
          $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
          $targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash
          if ($sourceHash -ne $targetHash) { throw "Deployment checksum mismatch: $name" }
        }
      } catch {
        foreach ($name in $updated) {
          if ($backup -and (Test-Path -LiteralPath (Join-Path $backup $name))) {
            Copy-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $dest $name) -Force
          } elseif (Test-Path -LiteralPath (Join-Path $dest $name)) {
            Remove-Item -LiteralPath (Join-Path $dest $name) -Force
          }
        }
        throw
      }
      Write-Host "Portable deployment SUCCESS: $dest" -ForegroundColor Green
      if ($backup) { Write-Host "Old binaries backup (not logs): $backup" }
      Write-Host 'server-settings.json, server-logs.db, server-logs.db-wal and server-logs.db-shm were NOT modified.'
      Write-Host 'Settings live beside the desktop EXE; old AppData JSON migrates on first launch if absent. Passwords/device keys remain in Windows Credential Manager.'
      if ($Launch) { Start-Process -FilePath (Join-Path $dest 'stockcount-server-console.exe') }
    } finally {
      Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
}
