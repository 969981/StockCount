---
title: "StockCount 后端管理器（Tauri 2）"
type: work-spec
date: 2026-11-23
status: implemented-unverified
tags: [work, StockCount, Tauri2, SQLite, CSharp, API, Server]
---

# StockCount Server Console · Tauri 2 UI 设计与架构

## 决策记录

2026-11-23 用户要求 Windows 后端有 UI：一键启动/停止 API、数据库配置先占位、由现场人员填写并从前台读取和修改，日志写入 SQLite。**后续用户明确不使用 WPF，改成 Tauri（Tauri 2）。** 已清理原 WPF 草稿；桌面不影响原 MAUI Android 端的 C# 技术路线。

## 分工

| 组件 | 技术 | 负责内容 |
| --- | --- | --- |
| 桌面视图 | Tauri 2 / React / TypeScript | 状态、启动停止按钮、数据库配置表单、日志展示 |
| 桌面宿主 | Rust + tauri-plugin-shell | Sidecar 生命周期、设置读写、设备密钥、凭据管理、日志读取 |
| 业务 API | ASP.NET Core 10 / C# | 既有只读 API、Sage X3 查询、HTTP 健康检查、后续受控写入 |
| SQL 数据 | Microsoft.Data.SqlClient + Dapper | 正式 COB / 测试 XTEST；只读账号优先 |
| 日志 | Microsoft.Data.Sqlite + ILogger + rusqlite | C# 写入 SQLite WAL，Rust 只读查询 |
| 凭据 | Windows Credential Manager（keyring） | SQL 密码、PFX 密码、设备密钥持久化 |

## 四屏 UI

1. **服务总览**：状态卡、端口、数据源、启动停止、测试数据库按钮、操作提示和最新问题。
2. **数据库连接**：SQL Server/实例、库名（预填 `sagex3`）、Windows/SQL 认证、用户名、密码输入、加密与证书校验选项、保存/测试连接。
3. **API 与设备**：端口、仅本机/HTTP LAN 模式切换、设备接入密钥复制、PDA 内网 HTTP URL 示例；LAN 不再要求 PFX。
4. **SQLite 日志**：实时刷新、关键字筛选、时间/级别/模块/消息表格、错误详情、实际 SQLite 文件路径。

## 行为状态机

`已停止 → 点击启动 → Rust spawn ASP.NET Core Sidecar → 健康检查已就绪 → 运行中 → 点击停止 → 本机受令牌保护的 POST /internal/shutdown → SQLite 日志队列 flush → 停止`；异常时进程终止回退并显示错误。

API 本地端口默认 `5080`，管理接口专用 loopback 端口 `5081`（固定取 API Port + 1）；LAN 开启时业务端口对外监听 HTTP（不需要 PFX），管理端口始终仅绑定 localhost；需限制防火墙可访问来源且严禁公网映射。C# API 在独立进程运行，而不是把 Rust 变成新的业务后端。

## 本机配置、密码和 SQLite

- `<Tauri EXE 所在目录>/server-settings.json`：只含非敏感配置，默认未填写 SQL Server 地址、数据库名 `sagex3`、端口 5080；所有变更必须先停止正在运行的服务。若不存在，首次运行会从旧 AppData 路径安全复制有效 JSON，旧文件保留。
- SQL 密码/PFX 密码/设备密钥：Windows Credential Manager，**不写入 JSON**。跨 Windows 用户账户迁移时应重新配置。
- SQLite：`<Tauri 桌面程序 EXE 所在目录>/server-logs.db`，存 API HTTP、错误和系统日志；真实扫码请求体、密钥和数据库密码不得被记录。
- 客户端 MAUI 后续提供 API URL、Test/Production、设备密钥的可编辑配置界面；客户端不得知道服务器的 SQL 连接信息。

**同目录持久化约束**：NSIS 使用 `currentUser` 安装模式；`server-settings.json` 与 `server-logs.db` 均与 Tauri EXE 相邻。目录不可写则报错，不隐式换到 AppData；Portable 更新只覆盖程序文件，保留同目录配置、SQLite、WAL/SHM。移动或卸载前应备份 JSON 和 SQLite 全部相关文件。Windows Credential Manager 中的凭据位置不变。

## Windows 本地构建

见 [Tauri 主项目 README](../desktop/server-console/README.md)，以及 [CI](../.github/workflows/tauri-server-console.yml)。依次完成 C# 自包含 EXE 发布、Tauri 图标生成、React 编译、Rust 编译和 NSIS 打包。具体命令亦见 README。

## 验收边界

已提交源文件，但**此轮尚未取得 Windows 实际编译和真实数据库联调成功结果**；不能称已经交付安装包。后续在现场要实测：启动/停止/占用端口、配置重启保存、Windows 凭据读取、SQL 认证与证书、SQLite 记录量/脱敏、PDA 内网 HTTP 连接和设备密钥（明文）传输风险，以及正式写入关闭状态。仅数据只读联调，不执行生产 INSERT。

关联：[[architecture|架构]] · [Markdown](../architecture.md) · [[docs/API_CONTRACT|API 规范]] · [Markdown](API_CONTRACT.md) · [[docs/SECURITY_AND_ENVIRONMENTS|环境保护]] · [Markdown](SECURITY_AND_ENVIRONMENTS.md)。
