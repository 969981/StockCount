---
title: "StockCount PDA Release APK 编译与 ADB 安装手册"
aliases: ["PDA Release APK 编译", "ADB 安装手册"]
created: 2026-11-25
updated: 2026-11-25
status: documented
type: guide
area: pda
tags: [StockCount, Android, MAUI, Release, ADB]
up: "[[docs/ANDROID_RELEASE_SIGNING]]"
---

# StockCount PDA：Release APK 编译与 ADB 安装手册

更新日期：2026-11-25 | 平台：Windows x64 / PowerShell 7 | 适用：StockCount main 分支

> **核心原则：正式 Release 必须使用同一个长期保管的 JKS 签名。** Debug 与 Release 默认包名相同，但签名通常不同。存在待确认盘点时，不得卸载旧应用、清除应用数据或重新生成请求 UUID。

## 1. 快速了解项目

| 项目 | 当前配置 |
|---|---|
| 仓库 | https://github.com/969981/StockCount |
| Android 项目 | `src/StockCount.Pad/StockCount.Pad.csproj` |
| 目标框架 | `net10.0-android` |
| 包名 | `com.dreamer.stockcount.pad` |
| 当前版本名称（versionName） | `0.2.0` |
| 当前版本代码（versionCode） | `3` |
| 最低 Android API | 26（Android 8.0） |
| Release 输出目录 | `dist\local\` |

仅构建 Android 客户端，无需编译 Windows 管理器、Tauri、Node.js 或 Rust，也无需连接 SQL Server。Release 用于实际运行及性能对照，不等于生产数据库业务验收完成。

## 2. 首次准备编译环境

如果已经能够通过 Visual Studio 编译 Debug APK，通常已有大部分依赖。

1. 安装 Windows x64 版 PowerShell 7、Git 和 .NET 10 SDK（不是仅安装 Runtime）。
2. 安装与 .NET 10 Android workload 兼容的 JDK、Android SDK 平台及 Build-Tools；可通过 Visual Studio Installer 的 MAUI/Android 开发工作负载准备。具体 SDK/JDK 版本以当前 workload 的要求为准。
3. 安装 Android SDK Platform-Tools，使 `adb` 可用；确保 JDK `bin` 下的 `keytool` 可用。

在 PowerShell 7 中检查：

```powershell
pwsh --version
git --version
dotnet --version
dotnet workload list
java -version
Get-Command keytool
adb version
```

缺少 MAUI Android workload 时执行：

```powershell
dotnet workload install maui-android
```

检查 Android SDK：

```powershell
$env:ANDROID_HOME
$env:ANDROID_SDK_ROOT
$env:JAVA_HOME
Get-Command adb
```

如果 SDK 安装在默认用户目录且 adb 不在 PATH，可临时设置（如果使用 Visual Studio 自定义 SDK 路径，请替换为实际路径）：

```powershell
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$env:Path = "$env:ANDROID_HOME\platform-tools;$env:Path"
adb version
Get-ChildItem "$env:ANDROID_HOME\build-tools" -Directory
```

仓库签名脚本在 `ANDROID_HOME`、`ANDROID_SDK_ROOT` 和默认 SDK 目录下查找 `build-tools\...\apksigner.bat`。若 SDK 或 JDK 缺失，请按编译错误补齐；不要仅靠安装 workload 就假定全部工具已经就绪。

## 3. 更新 StockCount 源码

已有本地仓库时，进入你的实际目录（以下为示例）：

```powershell
Set-Location 'D:\WorkSpace\Work\Business\00_WorkLogs\StockCount'
git status
git switch main
git pull --ff-only origin main
git rev-parse --short HEAD
```

如果尚未克隆：

```powershell
git clone https://github.com/969981/StockCount.git
Set-Location StockCount
```

若 `git status` 显示有未提交修改，先保存或提交自己的修改，不要使用 `git reset --hard` 强行覆盖。

## 4. 创建 Release 签名密钥（只需一次）

### 4.1 已有正式 JKS：直接跳到第 5 节

必须继续使用之前签署 Release APK 的同一个 JKS 和 alias。如果以前配置过 GitHub Actions 正式签名，也应复用其原始 JKS。

### 4.2 第一次签署 Release

在仓库根目录执行：

```powershell
pwsh -NoProfile -File .\scripts\new-android-release-jks.ps1 `
  -OutputPath 'D:\StockCountSecrets\stockcount-release.jks' `
  -Alias stockcount
```

脚本会调用 JDK `keytool`，交互输入 keystore 密码、证书信息与 alias 私钥密码。脚本拒绝覆盖已有 JKS，也拒绝把 JKS 放在 Git 工作区内。

长期保管以下信息：JKS 文件、alias `stockcount`、keystore 密码、alias 密码。至少保留一份独立加密备份，密码不写入仓库、命令历史或聊天。若以后丢失密钥，通常无法原地更新已安装的同签名 Release。

查询证书信息（交互输入密码）：

```powershell
keytool -list -v -keystore 'D:\StockCountSecrets\stockcount-release.jks' -alias stockcount
```

记录证书 SHA-256 指纹以便后续核对。JKS 文件本身的 SHA256 与证书指纹不是同一概念。

## 5. 推荐：使用仓库脚本编译 Release APK

以下在仓库根目录的 PowerShell 7 中执行。密码仅在当前终端进程及子进程环境变量中暂存；构建结束后清除。

```powershell
$storeSecure = Read-Host 'JKS keystore 密码' -AsSecureString
$keySecure   = Read-Host 'Alias key 密码' -AsSecureString
try {
    $env:STOCKCOUNT_ANDROID_STORE_PASS = [System.Net.NetworkCredential]::new('', $storeSecure).Password
    $env:STOCKCOUNT_ANDROID_KEY_PASS = [System.Net.NetworkCredential]::new('', $keySecure).Password

    pwsh -NoProfile -File .\scripts\build-local.ps1 `
      -Target Android `
      -AndroidVariant Release `
      -Version 0.2.0-local `
      -ReleaseKeyStore 'D:\StockCountSecrets\stockcount-release.jks' `
      -ReleaseKeyAlias stockcount
    if ($LASTEXITCODE -ne 0) { throw 'Release 构建失败，请检查日志。' }
} finally {
    Remove-Item Env:STOCKCOUNT_ANDROID_STORE_PASS, Env:STOCKCOUNT_ANDROID_KEY_PASS -ErrorAction SilentlyContinue
    $storeSecure.Dispose()
    $keySecure.Dispose()
}
```

说明：

- `-Target Android` 只构建 Android 目标；`-AndroidVariant Release` 明确指定 Release（默认其实是 Debug）。
- `build-local.ps1` 默认先运行 Core 单元测试，再编译、签名和校验 APK，不建议使用 `-SkipTests` 部署现场版本。
- `-Version 0.2.0-local` 为打包文件名后缀，不会把软件版本或 versionCode 改掉。项目版本变化后，请改成匹配的新版本或省略 `-Version`。
- 可使用 `-OutputDirectory` 自定义输出路径。

成功后的 APK：

```text
dist\local\StockCount-PDA-v0.2.0-local-Android-Release-Signed.apk
```

同目录还会有本次构建的 `SHA256SUMS.txt` 和 `build-manifest.json`。旧文件可能残留，只有本次脚本成功并且清单匹配，才可安装。

```powershell
$apk = (Resolve-Path '.\dist\local\StockCount-PDA-v0.2.0-local-Android-Release-Signed.apk').Path
Get-Item $apk | Select-Object Name, Length, LastWriteTime
Get-FileHash $apk -Algorithm SHA256
Get-Content .\dist\local\SHA256SUMS.txt
```

## 6. 备用：直接使用 dotnet publish

推荐仍使用第 5 节脚本，因为它额外检查签名、图标、输出文件及 SHA256。若要手动排查编译，在已经按第 5 节设置密码环境变量的同一终端（清除之前）运行：

```powershell
dotnet publish .\src\StockCount.Pad\StockCount.Pad.csproj `
  -f net10.0-android -c Release `
  -p:AndroidPackageFormats=apk `
  -p:AndroidKeyStore=true `
  '-p:AndroidSigningKeyStore=D:\StockCountSecrets\stockcount-release.jks' `
  '-p:AndroidSigningKeyAlias=stockcount' `
  '-p:AndroidSigningStorePass=env:STOCKCOUNT_ANDROID_STORE_PASS' `
  '-p:AndroidSigningKeyPass=env:STOCKCOUNT_ANDROID_KEY_PASS'
```

产物一般在 `src\StockCount.Pad\bin\Release\net10.0-android\publish\`，选择本次生成的 `*-Signed.apk`，不要选未签名 APK 或旧产物。手工构建后使用 SDK Build-Tools 的 `apksigner.bat verify --verbose --print-certs <APK路径>` 验证签名。完成后仍须清理密码环境变量。

## 7. Android 真机连接 ADB

1. PDA 设置 → 关于设备，按设备系统要求多次点击版本号启用开发者选项。
2. 开发者选项中打开 USB 调试；使用可传输数据的 USB 线连接 Windows。
3. 解锁 PDA，确认是否允许此电脑进行 USB 调试。

```powershell
adb devices -l
```

示例输出（序列号是示例）：

```text
List of devices attached
PDA_SERIAL    device product:... model:...
```

仅在设备状态为 `device` 时安装。`unauthorized` 表示尚未授权，`offline` 表示连接异常。连接多台 PDA 或模拟器时必须用 `-s` 指定真实序列号。

```powershell
$serial = 'PDA_SERIAL'  # 替换为 adb devices -l 中的实际序列号
adb -s $serial shell getprop ro.product.model
adb -s $serial shell getprop ro.build.version.release
adb -s $serial shell getprop ro.product.cpu.abilist
```

## 8. 安装 / 升级 Release APK

### 8.1 推荐直接使用 adb

```powershell
$apk = (Resolve-Path '.\dist\local\StockCount-PDA-v0.2.0-local-Android-Release-Signed.apk').Path
adb -s $serial install -r $apk
```

只有一台设备时，可以简写为 `adb install -r $apk`。首次安装也可使用 `-r`；同包名、同签名的覆盖升级一般保留应用数据。看到 `Success` 才算安装成功。

### 8.2 使用仓库部署脚本（可自动启动）

```powershell
pwsh -NoProfile -File .\scripts\deploy-local.ps1 `
  -Target Android `
  -PackagePath $apk `
  -DeviceSerial $serial `
  -Launch
```

脚本核对设备授权、执行 `adb install -r -t`，并在指定 `-Launch` 时启动 StockCount。它不会自动卸载旧应用。

### 8.3 检查安装结果与启动

```powershell
adb -s $serial shell pm path com.dreamer.stockcount.pad
adb -s $serial shell dumpsys package com.dreamer.stockcount.pad |
  Select-String 'versionName=|versionCode=|DEBUGGABLE'
adb -s $serial shell monkey -p com.dreamer.stockcount.pad -c android.intent.category.LAUNCHER 1
```

核对版本、是否存在 DEBUGGABLE 标志，并进入设置确认「客户端性能诊断」显示 Release/非Debug。注意：包的版本号相同并不能证明已换成新构建；同时核对构建来源和安装结果。

## 9. 重要：从 Debug 切换到正式 Release

当前 Debug 与 Release 使用同一包名，通常签名不同。如果出现 `INSTALL_FAILED_UPDATE_INCOMPATIBLE`，不可直接覆盖。优先确认旧应用签名、是否可以沿用原正式 JKS。

**若确实必须从不同签名的 Debug 迁移，先完成下列检查：**

- 在旧应用中处理全部待确认提交；使用原 UUID 查询结果，必要时使用同一个原 UUID 重试。不要创建新请求代替未知结果。
- 确认不存在尚未提交的箱信息和数量；与后台已盘记录对账。
- 记录 API 地址、帐套、扫码 Action/Extra 及设备密钥的安全配置方式；不要把密钥明文存入普通文档。
- 获得现场负责人的确认。Android 卸载会清除该应用的本地数据，包括 Preferences、SecureStorage 及待确认草稿；不能保证通过普通 ADB 备份恢复加密数据。

仅当以上条件全部满足、且确实需要更换签名时，在指定的测试 PDA 上手动执行：

```powershell
# 危险：这一条会删除应用和本地数据，不是普通升级操作。
adb -s $serial uninstall com.dreamer.stockcount.pad
# 确认卸载成功后再安装正式签名 APK。
adb -s $serial install $apk
```

安装后重新配置 PDA。以后持续使用同一 Release JKS，即可按第 8 节 `install -r` 更新，一般不需要卸载。不要使用 `pm clear`、反复卸载或 `-d` 强行解决正常升级问题。

## 10. 日后发布新版本

正式版本的 Android versionCode 应递增。当前仓库版本是 0.2.0 / 3。若正式升级为 0.2.1，可先查看版本并预演：

```powershell
pwsh -NoProfile -File .\scripts\get-app-version.ps1
pwsh -NoProfile -File .\scripts\set-app-version.ps1 -Version 0.2.1 -AndroidVersionCode 4 -WhatIf
```

确实要发布新软件版本时再去掉 `-WhatIf`。该脚本会同步项目中的多个软件版本配置，不只是 APK 文件名。请确认 versionCode 高于现场已装版本，并提交版本变更。然后按第 5 节重新编译（使用新版本名或省略 `-Version`），仍使用原 JKS。

## 11. 真机验收与性能对照

先在 XTEST 测试帐套进行，正式帐套不要用作压力测试。新版 APK 安装后：

- 设置 API 地址，测试连接，核对帐套和扫码广播参数。
- 扫描有效库位、未盘箱码，输入整数和小数；确认写入与历史记录。
- 验证重复箱拒绝、断网后原 UUID 恢复、成功后 400ms 非阻塞提示和连续扫码。
- 性能诊断先清空、热身后再次清空，再完成约 30 箱合法不重复的测试；回到设置复制报告，比较 Debug 与 Release 的扫码、提交、HTTP、本地草稿、界面刷新耗时。错误与人工等待应单独采样。

辅助 ADB 命令：

```powershell
# 查看帧统计与内存（不修改业务数据）
adb -s $serial shell dumpsys gfxinfo com.dreamer.stockcount.pad framestats
adb -s $serial shell dumpsys meminfo com.dreamer.stockcount.pad
# 查看崩溃日志
adb -s $serial logcat -d -b crash
```

日志可能含企业业务信息，仅在受控环境保存与共享。UI 刷新同步耗时不是实际绘制帧时间，HTTP 耗时也包含后台处理；不能直接据此归因。Release 的性能改善幅度需要实际设备测量。

## 12. 常见问题

| 现象 | 处理 |
|---|---|
| dotnet 找不到或非 10.x | 安装 .NET 10 SDK，检查 PATH |
| 缺少 maui-android | `dotnet workload install maui-android` |
| 找不到 SDK/JDK 或许可证错误 | Visual Studio Installer/SDK Manager 补齐 workload 对应依赖并接受许可证 |
| 找不到 apksigner.bat | 安装 Android SDK Build-Tools，确认 ANDROID_HOME 指向实际 SDK |
| 签名口令错误 | 检查 JKS 与 alias，重新交互输入正确密码；不要重新生成 JKS |
| 版本名与打包参数不一致 | 省略 -Version 或采用当前项目版本加合法后缀 |
| 找不到 adb | 安装 Platform-Tools 并加入 PATH |
| unauthorized / offline | 解锁设备接受授权，检查数据线，必要时重连 ADB |
| more than one device/emulator | 使用 `adb -s 实际序列号` |
| INSTALL_FAILED_UPDATE_INCOMPATIBLE | 签名可能不同；按第 9 节检查，不直接卸载 |
| INSTALL_FAILED_VERSION_DOWNGRADE | 提高真实 versionCode，不强行降级 |
| INSTALL_FAILED_NO_MATCHING_ABIS | 核对 PDA ABI 与 APK 包含的架构 |
| INSTALL_FAILED_OLDER_SDK | 核对设备 Android 版本，当前项目最低 API 26 |
| Release 启动闪退 | 收集 crash log，检查 Release 裁剪/运行时问题；不要将 Debug 成功当成 Release 验收 |

## 13. 参考资料

仓库依据（已读取 main 中相关脚本及工程文件）：

- [Android Release 签名手册](https://github.com/969981/StockCount/blob/main/docs/ANDROID_RELEASE_SIGNING.md)
- [本地构建与部署](https://github.com/969981/StockCount/blob/main/docs/LOCAL_BUILD_DEPLOY.md)
- [统一构建脚本](https://github.com/969981/StockCount/blob/main/scripts/build-local.ps1)
- [Release 签名构建脚本](https://github.com/969981/StockCount/blob/main/scripts/build-android-release.ps1)
- [JKS 生成脚本](https://github.com/969981/StockCount/blob/main/scripts/new-android-release-jks.ps1)
- [部署脚本](https://github.com/969981/StockCount/blob/main/scripts/deploy-local.ps1)
- [PDA 性能计划](https://github.com/969981/StockCount/blob/main/docs/pda/PDA_PERFORMANCE_PLAN.md)

官方工具文档：

- [Microsoft Learn：使用命令行发布 .NET MAUI Android 应用](https://learn.microsoft.com/zh-cn/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0)
- [Android Developers：ADB](https://developer.android.com/tools/adb)
- [Android SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools)

本手册依据仓库源码编写，未在你的 Windows 电脑或 PDA 上实际执行编译、签名或安装。示例路径、设备序列号与版本须结合现场实际配置调整。
