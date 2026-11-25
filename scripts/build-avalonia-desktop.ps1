#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory = 'publish/avalonia-desktop'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') { throw 'Windows x64 is required.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not [IO.Path]::IsPathFullyQualified($OutputDirectory)) { $OutputDirectory = Join-Path $root $OutputDirectory }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$names = @('StockCount.Server.Desktop.exe','StockCount.Api.exe')
# Do not delete a previous loose-file build, or mistake a live install for a build directory.
if (Test-Path -LiteralPath $OutputDirectory) {
    $dir = Get-Item -LiteralPath $OutputDirectory -Force
    if (-not $dir.PSIsContainer -or $dir.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Invalid publish directory.' }
    $unexpected = @(Get-ChildItem -LiteralPath $OutputDirectory -Force | Where-Object {
        $_.PSIsContainer -or $_.Name -notin $names -or $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)
    })
    if ($unexpected.Count) { throw "Publish directory contains old loose files or other data. Rename it or select a new -OutputDirectory: $OutputDirectory" }
}
$stage = Join-Path ([IO.Path]::GetTempPath()) ('stockcount-bundle-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Push-Location $root
try {
    dotnet test tests/StockCount.Server.Hosting.Tests/StockCount.Server.Hosting.Tests.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Hosting tests failed.' }
    $common = @('-c',$Configuration,'-r','win-x64','--self-contained','true',
        '-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false','-p:DebugType=embedded')
    $apiStage = Join-Path $stage 'api'
    $uiStage = Join-Path $stage 'desktop'
    dotnet publish src/StockCount.Api/StockCount.Api.csproj @common -o $apiStage
    if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }
    dotnet publish src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj @common -o $uiStage
    if ($LASTEXITCODE -ne 0) { throw 'Avalonia desktop publish failed.' }
    # API has always used environment-based configuration supplied by the manager;
    # standalone default appsettings and IIS web.config were not shipped previously.
    # Fail on any other loose runtime asset; never silently drop native dependencies.
    foreach ($item in @(@{Path=$uiStage;Exe=$names[0];Optional=@()},@{Path=$apiStage;Exe=$names[1];Optional=@('appsettings.json','web.config')})) {
        $exe = Join-Path $item.Path $item.Exe
        if (!(Test-Path -LiteralPath $exe -PathType Leaf) -or (Get-Item -LiteralPath $exe).Length -lt 1MB) {
            throw "Single-file executable missing or suspiciously small: $exe"
        }
        $extras = @(Get-ChildItem -LiteralPath $item.Path -Recurse -File -Force | Where-Object {
            $relative = [IO.Path]::GetRelativePath($item.Path,$_.FullName)
            $relative -ne $item.Exe -and $relative -notin $item.Optional -and $_.Extension -ne '.pdb'
        })
        if ($extras.Count) { throw "Unexpected unbundled publish dependency: $($extras[0].FullName)" }
    }
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $changed = [Collections.Generic.List[string]]::new()
    try {
        foreach ($name in $names) {
            $target = Join-Path $OutputDirectory $name
            if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $stage "old-$name") }
            $changed.Add($name)
            $source = Join-Path $(if ($name -eq $names[0]) { $uiStage } else { $apiStage }) $name
            Copy-Item -LiteralPath $source -Destination $target -Force
            if ((Get-FileHash $source).Hash -ne (Get-FileHash $target).Hash) { throw "Bundle copy hash mismatch: $name" }
        }
    } catch {
        foreach ($name in $changed) {
            $target = Join-Path $OutputDirectory $name
            $previous = Join-Path $stage "old-$name"
            if (Test-Path -LiteralPath $previous) { Copy-Item -LiteralPath $previous -Destination $target -Force }
            elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
        }
        throw
    }
    Write-Host "Avalonia single-file desktop + API: $OutputDirectory"
    Get-ChildItem -LiteralPath $OutputDirectory -File | Select-Object Name,Length | Format-Table
} finally {
    Pop-Location
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
