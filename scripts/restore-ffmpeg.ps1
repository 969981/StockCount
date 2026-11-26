# Restores the repository-locked Windows x64 build tool, not a system installation.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root 'tools/ffmpeg.lock.json') -Raw | ConvertFrom-Json
if (!$IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw 'The pinned FFmpeg build tool targets Windows x64, the supported PDA build host.'
}
$cache = Join-Path $root ('.tools/ffmpeg/' + $manifest.version)
$exe = Join-Path $cache 'ffmpeg.exe'
$stamp = Join-Path $cache 'verified.json'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
# Serialize restores across concurrent MSBuild processes.
$guard = $null
for ($attempt=0; $attempt -lt 240; $attempt++) {
    try { $guard = [IO.File]::Open((Join-Path $cache 'restore.lock'), 'OpenOrCreate', 'ReadWrite', 'None'); break }
    catch [IO.IOException] { Start-Sleep -Milliseconds 500 }
}
if ($null -eq $guard) { throw 'Timed out waiting for the project FFmpeg restore lock.' }
try {
    $valid = $false
    if ((Test-Path -LiteralPath $exe) -and (Test-Path -LiteralPath $stamp)) {
        try {
            $record = Get-Content -LiteralPath $stamp -Raw | ConvertFrom-Json
            $valid = $record.archiveSha256 -eq $manifest.sha256 -and
                $record.exeSha256 -eq (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        } catch { $valid = $false }
    }
    if (!$valid) {
        $zipPath = Join-Path $cache ('download-' + [guid]::NewGuid() + '.zip')
        $tempExe = Join-Path $cache ('ffmpeg-' + [guid]::NewGuid() + '.exe')
        try {
            Write-Host "Restoring pinned FFmpeg $($manifest.version) to project cache..."
            Invoke-WebRequest -Uri $manifest.url -OutFile $zipPath
            if ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -ne $manifest.sha256) {
                throw 'Pinned FFmpeg archive SHA-256 mismatch. Refusing to execute.'
            }
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
            try {
                $entry = $zip.GetEntry($manifest.archiveEntry)
                if ($null -eq $entry) { throw 'Pinned FFmpeg executable not found in verified archive.' }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$tempExe,$false)
            } finally { $zip.Dispose() }
            Move-Item -LiteralPath $tempExe -Destination $exe -Force
            @{ archiveSha256=$manifest.sha256; exeSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash } |
                ConvertTo-Json | Set-Content -LiteralPath $stamp -Encoding utf8
        } finally {
            Remove-Item -LiteralPath $zipPath,$tempExe -Force -ErrorAction SilentlyContinue
        }
    }
    $version = @(& $exe -version)
    if ($LASTEXITCODE -ne 0 -or $version.Count -eq 0 -or
        $version[0] -notmatch ('^ffmpeg version ' + [regex]::Escape($manifest.version) + '([\s-]|$)')) {
        throw 'Restored FFmpeg executable does not report the locked version.'
    }
    Write-Host $version[0]
    Write-Output $exe
} finally { $guard.Dispose() }
