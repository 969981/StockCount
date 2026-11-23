#requires -Version 7.0
# Generates native Windows .ico resources from the committed server SVG.
# This uses the repository's already-pinned Tauri CLI, not a third-party
# vector converter. Called automatically by Avalonia's MSBuild target.
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$desktop = Join-Path $root 'desktop/server-console'
$svg = Join-Path $desktop 'assets/app-icon.svg'
$generatedIco = Join-Path $desktop 'src-tauri/icons/icon.ico'
$target = Join-Path $root 'src/StockCount.Server.Desktop/Assets/server.ico'

if (!(Test-Path -LiteralPath $svg -PathType Leaf)) {
  throw "Missing source server SVG: $svg"
}
$needsGenerate = $Force -or !(Test-Path -LiteralPath $generatedIco -PathType Leaf) -or
  (Get-Item -LiteralPath $generatedIco).LastWriteTimeUtc -lt (Get-Item -LiteralPath $svg).LastWriteTimeUtc

if ($needsGenerate) {
  if (!(Get-Command node -ErrorAction SilentlyContinue) -or !(Get-Command npm -ErrorAction SilentlyContinue)) {
    throw 'Node.js 22+ and npm are required once to generate Windows icons from SVG. Install Node.js or use the Windows CI build.'
  }
  Push-Location -LiteralPath $desktop
  try {
    $cli = Join-Path $desktop 'node_modules/.bin/tauri.cmd'
    if (!(Test-Path -LiteralPath $cli)) {
      & npm ci --no-audit --no-fund
      if ($LASTEXITCODE -ne 0) { throw 'npm ci failed while preparing icon generator.' }
    }
    & npm run tauri -- icon './assets/app-icon.svg'
    if ($LASTEXITCODE -ne 0) { throw 'Tauri SVG icon generation failed.' }
  } finally { Pop-Location }
}
if (!(Test-Path -LiteralPath $generatedIco -PathType Leaf)) {
  throw "Tauri did not generate the Windows icon: $generatedIco"
}
New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
if ($Force -or !(Test-Path -LiteralPath $target) -or
  (Get-Item -LiteralPath $target).LastWriteTimeUtc -lt (Get-Item -LiteralPath $generatedIco).LastWriteTimeUtc) {
  Copy-Item -LiteralPath $generatedIco -Destination $target -Force
}
if ((Get-Item -LiteralPath $target).Length -lt 1000) {
  throw "Generated Windows .ico is too small: $target"
}
Write-Host "Windows icon ready: $target"
