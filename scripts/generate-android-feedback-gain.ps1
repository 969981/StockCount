param([switch]$Force)
$ErrorActionPreference = 'Stop'
$raw = Join-Path $PSScriptRoot '../src/StockCount.Pad/Platforms/Android/Resources/raw'
$sourceHashes = @{
  success = '7A301719C9845275C0108AFC536FD60D324BEE2E6201AF2006FFABF169E8AE57'
  failure = '4AB5DCA5C7DF319B8D597A6DB02B22845AD464A9032DA094B993ABDAD4927C3D'
}
$encoder = $null
foreach ($kind in @('success','failure')) {
  $source = Get-Item -LiteralPath (Join-Path $raw "scan_$kind.ogg")
  if ((Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash -ne $sourceHashes[$kind]) {
    throw "Original feedback OGG hash changed: $kind"
  }
  $recipeTime = (Get-Item -LiteralPath $PSCommandPath).LastWriteTimeUtc
  foreach ($step in 5..12) {
    $name = 'scan_{0}_g{1:D2}.wav' -f $kind,$step
    $destination = Join-Path $raw $name
    if (!$Force -and (Test-Path -LiteralPath $destination)) {
      $existing = Get-Item -LiteralPath $destination
      if ($existing.Length -gt 44 -and $existing.LastWriteTimeUtc -ge $source.LastWriteTimeUtc -and
          $existing.LastWriteTimeUtc -ge $recipeTime) { continue }
    }
    if ($null -eq $encoder) {
      $encoder = Get-Command ffmpeg -ErrorAction SilentlyContinue
      if ($null -eq $encoder) { throw 'Install FFmpeg (with alimiter latency support) on PATH, then run scripts/generate-android-feedback-gain.ps1.' }
    }
    $gain = ($step / 4.0).ToString('0.00',[Globalization.CultureInfo]::InvariantCulture)
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ("stockcount-" + [guid]::NewGuid() + ".wav")
    try {
      & $encoder.Source -v error -nostdin -y -i $source.FullName -af "volume=$gain,alimiter=limit=0.97:attack=2:release=25:level=false:latency=true" -ar 11025 -ac 1 -map_metadata -1 -c:a pcm_s16le $temporary
      if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $temporary)) { throw "FFmpeg failed: $name" }
      Move-Item -LiteralPath $temporary -Destination $destination -Force
      Write-Host "Generated $name ($gain x, limited 0.97 full scale)"
    } finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
  }
}
