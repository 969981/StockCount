---
title: StockCount · 统一 GitHub Releases 发布与免安装版
date: 2026-11-23
status: android-release-workflow-added-awaiting-signing-secrets
tags: [StockCount, GitHubActions, APK, Tauri, Windows, Portable, Release]
---

# 统一发布：APK + Setup + Portable

> CI 配置已加入。是否存在真实 Release 以及文件可用性，以 [Releases](https://github.com/969981/StockCount/releases) 和实际 [Actions](https://github.com/969981/StockCount/actions/workflows/publish-stockcount.yml) 成功记录为准。**没有同时通过 Android + Windows 构建时绝不发布缺少文件的版本。**

**版本说明**：`v0.2.0-preview.2` 已采用修复后的 SQLite 依赖并发布 Debug APK 与 Windows Setup/Portable。下一个统一版本 `v0.2.0-preview.3` 新增 **Android Release 签名 APK**。需要先在仓库配置四个 GitHub Actions Secrets，才会生成并上传正式签名版；缺少它时只做 Release 代码编译检查，**不创建不完整的 GitHub Release**。参见 [Android 正式签名指南](ANDROID_RELEASE_SIGNING.md)。

## 输出资产

| 文件 | 平台 | 内容 |
| --- | --- | --- |
| `StockCount-PDA-v0.2.0-preview.3-Android-Debug-Signed.apk` | Android PDA | Debug 配置、调试证书签名，用于现场联调 |
| `StockCount-PDA-v0.2.0-preview.3-Android-Release-Signed.apk` | Android PDA | Release 配置、长期专用 keystore 签名；必须先配置 Secrets |
| `StockCount-Server-v0.2.0-preview.3-Windows-x64-Setup.exe` | Windows x64 | Tauri 2 + React + ASP.NET Core 10 Sidecar 安装版 |
| `StockCount-Server-v0.2.0-preview.3-Windows-x64-Portable.zip` | Windows x64 | 程序、Sidecar、启动脚本及说明；解压即用 |
| `SHA256SUMS.txt` | 多平台 | 以上 **4 个**可执行文件的 SHA256 校验清单 |

**预览版说明**：Debug 签名和正式 Release keystore 签名不同，默认同一 Android 包 ID，互相不能直接覆盖安装；切换前先确认待提交 UUID，妥善迁移或备份数据。Windows 尚无代码签名，可能出现系统 SmartScreen 警示；不得关闭安全机制或从非官方地址安装。

## Portable 免安装版如何使用

1. 下载 Portable.zip 并校验 SHA256，解压到当前用户可写目录，比如 `D:\Apps\StockCount-Server\`。
2. 直接执行 `Start-StockCount.cmd` 或 `stockcount-server-console.exe`。与 Setup 使用相同的桌面UI和 C# API。
3. 保持 `StockCount.Api.exe` 与 Tauri exe **处于同一目录**。这是运行时 Sidecar 依赖，不是可丢弃的临时安装程序。
4. 本机日志文件为 `<exe目录>\server-logs.db`，可能生成 WAL/SHM 文件。不要放进受写保护的 `Program Files`，也不应直接从压缩包内部启动。
5. **免安装仅指免 Setup，不代表彻底免依赖**：Windows WebView2 Runtime 必须存在。C# API Sidecar 是 self-contained .NET 10，无需额外安装 .NET Runtime。
6. 设置文件继续在 `%LOCALAPPDATA%\StockCount\ServerHost\server-settings.json`，密码/设备密钥在 **Windows Credential Manager**。因此免安装 ZIP **不是**可随 U 盘跨机器迁移全部密钥的绿色版。
7. 关闭 Tauri 管理器会停止该用户会话的 API Sidecar。日志/密钥备份以及更新时需先停止服务。

## Setup 安装版

- 默认 NSIS `currentUser` 模式，减少管理员权限要求，选择当前用户可写的安装路径。
- 与 Portable 版使用相同源代码、API、SQLite 日志规则、设置位置与 Windows Credential Manager。
- 现场 API 需要 HTTPS PFX 才可对 PDA 开放局域网监听；不能通过关闭 TLS 验证替代可信证书。

## 发布流水线和门禁

工作流：[publish-stockcount.yml](../.github/workflows/publish-stockcount.yml)。

1. Android Windows Runner：安装 MAUI Android workload，运行 Core 测试，生成 Debug APK，同时编译 Release 配置。只有 GitHub Secrets 配齐时，才用长期 JKS 签名并通过 apksigner 验证，上传第二个 Release APK；未签名产物绝不发布。
2. Windows x64 Runner：编译 ASP.NET Core、发布 self-contained Sidecar、构建 Tauri NSIS Setup，运行 [便携版打包脚本](../scripts/package-windows-portable.ps1)；验证主程序、Sidecar、启动脚本和压缩包内容。
3. 两个 Job 都成功后，Linux Runner 必须检查 **两个签名 APK、Setup.exe、Portable.zip 共四个可执行文件**，生成 SHA256SUMS，再将五个文件发布到同一个 GitHub 预发布版本。签名 Secrets 缺失则发布任务失败，不会生成不完整的版本。
4. PR 只构建、不发布；`workflow_dispatch` 显式发布，或在 main 的提交消息中包含 `[publish-stockcount]` 且修改触发路径。发布标签 `v0.2.0-preview.3` 已存在时禁止静默覆盖。
5. Android keystore 通过仓库 Secrets 注入，无证书不能发布 Release；Windows 签名证书管理仍需独立完善，不能把未验收的预览版当作生产认证。

## 与 Sage X3 写入权限的关系

XTEST 和 COB 都有独立写入能力，但**首次均不启用**。安装、更新或免安装启动不会自动打开写入开关；管理员须分别核验 `XCOUTD` 字段、单位换算、SQL Server 原子事务和幂等记录迁移。免安装包内绝不可预置现场 SQL Server 地址、账号、密码或设备密钥。

关联：[[docs/WRITE_FLOW_IMPLEMENTATION|受控写入]] · [Markdown](WRITE_FLOW_IMPLEMENTATION.md)；[[docs/SCANNER_INTEGRATION|扫码联调]] · [Markdown](SCANNER_INTEGRATION.md)；[[docs/00-文档导航|文档导航]] · [Markdown](00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)。
