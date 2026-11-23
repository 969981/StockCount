#requires -Version 5.1
# Build StockCount.Api as a standalone .NET 10 Windows Sidecar for Tauri 2.
# No credentials or internal network addresses are embedded in the binary.
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "src/StockCount.Api/StockCount.Api.csproj"
$publish = Join-Path $root "publish/tauri-sidecar"
$binDir = Join-Path $root "desktop/server-console/src-tauri/binaries"
$target = Join-Path $binDir "StockCount.Api-x86_64-pc-windows-msvc.exe"
if ($env:PROCESSOR_ARCHITECTURE -ne "AMD64") { throw "目前仅支持 win-x64 构建。" }
New-Item -Path $binDir -ItemType Directory -Force | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
$exe = Join-Path $publish "StockCount.Api.exe"
if (-not (Test-Path $exe)) { throw "没有找到 StockCount.Api.exe" }
Copy-Item $exe $target -Force
Write-Host "Tauri Sidecar ready: $target"
