---
title: StockCount · 统一 GitHub Releases 发布与免安装版
date: 2026-11-23
status: android-release-workflow-added-awaiting-signing-secrets
tags: [StockCount, GitHubActions, APK, Tauri, Windows, Portable, Release]
---

# 统一发布：APK + Setup + Portable

> CI 配置已加入。是否存在真实 Release 以及文件可用性，以 [Releases](https://github.com/969981/StockCount/releases) 和实际 [Actions](https://github.com/969981/StockCount/actions/workflows/publish-stockcount.yml) 成功记录为准。**没有同时通过 Android + Windows 构建时绝不发布缺少文件的版本。**

**版本说明**：`v0.2.0-preview.2` 已采用修复后的 SQLite 依赖并发布 Debug APK 与 Windows Setup/Portable。下一个统一版本 `v0.2.0-preview.3` 新增 **Android Release 签名 APK**。需要先在仓库配置四个 GitHub Actions Secrets，才会生成并上传正式签名版；缺少它时只做 Release 代码编译检查，**不创建不完整的 GitHub Release**。参见 [Android 正式签名指南](ANDROID_RELEASE_SIGNING.md)。

## 本地 PowerShell 一键创建签名与发布 GitHub Release

**第一次配置：生成 JKS（只做一次）→ 上传签名 Secrets（只做一次）→ 触发发布。以后发布新版本只需要检查版本号、备份和执行最后一条命令。**

### 0. 准备环境

在 Windows PowerShell 7 中，确保 JDK `keytool`、GitHub CLI (`gh`) 已安装且已登录。JKS 保存路径应在 **Git 工作区之外**；不要把密码、私钥或 Base64 文件放入仓库。

```powershell
cd D:\WorkSpace\StockCount
pwsh --version
keytool -help
gh auth login
gh auth status
```

如果已经有一直沿用的 StockCount JKS，**跳过生成步骤**，必须继续使用原来的 JKS，不要创建新密钥来覆盖旧版本。

### 1. 生成长期 JKS（首次一次）

```powershell
# 查看将发生的操作，不会创建文件
pwsh -NoProfile -File .\scripts\new-android-release-jks.ps1 -OutputPath D:\StockCountSecrets\stockcount-release.jks -WhatIf

# 实际创建，keytool 在终端中交互询问 STORE/KEY 密码和证书信息
pwsh -NoProfile -File .\scripts\new-android-release-jks.ps1 -OutputPath D:\StockCountSecrets\stockcount-release.jks -Alias stockcount
```

脚本使用 JKS 格式、RSA 3072 位与 10000 天证书有效期，拒绝覆盖已有同路径密钥，也拒绝在仓库内生成。生成后应安全备份 JKS 与密码，记录证书 SHA-256 指纹；JKS 文件哈希与**证书指纹不是同一概念**。

### 2. 配置四个 GitHub Actions Secrets（首次一次）

```powershell
# 先预演，无 GitHub 写入，也不会要求输入密码
pwsh -NoProfile -File .\scripts\configure-android-release-secrets.ps1 -KeyStore D:\StockCountSecrets\stockcount-release.jks -WhatIf

# 正式上传：交互输入密码，密钥内容通过 gh 的标准输入加密提交到 GitHub Actions
pwsh -NoProfile -File .\scripts\configure-android-release-secrets.ps1 -KeyStore D:\StockCountSecrets\stockcount-release.jks -KeyAlias stockcount

# 只能看到 Secret 名称，不会显示私钥或明文密码
gh secret list -R 969981/StockCount
```

脚本配置 `STOCKCOUNT_ANDROID_KEYSTORE_BASE64`、`STOCKCOUNT_ANDROID_KEY_ALIAS`、`STOCKCOUNT_ANDROID_STORE_PASS` 和 `STOCKCOUNT_ANDROID_KEY_PASS` 四项；**如同名 Secrets 已存在，默认拒绝覆盖**。只有确认上传的是同一把原始 JKS 和对应密码，才可手动加 `-Force` 更新，避免换签名导致旧 PDA APK 无法覆盖升级。

### 3. 发布指定版本到 GitHub Releases

```powershell
# 先预演：仅验证格式并展示目标，不触发 CI
pwsh -NoProfile -File .\scripts\publish-stockcount-release.ps1 -Version 0.2.0-preview.3 -WhatIf

# 执行：先检查 GitHub 权限、Secrets 名称、版本标签及 workflow，然后触发 main 分支构建
pwsh -NoProfile -File .\scripts\publish-stockcount-release.ps1 -Version 0.2.0-preview.3

# 查看编译是否成功；触发成功不等于 Release 已发布
gh run list -R 969981/StockCount --workflow publish-stockcount.yml --limit 5

# 只有 Actions 全部成功，且四个程序文件与 SHA256SUMS 均存在，才算发布成功
gh release view v0.2.0-preview.3 -R 969981/StockCount
```

发布流程使用 [publish-stockcount.yml](../.github/workflows/publish-stockcount.yml) 的 `workflow_dispatch.inputs.version`，由 GitHub Actions 在 main 上编译 **Android Debug Signed APK、Android Release Signed APK、Windows Setup.exe、Windows Portable.zip**，再生成 `SHA256SUMS.txt`，**五份资产同时发布到一个预发布版本**。任一构建、签名或完整性校验失败，不创建 Release，也不把 unsigned APK 冒充正式安装包。标签已存在时拒绝覆盖。

**版本注意**：`-Version` 负责 Release 标签和产物文件名，**不自动提升安卓 `ApplicationVersion`（versionCode）或 Windows 产品版本**。每次准备新版本时，需先修改提交相应元数据，且 Android versionCode 必须比设备上已安装的版本高；否则即使签名相同也可能无法覆盖升级。要发布其它版本请将示例版本号替换为相应 SemVer。

**安全与权限注意**：本地只上传密钥到 GitHub Secrets，不会把 JKS 打包进 APK 或源码 ZIP；Actions 正式签名仅在 main 上进行；普通 PR 无法读取签名凭据。操作员需要仓库 Secrets 管理和 Actions 权限，且不能把签名密钥泄露给代码贡献者。正式发布前依然需要海康真机联调与 Sage X3 验收。

相关脚本：[JKS 生成](../scripts/new-android-release-jks.ps1) · [签名 Secrets 配置](../scripts/configure-android-release-secrets.ps1) · [一键触发 GitHub Release](../scripts/publish-stockcount-release.ps1) · [[docs/ANDROID_RELEASE_SIGNING|签名细节]] · [Markdown](ANDROID_RELEASE_SIGNING.md)。
## 软件实际版本与发布文件名

软件版本以 `desktop/server-console/src-tauri/tauri.conf.json` 的 `version` 为准，配套的 `Cargo.toml`、npm `package.json`、MAUI `ApplicationDisplayVersion` 必须一致。不要只在免安装脚本里填 `-Version 1.0.1`，那只会改变文件名，不会更改 EXE 真实版本。

```powershell
pwsh -File .\scripts\get-app-version.ps1
# 修改真实版本；如发布新的 Android APK，记得同步递增 versionCode
pwsh -File .\scripts\set-app-version.ps1 -Version 1.0.1 -AndroidVersionCode 4
pwsh -File .\scripts\build-local.ps1 -Target Windows
```

本地 `build-local.ps1` 与 `package-windows-portable.ps1` 默认自动读取配置版本；显式传入版本时只接受与项目基础版本一致的预览后缀（例如 `0.2.0-preview.3`）。GitHub Releases 的版本校验同样拒绝 `1.0.1` 标签配 `0.2.0` 程序。准备新正式版本时需先提交工程版本变更，再触发 Actions 发布。

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
- 当前可信局域网 PDA 接入使用 **HTTP**，不需要 PFX；服务器上的 `allowLan` 必须明确开启，Windows 防火墙仅放通 PDA 网段，且不能将端口映射至公网。设备密钥通过 HTTP 明文传输，正式生产部署应另行评估安全网络。

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
