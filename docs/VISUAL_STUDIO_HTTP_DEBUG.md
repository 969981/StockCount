---
title: "StockCount · Visual Studio 全项目构建与 HTTP 调试"
aliases: ["StockCount Visual Studio Debug", "StockCount VS 2026"]
tags: [StockCount, VisualStudio, Avalonia, MAUI, Android, ASPNETCore, HTTP]
status: active
date: 2026-11-23
---

# Visual Studio 2026 · HTTP + Avalonia + MAUI Android

## 先打开正确的解决方案

- **`StockCount.slnx`**：完整解决方案，包含 ASP.NET Core 后端、Avalonia Windows 管理 UI、MAUI Android PDA 和测试项目。需要在 Visual Studio Installer 勾选 **.NET Multi-platform App UI development**，安装 .NET 10 SDK、Android SDK、MAUI Android workload。没有 MAUI 工具链时，完整解决方案的“全部生成”可能失败。
- **`StockCount.Backend.slnx`**：兼容原有的后端专用解决方案，只包含 API/Contracts/Core/Infrastructure/Core.Tests，不需要 Android 或 Avalonia 工作负载。
- 修改 / Git 拉取后若项目未出现，请关闭当前解决方案，再明确通过“文件 → 打开 → 项目或解决方案”打开仓库根目录的 **`StockCount.slnx`**，不是旧的 `StockCount.Backend.slnx`，也不是 `src/StockCount.Api/StockCount.Api.csproj`。

## 为什么 Visual Studio 曾出现“信任 Asp.Net Core SSL 证书”弹窗？

原 API 没有提交显式的 `Properties/launchSettings.json`，Visual Studio 可以使用或自动提供包含 HTTPS/IIS Express 的调试方式。现在新增 **唯一**项目启动配置 **`StockCount.Api (HTTP)`**：

- `commandName: Project`
- `applicationUrl: http://127.0.0.1:5080`
- `launchBrowser: false`
- 无 `sslPort`、`https://`、IIS Express profile、HTTPS 重定向。

在“启动项目”下拉框选择 **StockCount.Api (HTTP)**，不要选择 IIS Express 或旧缓存的 HTTPS 配置。之前提示证书可以选“否”（不需要信任）。如 VS 显示旧启动项，重新加载解决方案并重新选择 HTTP 配置。局域网业务监听仍由 **桌面管理器**控制；单独在 VS 启动 API 的默认监听仅限 127.0.0.1。

**注意**：API 的 HTTP 与 **SQL Server 的 EncryptSql 加密连接**是不同层次，不要因为局域网 HTTP 联调而关闭 SQL Server TLS 校验。HTTP 不加密 PDA 数据/设备密钥，仅适用于可信隔离内网+防火墙，禁止公网映射。

## 后台 API 的启动与验证

1. 在解决方案资源管理器中右键 `StockCount.Api` → **设为启动项目**。
2. 工具栏选择 `StockCount.Api (HTTP)` → F5。
3. PowerShell 验证：`Invoke-RestMethod http://127.0.0.1:5080/health`。
4. 如端口 5080 被 Avalonia/Tauri 管理器占用，先停止管理器内启动的 API，再启动独立 API；同一台电脑不要启动两个占用 5080 的后端。

独立 API 默认不会从桌面管理器目录读取 `server-settings.json`：桌面管理器负责读取并传递配置，独立 API 运行时需要按照 [[src/StockCount.Api/RUN|API 运行指南]] 设置对应环境变量，敏感密码不应加入公开配置。

## Windows Avalonia UI · F5

1. 右键 `StockCount.Server.Desktop` → **设为启动项目**，选 Windows 本机调试。
2. 直接 **生成 / F5**。在 Windows Debug 编译时，桌面工程自动先编译 API，再将 `StockCount.Api.exe` 及所需调试依赖复制到桌面 EXE 同目录，避免“找不到 Sidecar”。
3. Avalonia 的 API 服务由主界面“启动”按钮控制，不必同时 F5 启动 `StockCount.Api`。
4. `server-settings.json` / `server-logs.db` 存储在实际桌面 EXE 同目录，Debug 与 Release 是 **不同目录**，不应误以为它们自动共享配置。SQL 密码及设备 Key 仍位于 Windows Credential Manager。
5. 发布可用 Windows 目录：`pwsh -File .\scripts\build-avalonia-desktop.ps1 -Configuration Release`。默认现有 `build-local.ps1 -Target Windows` 和 GitHub Release 仍构建 **Tauri** 安装包；不要把这两条不同的发布流程混为一谈。

## MAUI Android · 真机/模拟器编译

1. 在 Visual Studio Installer 安装 **.NET Multi-platform App UI development** 工作负载；检查 `dotnet workload list` 是否包含 `maui-android`。必要时以管理员权限运行 `dotnet workload install maui-android`。
2. 右键 `StockCount.Pad` → **设为启动项目**，选择已连接的 Android 设备或已创建的模拟器；PDA 真机需打开开发者选项与 USB 调试，`adb devices` 显示 `device`。
3. 生成 Debug 签名 APK，不必启动 API 或 Avalonia：
   
   ```powershell
   dotnet build src/StockCount.Pad/StockCount.Pad.csproj -f net10.0-android -c Debug -p:AndroidPackageFormat=apk -p:EmbedAssembliesIntoApk=true
   ```

4. APK 通常在 `src/StockCount.Pad/bin/Debug/net10.0-android/`；用 `Get-ChildItem ... -Recurse -Filter '*-Signed.apk'` 确认确切名称。签名发布版仍需长期 JKS，Debug 包不能替代正式签名版。
5. 手机端内网 API URL 为 `http://服务器局域网IP:5080`，需在 Avalonia 管理器中显式开启 LAN HTTP、配置设备 Key、防火墙限制来源；AndroidManifest 已设置 `android:usesCleartextTraffic="true"`。真实扫码/ERP SQL 写入仍需现场联调。

## PowerShell · 分项目验证

```powershell
# 仓库根目录
dotnet build StockCount.Backend.slnx -c Debug
dotnet build src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj -c Debug
dotnet test tests/StockCount.Server.Hosting.Tests/StockCount.Server.Hosting.Tests.csproj -c Debug
dotnet build src/StockCount.Pad/StockCount.Pad.csproj -f net10.0-android -c Debug
# 仅当 Windows + MAUI SDK/Android SDK 已齐全：
dotnet build StockCount.slnx -c Debug
```

**验收原则**：后端/API 与 Avalonia 构建通过不等于 Android APK 构建通过；Android APK 构建通过也不代表海康真机扫码与企业 SQL Server 写入通过。生产 COB/XTEST 仍默认禁止写入，须分开授权。
