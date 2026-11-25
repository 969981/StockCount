---
aliases: ["StockCount 本地构建与部署", "Build and Deploy"]
title: StockCount · Windows 本地编译、部署和源码归档
created: 2026-11-23
updated: 2026-11-24
status: implementation-guide
tags: [StockCount, PowerShell, Build, Deploy, Source, APK, Tauri, Avalonia, Obsidian]
---

# StockCount · 本地编译、部署与源码打包手册

> 对应当前仓库的真实工程：MAUI Android PDA、ASP.NET Core 10 API、Tauri 2 和 Avalonia Windows 服务管理器。本手册的脚本已提交，但**在本对话环境中没有 Windows/.NET/Android 工具链，最终执行以本机结果或 Windows GitHub Actions 为准**。

## 一、统一脚本入口与平台

| 文件 | 用途 | 默认结果 |
| --- | --- | --- |
| [build-local.ps1](../scripts/build-local.ps1) | 统一构建 + 自动打包 | Android APK、Tauri Setup/Portable、Avalonia Portable 与 SHA256 |
| [deploy-local.ps1](../scripts/deploy-local.ps1) | 本地安装或升级 | Android ADB、Tauri NSIS/Portable、Avalonia Portable 安全升级 |
| [package-source.ps1](../scripts/package-source.ps1) | 将 Git 源码做可复现压缩包 | StockCount-Source-短CommitHash.zip、SourceInfo.txt、SHA256 文件 |
| [package-avalonia-portable.ps1](../scripts/package-avalonia-portable.ps1) | 打包双 EXE 单文件发布目录 | 含依赖的 StockCount-Avalonia-Portable.zip，拒绝现场配置 |
| [deploy-avalonia-portable.ps1](../scripts/deploy-avalonia-portable.ps1) | Avalonia 更新执行器 | 逐文件校验、备份、失败尽力恢复 |
| [new-android-release-jks.ps1](../scripts/new-android-release-jks.ps1) | 首次生成长期 JKS | 私有 keystore，不覆盖已有文件 |
| [configure-android-release-secrets.ps1](../scripts/configure-android-release-secrets.ps1) | 交互上传四个 Secrets | GitHub Actions 签名凭据 |
| [publish-stockcount-release.ps1](../scripts/publish-stockcount-release.ps1) | 版本号参数化并触发 GitHub Release | Actions 任务，不直接创建未验收 Release |

原有 [build-tauri-sidecar.ps1](../scripts/build-tauri-sidecar.ps1) 和 [package-windows-portable.ps1](../scripts/package-windows-portable.ps1) 仍复用，不复制或重写已有后端打包逻辑。

## JKS 一次性生成与 GitHub Release 一键发布

如果需要把 **Debug APK、正式签名 Release APK、Tauri Windows Setup/Portable、Avalonia Portable** 同时发布到 GitHub Releases，可以直接使用三份专用脚本，不需要手动更改 Actions 中的版本号。

```powershell
# 1. 首次创建长期 JKS，密码由 keytool 交互输入；已存在的 JKS 不要重建
pwsh -File .\scripts\new-android-release-jks.ps1 -OutputPath D:\StockCountSecrets\stockcount-release.jks

# 2. 首次配置签名凭据到 GitHub Secrets（需要 gh auth login）
pwsh -File .\scripts\configure-android-release-secrets.ps1 -KeyStore D:\StockCountSecrets\stockcount-release.jks -KeyAlias stockcount

# 3. 发布之前先 -WhatIf 预演，再真正触发工作流
pwsh -File .\scripts\publish-stockcount-release.ps1 -Version 0.2.0-preview.3 -WhatIf
pwsh -File .\scripts\publish-stockcount-release.ps1 -Version 0.2.0-preview.3
```

上述发布命令**仅触发 CI**；必须确认构建和签名均成功、Releases 页面已有完整资产，才算发布成功。密钥保存于仓库之外，不能上传到公开 GitHub 代码或写入文档。只有第一次需要生成 JKS，后续发布必须沿用同一签名密钥。详细安全配置：[Android Release 签名指南](ANDROID_RELEASE_SIGNING.md) · [发布完整手册](RELEASE_PACKAGING.md)。

## 解决 Tauri exe was not built 与软件版本管理

直接运行 `package-windows-portable.ps1`（不带 `-Build`）只打包已经编译好的程序。如果 `desktop/server-console/src-tauri/target/release/stockcount-server-console.exe` 不存在，说明本机还没构建 Tauri Release EXE。GitHub Actions 成功并不会自动下载 EXE 到本机。

**推荐一键编译 + 打包**（软件版本自动读取）：

```powershell
cd 'D:\WorkSpace\Work\Business\00_WorkLogs\StockCount'
pwsh -NoProfile -File .\scripts\package-windows-portable.ps1 -Build
# 或使用统一构建入口，输出默认位于 dist\local
pwsh -NoProfile -File .\scripts\build-local.ps1 -Target Windows
```

已经编译过、只想重新打包时，无需再输入 Version，运行：

```powershell
pwsh -NoProfile -File .\scripts\package-windows-portable.ps1
```

### 版本号应该在软件配置中，而不是打包时才手输

版本权威来源是 `desktop/server-console/src-tauri/tauri.conf.json` 的 `version`；`get-app-version.ps1` 会检查 `Cargo.toml`、npm `package.json` 和 MAUI `ApplicationDisplayVersion` 与其一致。两个 Windows 编译/打包脚本都默认读取该版本。

```powershell
# 查看软件真实版本（当前仓库是 0.2.0）
pwsh -NoProfile -File .\scripts\get-app-version.ps1

# 如果实际要将软件升级到 1.0.1，先预演，再统一修改工程版本
# Android versionCode 为独立的递增整数；当前仓库为 3，下一版可改 4
pwsh -NoProfile -File .\scripts\set-app-version.ps1 -Version 1.0.1 -AndroidVersionCode 4 -WhatIf
pwsh -NoProfile -File .\scripts\set-app-version.ps1 -Version 1.0.1 -AndroidVersionCode 4

# 再编译，程序和 ZIP 将采用一致的 1.0.1 版本
pwsh -NoProfile -File .\scripts\build-local.ps1 -Target Windows
```

`-Version` 是可选的，主要用于附加同一基础版本的后缀。例如软件 `0.2.0` 允许打包成 `0.2.0-preview.3`；但程序 `0.2.0` 时传入 `1.0.1` 会被拒绝，避免出现安装包标为 `1.0.1` 而程序仍为 `0.2.0` 的情况。请先修改并提交真实版本配置，再生成新的发布文件。

## 二、本地环境准备（Windows 10/11 x64）

建议使用 **PowerShell 7**（不是必须以管理员启动）。需安装以下工具：

1. Git for Windows、**.NET SDK 10.x**；Android 构建需 Android SDK/Platform-Tools，以及 `dotnet workload install maui-android`。
2. **Node.js 22+**、npm、Rust `x86_64-pc-windows-msvc`，以及 Visual Studio C++ Build Tools（MSVC 编译器、Windows SDK）。这些属于 Windows Tauri 2 编译依赖。
3. 运行 Windows Tauri 桌面程序时需 **Microsoft Edge WebView2 Runtime**。Windows 11 常见预装，但不可依赖其必然存在。
4. Android 真机部署需 `adb`，PDA 开启开发者选项 / USB 调试，并在屏幕上授权电脑。
5. PDA 与 Windows 通过可信局域网 HTTP（例如 `http://192.168.1.100:5080`）联调，不需要 PFX；必须限制 Windows 防火墙的 PDA 来源网段，禁止公网映射，并独立配置 Sage X3 SQL Server 权限。

检查已安装工具：

```powershell
pwsh --version
git --version
dotnet --version
dotnet workload list
node --version
npm --version
rustc -vV
cargo --version
adb devices
```

克隆仓库并进入根目录：

```powershell
git clone https://github.com/969981/StockCount.git
cd StockCount
```

### 只第一次需要准备的 Android SDK

```powershell
dotnet workload install maui-android
```

`maui-android` 安装不等于 Android SDK、SDK License 或 Java 环境一定齐全。若 Android 构建提示缺少 SDK/JDK，按 .NET MAUI Android 工具链的错误安装缺失组件；也可以通过 Visual Studio 的 .NET MAUI / Android 开发工作负载统一安装。

## 三、一键本地编译

### 全部编译：Android APK + Tauri Setup/Portable + Avalonia Portable

```powershell
pwsh -NoProfile -File .\scripts\build-local.ps1 -Target All -Version 0.2.0-local
```

### 只编译 Tauri Windows（同时生成 Setup 与免安装 ZIP）

```powershell
pwsh -NoProfile -File .\scripts\build-local.ps1 -Target Windows -Version 0.2.0-local
```

### 只编译 Avalonia Windows 原生管理器及 Portable ZIP

不使用发布脚本、直接执行 dotnet 命令：见 [Avalonia 双 EXE 单行 dotnet publish 手册](StockCount-Avalonia-SingleFile-Publish-Guide.md)。

一条命令发布完整后台（管理器 csproj 自动发布 API 并汇总两个 EXE）：

~~~powershell
dotnet publish .\src\StockCount.Server.Desktop\StockCount.Server.Desktop.csproj -c Release -r win-x64 -o .\publish\avalonia-portable
~~~

仅在命令成功退出后使用输出目录。不要指向实际安装目录。

**推荐单行 PowerShell 命令**（一键编译、发布和压缩，无需 Git、Node.js 或 Rust）：

~~~powershell
pwsh -NoProfile -File .\scripts\package-avalonia-portable.ps1 -Build
~~~

编译输出默认在 publish/avalonia-desktop，只有两个 EXE，ZIP 默认在 dist/local 下，另附原有启动 CMD 与说明 TXT。解压后直接运行 StockCount.Server.Desktop.exe；API EXE 由管理器启动，无需手动运行。

首次从旧版升级构建工具时，如果 publish/avalonia-desktop 已有旧 DLL，先将该**构建输出目录**改名备份，或使用新目录（不要处理实际部署目录）：

~~~powershell
pwsh -NoProfile -File .\scripts\package-avalonia-portable.ps1 -Build -InputDirectory publish/avalonia-singlefile
~~~

只需生成两个 EXE、不需要 ZIP：

~~~powershell
pwsh -NoProfile -File .\scripts\build-avalonia-desktop.ps1 -Configuration Release -OutputDirectory publish/avalonia-singlefile
~~~

默认禁用裁剪，保留 Avalonia XAML、SQL Client、SQLite 及原生依赖。采用 .NET PublishSingleFile 与 IncludeNativeLibrariesForSelfExtract。默认框架依赖，不内置 .NET Runtime；目标机安装 x64 .NET 10 和 ASP.NET Core 10 Runtime。SDK 不允许框架依赖 EXE 内部压缩，ZIP 仍压缩。可选自包含压缩命令见单行发布手册。

**旧安装升级**：保留现有部署脚本，自动备份被替换的程序文件，但不自动删除旧版目录内 DLL。若希望彻底去除旧 DLL，在停止管理器及 API、确认数据库关闭后，将新版解压到新的可写目录，再备份和迁移原 server-settings.json、server-logs.db 及存在的 WAL/SHM（同一关闭状态下作为一组），保持同一 Windows 用户的 Credential Manager。旧目录完整保留供回退，不要同时启动两个管理器。正式部署仍需检查端口、SQL 连接、日志与 API 启停。


```powershell
# .NET 10，不需要 Node.js、npm、Rust 或 WebView2
pwsh -File .\scripts\build-local.ps1 -Target Avalonia -Version 0.2.0-local

# 或者独立编译、打包
pwsh -File .\scripts\build-avalonia-desktop.ps1 -Configuration Release
pwsh -File .\scripts\package-avalonia-portable.ps1 -Version 0.2.0-local
```

Avalonia Release 现在采用两个框架依赖单文件 EXE：`StockCount.Server.Desktop.exe` 和 `StockCount.Api.exe`，不再分发零散 DLL。运行时原生库可能首次运行解包到当前用户临时 .NET 缓存。`Assets/server.ico` 已随 Git 与源码 ZIP 分发，直接注入 Windows EXE 和窗口；无需在编译期间生成。
### Android 同时构建 Debug + 正式签名 Release APK

注意：**Release APK 必须使用项目长期保管的 JKS**。先阅读 [Android Release 签名手册](ANDROID_RELEASE_SIGNING.md)，将 keystore 放在仓库之外，并在当前终端设置签名口令环境变量（不得把密码写在命令行或仓库里）。

```powershell
pwsh -File .\scripts\build-local.ps1 -Target Android -AndroidVariant Both -Version 0.2.0-local -ReleaseKeyStore D:\StockCountSecrets\stockcount-release.jks -ReleaseKeyAlias stockcount
```

也可改为 `-AndroidVariant Release` 仅编译 Release；默认仍为 `Debug`。成功后 `dist\local\` 将额外包含 `StockCount-PDA-v0.2.0-local-Android-Release-Signed.apk`，并纳入 SHA256SUMS.txt。Debug 和 Release 均移除盘点页面模拟扫码输入控件，现场仅使用硬件扫码。

**注意**：使用另一个签名密钥发布的 APK 无法覆盖旧签名 APK；更换前先核对正在处理的盘点单和待确认 UUID，不要直接卸载。GitHub Actions 签名必须配置四个 Repository Secrets；没有密钥时只验证 Release 代码编译，不会上传伪装成 Release 的调试包。
### 只编译 Android PDA APK

```powershell
pwsh -NoProfile -File .\scripts\build-local.ps1 -Target Android -Version 0.2.0-local
```

如果首次安装 MAUI Android 工作负载，可带 `-InstallAndroidWorkload`；如果在受控本地开发环境临时跳过业务测试可带 `-SkipTests`，**不建议将跳过测试的构建用于正式现场部署**。

```powershell
pwsh -NoProfile -File .\scripts\build-local.ps1 -Target Android -Version 0.2.0-local -InstallAndroidWorkload
```

输出默认在仓库根目录 `dist\local\`；可用 `-OutputDirectory D:\Builds\StockCount` 自定义路径（不会修改软件运行时配置）。

```text
dist/
  local/
    StockCount-PDA-v0.2.0-local-Android-Debug-Signed.apk
    StockCount-Server-v0.2.0-local-Windows-x64-Setup.exe
    StockCount-Server-v0.2.0-local-Windows-x64-Portable.zip
    StockCount-Avalonia-v0.2.0-local-Windows-x64-Portable.zip
    SHA256SUMS.txt
    build-manifest.json
```

**说明**：如果仅选择 Windows、Android 或 Avalonia，清单仅包含本次目标成功构建的文件；旧版本或其它目标可能仍留在输出目录。请依据清单和本次版本号选取文件，不要将旧文件混合发布。构建任何一步失败都会抛出异常且不生成“成功清单”；请勿发布失败输出。

Windows Setup 使用 Tauri 2 NSIS 的当前用户安装模式；便携 ZIP 包含 `stockcount-server-console.exe`、`StockCount.Api.exe`、`Start-StockCount.cmd` 和说明；API Sidecar 采用 .NET 10 自包含发布。APK 当前为 **Debug 签名测试版本**，并非企业生产签名。

## 四、部署 Windows 免安装版（推荐本地调试）

### 首次部署

```powershell
pwsh -NoProfile -File .\scripts\deploy-local.ps1 -Target WindowsPortable -PackagePath .\dist\local\StockCount-Server-v0.2.0-local-Windows-x64-Portable.zip -Destination D:\Apps\StockCount-Server -Launch
```

部署脚本验证 ZIP 条目没有路径穿越，解压到独立临时目录，确认四个必需文件存在，核对复制后的 SHA256，并在成功后可选启动软件。

### 旧版升级（保留数据库日志）

先在软件内点击“停止服务”，然后**完全退出 Tauri 管理器和 StockCount.Api 子进程**，再执行同一条命令。脚本会检查进程仍在运行则拒绝更新，以免损坏 SQLite WAL。

脚本仅覆盖四个程序文件，保留部署目录中的 **`server-settings.json`、`server-logs.db`、`server-logs.db-wal`、`server-logs.db-shm`** 及其它文件。若有旧版程序文件，将在目标目录保存 `backup-binaries-时间戳`；升级复制失败会尽可能从此备份恢复原二进制。

**注意**：非敏感设置 `server-settings.json` 与日志 `server-logs.db` 都在主程序 EXE 同目录。便携目录必须由当前用户拥有写入权限；不要部署到受保护的 Program Files 文件夹。第一次运行时，如同目录配置缺失，会尝试从旧路径 `%LOCALAPPDATA%\StockCount\ServerHost\server-settings.json` 校验并复制，旧文件保留。若已有同目录配置则优先使用，不覆盖。数据库密码和设备密钥仍在 Windows Credential Manager，跨电脑/Windows 用户不会自动转移。

### Avalonia Portable 首次部署与升级

```powershell
pwsh -File .\scripts\deploy-local.ps1 -Target AvaloniaPortable `
  -PackagePath .\dist\local\StockCount-Avalonia-v0.2.0-local-Windows-x64-Portable.zip `
  -Destination D:\Apps\StockCount-Avalonia -Launch
```

Avalonia Portable 使用独立目录，避免与 Tauri 混用 EXE/配置；启动前检查 API 端口，不能同时启动两个后台。升级前必须退出两个管理器与 API。脚本校验 ZIP 条目、完整复制两个单文件 EXE 与辅助说明、逐文件核对 SHA256，并备份被替换的旧文件，失败时尽力回滚。

**现场数据不由升级程序覆盖**：`server-settings.json`、`server-logs.db`、`server-logs.db-wal`、`server-logs.db-shm` 保持原位；Credential Manager 凭据保持不变。若发布目录混入这些数据或 JKS/PFX/密钥，打包脚本会拒绝创建 ZIP。Avalonia 当前为框架依赖 Portable（需要 x64 .NET 10 与 ASP.NET Core 10 共享运行时），Windows NSIS Setup 仍为 Tauri。两套 ERP 写入门禁保持独立且默认禁止。
### Windows Setup 正常安装

```powershell
pwsh -NoProfile -File .\scripts\deploy-local.ps1 -Target WindowsSetup -PackagePath .\dist\local\StockCount-Server-v0.2.0-local-Windows-x64-Setup.exe
```

这会打开 NSIS 安装向导，按正常交互安装；不会擅自使用静默安装参数、提升权限或修改数据库。可以直接双击 Setup.exe 获得相同效果。

## 五、Android APK 安装 / 升级

### 自定义图标刷新与真机验收

若旧 APK 仍显示默认 Android 图标，更新到含 adaptive icon 和 Manifest 绑定的提交。关闭 VS 后只清理 PDA 的构建目录，重新构建并验证 APK：

```powershell
Remove-Item .\src\StockCount.Pad\bin,.\src\StockCount.Pad\obj -Recurse -Force -ErrorAction SilentlyContinue
pwsh -File .\scripts\build-local.ps1 -Target Android
```

构建脚本通过 Android SDK aapt2 验证 APK 内图标。优先同签名覆盖安装（保留设备 Key、设置、待确认请求 UUID）。仅当覆盖安装后图标仍是缓存旧图标、且所有待确认盘点已处理并备份配置时，才可手动卸载测试设备的 `com.dreamer.stockcount.pad` 再安装。不要自动卸载现场生产 PDA。



连接已授权 PDA，先检查设备：

```powershell
adb devices
pwsh -NoProfile -File .\scripts\deploy-local.ps1 -Target Android -PackagePath .\dist\local\StockCount-PDA-v0.2.0-local-Android-Debug-Signed.apk
```

多设备时显式指定设备序列号，避免安装到错误 PDA：

```powershell
pwsh -NoProfile -File .\scripts\deploy-local.ps1 -Target Android -PackagePath .\dist\local\StockCount-PDA-v0.2.0-local-Android-Debug-Signed.apk -DeviceSerial PDA_SERIAL -Launch
```

脚本执行 `adb install -r -t`，保留同签名版本的应用数据；如果提示 `INSTALL_FAILED_UPDATE_INCOMPATIBLE`，说明 APK 签名不同，**脚本不会自动卸载**。Android 卸载会清除应用 SecureStorage 等数据，必须先解决未确认盘点单、UUID、草稿的数据迁移或备份，再决定后续操作。

新安装 PDA 默认 API 为 `http://192.168.1.1:5080`，Avalonia 新配置默认 SQL Server 为 `192.168.1.1,1433`；已保存地址不覆盖。可在 PDA 设置页修改 Windows 局域网 IP:5080（自动补全 http://）、选择 Test/XTEST 环境及配置海康扫码广播 Action/Extra。Avalonia 的 API/PDA 页启用局域网 HTTP 与局域网免密连接，保存连接即可，无需 PDA 密钥；旧配置保留原认证方式，主动勾选免密才切换。Tauri 旧版仍可使用可选设备密钥。免密时同网段设备可直接访问业务 API，必须使用隔离网络、Windows 防火墙和 ACL，禁止公网映射。

写入设置独立保存：四项在无审计编码时默认预选为界面草稿，不代表已核验或实际启用；保存连接不受空审计编码阻塞。现场核实对应帐套后确认保存写入授权即可；X3 操作员编码可留空，Avalonia 自动使用程序标识 `PDA`（写入 CREUSR_0/UPDUSR_0），不是个人用户身份。现场若要求使用已批准的 X3 编码，可选填 1–5 字符；真实数据库是否接受该标识须现场核验。更换数据库身份会撤销已有授权。

## 六、打包完整源码（独立于编译产物）

**当前完整源码 ZIP 同时包含：** `StockCount.slnx`（API + Avalonia Windows + MAUI Android）、`StockCount.Backend.slnx`（仅后端）、`src/StockCount.Server.Desktop/`、`src/StockCount.Server.Hosting/`、`src/StockCount.Pad/`、`src/StockCount.Api/`、Tauri 2 管理器、GitHub Actions、SQL 脚本和文档。无需分别压缩 Windows UI 与 Android UI。

本次增强的打包校验不仅检查项目文件存在，还会逐项核对 **ZIP 内所有已提交源码**和两个 `.slnx` 的全部项目引用；如果任意 Avalonia、MAUI、Hosting 工程或其它已提交文件缺失，则报错而非输出貌似完整的源码包。旧版 Tag 不包含 Avalonia 时仍按该历史版本原有布局打包，不强加新项目要求。

**注意区分源码打包与二进制构建：** `package-source.ps1` 不编译，不生成 EXE / APK；`build-local.ps1` 的 `All` 目标构建 Tauri Setup/Portable、Avalonia Portable 与 Android APK；Avalonia 也可独立运行 `build-avalonia-desktop.ps1` + `package-avalonia-portable.ps1`。源码 ZIP 只是供接收方重新构建的完整快照，不含 NuGet/npm/Cargo 已下载依赖或本地密钥。



### 当前提交的所有 Git 源码

```powershell
git status
pwsh -NoProfile -File .\scripts\package-source.ps1
```

### 对指定 Tag 制作可复现源码 ZIP

```powershell
git fetch --tags
pwsh -NoProfile -File .\scripts\package-source.ps1 -Ref v0.2.0-preview.2
```

源码默认打包到 `dist\source\`：

```text
dist/
  source/
    StockCount-Source-a39ec60.zip
    StockCount-Source-a39ec60.SourceInfo.txt
    StockCount-Source-a39ec60.SHA256SUMS.txt
```

**打包语义**：严格使用 `git archive` 导出指定 Git 提交（`.cs/.csproj/.xaml/.rs/.ts/.tsx/.sql/.ps1`、文档、GitHub Actions 等），不含 `.git` 历史、node_modules、编译输出、SQLite 日志、证书和未跟踪的本地文件。归档文件名与 ZIP 内顶层目录统一为 `StockCount-Source-短CommitHash`，默认打包 HEAD，`-Ref` 可选择分支、Tag 或 Commit。Git 自动生成至少 7 位的唯一短哈希（例如上面的 `a39ec60` 仅为示例）；SourceInfo 同时记录短哈希、完整 Commit SHA 和 SHA256。旧 `-Version` 参数仍兼容，但仅作为可选说明字段，不改变文件名。不同提交生成不同文件名，同一提交重复打包覆盖同名归档。

如果 `git status` 有未提交修改或未跟踪文件，脚本默认拒绝执行，避免误以为这些修改已包含在源码 ZIP。建议先 `git add` / `git commit`。如果有意只需要已提交的快照，可以加 `-AllowDirtySnapshot`，但它**不会把任何本地未提交修改放进 ZIP**。

**中文路径与终端编码**：源码归档脚本显式按 UTF-8 读取 Git 的 NUL 分隔文件名，ZIP 路径也显式使用 UTF-8，不依赖 Windows 控制台的 CP936/GBK 或 UTF-8 设置。中文 Obsidian 文档无需改名。若旧脚本提示 `Archive missing ... committed file(s)` 且文件名乱码，先拉取新版脚本再打包；不要禁用完整性校验。失败的旧 ZIP 不应交付。

**源码隐私边界**：脚本在归档前拒绝已跟踪的 `.env`、数据库日志、证书私钥或构建目录；这是文件名筛查，不能保证源文件内容中完全没有明文密码。因此提交源码前仍需检查 SQL 连接串、PFX 口令、真实二维码、内网地址和个人信息。

## 七、校验和常见故障

校验生成包 SHA256（与同目录 SHA256SUMS 对照）：

```powershell
Get-FileHash .\dist\local\StockCount-Server-v0.2.0-local-Windows-x64-Portable.zip -Algorithm SHA256
$short = git rev-parse --short=7 HEAD
Get-FileHash ".\dist\source\StockCount-Source-$short.zip" -Algorithm SHA256
```

| 现象 | 排查 / 处理 |
| --- | --- |
| 缺少 dotnet / SDK 不是 10.x | 安装 .NET 10 SDK；执行 dotnet --list-sdks |
| 缺少 maui-android | dotnet workload install maui-android；检查 Android SDK/JDK |
| Rust/MSVC 链接失败 | 检查 `rustc -vV` 的 host 是否为 x86_64-pc-windows-msvc，安装 Visual Studio C++ Build Tools 与 Windows SDK |
| npm 或 Tauri 找不到命令 | 安装 Node.js 22+、Rust，先 npm install，再运行构建 |
| Portable 无法启动 API 或保存设置 | 确认解压后的 `StockCount.Api.exe` 与主 EXE 同目录，`server-settings.json` 和 `server-logs.db` 均可在当前用户可写的目录中创建、WebView2 已安装 |
| Avalonia GenerateAvaloniaResourcesTask 找不到 server.ico | 旧源码包遗漏了被忽略的 ICO；更新仓库并重新归档，或将新版已提交的 `src/StockCount.Server.Desktop/Assets/server.ico` 复制到旧源码包同一相对路径。 |
| Avalonia Portable 缺少依赖 | 使用 package-avalonia-portable.ps1 -Build 重新生成两个自包含单文件 EXE，不能手工删 DLL 来冒充单文件；Avalonia 无需 WebView2，Tauri 则需要 |
| Avalonia 升级被拒绝 | 先关闭 StockCount.Server.Desktop、Tauri 和 StockCount.Api，确认 ZIP 不含现场设置、SQLite 或签名私钥 |
| Obsidian / Markdown 检查失败 | 执行 `python scripts/check_obsidian_links.py`，目录不能使用 `variants/` 这类相对链接，应链接具体文件或 GitHub 目录 URL |
| SQLite 文件锁或 Windows 文件被占用 | 停止 API 并退出管理器，再运行 deploy-local；不要手工删 WAL/SHM |
| ADB 无设备或 unauthorized | 检查数据线、USB 调试、手机授权弹框；多设备用 -DeviceSerial |
| APK 签名不兼容 | 不自动卸载；先处理应用中可能存在的未知提交原 UUID 与数据备份 |
| Source ZIP 缺本地修改 | git archive 只包含 Git Commit；先提交或使用明确的已提交快照模式 |
| XTEST/COB 不可写 | 属于独立的数据库 Schema、事务和服务端写入授权；不能通过本地部署脚本绕过 |

## 八、相关文档

- [[docs/RELEASE_PACKAGING|GitHub Releases、Setup 与 Portable]] · [Markdown](RELEASE_PACKAGING.md)
- [[docs/WRITE_FLOW_IMPLEMENTATION|事务写入与 DBA 验收]] · [Markdown](WRITE_FLOW_IMPLEMENTATION.md)
- [[docs/SCANNER_INTEGRATION|海康 PDA 扫码与广播参数]] · [Markdown](SCANNER_INTEGRATION.md)
- [[docs/00-文档导航|Obsidian 文档总导航]] · [Markdown](00-文档导航.md)

本地打包不会触发 GitHub Release，也不会连接生产 SQL Server、自动安装服务或修改 Sage X3 的任何盘点记录。
