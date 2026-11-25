---
title: "StockCount Avalonia 双 EXE 发布手册"
aliases: ["Avalonia 单文件发布", "Avalonia dotnet publish"]
created: 2026-11-25
status: documented
tags: [StockCount, Avalonia, Windows, Release, Publish]
---

# StockCount Avalonia：一条 dotnet publish 发布完整后台

适用 Windows x64、.NET 10 SDK、PowerShell 7。命令从 StockCount 源码根目录执行，Git 仓库与完整源码 ZIP 解压目录均可。无需 MAUI Android workload、Node.js、Rust 或 WebView2。

## 1. 推荐：一条命令发布完整后台

```powershell
dotnet publish .\src\StockCount.Server.Desktop\StockCount.Server.Desktop.csproj -c Release -r win-x64 -o .\publish\avalonia-portable
```

无需手动发布 API，也无需手工复制 EXE。Avalonia 项目的 MSBuild 发布目标自动以独立项目发布 API，验证并复制 API EXE 至同一个输出目录。Release win-x64 的框架依赖、单文件、原生依赖打包、不裁剪和嵌入调试符号参数已经写入 Avalonia csproj。

使用新建或仅含之前两个 EXE 的构建输出目录；不要使用正在运行的安装目录。若命令返回非零退出码，不要部署任何产物（旧 API 可能仍在输出目录）。发布目标不是部署事务；安全覆盖、备份与校验使用第3节脚本。

## 2. 输出与启动

预期发布目录：

```text
publish/avalonia-portable/
  StockCount.Server.Desktop.exe
  StockCount.Api.exe
```

```powershell
Get-ChildItem .\publish\avalonia-portable -Force
```

```powershell
Get-FileHash .\publish\avalonia-portable\*.exe -Algorithm SHA256
```

```powershell
Start-Process -FilePath .\publish\avalonia-portable\StockCount.Server.Desktop.exe
```

管理器负责启动和停止 API，两个程序仍是独立进程。默认两个 EXE 不内置 .NET Runtime，目标电脑必须安装 x64 .NET 10 和 ASP.NET Core 10 Runtime。安装 [ASP.NET Core Runtime 10 x64](https://dotnet.microsoft.com/download/dotnet/10.0)（Windows 安装器包含基础 .NET Runtime），或安装 .NET 10 x64 SDK。Avalonia 不需要 Windows Desktop Runtime 或 WebView2。检查：

~~~powershell
dotnet --list-runtimes
~~~

应有 Microsoft.NETCore.App 10.0.x 与 Microsoft.AspNetCore.App 10.0.x。原生库可能在首次启动时解包至用户临时缓存。不要开启裁剪或 AOT。若出现未打包依赖，发布应失败，不能手工删 DLL 继续部署。

## 3. 可选：脚本化测试、发布、归档

```powershell
pwsh -NoProfile -File .\scripts\build-avalonia-desktop.ps1 -Configuration Release -OutputDirectory publish/avalonia-singlefile
```

脚本执行 Hosting 测试、调用同一 Avalonia publish 入口、检查双 EXE 并校验复制哈希。需要 ZIP 时：

```powershell
pwsh -NoProfile -File .\scripts\package-avalonia-portable.ps1 -Build -InputDirectory publish/avalonia-singlefile
```

ZIP 位于 dist/local，另附启动 CMD 和说明 TXT。

## 4. 可选：仅更新 API

仅后端业务代码修改时，可以单独发布 API：

```powershell
dotnet publish .\src\StockCount.Api\StockCount.Api.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=embedded -o .\publish\avalonia-api
```

确认发布成功且没有必需的未打包依赖后，关闭管理器及 API，备份旧 API EXE 并替换。默认 appsettings.json、web.config 不是原便携部署的一部分，管理器负责传递配置。完整更新推荐第1节。

## 5. 数据保护

停止管理器与 API 后升级。保留 server-settings.json、server-logs.db 以及存在的 WAL/SHM，Windows Credential Manager 密钥仍属于当前用户。旧安装中的 DLL 不自动删除，迁移见 [本地构建部署手册](LOCAL_BUILD_DEPLOY.md)。不要把运行配置、数据库、密钥打包进发布工件。

## 6. 实现参数

| 参数 | 用途 |
|---|---|
| -c Release | Release 编译配置 |
| -r win-x64 | Windows x64，并激活项目默认单文件发布属性 |
| SelfContained | false，使用已安装的共享 .NET Runtime |
| EnableCompressionInSingleFile | false（SDK 只支持自包含 EXE 压缩） |
| PublishSingleFile | true，合并托管程序集 |
| IncludeNativeLibrariesForSelfExtract | true，打包原生库 |
| PublishTrimmed | false，保护 XAML 与反射 |
| DebugType | embedded |
| -o | 输出目录 |

Debug/F5 保留原来的 API 项目引用与调试资产复制。Release 不再将 API 项目引用带入管理器发布，避免 appsettings.json 与 API runtimeconfig 泄漏到管理器包；独立 API 发布在 MSBuild Publish 后运行。

参考 [Microsoft .NET 单文件部署](https://learn.microsoft.com/zh-cn/dotnet/core/deploying/single-file/overview)。

实际 Windows 编译、窗口启动、API 健康检查及文件大小以 CI 和现场验收为准。

## 7. 可选：自包含压缩发布（不依赖机器上的 Runtime）

.NET SDK 的 EnableCompressionInSingleFile=true 与 SelfContained=false 不兼容（NETSDK1176），因此默认选择不携带 Runtime 的框架依赖单文件模式，Portable ZIP 仍使用 Optimal 压缩。如果目标电脑不便安装 Runtime，可改用以下命令，两个 EXE 会同时采用自包含压缩：

~~~powershell
dotnet publish .\src\StockCount.Server.Desktop\StockCount.Server.Desktop.csproj -c Release -r win-x64 --self-contained true -p:EnableCompressionInSingleFile=true -o .\publish\avalonia-compressed
~~~

不要使用 UPX 等第三方 EXE 壳或绕过 SDK 检查。CI 将记录默认产物大小并与此前 219,550,207 字节的自包含基线比较；实际体积以构建结果为准。

## 8. 发布故障排查（2026-11-25）

- Unexpected unbundled API dependencies: StockCount.Api.staticwebassets.endpoints.json：ASP.NET SDK 自动生成的端点清单，当前 API 未使用静态文件路由，Swagger UI 及本地化 JS 为嵌入资源。新版仅将此已知清单纳入临时目录允许列表，不随便携包分发；其他未打包依赖仍报错。
- MSB3026：Core/Contracts DLL 无法覆盖，提示用户映射区域占用。新版将子 API 的 bin/obj 和发布目录隔离到独立构建工作区，并禁用子构建服务器缓存。若其他进程仍占用文件，关闭 Visual Studio 的调试会话及旧版管理器/API 后执行 dotnet build-server shutdown，再重新发布；不要强制终止不明进程，不要删除运行目录的配置及日志。
- AVLN5001：Watermark 弃用警告，不是本次发布失败原因。

旧源码 ZIP 不会自动更新，请重新下载修复后的源码。发布命令仍与第1节相同；仅成功退出后才能部署两个 EXE。
