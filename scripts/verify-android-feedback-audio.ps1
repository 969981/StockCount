param([Parameter(Mandatory=$true)][string]$ApkPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../tools/feedback-audio.lock.json') -Raw | ConvertFrom-Json -AsHashtable
$expectedHashes = @{

    'scan_success.ogg' = '7A301719C9845275C0108AFC536FD60D324BEE2E6201AF2006FFABF169E8AE57'
    'scan_failure.ogg' = '4AB5DCA5C7DF319B8D597A6DB02B22845AD464A9032DA094B993ABDAD4927C3D'
    'scan_read.wav' = (Get-FileHash (Join-Path $PSScriptRoot '../docs/pda/audio-feedback/scan_read.wav') -Algorithm SHA256).Hash
}
$raw = Join-Path $PSScriptRoot '../src/StockCount.Pad/Platforms/Android/Resources/raw'
foreach ($kind in @('success','failure')) {
    foreach ($step in 5..12) {
        $name = 'scan_{0}_g{1:D2}.wav' -f $kind,$step
        $file = Join-Path $raw $name
        $bytes = [IO.File]::ReadAllBytes($file)
        if ($bytes.Length -lt 44 -or [Text.Encoding]::ASCII.GetString($bytes,0,4) -ne 'RIFF' -or
            [Text.Encoding]::ASCII.GetString($bytes,8,4) -ne 'WAVE') { throw "Invalid gain WAV: $name" }
        $offset = 12
        $samples = 0
        $peak = 0
        $formatValid = $false
        while ($offset + 8 -le $bytes.Length) {
            $id = [Text.Encoding]::ASCII.GetString($bytes,$offset,4)
            $size = [BitConverter]::ToUInt32($bytes,$offset+4)
            $start = $offset+8
            if ($size -gt $bytes.Length-$start) { throw "Invalid WAV chunk: $name" }
            if ($id -eq 'fmt ' -and $size -ge 16) {
                $formatValid = [BitConverter]::ToUInt16($bytes,$start) -eq 1 -and
                    [BitConverter]::ToUInt16($bytes,$start+2) -eq 1 -and
                    [BitConverter]::ToUInt32($bytes,$start+4) -eq 11025 -and
                    [BitConverter]::ToUInt16($bytes,$start+14) -eq 16
            }
            if ($id -eq 'data') {
                if ($size % 2 -ne 0) { throw "Invalid PCM alignment: $name" }
                $samples = $size / 2
                for ($i=$start; $i -lt $start+$size; $i+=2) {
                    $peak = [Math]::Max($peak,[Math]::Abs([int][BitConverter]::ToInt16($bytes,$i)))
                }
            }
            $offset = $start + $size + ($size % 2)
        }
        $expectedMs = if ($kind -eq 'success') { 626 } else { 840 }
        $actualMs = $samples * 1000.0 / 11025
        if (!$formatValid -or [Math]::Abs($actualMs-$expectedMs) -gt 40 -or $peak -gt 32113 -or $peak -eq 0) {
            throw "Gain WAV format/duration/peak invalid: $name ($actualMs ms, peak $peak)"
        }
        if (!$manifest.files.Contains($name)) { throw "Missing audio lock: $name" }
        $expectedHashes[$name] = $manifest.files[$name].sha256
    }
}
if ($manifest.files.Count -ne 19 -or $expectedHashes.Count -ne 19) { throw 'Expected 19 locked audio files' }
foreach ($name in $expectedHashes.Keys) {
    if (!$manifest.files.Contains($name) -or $expectedHashes[$name] -ne $manifest.files[$name].sha256) { throw "Audio lock mismatch: $name" }
}
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $ApkPath).Path)
try {
    foreach ($name in ($expectedHashes.Keys | Sort-Object)) {
        $source = Join-Path $PSScriptRoot "../src/StockCount.Pad/Platforms/Android/Resources/raw/$name"
        $expected = $expectedHashes[$name]
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expected) { throw "Source audio mismatch: $name" }
        if ((Get-Item -LiteralPath $source).Length -ne $manifest.files[$name].bytes) { throw "Source length mismatch: $name" }
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
