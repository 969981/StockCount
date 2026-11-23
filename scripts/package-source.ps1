#requires -Version 7.0
<#
.SYNOPSIS
  Export a reproducible ZIP of committed StockCount source code, docs,
  workflows and scripts, without Git history or untracked local files.
.EXAMPLE
  pwsh -File .\scripts\package-source.ps1 -Version 0.2.0-local
.EXAMPLE
  pwsh -File .\scripts\package-source.ps1 -Ref v0.2.0-preview.2 -Version 0.2.0-preview.2
.NOTES
  Refuses a dirty working tree by default so changes are not silently lost.
  -AllowDirtySnapshot archives ONLY the selected commit and warns that
  uncommitted files are intentionally excluded.
#>
[CmdletBinding()]
param(
  [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9]+(\.[A-Za-z0-9]+)*)?$')][string]$Version = '0.2.0-local',
  [string]$Ref = 'HEAD',
  [string]$OutputDirectory = 'dist\source',
  [switch]$AllowDirtySnapshot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
  throw 'Git is required. Install Git for Windows and rerun in a cloned repository.'
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gitRoot = (& git -C $root rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0) { throw 'Not a Git repository. Clone StockCount first.' }
if ([IO.Path]::GetFullPath($gitRoot.Trim()) -ne [IO.Path]::GetFullPath($root)) {
  throw "Run this script inside the StockCount repository. Current root: $gitRoot"
}

$status = @(& git -C $root status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Git working tree.' }
if ($status.Count -gt 0) {
  if (-not $AllowDirtySnapshot) {
    $details = ($status | Select-Object -First 12) -join [Environment]::NewLine
    throw ("Working tree has uncommitted/untracked files. Commit them first or use -AllowDirtySnapshot (exports committed files only)." + [Environment]::NewLine + $details)
  }
  Write-Warning 'Working tree is dirty. Local modifications and untracked files WILL NOT appear in the source ZIP.'
}

$commit = (& git -C $root rev-parse --verify "$Ref^{commit}" | Select-Object -First 1)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
  throw "Git reference cannot be resolved to a commit: $Ref"
}
$commit = $commit.Trim()
$tracked = @(& git -C $root ls-tree -r --name-only $commit)
if ($LASTEXITCODE -ne 0 -or $tracked.Count -eq 0) { throw 'Selected commit contains no tracked files.' }

# Fail closed if secrets or compiled build output were accidentally committed.
# Filename checks do not replace a full content-level secret audit.
$forbidden = '(^|/)(\.git|node_modules|bin|obj|target|dist|publish|\.vs)(/|$)|(^|/)(server-logs\.db.*|server-settings\.json|appsettings\.Production\.json|\.env($|\.(?!example$))|[^/]+\.(pfx|p12|pem|key|keystore|jks|db|sqlite|sqlite3))$'
$bad = @($tracked | Where-Object { $_ -match $forbidden })
if ($bad.Count -gt 0) {
  throw "Refusing to archive tracked secrets/build outputs: $($bad[0..([Math]::Min($bad.Count-1,9))] -join ', ')"
}

$mustHave = @('README.md','StockCount.slnx',
  'src/StockCount.Pad/StockCount.Pad.csproj',
  'src/StockCount.Api/StockCount.Api.csproj',
  'desktop/server-console/src-tauri/Cargo.toml',
  'scripts/build-local.ps1','scripts/build-android-release.ps1',
  'scripts/get-app-version.ps1','scripts/set-app-version.ps1',
  'scripts/new-android-release-jks.ps1','scripts/configure-android-release-secrets.ps1',
  'scripts/publish-stockcount-release.ps1','scripts/package-source.ps1')
foreach ($file in $mustHave) {
  if ($tracked -notcontains $file) {
    throw "Missing essential source file in $Ref ($commit): $file"
  }
}

$out = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
  [IO.Path]::GetFullPath($OutputDirectory)
} else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }
New-Item -Path $out -ItemType Directory -Force | Out-Null
$folder = "StockCount-Source-v$Version"
$zip = Join-Path $out "$folder.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
& git -C $root archive --format=zip "--prefix=$folder/" "--output=$zip" $commit
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $zip)) {
  throw 'git archive failed. No source package was produced.'
}
if ((Get-Item -LiteralPath $zip).Length -lt 10240) {
  throw "Source ZIP is unexpectedly small: $zip"
}

Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
  $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
  foreach ($file in $mustHave) {
    if ($names -notcontains "$folder/$file") {
      throw "Archive verification failed: $file not included."
    }
  }
  $insecure = @($names | Where-Object { $_ -match '(^|/)(\.git|node_modules|dist)(/|$)' })
  if ($insecure.Count -gt 0) { throw "Archive contains disallowed path $($insecure[0])" }
} finally { $archive.Dispose() }

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $folder.zip" | Set-Content -LiteralPath (Join-Path $out "$folder.SHA256SUMS.txt") -Encoding ascii
@"
StockCount SOURCE SNAPSHOT
Version: $Version
Ref: $Ref
Commit: $commit
ZIP: $folder.zip
SHA256: $hash
Source: ONLY committed Git files at this ref. No Git history, compiled binaries,
local secrets, untracked files or working tree modifications are included.
Restore: extract, then follow docs/LOCAL_BUILD_DEPLOY.md and run build-local.ps1.
"@ | Set-Content -LiteralPath (Join-Path $out "$folder.SourceInfo.txt") -Encoding utf8
Write-Host "SOURCE ZIP CREATED: $zip" -ForegroundColor Green
Write-Host "Commit: $commit"
Write-Host "SHA256: $hash"
