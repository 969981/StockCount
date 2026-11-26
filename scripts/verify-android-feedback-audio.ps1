param([Parameter(Mandatory=$true)][string]$ApkPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $ApkPath).Path)
try {
    foreach ($name in @('scan_success.wav','scan_failure.wav','scan_read.wav')) {
        $entry = $zip.GetEntry("res/raw/$name")
        if ($null -eq $entry) { throw "Missing native audio resource: $name" }
        $expected = (Get-FileHash (Join-Path $PSScriptRoot "../docs/pda/audio-feedback/$name") -Algorithm SHA256).Hash
        $stream = $entry.Open()
        try { $actual = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash } finally { $stream.Dispose() }
        if ($actual -ne $expected) { throw "Audio payload mismatch: $name" }
        Write-Host "$name verified in APK ($($entry.Length) bytes)"
    }
} finally { $zip.Dispose() }
