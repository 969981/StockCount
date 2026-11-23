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
3. **API 与设备**：端口、仅本机/HTTPS LAN、证书 PFX 路径与密码、设备接入密钥复制、PDA URL 示例。
4. **SQLite 日志**：实时刷新、关键字筛选、时间/级别/模块/消息表格、错误详情、实际 SQLite 文件路径。

## 行为状态机

`已停止 → 点击启动 → Rust spawn ASP.NET Core Sidecar → 健康检查已就绪 → 运行中 → 点击停止 → 本机受令牌保护的 POST /internal/shutdown → SQLite 日志队列 flush → 停止`；异常时进程终止回退并显示错误。

API 本地端口默认 `5080`，管理接口专用 loopback 端口 `5081`（固定取 API Port + 1）；LAN 对外监听 HTTPS，管理端口始终不会绑定公网。C# API 在独立进程运行，而不是把 Rust 变成新的业务后端。

## 本机配置、密码和 SQLite

- `server-settings.json`：只含非敏感配置，默认未填写 SQL Server 地址、数据库名 `sagex3`、端口 5080；所有变更必须先停止正在运行的服务。
- SQL 密码/PFX 密码/设备密钥：Windows Credential Manager，**不写入 JSON**。跨 Windows 用户账户迁移时应重新配置。
- SQLite：`%LOCALAPPDATA%/StockCount/ServerHost/logs/server-logs.db`，存 API HTTP、错误和系统日志；真实扫码请求体、密钥和数据库密码不得被记录。
- 客户端 MAUI 后续提供 API URL、Test/Production、设备密钥的可编辑配置界面；客户端不得知道服务器的 SQL 连接信息。

## Windows 本地构建

见 [Tauri 主项目 README](../desktop/server-console/README.md)，以及 [CI](../.github/workflows/tauri-server-console.yml)。依次完成 C# 自包含 EXE 发布、Tauri 图标生成、React 编译、Rust 编译和 NSIS 打包。具体命令亦见 README。

## 验收边界

已提交源文件，但**此轮尚未取得 Windows 实际编译和真实数据库联调成功结果**；不能称已经交付安装包。后续在现场要实测：启动/停止/占用端口、配置重启保存、Windows 凭据读取、SQL 认证与证书、SQLite 记录量/脱敏、PDA TLS 连接，以及正式写入关闭状态。仅数据只读联调，不执行生产 INSERT。

关联：[[architecture|架构]] · [Markdown](../architecture.md) · [[docs/API_CONTRACT|API 规范]] · [Markdown](API_CONTRACT.md) · [[docs/SECURITY_AND_ENVIRONMENTS|环境保护]] · [Markdown](SECURITY_AND_ENVIRONMENTS.md)。
