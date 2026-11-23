---
title: "StockCount Avalonia 服务端管理器"
aliases: ["Avalonia Server Manager", "StockCount Avalonia"]
tags: [StockCount, Avalonia, Desktop, Windows, DotNet]
status: initial-implementation-awaiting-qa
---

# StockCount Server Desktop · Avalonia UI P0/P1

> 2026-11-23：与现有 Tauri 2 版本并行的 Windows 原生管理器初版。**源码已提交，Windows CI 编译/真机 SQL 联调通过前不得替代生产 Tauri 版本。** 原 PDA MAUI 和 C# ASP.NET Core API 不变。

## 技术

- .NET 10 Windows、Avalonia 12.1.6、Fluent Theme、CommunityToolkit.Mvvm。
- `../StockCount.Server.Hosting` 管理同目录 JSON、Windows Credential Manager、ASP.NET Core 子进程与 SQLite 日志。
- 管理器只启动/停止它自己创建的 `StockCount.Api.exe`，不接管其他 Windows 进程。
- 初版随 GUI 关闭停止 API；**不是** Windows Service，无人值守另做 P3。
- 服务与管理 UI 不共享 SQL 凭据明文文件。旧 Rust keyring 的 Windows target 为 `username.com.dreamer.stockcount.server`，C# 读取同一凭据。跨 Windows 用户仍须重新配置。

## Windows 运行（开发）

```powershell
# 仓库根目录
dotnet restore src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj
dotnet build src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj -c Debug
# 发布 API 可执行文件，复制到桌面 EXE 同目录后启动：
dotnet publish src/StockCount.Api/StockCount.Api.csproj -c Debug -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/avalonia-api-debug
Copy-Item publish/avalonia-api-debug/StockCount.Api.exe src/StockCount.Server.Desktop/bin/Debug/net10.0-windows/ -Force
dotnet run --project src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj --no-build
```

若 Visual Studio 的输出目录因 RID 发生变化，请以当前 `StockCount.Server.Desktop.exe` 实际所在目录为准。也可运行 `scripts/build-avalonia-desktop.ps1` 自动生成可移植目录。

## 设置持久化与安全

```text
<StockCount.Server.Desktop.exe 所在目录>/
  StockCount.Server.Desktop.exe
  StockCount.Api.exe
  server-settings.json       # 无密码，首次运行可从旧 AppData 迁移
  server-logs.db             # SQLite，运行时可能有 -wal / -shm
```

- EXE 所在目录必须可写，不会自动退回 AppData。
- JSON 配置存在时优先读取；旧 `%LOCALAPPDATA%\StockCount\ServerHost\server-settings.json` 只在 JSON 缺失时复制迁移，绝不删除原文件。错误 JSON 不默默降级。
- SQL 密码、遗留证书密码、PDA 设备密钥位于 Windows Credential Manager。
- 设置数据库身份发生改变时，XTEST/COB 结构核验和写入开关均清空，重新审批后才可启用。
- 仅启用已隔离的 PDA 局域网 HTTP，并使用网络 ACL、防火墙及设备 Key；不向公网开放。
- 关闭/迁移前请备份 JSON、SQLite（包括 -wal、-shm）和需要重新录入的密钥；更新只覆盖可执行文件。

## 验收

```powershell
dotnet test tests/StockCount.Server.Hosting.Tests/StockCount.Server.Hosting.Tests.csproj -c Release
dotnet publish src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/avalonia-desktop
```

开发侧至少验证：服务启停、重启保留配置、SQL 测试、SQLite 查询、密钥与 Tauri 兼容、端口冲突、LAN HTTP、防火墙、XTEST/COB 默认关闭及独立核验；真实 ERP 写入需单独授权，不在本阶段自动启用。

原有 Tauri 2 版保持不变，现有 Setup/Portable GitHub Release 仍指向 Tauri。Avalonia 经过构建、联调和打包验收后才讨论替换正式发布入口。
