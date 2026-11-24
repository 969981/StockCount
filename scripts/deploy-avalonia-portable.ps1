#requires -Version 7.0
<#
.SYNOPSIS
  Safely install/upgrade StockCount Avalonia Portable without touching user
  settings, SQLite history (including WAL/SHM) or Credential Manager secrets.
.EXAMPLE
  pwsh -File .\scripts\deploy-local.ps1 -Target AvaloniaPortable -PackagePath .\dist\local\StockCount-Avalonia-v0.2.0-local-Windows-x64-Portable.zip -Destination D:\Apps\StockCount-Avalonia -Launch
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$PackagePath,
  [string]$Destination,
  [switch]$Launch
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'Avalonia Portable deployment requires Windows.' }
$package = (Resolve-Path -LiteralPath $PackagePath).Path
if ([IO.Path]::GetExtension($package).ToLowerInvariant() -ne '.zip') {
  throw 'Avalonia Portable must be supplied as a ZIP.'
}
if (!$Destination) {
  $Destination = Join-Path $env:LOCALAPPDATA 'StockCount\AvaloniaPortable'
}
$dest = [IO.Path]::GetFullPath($Destination)
if ($dest -eq [IO.Path]::GetPathRoot($dest)) {
  throw 'Refusing to deploy into a drive root.'
}
if (@(Get-Process -ErrorAction SilentlyContinue | Where-Object {
  $_.ProcessName -in @('StockCount.Server.Desktop','stockcount-server-console','StockCount.Api')
}).Count -gt 0) {
  throw 'StockCount UI/API is still running. Stop both managers and the API before upgrading; never touch open SQLite WAL/SHM.'
}

Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
  $entries = @($archive.Entries | Where-Object { $_.Name.Length -gt 0 })
  if ($entries.Count -eq 0) { throw 'Avalonia Portable ZIP contains no files.' }
  $roots = @($entries | ForEach-Object { $_.FullName.Replace('\','/').Split('/')[0] } | Select-Object -Unique)
  if ($roots.Count -ne 1 -or $roots[0] -notmatch '^StockCount-Avalonia-v[a-zA-Z0-9.\-]+-Windows-x64-Portable$') {
    throw 'Avalonia archive has an unexpected or ambiguous root folder.'
  }
  $root = $roots[0]
  $fileNames = @()
  foreach ($entry in $entries) {
    $path = $entry.FullName.Replace('\','/')
    $parts = @($path.Split('/'))
    if ($parts.Count -lt 2 -or $parts[0] -ne $root -or
        $parts -contains '..' -or $parts -contains '.' -or $path.StartsWith('/') -or
        $path.Contains(':') -or $path.Contains('//')) {
      throw "Unsafe archive entry: $path"
    }
    # A ZIP with Unix-style symlink entries cannot be trusted for deployment.
    $unixMode = ($entry.ExternalAttributes -shr 16) -band 0xF000
    if ($unixMode -eq 0xA000) { throw "Archive contains symlink: $path" }
    if ($path -match '(?i)(^|/)(server-settings\.json|server-logs\.db(-wal|-shm)?|.*\.pfx|.*\.jks|.*\.pem|.*\.key|\.env(\..*)?)$') {
      throw "Portable archive must not contain persisted user data or keys: $path"
    }
    $fileNames += $path
  }
  foreach ($name in @('StockCount.Server.Desktop.exe','StockCount.Api.exe',
      'Start-StockCount-Avalonia.cmd','README-AVALONIA-PORTABLE.txt')) {
    if ($fileNames -notcontains "$root/$name") {
      throw "Avalonia Portable archive is missing $name"
    }
  }
} finally { $archive.Dispose() }

$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ('stockcount-avalonia-deploy-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
try {
  Expand-Archive -LiteralPath $package -DestinationPath $stageRoot -Force
  $sourceDir = Join-Path $stageRoot $root
  $files = @(Get-ChildItem -LiteralPath $sourceDir -File -Recurse -Force)
  if ($files.Count -ne $fileNames.Count) { throw 'Archive expansion file count mismatch.' }

  New-Item -ItemType Directory -Path $dest -Force | Out-Null
  $backup = Join-Path $dest ('backup-binaries-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
  $changed = [Collections.Generic.List[string]]::new()
  $previous = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  try {
    foreach ($file in $files) {
      $relative = [IO.Path]::GetRelativePath($sourceDir, $file.FullName)
      $target = Join-Path $dest $relative
      $parent = Split-Path $target -Parent
      New-Item -ItemType Directory -Path $parent -Force | Out-Null
      if (Test-Path -LiteralPath $target) {
        if (!(Test-Path -LiteralPath $target -PathType Leaf)) {
          throw "Refusing to replace directory with file: $relative"
        }
        $backupFile = Join-Path $backup $relative
        New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $target -Destination $backupFile -Force
        [void]$previous.Add($relative)
      }
      $changed.Add($relative)
      Copy-Item -LiteralPath $file.FullName -Destination $target -Force
      if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne
          (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
        throw "Deployment SHA256 verification failed: $relative"
      }
    }
  } catch {
    $failure = $_
    foreach ($relative in $changed) {
      $target = Join-Path $dest $relative
      $saved = Join-Path $backup $relative
      if ($previous.Contains($relative) -and (Test-Path -LiteralPath $saved)) {
        Copy-Item -LiteralPath $saved -Destination $target -Force
      } elseif (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Force
      }
    }
    throw $failure
  }
  Write-Host "Avalonia Portable upgrade succeeded: $dest" -ForegroundColor Green
  if (Test-Path -LiteralPath $backup) { Write-Host "Previous program files backed up: $backup" }
  Write-Host 'server-settings.json, server-logs.db, -wal and -shm untouched. Windows Credential Manager credentials remain on this account.'
  if ($Launch) { Start-Process -FilePath (Join-Path $dest 'StockCount.Server.Desktop.exe') }
} finally {
  Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
}
