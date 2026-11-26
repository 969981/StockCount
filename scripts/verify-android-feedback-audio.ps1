param([Parameter(Mandatory=$true)][string]$ApkPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$expectedHashes = @{
    'scan_success.ogg' = '7A301719C9845275C0108AFC536FD60D324BEE2E6201AF2006FFABF169E8AE57'
    'scan_failure.ogg' = '4AB5DCA5C7DF319B8D597A6DB02B22845AD464A9032DA094B993ABDAD4927C3D'
    'scan_read.wav' = (Get-FileHash (Join-Path $PSScriptRoot '../docs/pda/audio-feedback/scan_read.wav') -Algorithm SHA256).Hash
}
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $ApkPath).Path)
try {
    foreach ($name in @('scan_success.ogg','scan_failure.ogg','scan_read.wav')) {
        $source = Join-Path $PSScriptRoot "../src/StockCount.Pad/Platforms/Android/Resources/raw/$name"
        $expected = $expectedHashes[$name]
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expected) { throw "Source audio mismatch: $name" }
        $entry = $zip.GetEntry("res/raw/$name")
        if ($null -eq $entry) { throw "Missing native audio resource: $name" }
        $stream = $entry.Open()
        try { $actual = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash } finally { $stream.Dispose() }
        if ($actual -ne $expected) { throw "Audio payload mismatch: $name" }
        Write-Host "$name verified in APK ($($entry.Length) bytes) SHA256=$actual"
    }
    foreach ($name in @('scan_success.wav','scan_failure.wav')) {
        if ($null -ne $zip.GetEntry("res/raw/$name")) { throw "Unexpected legacy audio resource: $name" }
    }
} finally { $zip.Dispose() }
