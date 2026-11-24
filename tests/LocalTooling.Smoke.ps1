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

    # Simulate a fully self-contained Avalonia portable (large fake EXEs and
    # an extra dependency) without requiring a real .NET compilation.
    $avaloniaFake = Join-Path $temp 'avalonia-publish'
    $avaloniaDest = Join-Path $temp 'deployed-Avalonia'
    $avaloniaPackages = Join-Path $temp 'avalonia-zips'
    New-Item -ItemType Directory -Force -Path $avaloniaFake | Out-Null
    $bytes = New-Object byte[] 1048577
    [Array]::Fill($bytes, [byte]65)
    [IO.File]::WriteAllBytes((Join-Path $avaloniaFake 'StockCount.Server.Desktop.exe'), $bytes)
    [Array]::Fill($bytes, [byte]66)
    [IO.File]::WriteAllBytes((Join-Path $avaloniaFake 'StockCount.Api.exe'), $bytes)
    New-Item -ItemType Directory -Force -Path (Join-Path $avaloniaFake 'runtimes/win-x64/native') | Out-Null
    'managed-dependency-v1' | Set-Content (Join-Path $avaloniaFake 'runtimes/win-x64/native/host.dll')
    $version = '0.2.0-local'
    & (Join-Path $repo 'scripts/package-avalonia-portable.ps1') -Version $version -InputDirectory $avaloniaFake -OutputDirectory $avaloniaPackages
    $avaloniaZip = Join-Path $avaloniaPackages "StockCount-Avalonia-v$version-Windows-x64-Portable.zip"
    if (!(Test-Path $avaloniaZip)) { throw 'Avalonia Portable ZIP missing from packager' }

    & (Join-Path $repo 'scripts/deploy-local.ps1') -Target AvaloniaPortable -PackagePath $avaloniaZip -Destination $avaloniaDest
    'important API config' | Set-Content (Join-Path $avaloniaDest 'server-settings.json')
    'sqlite history' | Set-Content (Join-Path $avaloniaDest 'server-logs.db')
    'wal preserved' | Set-Content (Join-Path $avaloniaDest 'server-logs.db-wal')
    'shm preserved' | Set-Content (Join-Path $avaloniaDest 'server-logs.db-shm')
    [Array]::Fill($bytes, [byte]67)
    [IO.File]::WriteAllBytes((Join-Path $avaloniaFake 'StockCount.Api.exe'), $bytes)
    'managed-dependency-v2' | Set-Content (Join-Path $avaloniaFake 'runtimes/win-x64/native/host.dll')
    & (Join-Path $repo 'scripts/package-avalonia-portable.ps1') -Version $version -InputDirectory $avaloniaFake -OutputDirectory $avaloniaPackages
    & (Join-Path $repo 'scripts/deploy-local.ps1') -Target AvaloniaPortable -PackagePath $avaloniaZip -Destination $avaloniaDest
    if ((Get-Content (Join-Path $avaloniaDest 'runtimes/win-x64/native/host.dll') -Raw) -notmatch 'v2') {
      throw 'Avalonia upgrade lost one of the nested .NET runtime dependencies'
    }
    foreach ($name in @('server-settings.json','server-logs.db','server-logs.db-wal','server-logs.db-shm')) {
      if (!(Test-Path (Join-Path $avaloniaDest $name))) { throw "Avalonia upgrade deleted $name" }
    }
    if ((Get-Content (Join-Path $avaloniaDest 'server-settings.json') -Raw) -notmatch 'important API config') {
      throw 'Avalonia upgrade overwrote settings'
    }
    $backupsAvalonia = @(Get-ChildItem -LiteralPath $avaloniaDest -Directory -Filter 'backup-binaries-*')
    if ($backupsAvalonia.Count -ne 1) { throw 'Avalonia upgrade did not back up previous executable files' }
    if (!(Test-Path (Join-Path $backupsAvalonia[0].FullName 'runtimes/win-x64/native/host.dll'))) {
      throw 'Avalonia backup missing nested runtime dependency'
    }
    'must-not-ship' | Set-Content (Join-Path $avaloniaFake 'server-settings.json')
    $rejectedSensitive = $false
    try {
      & (Join-Path $repo 'scripts/package-avalonia-portable.ps1') -Version $version -InputDirectory $avaloniaFake -OutputDirectory $avaloniaPackages
    } catch {
      $rejectedSensitive = $_.Exception.Message -match 'user data / signing material'
    }
    Remove-Item (Join-Path $avaloniaFake 'server-settings.json') -Force
    if (-not $rejectedSensitive) { throw 'Avalonia ZIP packager accepted live user settings' }

    $sourceOut = Join-Path $repo 'dist\ci-source-test'
    $sourceCommit = (& git -C $repo rev-parse HEAD).Trim()
    $sourceShort = (& git -C $repo rev-parse --short=7 $sourceCommit).Trim()
    $sourceFolder = "StockCount-Source-$sourceShort"
    # Reproduce the Chinese Windows/Anaconda terminal environment: Git emits
    # UTF-8 filenames while PowerShell's native output decoder is CP936.
    $savedConsoleEncoding = [Console]::OutputEncoding
    $savedOutputEncoding = $OutputEncoding
    try {
        [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
        [Console]::OutputEncoding = [System.Text.Encoding]::GetEncoding(936)
        $OutputEncoding = [System.Text.Encoding]::GetEncoding(936)
        & (Join-Path $repo 'scripts/package-source.ps1') -OutputDirectory $sourceOut
        if ([Console]::OutputEncoding.CodePage -ne 936 -or $OutputEncoding.CodePage -ne 936) {
            throw 'Source packager changed the calling terminal encoding'
        }
    } finally {
        [Console]::OutputEncoding = $savedConsoleEncoding
        $OutputEncoding = $savedOutputEncoding
    }
    $sourceZip = Join-Path $sourceOut "$sourceFolder.zip"
    $info = Get-Content -LiteralPath (Join-Path $sourceOut "$sourceFolder.SourceInfo.txt") -Raw
    if (!$info.Contains("Commit: $sourceCommit") -or !$info.Contains("Short commit: $sourceShort")) {
        throw 'SourceInfo does not record the actual archived commit'
    }
    $digest = (Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksum = (Get-Content -LiteralPath (Join-Path $sourceOut "$sourceFolder.SHA256SUMS.txt") -Raw).Trim()
    if ($checksum -ne "$digest  $sourceFolder.zip") { throw 'Source checksum filename or digest mismatch' }
    # Explicit commit and old -Version callers must produce the same archive name and bytes.
    & (Join-Path $repo 'scripts/package-source.ps1') -Ref $sourceCommit -Version '0.2.0-local' -OutputDirectory $sourceOut
    if ((Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $digest) {
        throw 'Source ZIP is not reproducible for the same commit'
    }
    if (@(Get-ChildItem -LiteralPath $sourceOut -File -Filter '*.zip').Count -ne 1) {
        throw 'Legacy version label changed the source ZIP name'
    }
    if (-not (Test-Path $sourceZip)) { throw 'Git source ZIP was not created' }
    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::OpenRead($sourceZip)
    try {
        $files = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
        # A successful modern source snapshot must be independently buildable:
        # both solution layouts, Avalonia + Hosting, Android MAUI, Tauri and CI.
        $mustContain = @(
            'StockCount.slnx',
            'StockCount.Backend.slnx',
            '00-StockCount-知识库主页.md',
            'src/StockCount.Api/StockCount.Api.csproj',
            'src/StockCount.Api/Properties/launchSettings.json',
            'src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj',
            'src/StockCount.Server.Desktop/App.axaml',
            'src/StockCount.Server.Desktop/MainWindow.axaml',
            'src/StockCount.Server.Desktop/ViewModels/MainViewModel.cs',
            'src/StockCount.Server.Hosting/StockCount.Server.Hosting.csproj',
            'src/StockCount.Server.Hosting/ServerSettings.cs',
            'src/StockCount.Server.Hosting/LogReader.cs',
            'src/StockCount.Pad/StockCount.Pad.csproj',
            'src/StockCount.Pad/Platforms/Android/AndroidManifest.xml',
            'desktop/server-console/src-tauri/Cargo.toml',
            'scripts/build-local.ps1',
            'scripts/build-avalonia-desktop.ps1',
            'scripts/package-avalonia-portable.ps1',
            'scripts/deploy-avalonia-portable.ps1',
            'scripts/package-source.ps1',
            '.github/workflows/avalonia-server-desktop.yml',
            '.github/workflows/pda-android-build.yml'
        )
        foreach ($relative in $mustContain) {
            $entry = "$sourceFolder/$relative"
            if ($files -notcontains $entry) {
                throw "Modern source ZIP is missing a required build input: $relative"
            }
        }

        if ($files -notcontains "$sourceFolder/scripts/build-local.ps1") {
            throw 'Build script missing from source ZIP'
        }
        if (@($files | Where-Object { $_ -match '/(node_modules|\.git|bin|obj)/' }).Count -gt 0) {
            throw 'Forbidden artifacts found in Git source archive'
        }
    } finally { $archive.Dispose() }
    if ((Get-Item -LiteralPath $sourceZip).Length -lt 10240) { throw 'Source ZIP unexpectedly small' }
    # Release packaging must fail closed when signing credentials are absent.
    $fixtureKey = Join-Path $temp 'fake-android.jks'
    'not-an-actual-keystore' | Set-Content -LiteralPath $fixtureKey
    $previousStorePass = $env:STOCKCOUNT_ANDROID_STORE_PASS
    $previousKeyPass = $env:STOCKCOUNT_ANDROID_KEY_PASS
    Remove-Item Env:STOCKCOUNT_ANDROID_STORE_PASS, Env:STOCKCOUNT_ANDROID_KEY_PASS -ErrorAction SilentlyContinue
    $refused = $false
    try {
        try {
            & (Join-Path $repo 'scripts/build-android-release.ps1') -KeyStore $fixtureKey -KeyAlias stockcount -Version 0.2.0-local -OutputDirectory $temp
        } catch {
            if ($_.Exception.Message -notmatch 'STOCKCOUNT_ANDROID_STORE_PASS') { throw }
            $refused = $true
        }
    } finally {
        if ($null -ne $previousStorePass) { $env:STOCKCOUNT_ANDROID_STORE_PASS = $previousStorePass }
        if ($null -ne $previousKeyPass) { $env:STOCKCOUNT_ANDROID_KEY_PASS = $previousKeyPass }
    }
    if (-not $refused) { throw 'Release signing script incorrectly allowed missing credentials' }
    # New key generation and release dispatch must support side-effect-free
    # WhatIf runs even when JDK/GitHub CLI are unavailable on a clean runner.
    $freshJks = Join-Path $temp 'stockcount-release.jks'
    & (Join-Path $repo 'scripts/new-android-release-jks.ps1') -OutputPath $freshJks -WhatIf
    if (Test-Path -LiteralPath $freshJks) { throw 'JKS generator modified files in WhatIf mode.' }

    $rejectedInsideCheckout = $false
    try {
        & (Join-Path $repo 'scripts/new-android-release-jks.ps1') -OutputPath (Join-Path $repo 'unsafe-private-key.jks') -WhatIf
    } catch {
        $rejectedInsideCheckout = $_.Exception.Message -match 'OUTSIDE'
    }
    if (-not $rejectedInsideCheckout) { throw 'JKS generator did not reject in-repo private key location.' }

    $fakeJks = Join-Path $temp 'fake-release-key.jks'
    [IO.File]::WriteAllBytes($fakeJks, [byte[]]::new(2048))
    & (Join-Path $repo 'scripts/configure-android-release-secrets.ps1') -KeyStore $fakeJks -WhatIf
    & (Join-Path $repo 'scripts/publish-stockcount-release.ps1') -Version '0.2.0-preview.3' -WhatIf

    $invalidVersionRejected = $false
    try {
        & (Join-Path $repo 'scripts/publish-stockcount-release.ps1') -Version 'bad;version' -WhatIf
    } catch {
        $invalidVersionRejected = $true
    }
    if (-not $invalidVersionRejected) { throw 'Publisher allowed unsafe release version.' }
    # All app metadata must resolve to a single Tauri-defined version.
    $actualVersion = & (Join-Path $repo 'scripts/get-app-version.ps1')
    if ($actualVersion -notmatch '^\d+\.\d+\.\d+') { throw "Invalid project version $actualVersion" }

    # Test the mutating synchronizer inside a private fixture, never in Git.
    $fixture = Join-Path $temp 'version-project'
    $filesToCopy = @(
      'scripts/get-app-version.ps1',
      'scripts/set-app-version.ps1',
      'desktop/server-console/src-tauri/tauri.conf.json',
      'desktop/server-console/src-tauri/Cargo.toml',
      'desktop/server-console/package.json',
      'src/StockCount.Pad/StockCount.Pad.csproj'
    )
    foreach ($relative in $filesToCopy) {
      $target = Join-Path $fixture $relative
      New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
      Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $target
    }
    $fixtureMaui = Get-Content (Join-Path $fixture 'src/StockCount.Pad/StockCount.Pad.csproj') -Raw
    $currentCode = [int]([regex]::Match($fixtureMaui, '<ApplicationVersion>(\d+)</ApplicationVersion>').Groups[1].Value)
    $fixtureSet = Join-Path $fixture 'scripts/set-app-version.ps1'
    $fixtureGet = Join-Path $fixture 'scripts/get-app-version.ps1'
    & $fixtureSet -Version '1.0.1' -AndroidVersionCode ($currentCode + 1) -WhatIf
    if ((& $fixtureGet) -ne $actualVersion) { throw 'Version -WhatIf unexpectedly modified project files' }
    & $fixtureSet -Version '1.0.1' -AndroidVersionCode ($currentCode + 1)
    if ((& $fixtureGet) -ne '1.0.1') { throw 'Version synchronization failed in fixture' }
    if ((Get-Content (Join-Path $fixture 'src/StockCount.Pad/StockCount.Pad.csproj') -Raw) -notmatch "<ApplicationVersion>$($currentCode + 1)</ApplicationVersion>") {
      throw 'Android versionCode was not updated by version setter'
    }
    if ((& (Join-Path $repo 'scripts/get-app-version.ps1')) -ne $actualVersion) {
      throw 'Version setter wrote outside its isolated fixture'
    }

    # Portable wrapper rejects an archive name from a DIFFERENT app version.
    $mismatchRejected = $false
    try {
      & (Join-Path $repo 'scripts/package-windows-portable.ps1') -Version '9.9.9' -OutputDirectory $temp
    } catch {
      $mismatchRejected = $_.Exception.Message -match 'does not match software version'
    }
    if (-not $mismatchRejected) { throw 'Portable packager accepted a misleading version label' }

    $desktopExe = Join-Path $repo 'desktop/server-console/src-tauri/target/release/stockcount-server-console.exe'
    if (-not (Test-Path -LiteralPath $desktopExe)) {
      $helpfulMissingExe = $false
      try {
        & (Join-Path $repo 'scripts/package-windows-portable.ps1') -OutputDirectory $temp
      } catch {
        $helpfulMissingExe = $_.Exception.Message -match 'build-local.ps1'
      }
      if (-not $helpfulMissingExe) { throw 'Missing-Tauri-exe error must show correct build command' }
    }
    Write-Host 'SUCCESS: Tauri and Avalonia portable upgrades preserve SQLite, previous binaries backup and Git source ZIP integrity, JKS dry runs and safe GitHub release dispatch, and app-version synchronization.' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
