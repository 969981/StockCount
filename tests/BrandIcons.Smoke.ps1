#requires -Version 7.0
[CmdletBinding()] param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
foreach ($role in @('pda','server')) {
    $masterPath = Join-Path $root "assets/branding/$role.svg"
    [xml]$master = Get-Content -LiteralPath $masterPath -Raw
    if ($master.DocumentElement.GetAttribute('viewBox') -ne '0 0 512 512') {
        throw "$role master viewBox is not 512x512"
    }
    foreach ($size in @(64,128,256,512,1024)) {
        $path = Join-Path $root "assets/branding/variants/$role-$size.svg"
        [xml]$image = Get-Content -LiteralPath $path -Raw
        if ($image.DocumentElement.GetAttribute('width') -ne "$size" -or
            $image.DocumentElement.GetAttribute('height') -ne "$size" -or
            $image.DocumentElement.GetAttribute('viewBox') -ne '0 0 512 512') {
            throw "Incorrect SVG dimensions: $path"
        }
        $canonical = $master.OuterXml -replace 'width="512" height="512"', "width=`"$size`" height=`"$size`""
        if ($image.OuterXml -ne $canonical) {
            throw "Variant content differs from scalable master: $path"
        }
    }
}
[xml]$apk = Get-Content (Join-Path $root 'src/StockCount.Pad/Resources/AppIcon/appicon.svg') -Raw
[xml]$win = Get-Content (Join-Path $root 'desktop/server-console/assets/app-icon.svg') -Raw
[xml]$masterPda = Get-Content (Join-Path $root 'assets/branding/pda.svg') -Raw
[xml]$masterServer = Get-Content (Join-Path $root 'assets/branding/server.svg') -Raw
# Android now uses separate full-bleed background and transparent foreground.
[xml]$fg = Get-Content (Join-Path $root 'src/StockCount.Pad/Resources/AppIcon/appiconfg.svg') -Raw
foreach ($layer in @($apk,$fg)) {
    if ($layer.DocumentElement.GetAttribute('viewBox') -ne '0 0 512 512') { throw 'Invalid Android adaptive icon canvas' }
}
if ($apk.DocumentElement.InnerXml -notmatch 'width="512" height="512" fill="url\(#badge\)"') { throw 'Adaptive background must cover the canvas' }
if ($fg.DocumentElement.InnerXml -match 'url\(#badge\)' -or $fg.DocumentElement.InnerXml -notmatch '#0ABE71') { throw 'PDA foreground artwork is invalid' }
[xml]$pad = Get-Content (Join-Path $root 'src/StockCount.Pad/StockCount.Pad.csproj') -Raw
$icon = $pad.Project.ItemGroup.MauiIcon
if ($icon.Include -ne 'Resources/AppIcon/appicon.svg' -or
    $icon.ForegroundFile -ne 'Resources/AppIcon/appiconfg.svg' -or $icon.ForegroundScale -ne '0.75') { throw 'Android adaptive MauiIcon configuration missing' }
[xml]$manifest = Get-Content (Join-Path $root 'src/StockCount.Pad/Platforms/Android/AndroidManifest.xml') -Raw
$androidNs = 'http://schemas.android.com/apk/res/android'
if ($manifest.manifest.application.GetAttribute('icon',$androidNs) -ne '@mipmap/appicon' -or
    $manifest.manifest.application.GetAttribute('roundIcon',$androidNs) -ne '@mipmap/appicon_round') { throw 'Android launcher manifest binding missing' }
if ($win.OuterXml -ne $masterServer.OuterXml) { throw 'Windows Tauri icon is not the server brand master' }
Write-Host 'All 12 canonical/preset SVG files are well-formed, size-correct, and platform bindings match.' -ForegroundColor Green

# The Windows Avalonia icon is a tracked binary resource, including in source ZIPs.
$icoPath = Join-Path $root 'src/StockCount.Server.Desktop/Assets/server.ico'
$ico = [IO.File]::ReadAllBytes($icoPath)
if ($ico.Length -lt 1000 -or [BitConverter]::ToUInt16($ico,0) -ne 0 -or
    [BitConverter]::ToUInt16($ico,2) -ne 1) { throw 'Invalid ICO header' }
$count = [BitConverter]::ToUInt16($ico,4)
if ($count -lt 4 -or $ico.Length -lt (6 + 16 * $count)) { throw 'Invalid ICO image directory' }
$sizes = @()
for ($i = 0; $i -lt $count; $i++) {
    $offset = 6 + 16 * $i
    $width = if ($ico[$offset] -eq 0) { 256 } else { [int]$ico[$offset] }
    $height = if ($ico[$offset+1] -eq 0) { 256 } else { [int]$ico[$offset+1] }
    $length = [BitConverter]::ToUInt32($ico,$offset+8)
    $dataOffset = [BitConverter]::ToUInt32($ico,$offset+12)
    if ($width -ne $height -or $length -eq 0 -or
        $dataOffset -lt (6 + 16 * $count) -or ([long]$dataOffset + $length) -gt $ico.Length) {
        throw "Invalid ICO image entry $i"
    }
    $sizes += $width
}
foreach ($size in @(16,32,48,256)) {
    if ($sizes -notcontains $size) { throw "Tracked ICO missing size $size" }
}
Write-Host "Tracked Avalonia ICO verified: $($sizes -join ', ') pixels."
