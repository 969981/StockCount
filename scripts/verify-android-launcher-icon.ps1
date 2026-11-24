#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ApkPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$apk = (Resolve-Path -LiteralPath $ApkPath).Path
$candidates = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)
if ($env:LOCALAPPDATA) { $candidates += (Join-Path $env:LOCALAPPDATA 'Android/Sdk') }
$programFiles86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
if ($programFiles86) { $candidates += (Join-Path $programFiles86 'Android/android-sdk') }
$aapt = $null
foreach ($sdk in $candidates) {
  if ([string]::IsNullOrWhiteSpace($sdk)) { continue }
  $toolsDir = Join-Path $sdk 'build-tools'
  if (!(Test-Path -LiteralPath $toolsDir)) { continue }
  $aapt = Get-ChildItem -LiteralPath $toolsDir -Recurse -File -Filter 'aapt2.exe' |
    Sort-Object FullName -Descending | Select-Object -First 1
  if ($aapt) { break }
}
if (!$aapt) { throw 'Android SDK aapt2.exe is required to verify the actual packaged launcher icon.' }
function Read-Aapt([string[]]$Arguments) {
  $result = & $aapt.FullName @Arguments
  if ($LASTEXITCODE -ne 0) { throw "aapt2 failed: $($Arguments -join ' ')" }
  return ($result -join [Environment]::NewLine)
}
$manifest = Read-Aapt @('dump','xmltree',$apk,'--file','AndroidManifest.xml')
$resources = Read-Aapt @('dump','resources',$apk)
foreach ($icon in @('appicon','appicon_round')) {
  $name = [regex]::Escape($icon)
  $resource = [regex]::Match($resources, "(?im)\bresource\s+(0x[0-9a-f]+)\s+(?:[^\s:]+:)?mipmap/$name(?=\s|$)")
  if (!$resource.Success) { throw "Packaged resource mipmap/$icon missing" }
  $id = $resource.Groups[1].Value
  $attribute = if ($icon -eq 'appicon') { 'icon' } else { 'roundIcon' }
  if ($manifest -notmatch "(?im)A:\s+[^\r\n]*?:$attribute\([^\r\n]*?\)\s*=\s*@$id\b") {
    throw "Packaged AndroidManifest does not bind android:$attribute to mipmap/$icon ($id)"
  }
}
Add-Type -AssemblyName System.IO.Compression
$zip = [IO.Compression.ZipFile]::OpenRead($apk)
try {
  $names = @($zip.Entries | ForEach-Object { $_.FullName })
  foreach ($icon in @('appicon','appicon_round')) {
    $name = [regex]::Escape($icon)
    $adaptive = @($names | Where-Object { $_ -match "^res/mipmap-anydpi(?:-v\d+)?/$name\.xml$" })
    if ($adaptive.Count -eq 0) { throw "Packaged adaptive XML for $icon missing" }
    foreach ($path in $adaptive) {
      $xml = Read-Aapt @('dump','xmltree',$apk,'--file',$path)
      foreach ($element in @('adaptive-icon','background','foreground')) {
        if ($xml -notmatch "(?m)E:\s+$element(?:\s|$)") { throw "Adaptive icon $path missing $element" }
      }
    }
    if (!@($names | Where-Object { $_ -match "^res/mipmap-[^/]+/$name\.png$" }).Count) {
      throw "Packaged fallback PNG for $icon missing"
    }
  }
} finally { $zip.Dispose() }
Write-Host "Verified APK launcher: appicon + appicon_round, adaptive foreground/background and fallback PNGs: $apk" -ForegroundColor Green
