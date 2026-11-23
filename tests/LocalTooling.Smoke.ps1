#requires -Version 7.0
# Runs on windows-latest, no .NET or Android workload required.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) ("stockcount-localtools-test-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp -Force | Out-Null

function New-TestPortable([string]$Version) {
    $root = Join-Path $temp "zip-root"
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    $folder = Join-Path $root "StockCount-Server-v$Version-Windows-x64-Portable"
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $names = @('stockcount-server-console.exe','StockCount.Api.exe','Start-StockCount.cmd','README-PORTABLE.txt')
    foreach ($name in $names) {
        "$Version fake fixture $name" | Set-Content -LiteralPath (Join-Path $folder $name) -Encoding ascii
    }
    $zip = Join-Path $temp "portable-$Version.zip"
    Compress-Archive -LiteralPath $folder -DestinationPath $zip
    return $zip
}

try {
    $destination = Join-Path $temp "deployed-StockCount"
    $v1 = New-TestPortable '1.0.0'
    & (Join-Path $repo 'scripts/deploy-local.ps1') -Target WindowsPortable -PackagePath $v1 -Destination $destination

    'valuable-station-log-data' | Set-Content -LiteralPath (Join-Path $destination 'server-logs.db')
    'wal-file-keep' | Set-Content -LiteralPath (Join-Path $destination 'server-logs.db-wal')
    'shm-file-keep' | Set-Content -LiteralPath (Join-Path $destination 'server-logs.db-shm')
    'custom-config-stays' | Set-Content -LiteralPath (Join-Path $destination 'my-settings.json')

    $v2 = New-TestPortable '1.1.0'
    & (Join-Path $repo 'scripts/deploy-local.ps1') -Target WindowsPortable -PackagePath $v2 -Destination $destination
    if ((Get-Content (Join-Path $destination 'stockcount-server-console.exe') -Raw) -notmatch '1.1.0') {
        throw 'Portable deploy did not update the desktop EXE'
    }
    foreach ($name in @('server-logs.db','server-logs.db-wal','server-logs.db-shm','my-settings.json')) {
        if (-not (Test-Path (Join-Path $destination $name))) { throw "Upgrade deleted $name" }
    }
    if ((Get-Content (Join-Path $destination 'server-logs.db') -Raw) -notmatch 'valuable-station-log-data') {
        throw 'Portable deployment damaged SQLite log contents'
    }
    $backups = @(Get-ChildItem -LiteralPath $destination -Directory -Filter 'backup-binaries-*')
    if ($backups.Count -ne 1) { throw "Expected one backup of previous binaries, got $($backups.Count)" }
    if ((Get-Content (Join-Path $backups[0].FullName 'StockCount.Api.exe') -Raw) -notmatch '1.0.0') {
        throw 'Previous API binary backup is invalid'
    }

    $sourceOut = Join-Path $repo 'dist\ci-source-test'
    & (Join-Path $repo 'scripts/package-source.ps1') -Version '0.2.0-local' -Ref HEAD -OutputDirectory $sourceOut
    $sourceZip = Join-Path $sourceOut 'StockCount-Source-v0.2.0-local.zip'
    if (-not (Test-Path $sourceZip)) { throw 'Git source ZIP was not created' }
    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::OpenRead($sourceZip)
    try {
        $files = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
        if ($files -notcontains 'StockCount-Source-v0.2.0-local/scripts/build-local.ps1') {
            throw 'Build script missing from source ZIP'
        }
        if (@($files | Where-Object { $_ -match '/(node_modules|\.git|bin|obj)/' }).Count -gt 0) {
            throw 'Forbidden artifacts found in Git source archive'
        }
    } finally { $archive.Dispose() }
    if ((Get-Item -LiteralPath $sourceZip).Length -lt 10240) { throw 'Source ZIP unexpectedly small' }
    Write-Host 'SUCCESS: portable upgrade preserves SQLite, previous binaries backup and Git source ZIP integrity.' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
