---
title: StockCount · 跨平台应用图标与品牌资源
aliases: [StockCount Icon Pack, StockCount 品牌图标]
tags: [StockCount, Branding, SVG, Avalonia, MAUI, Tauri]
status: active
---

# StockCount 应用图标

两套图标使用统一深蓝色背景、青色扫描/网络状态和绿色确认标记：

- **PDA / Android**：手持扫码设备、纸箱条码和绿色盘点确认，源图 [pda.svg](pda.svg)。
- **Windows 服务端**：服务器机架、网络节点与管理状态，源图 [server.svg](server.svg)。

源文件采用真正的 SVG 路径和几何形状重新绘制，不是把 PNG 包装进 SVG。文件在 [SVG 尺寸预设](https://github.com/969981/StockCount/tree/main/assets/branding/variants) 提供 **64、128、256、512、1024** 像素的画布预设；SVG 使用同一个 `viewBox="0 0 512 512"`，可以无损自由缩放，预设不改变线条或色彩。

## 实际应用接入

| 平台 | 图标接入 | 产物 |
| --- | --- | --- |
| Android MAUI APK | `src/StockCount.Pad/Resources/AppIcon/appicon.svg` 的 `MauiIcon` | Resizetizer 自动生成各 density 的 Android Launcher 图标 |
| Windows Tauri 2（现有 Setup/Portable） | `desktop/server-console/assets/app-icon.svg` | `npm run tauri -- icon` 生成 `src-tauri/icons/` 下的 `icon.ico`、PNG 等 |
| Windows Avalonia .NET 10 | `scripts/prepare-windows-brand-icons.ps1` | 从 **同一服务器 SVG** 生成 Tauri ICO 并复制为 `src/StockCount.Server.Desktop/Assets/server.ico`，作为 EXE 和主窗口图标 |

## 构建

```powershell
# Windows：生成 Tauri + Avalonia 所需 ICO
pwsh -File .\scripts\prepare-windows-brand-icons.ps1

# Avalonia Debug / Release（MSBuild 自动执行上述准备步骤）
dotnet build src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj -c Debug
pwsh -File .\scripts\build-avalonia-desktop.ps1 -Configuration Release

# Android：重新生成 APK Launcher 图标
dotnet build src/StockCount.Pad/StockCount.Pad.csproj -c Debug -f net10.0-android

# Tauri Windows Setup / Portable
pwsh -File .\scripts\build-local.ps1 -Target Windows
```

首次生成 Windows 图标需要 Node.js 22+ 和 npm：共用项目已有的 `@tauri-apps/cli`，脚本会在缺失依赖时调用 `npm install`。生成的 `*.ico`、Android mipmap PNG 由构建产生，不纳入源码 Git 提交；原始 SVG 才是唯一可维护的设计源。

## Windows Avalonia Portable 图标随软件部署

新的服务器图标会在 Windows Avalonia 构建时生成 ICO 并写入 EXE 资源，因此 Portable ZIP **无需另外复制 SVG/ICO 到运行目录**。请使用完整发布目录打包，避免缺失 .NET 原生依赖：

```powershell
pwsh -File .\scripts\build-local.ps1 -Target Avalonia -Version 0.2.0-local
pwsh -File .\scripts\deploy-local.ps1 -Target AvaloniaPortable `
  -PackagePath .\dist\local\StockCount-Avalonia-v0.2.0-local-Windows-x64-Portable.zip `
  -Destination D:\Apps\StockCount-Avalonia -Launch
```

正式统一发布流程还会将 Avalonia Portable ZIP 作为独立的第五份程序资产上传，和 Android Debug/Release APK、Tauri Setup/Portable 并列。提交 `main` 只会触发 CI 构建与 QA 产物，不会绕过 Android JKS 签名门禁自动创建 Release。完整部署说明：[本地部署手册](../../docs/LOCAL_BUILD_DEPLOY.md) · [发布指南](../../docs/RELEASE_PACKAGING.md)。
## 发布验证

- GitHub Actions 的 Avalonia 构建检查真实 `server.ico` 和 Windows EXE。
- Android Debug/Release 工作流编译新的 `MauiIcon`，新 APK 的启动器图标随构建更新。
- Tauri 工作流和统一 Release 工作流生成 Windows `icon.ico` 和 NSIS 安装包。
- 正式 Android Release APK 仍需要仓库预先配置持久签名证书的 Secrets；没有签名密钥时，不能宣称完整的 GitHub Release 已发布。
