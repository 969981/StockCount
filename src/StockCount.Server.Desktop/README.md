---
title: "StockCount Avalonia 服务端管理器"
aliases: ["Avalonia Server Manager", "StockCount Avalonia"]
tags: [StockCount, Avalonia, Desktop, Windows, DotNet]
status: initial-implementation-awaiting-qa
---

# StockCount Server Desktop · Avalonia UI P0/P1

> 2026-11-23：与现有 Tauri 2 版本并行的 Windows 原生管理器初版。**源码已提交，Windows CI 编译/真机 SQL 联调通过前不得替代生产 Tauri 版本。** 原 PDA MAUI 和 C# ASP.NET Core API 不变。

## 程序图标

Windows 窗口和 EXE 使用 [server.svg](../../assets/branding/server.svg)，与 Tauri 管理器保持统一。
Avalonia 的 `Assets/server.ico` 是已提交的多尺寸原生资源（16、32、48、256 像素），随源码 ZIP 分发。Visual Studio Debug、Release 和 Publish 不再生成图标，也不依赖 Node.js/npm。仅当修改品牌 SVG 时，手动运行 `pwsh -File .\scripts\prepare-windows-brand-icons.ps1 -Force` 并提交更新后的 ICO。

图标完整设计与多尺寸 SVG：[[assets/branding/README|StockCount 图标指南]] ·
[Markdown 说明](../../assets/branding/README.md)。

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
# Windows Debug 构建会自动从 API 项目复制 StockCount.Api.exe 及相关依赖到桌面 bin 目录
dotnet run --project src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj
```

Visual Studio 请在完整 `StockCount.slnx` 中把 `StockCount.Server.Desktop` 设为启动项目；F5 可自动构建并准备 API 依赖，具体见 [统一调试指南](../../docs/VISUAL_STUDIO_HTTP_DEBUG.md)。若 Visual Studio 的输出目录因 RID 发生变化，请以当前 `StockCount.Server.Desktop.exe` 实际所在目录为准。也可运行 `scripts/build-avalonia-desktop.ps1` 自动生成可移植目录。

## 启动行为与中文版日志（2026-11-23）

- 默认 `autoStartApi: true`：打开 Avalonia 管理器会尝试启动它自己的 `StockCount.Api.exe`。首次默认 SQL Server 地址 `192.168.1.1,1433`，但未保存配置前不会擅自启动；先确认并保存数据库设置，之后自动启动；启动失败会在顶部显示错误。已通过 UI/JSON 禁用自动启动时，仍可以手动点击“启动 API”。
- SQLite 日志页把常见 ASP.NET Core 运行消息、日志级别与分类**翻译成中文**，时间按本机时区显示，**数据库内仍保留未修改的原始英文/异常诊断内容**。
- SQLite 日志页选择 **1、3、7、14、30 天**，首次点击“清理旧日志”显示将永久删除的范围；再次点击确认才执行 SQL `DELETE`。支持 API 正在写入的 WAL 模式，不删除 `server-logs.db`、`-wal`、`-shm` 文件。
- 现有 API 在进程启动时依然**自动清理超过 30 天的日志**；新按钮是手动追加清理，可选保留时间更短。日志搜索按中文显示内容筛选最近 500 条记录，显示最多 200 条。
- 写入授权与连接独立保存：未配置审计编码时四项默认预选为草稿，实际保存的四个授权字段仍默认关闭。确认各帐套核验后，使用“确认并保存写入授权”生效。操作员编码可留空，自动使用 `PDA` 程序标识；如 ERP 要求已批准的具体编码，可选填 1–5 字符。现场仍需核验数据库接受该标识。数据库身份变更撤销旧授权。

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
- SQL 密码、遗留证书密码、PDA 设备密钥位于 Windows Credential Manager。已保存 SQL 密码时，密码框显示固定八个圆点占位符；真实密码不会回填。留空保存保留原密码且不会撤销写入授权，只有实际输入新密码才替换凭据。
- 设置数据库身份发生改变时，XTEST/COB 结构核验和写入开关均清空，重新审批后才可启用。
- 局域网 HTTP 可选择免密连接（新配置默认预选、旧配置需主动启用），只允许直接内网 IP/回环访问业务 API。无需 PDA Key，但必须使用隔离网络、ACL 和防火墙；不向公网开放。旧版密钥模式和管理端口令牌仍保留。
- 关闭/迁移前请备份 JSON、SQLite（包括 -wal、-shm）和需要重新录入的密钥；更新只覆盖可执行文件。

## 验收

```powershell
dotnet test tests/StockCount.Server.Hosting.Tests/StockCount.Server.Hosting.Tests.csproj -c Release
dotnet publish src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/avalonia-desktop
```

开发侧至少验证：服务启停、重启保留配置、SQL 测试、SQLite 查询、密钥与 Tauri 兼容、端口冲突、LAN HTTP、防火墙、XTEST/COB 默认关闭及独立核验；真实 ERP 写入需单独授权，不在本阶段自动启用。

原有 Tauri 2 版保持不变，现有 Setup/Portable GitHub Release 仍指向 Tauri。Avalonia 经过构建、联调和打包验收后才讨论替换正式发布入口。
