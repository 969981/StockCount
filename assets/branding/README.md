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
| Android MAUI APK | `appicon.svg`（满幅背景）+ `appiconfg.svg`（透明 PDA 前景）；`ForegroundScale=0.75` | 普通与圆形图标、Android 8+ 自适应图标及各密度 PNG |
| Windows Tauri 2（现有 Setup/Portable） | `desktop/server-console/assets/app-icon.svg` | `npm run tauri -- icon` 生成 `src-tauri/icons/` 下的 `icon.ico`、PNG 等 |
| Windows Avalonia .NET 10 | `src/StockCount.Server.Desktop/Assets/server.ico` | 由服务器 SVG 派生的多尺寸 ICO，已纳入 Git 与源码 ZIP，直接作为 EXE 和主窗口图标 |

## Android Launcher 图标

原有品牌主图仍为 [pda.svg](pda.svg)。Android 使用两层 SVG，保留原有 PDA、箱子、条码、绿色确认勾。背景填满 512×512 画布，前景不含底板；75% 缩放给系统圆形、圆角方形等裁切留出空间。Manifest 显式设置 `android:icon="@mipmap/appicon"` 与 `android:roundIcon="@mipmap/appicon_round"`，修复系统回退到默认安卓图标的问题。

构建与发布前通过 [APK 图标验证脚本](../../scripts/verify-android-launcher-icon.ps1) 调用 Android SDK aapt2 检查实际 APK 的资源表、Manifest 引用、两种 adaptive-icon XML 和各自 PNG，而不是只验证源 SVG。设备桌面视觉及缓存刷新仍需真机验收。

## 构建

```powershell
# 仅在品牌 SVG 修改后手动更新 ICO（需要 Node/npm）
pwsh -File .\scripts\prepare-windows-brand-icons.ps1 -Force

# Avalonia Debug / Release（直接使用已提交 ICO，不需要 Node/npm）
dotnet build src/StockCount.Server.Desktop/StockCount.Server.Desktop.csproj -c Debug
pwsh -File .\scripts\build-avalonia-desktop.ps1 -Configuration Release

# Android：重新生成 APK Launcher 图标
dotnet build src/StockCount.Pad/StockCount.Pad.csproj -c Debug -f net10.0-android

# Tauri Windows Setup / Portable
pwsh -File .\scripts\build-local.ps1 -Target Windows
```

仅 Tauri 图标生成或手动刷新 Avalonia ICO 需要 Node.js 22+ 和 npm。Avalonia 的 `Assets/server.ico` 是刻意提交的派生资源，源码 ZIP 也必须包含；其余 Tauri 生成图标、Android mipmap PNG 仍由构建生成。原始 SVG 是设计源。

## Windows Avalonia Portable 图标随软件部署

服务器 ICO 已随源码提交，Windows Avalonia 构建时直接写入 EXE 资源，因此 Portable ZIP **无需另外复制 SVG/ICO 到运行目录**。请使用完整发布目录打包，避免缺失 .NET 原生依赖：

```powershell
pwsh -File .\scripts\build-local.ps1 -Target Avalonia -Version 0.2.0-local
pwsh -File .\scripts\deploy-local.ps1 -Target AvaloniaPortable `
  -PackagePath .\dist\local\StockCount-Avalonia-v0.2.0-local-Windows-x64-Portable.zip `
  -Destination D:\Apps\StockCount-Avalonia -Launch
```

正式统一发布流程还会将 Avalonia Portable ZIP 作为独立的第五份程序资产上传，和 Android Debug/Release APK、Tauri Setup/Portable 并列。提交 `main` 只会触发 CI 构建与 QA 产物，不会绕过 Android JKS 签名门禁自动创建 Release。完整部署说明：[本地部署手册](../../docs/LOCAL_BUILD_DEPLOY.md) · [发布指南](../../docs/RELEASE_PACKAGING.md)。
## 发布验证

- GitHub Actions 的 Avalonia 构建检查真实 `server.ico` 和 Windows EXE。
- Android Debug/Release 工作流校验真实 APK 的自适应启动器资源；正式签名包也执行同一校验。
- Tauri 工作流和统一 Release 工作流生成 Windows `icon.ico` 和 NSIS 安装包。
- 正式 Android Release APK 仍需要仓库预先配置持久签名证书的 Secrets；没有签名密钥时，不能宣称完整的 GitHub Release 已发布。
