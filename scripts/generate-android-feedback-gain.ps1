param([switch]$Force)
# Optional maintainer tool only. Ordinary APK builds use committed, hash-locked WAVs.
# Review regenerated audio and explicitly update tools/feedback-audio.lock.json before committing.
$ErrorActionPreference = 'Stop'
if (!$Force) { throw 'Prebuilt gain WAVs are checked in. Use -Force only for intentional audio maintenance, then review and update tools/feedback-audio.lock.json.' }
$raw = Join-Path $PSScriptRoot '../src/StockCount.Pad/Platforms/Android/Resources/raw'
$sourceHashes = @{
  success = '7A301719C9845275C0108AFC536FD60D324BEE2E6201AF2006FFABF169E8AE57'
  failure = '4AB5DCA5C7DF319B8D597A6DB02B22845AD464A9032DA094B993ABDAD4927C3D'
}
$root = Split-Path -Parent $PSScriptRoot
$lock = Join-Path $root 'tools/ffmpeg.lock.json'
$recipe = ((Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash + ':' +
           (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash + ':' +
           (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'restore-ffmpeg.ps1') -Algorithm SHA256).Hash)
$stamp = Join-Path $raw '.gain-build.json'
$previous = $null
if (!$Force -and (Test-Path -LiteralPath $stamp)) {
    try { $previous = Get-Content -LiteralPath $stamp -Raw | ConvertFrom-Json -AsHashtable } catch { $previous = $null }
}
$hashes = @{}
$encoder = $null
foreach ($kind in @('success','failure')) {
  $source = Get-Item -LiteralPath (Join-Path $raw "scan_$kind.ogg")
  if ((Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash -ne $sourceHashes[$kind]) {
    throw "Original feedback OGG hash changed: $kind"
  }
  foreach ($step in 5..12) {
    $name = 'scan_{0}_g{1:D2}.wav' -f $kind,$step
    $destination = Join-Path $raw $name
    if ($null -ne $previous -and $previous.recipe -eq $recipe -and $null -ne $previous.files -and
        (Test-Path -LiteralPath $destination)) {
      $actual = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
      if ($previous.files[$name] -eq $actual) { $hashes[$name] = $actual; continue }
    }
    if ($null -eq $encoder) { $encoder = & (Join-Path $PSScriptRoot 'restore-ffmpeg.ps1') }
    $gain = ($step / 4.0).ToString('0.00',[Globalization.CultureInfo]::InvariantCulture)
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ("stockcount-" + [guid]::NewGuid() + ".wav")
    try {
      & $encoder -v error -nostdin -y -i $source.FullName -af "volume=$gain,alimiter=limit=0.97:attack=2:release=25:level=false:latency=true" -ar 11025 -ac 1 -map_metadata -1 -c:a pcm_s16le $temporary
      if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $temporary)) { throw "FFmpeg failed: $name" }
      Move-Item -LiteralPath $temporary -Destination $destination -Force
      $hashes[$name] = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
      Write-Host "Generated $name ($gain x, limited 0.97 full scale)"
    } finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
  }
}
@{ recipe=$recipe; files=$hashes } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $stamp -Encoding utf8
