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
$tracked = @(& git -C $root -c core.quotePath=false ls-tree -r --name-only $commit)
if ($LASTEXITCODE -ne 0 -or $tracked.Count -eq 0) { throw 'Selected commit contains no tracked files.' }

# Fail closed if secrets or compiled build output were accidentally committed.
# Filename checks do not replace a full content-level secret audit.
$forbidden = '(^|/)(\.git|node_modules|bin|obj|target|dist|publish|\.vs)(/|$)|(^|/)(server-logs\.db.*|server-settings\.json|appsettings\.Production\.json|\.env($|\.(?!example$))|[^/]+\.(pfx|p12|pem|key|keystore|jks|db|sqlite|sqlite3))$'
$bad = @($tracked | Where-Object { $_ -match $forbidden })
if ($bad.Count -gt 0) {
  throw "Refusing to archive tracked secrets/build outputs: $($bad[0..([Math]::Min($bad.Count-1,9))] -join ', ')"
}

# Baseline shared by legacy release tags and the current repository.
# Require newer components only for commits containing the modern solution layout.
$mustHave = @('README.md','StockCount.slnx',
  'src/StockCount.Pad/StockCount.Pad.csproj',
  'src/StockCount.Api/StockCount.Api.csproj',
  'desktop/server-console/src-tauri/Cargo.toml',
  'scripts/build-local.ps1','scripts/build-android-release.ps1',
  'scripts/get-app-version.ps1','scripts/set-app-version.ps1',
  'scripts/new-android-release-jks.ps1','scripts/configure-android-release-secrets.ps1',
  'scripts/publish-stockcount-release.ps1','scripts/package-source.ps1')

$isModernSnapshot = ($tracked -contains 'StockCount.Backend.slnx' -or
  $tracked -contains 'src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj')
if ($isModernSnapshot) {
  $mustHave += @(
    'StockCount.Backend.slnx',
    'src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj',
    'src/StockCount.Server.Desktop/App.axaml',
    'src/StockCount.Server.Desktop/MainWindow.axaml',
    'src/StockCount.Server.Desktop/ViewModels/MainViewModel.cs',
    'src/StockCount.Server.Hosting/StockCount.Server.Hosting.csproj',
    'src/StockCount.Server.Hosting/ServerSettings.cs',
    'src/StockCount.Server.Hosting/LogReader.cs',
    'src/StockCount.Api/Properties/launchSettings.json',
    'src/StockCount.Pad/Platforms/Android/AndroidManifest.xml',
    'scripts/build-avalonia-desktop.ps1',
    '.github/workflows/avalonia-server-desktop.yml',
    '.github/workflows/pda-android-build.yml'
  )
}
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
  # git archive can honor export-ignore; reject any incomplete source snapshot.
  $packedFiles = @($archive.Entries |
    Where-Object { $_.Name.Length -gt 0 } |
    ForEach-Object { $_.FullName.Replace('\','/') })
  $expectedFiles = @($tracked | ForEach-Object { "$folder/$_" })
  $missingFiles = @($expectedFiles | Where-Object { $packedFiles -notcontains $_ })
  if ($missingFiles.Count -gt 0) {
    throw "Archive missing $($missingFiles.Count) committed file(s), first: $($missingFiles[0]). Check export-ignore rules."
  }
  $unexpectedFiles = @($packedFiles | Where-Object { $expectedFiles -notcontains $_ })
  if ($unexpectedFiles.Count -gt 0) {
    throw "Archive contains unexpected source entry: $($unexpectedFiles[0])"
  }

  # Check all project paths declared in the included Visual Studio solutions.
  foreach ($solutionPath in @('StockCount.slnx', 'StockCount.Backend.slnx')) {
    if ($tracked -notcontains $solutionPath) { continue }
    $entry = $archive.GetEntry("$folder/$solutionPath")
    if ($null -eq $entry) { throw "Missing solution in source ZIP: $solutionPath" }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$sln = $reader.ReadToEnd() }
    finally { $reader.Dispose() }
    foreach ($project in @($sln.Solution.Project)) {
      $projectPath = ([string]$project.Path).Replace('\','/')
      if ([string]::IsNullOrWhiteSpace($projectPath) -or
          $projectPath.StartsWith('/') -or $projectPath.Contains('../')) {
        throw "Invalid project reference in $solutionPath : $projectPath"
      }
      if ($packedFiles -notcontains "$folder/$projectPath") {
        throw "Broken $solutionPath project reference in source ZIP: $projectPath"
      }
    }
  }

  $insecure = @($names | Where-Object { $_ -match '(^|/)(\.git|node_modules|dist)(/|$)' })
  if ($insecure.Count -gt 0) { throw "Archive contains disallowed path $($insecure[0])" }
} finally { $archive.Dispose() }

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$restoreInfo = if ($isModernSnapshot) {
  "Solutions: StockCount.slnx (API + Avalonia + MAUI Android), StockCount.Backend.slnx (backend-only).$([Environment]::NewLine)Avalonia build: scripts/build-avalonia-desktop.ps1.$([Environment]::NewLine)Tauri + Android build: scripts/build-local.ps1."
} else {
  "Legacy layout: follow the build scripts and solution files included in this historical commit."
}
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
Restore: extract, then follow docs/LOCAL_BUILD_DEPLOY.md.
$restoreInfo
"@ | Set-Content -LiteralPath (Join-Path $out "$folder.SourceInfo.txt") -Encoding utf8
Write-Host "SOURCE ZIP CREATED: $zip" -ForegroundColor Green
Write-Host "Commit: $commit"
Write-Host "SHA256: $hash"
