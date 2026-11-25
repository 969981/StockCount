---
title: "StockCount Avalonia 双 EXE 发布手册"
aliases: ["Avalonia 单文件发布", "Avalonia dotnet publish"]
created: 2026-11-25
status: documented
tags: [StockCount, Avalonia, Windows, Release, Publish]
---

# StockCount Avalonia：单行 dotnet publish 发布手册

适用 Windows x64、.NET 10 SDK、PowerShell 7。所有命令均从 StockCount 源码根目录执行，Git 仓库与完整源码 ZIP 解压目录均可。无需 MAUI Android workload、Node.js、Rust 或 WebView2。

## 1. 直接使用 dotnet 命令（不依赖项目发布脚本）

管理器与 API 是两个独立进程，必须分别发布。先使用新的空输出目录，勿直接对正在运行的安装目录发布。每个代码块是一条完整的 PowerShell 命令，确认成功后才执行下一条。

### 1.1 发布 Avalonia 管理器

```powershell
dotnet publish .\src\StockCount.Server.Desktop\StockCount.Server.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=embedded -o .\publish\avalonia-ui
```

### 1.2 发布 API 后端

```powershell
dotnet publish .\src\StockCount.Api\StockCount.Api.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=embedded -o .\publish\avalonia-api
```

检查两个发布目录的实际文件：

```powershell
Get-ChildItem .\publish\avalonia-ui,.\publish\avalonia-api -Recurse -File | Select-Object FullName,Length
```

预期管理器和 API 各自生成一个包含运行时的 EXE。API 默认 appsettings.json、web.config 是原本便携模式不携带的配置文件；PDB 是调试符号。管理器运行配置由自身保存，并通过环境变量传给 API。若出现其他 DLL 或原生运行时文件，不要手工删除或忽略，应检查单文件发布兼容性，并优先使用第3节的自动校验脚本。

## 2. 合并两个 EXE

确认两个项目发布成功，均无必需的未打包依赖，再新建独立的便携目录：

```powershell
New-Item -ItemType Directory -Path .\publish\avalonia-portable -Force
```

```powershell
Copy-Item .\publish\avalonia-ui\StockCount.Server.Desktop.exe,.\publish\avalonia-api\StockCount.Api.exe -Destination .\publish\avalonia-portable -Force
```

```powershell
Get-ChildItem .\publish\avalonia-portable -Force
```

```powershell
Get-FileHash .\publish\avalonia-portable\*.exe -Algorithm SHA256
```

最终目录只有两个程序：

```text
publish/avalonia-portable/
  StockCount.Server.Desktop.exe
  StockCount.Api.exe
```

启动：

```powershell
Start-Process -FilePath .\publish\avalonia-portable\StockCount.Server.Desktop.exe
```

在管理器界面内启动 API；无需手动双击 API。自包含运行时无需目标电脑另装 .NET Runtime，原生库可能首次启动时解包至用户临时缓存。不要开启裁剪或 AOT 作为本手册的默认参数。

## 3. 推荐：一条脚本命令完成双 EXE 发布

第1节是手动 dotnet 操作。实际发布优先使用仓库脚本，自动执行 Hosting 测试、隔离临时构建目录、检查未打包依赖、验证复制的 SHA256。

```powershell
pwsh -NoProfile -File .\scripts\build-avalonia-desktop.ps1 -Configuration Release -OutputDirectory publish/avalonia-singlefile
```

同时生成便携 ZIP：

```powershell
pwsh -NoProfile -File .\scripts\package-avalonia-portable.ps1 -Build -InputDirectory publish/avalonia-singlefile
```

ZIP 位于 dist/local，含两个 EXE、启动 CMD 和说明 TXT。脚本拒绝含旧 DLL 或现场数据的构建输出目录；请另选干净目录，不要清空正在使用的程序目录。

## 4. 更新部署

停止后台管理器及 API 后升级。两个 EXE 必须放在同一目录，保留 server-settings.json、server-logs.db 以及可能存在的 WAL/SHM 文件。密码仍在当前 Windows 用户的 Credential Manager。不要直接删除旧目录或历史日志。旧版零散 DLL 不会由部署脚本自动删除；迁移方式见 [本地构建部署手册](LOCAL_BUILD_DEPLOY.md)。

## 5. 参数说明

| 参数 | 用途 |
|---|---|
| -c Release | Release 编译配置 |
| -r win-x64 | Windows x64 |
| --self-contained true | 包含 .NET 运行时 |
| -p:PublishSingleFile=true | 合并托管程序集 |
| -p:IncludeNativeLibrariesForSelfExtract=true | 打包原生依赖并在需要时解包 |
| -p:PublishTrimmed=false | 不裁剪，保护反射和 XAML |
| -p:DebugType=embedded | 嵌入调试符号 |
| -o | 指定发布目录 |

如果 PowerShell 出现 >>，按 Ctrl+C 取消续行，完整粘贴上面的单行命令。发布失败时不要安装旧产物；先核对退出码和实际输出。

参考 [Microsoft .NET 单文件部署](https://learn.microsoft.com/zh-cn/dotnet/core/deploying/single-file/overview)。

本手册与 PR #39 的发布参数一致。实际 Windows 编译、窗口启动、API 健康检查及文件大小以 CI 和现场验收为准。
