# StockCount Server Console · Tauri 2 + C# Sidecar

> 2026-11-23：现有 API 的 Windows 管理器改为 **Tauri 2 / React / TypeScript + Rust 受控命令**。WPF 草稿已撤出主分支。本仓库已经有桌面源码、C# SQLite 日志提供器、打包脚本和 CI，**但此轮未取得 Windows 构建及真实 SQL Server 联调的通过结果**。

## 架构

- 管理 UI：`desktop/server-console`，Tauri 2 + React + TypeScript + CSS。
- 业务 API：`src/StockCount.Api`，仍为 .NET 10 ASP.NET Core Minimal API，不迁移数据库业务到 Rust。
- 进程：Rust 通过 `tauri-plugin-shell` 的受限 Sidecar 启动 `StockCount.Api.exe`，负责启停/状态/DB 测试；UI 没有通用 Shell 权限。
- SQLite：`StockCount.Api.Logging.SqliteLogStore` 把 `ILogger` 写入数据库；Rust `rusqlite` 从相同文件只读，React 展示查询结果。
- 配置：本机非敏感参数保存在 `%LOCALAPPDATA%\StockCount\ServerHost\server-settings.json`，数据库/证书密码和设备密钥单独存于 Windows Credential Manager。

## 开发与编译

在 Windows 11 / Windows Server GUI 上，安装 .NET 10 SDK、Node.js 22、Rust MSVC 工具链、C++ Build Tools 和 WebView2（若尚未安装）。运行：

```powershell
git clone https://github.com/969981/StockCount.git
cd StockCount
dotnet build StockCount.slnx -c Release
./scripts/build-tauri-sidecar.ps1
cd desktop/server-console
npm install
npm run tauri -- icon ./assets/app-icon.svg
npm run dev:desktop
```

桌面打开后先到「数据库连接」填写真实服务器地址和最小权限账户，点击保存；「测试连接」会单独运行 C# Sidecar 的一次性 `SELECT 1`，此操作不需要让 Web API 先启动。若当前未配置数据库，仍可在 localhost 启动服务，但 `/ready` 仍然不可用。

## Windows 安装包

```powershell
# 在 desktop/server-console 目录执行；必须已先运行 sidecar 构建脚本和 icon 命令
npm run build:desktop
```

生成 Windows NSIS 安装器的规划路径：`desktop/server-console/src-tauri/target/release/bundle/nsis/`。

CI 定义：[tauri-server-console.yml](../../.github/workflows/tauri-server-console.yml)，在 `windows-latest` 执行 .NET publish、npm install、Rust 构建、Tauri Windows NSIS 打包，并上传安装文件。**仅在工作流真实成功时才可交付可安装 EXE。**

## 操作流程

1. 进入「数据库连接」，填写 SQL Server 地址 / 库名 `sagex3`、认证方式、用户和密码。默认数据库密码为空，由现场人员配置；输入并保存后密码仅存在 Windows Credential Manager。
2. 点击「测试连接」，通过 .NET API Sidecar 查询 `SELECT 1`（只读），验证连通性。
3. 进入「API 与设备」，设置监听端口（默认 5080）。默认 HTTP 仅监听 localhost，管理员测试优先保持这个模式。
4. 如需其他 PDA 通过 Wi-Fi 连接，选择受控 LAN HTTPS 并配置真实可信 PFX 证书及密码；同时设置服务器防火墙只允许可信 PDA 网段。
5. 返回「服务总览」，点击「启动服务」。Rust 启动托管 C# API，并通过额外的 **localhost 管理端口（API 端口+1）** 查询 `/health`；界面显示进程状态/健康结果。
6. 点击「停止服务」，Rust 向 loopback 管理端口发短期随机令牌授权的 `/internal/shutdown` POST，等待 C# 关闭并刷新 SQLite 日志；异常时兜底终止子进程。
7. 切到「SQLite 日志」按日志等级、分类和内容搜索，查看日期及错误详情。

## PDA 前台如何自己设置

PDA 端的 MAUI 配置页在未来接入：只允许配置 API URL、`Test` / `Production` 和设备密钥（建议 SecureStorage）。由桌面管理器「复制设备接入密钥」后，通过受控方式录入 PDA。**PDA 永远不能读取或编辑服务器的 SQL Server 凭据。**

## 数据和安全边界

- 正式环境对应 `sagex3.COB`；测试对应 `sagex3.XTEST`。服务器固定白名单，不允许动态 Schema。
- 现有 API 是只读骨架：不创建/关闭单据，不修改标签状态，所有盘点明细写入（包括 XTEST）依然禁用，直到事务/幂等/单位字段经现场核验。
- SQLite 默认位置：`<桌面管理端 EXE 所在目录>\server-logs.db`，采用 WAL、后台单写入队列；启动时清理 30 天前记录。不是正式业务数据仓库。
- SQLite 日志与 Tauri EXE 放在同一文件夹，NSIS 固定当前用户安装模式，避免默认放进 Program Files。如果手动改到不可写目录，服务启动会提示权限错误，绝不静默切回 AppData。同目录可能生成 `server-logs.db-wal` 与 `server-logs.db-shm`；更新/卸载前应备份。
- 本程序是用户会话桌面管理器，**不是 Windows Service**；当前 Tauri 关闭时会终止 Sidecar。未来需要开机自启/无人值守时，应另行实现 Windows Service 和远程管理 UI。
- 管理端口仅监听 127.0.0.1 / ::1 并要求进程级随机 admin token；不会公开下发到 PDA。
- 公开 GitHub 中不应提交实际连接凭据、API 设备密钥、证书、内网主机、现场日志、真实箱码。

## 代码入口

- [React 页面](src/App.tsx) / [主题样式](src/style.css)
- [Rust Sidecar 生命周期](src-tauri/src/server.rs)
- [本地设置与 Windows 凭据](src-tauri/src/config.rs)
- [SQLite 只读浏览](src-tauri/src/logs.rs)
- [.NET Sidecar 包装脚本](../../scripts/build-tauri-sidecar.ps1)
- [C# SQLite ILogger](../../src/StockCount.Api/Logging/SqliteLogStore.cs)
