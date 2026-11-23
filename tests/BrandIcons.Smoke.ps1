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
if ($apk.OuterXml -ne $masterPda.OuterXml) { throw 'Android app icon is not the PDA brand master' }
if ($win.OuterXml -ne $masterServer.OuterXml) { throw 'Windows Tauri icon is not the server brand master' }
Write-Host 'All 12 canonical/preset SVG files are well-formed, size-correct, and platform bindings match.' -ForegroundColor Green
