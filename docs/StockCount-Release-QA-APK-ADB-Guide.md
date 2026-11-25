---
title: "StockCount PDA 无自建 JKS 的 Release QA 编译发布与 ADB 安装"
aliases: ["Release QA APK", "免自建 JKS 编译"]
created: 2026-11-25
updated: 2026-11-25
status: documented
type: guide
area: pda
tags: [StockCount, Android, MAUI, Release, QA, ADB]
up: "[[docs/ANDROID_RELEASE_SIGNING]]"
---

# StockCount PDA：无需自建 JKS 的 Release QA 编译与安装

适用环境：Windows x64、PowerShell 7、.NET 10、StockCount main。适合当前 PDA 性能对照、界面调整和 XTEST 联调。

## 1. 原理与适用范围

Release 是编译配置，签名是安装身份，两者独立。Android 不允许安装未签名 APK。这里的“免 JKS”是指不需要自行生成正式 JKS、不需要输入正式密钥密码、不需要 GitHub Actions Secrets；.NET Android SDK 仍会用默认调试签名密钥签署 APK。

| 类型 | 编译配置 | 签名 | 用途 |
|---|---|---|---|
| Debug | Debug | 默认调试密钥 | 断点调试 |
| Release QA | Release | 默认调试密钥 | 优化后的性能测试、内部试装 |
| 正式 Release | Release | 长期保管的私有密钥 | 正式持续部署 |

Release QA 保留 Release 编译优化，不因使用默认调试证书而变成 Debug 构建。签名本身不提升或降低应用运行性能。不要用 QA 包冒充企业正式发布包。

项目包名 com.dreamer.stockcount.pad，目标 net10.0-android，当前版本名 0.2.0、versionCode 3、最低 Android 8.0（API 26）。版本以实际源码为准。

## 2. 准备环境

已有 Visual Studio Debug 开发环境通常无需重复安装。进入仓库根目录，检查：

~~~powershell
pwsh --version
dotnet --version
dotnet workload list
adb version
~~~

需要 .NET 10 SDK、兼容的 JDK/Android SDK 和 Platform-Tools。缺少 MAUI Android workload 时：

~~~powershell
dotnet workload install maui-android
~~~

若 SDK/JDK 未找到，通过 Visual Studio Installer 安装 MAUI Android 开发组件，或者按工具链提示设置 SDK 路径。若 adb 不在 PATH，可将 Android SDK 的 platform-tools 目录加入 PATH。

## 3. 同步源码及运行测试

~~~powershell
git status
git switch main
git pull --ff-only origin main

dotnet test .\tests\StockCount.Core.Tests\StockCount.Core.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core 测试失败' }
~~~

已有本地修改时先处理，不强制 reset。只构建 Android，不需要 Node.js、Rust、Windows 管理器或 SQL Server。

## 4. 推荐：直接编译 Release QA

这是现有 Android CI 使用的无正式签名密钥编译方式。

~~~powershell
dotnet build .\src\StockCount.Pad\StockCount.Pad.csproj `
  -f net10.0-android `
  -c Release `
  -p:AndroidPackageFormats=apk `
  -p:AndroidKeyStore=false

if ($LASTEXITCODE -ne 0) { throw 'Release QA 编译失败' }
~~~

参数说明：

- -c Release：Release 编译优化。
- AndroidPackageFormats=apk：只生成 APK，不生成 AAB。
- AndroidKeyStore=false：使用 SDK 默认调试签名，不是关闭签名。

不要直接调用 build-local.ps1 -AndroidVariant Release 来构建这个 QA 包：现有脚本按正式发布策略要求自定义密钥。本手册不修改该策略。

输出目录：src/StockCount.Pad/bin/Release/net10.0-android/。仅选取本次新生成的 *-Signed.apk；不要安装无 Signed 后缀的中间产物。若有多个候选或旧产物，先核对时间、版本和签名，不能随便取第一个。

## 5. 可选：dotnet publish 发布方式

与 build 二选一。此处“发布”指本地生成可安装产物，不是上传 GitHub Releases 或应用商店。

~~~powershell
dotnet publish .\src\StockCount.Pad\StockCount.Pad.csproj `
  -f net10.0-android `
  -c Release `
  -p:AndroidPackageFormats=apk `
  -p:AndroidKeyStore=false
if ($LASTEXITCODE -ne 0) { throw 'Release QA publish 失败' }
~~~

优先检查 src/StockCount.Pad/bin/Release/net10.0-android/publish/，也可能在上一层产生 APK。需核验新生成的签名文件。后续示例采用第4节的 build 方式。

## 6. 一次执行：编译、签名校验、归档 QA APK

以下 PowerShell 从仓库根目录执行。它不生成正式密钥、不读取正式签名密码、不自动安装、不卸载设备应用。只在编译与验证成功后生成 QA 文件。

~~~powershell
$project = '.\src\StockCount.Pad\StockCount.Pad.csproj'
$started = [DateTime]::UtcNow
dotnet build $project -f net10.0-android -c Release `
  -p:AndroidPackageFormats=apk -p:AndroidKeyStore=false
if ($LASTEXITCODE -ne 0) { throw 'Release QA 编译失败' }

$apks = @(Get-ChildItem '.\src\StockCount.Pad\bin\Release\net10.0-android' `
  -File -Filter '*-Signed.apk' |
  Where-Object { $_.LastWriteTimeUtc -ge $started.AddSeconds(-5) })
if ($apks.Count -ne 1) {
    throw "未找到唯一的新签名 APK：$($apks.Count)。请检查输出，必要时 dotnet clean 后重试。"
}

# 定位 SDK 的 apksigner，兼容常见 Windows SDK 安装位置
$sdkPaths = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, `
    (Join-Path $env:LOCALAPPDATA 'Android\Sdk')) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique
$signer = $null
foreach ($sdk in $sdkPaths) {
    $tools = Join-Path $sdk 'build-tools'
    if (Test-Path $tools) {
        $signer = Get-ChildItem $tools -Recurse -File -Filter 'apksigner.bat' |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($signer) { break }
    }
}
if (-not $signer) { throw '找不到 apksigner.bat，请安装 Android SDK Build-Tools 并配置 ANDROID_HOME' }

& $signer.FullName verify --verbose --print-certs $apks[0].FullName
if ($LASTEXITCODE -ne 0) { throw 'APK 签名校验失败' }

# 复用 StockCount 现有图标检查
& .\scripts\verify-android-launcher-icon.ps1 -ApkPath $apks[0].FullName
if (-not $?) { throw 'APK 图标检查失败' }

$version = (& .\scripts\get-app-version.ps1).Trim()
$commit = (git rev-parse --short HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw '无法读取 Git 版本' }
$out = '.\dist\local'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$apk = Join-Path $out "StockCount-PDA-v$version-$commit-Android-Release-QA-DebugSigned.apk"
Copy-Item -LiteralPath $apks[0].FullName -Destination $apk -Force
$hash = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash
if ($hash -ne (Get-FileHash -LiteralPath $apks[0].FullName -Algorithm SHA256).Hash) {
    throw '归档 SHA256 校验失败'
}
"$hash  $(Split-Path $apk -Leaf)" | Set-Content "$apk.sha256" -Encoding ascii
Get-Item $apk
Get-FileHash $apk -Algorithm SHA256
~~~

输出：dist/local/StockCount-PDA-v版本-提交号-Android-Release-QA-DebugSigned.apk 和对应 .sha256 文件。文件名明确标注 QA 和调试签名，不覆盖正式 Release 命名。

增量构建未重新生成 APK 时，脚本可能因时间检查拒绝归档，这是有意保护；可先执行 dotnet clean $project -f net10.0-android -c Release 再执行本节。签名证书 SHA256 是身份指纹，与 APK 文件 SHA256 不同。

## 7. ADB 安装

在 PDA 打开开发者选项和 USB 调试，用数据线连接，在设备上授权电脑。

~~~powershell
adb devices -l
~~~

状态为 device 才能安装。沿用上节生成的 $apk 变量：

~~~powershell
adb install -r "$apk"
if ($LASTEXITCODE -ne 0) { throw 'ADB 安装失败' }
adb shell monkey -p com.dreamer.stockcount.pad -c android.intent.category.LAUNCHER 1
~~~

-r 表示覆盖安装并保留应用数据（签名和版本允许的情况下）。若系统要求安装 testOnly APK，才使用 adb install -r -t "$apk"。不默认使用 -d 强制降级。

多台设备连接时，先从 adb devices -l 获取真实序列号，明确指定目标：

~~~powershell
$serial = Read-Host '输入目标 PDA 序列号'
adb -s $serial install -r "$apk"
if ($LASTEXITCODE -ne 0) { throw 'ADB 安装失败' }
adb -s $serial shell monkey -p com.dreamer.stockcount.pad -c android.intent.category.LAUNCHER 1
~~~

也可用现有部署脚本（内部 adb install -r -t，不自动卸载）：

~~~powershell
pwsh -NoProfile -File .\scripts\deploy-local.ps1 -Target Android -PackagePath $apk -Launch
~~~

多设备时添加 -DeviceSerial $serial。新终端需要重新设置 $apk 为实际文件路径。

## 8. Debug 升级 Release QA 与签名检查

同一台电脑、同一开发账户通常可以复用默认调试签名，但不能仅凭文件名推断相同签名。Visual Studio 与 CLI 或另一台电脑/GitHub runner 可能使用不同密钥。

如保留了原 Debug APK，可分别运行 apksigner verify --print-certs 旧APK、新APK，比较 signer certificate SHA-256 digest。默认调试密钥实际位置由 SDK 决定，可检查用户目录的 .android/debug.keystore 或构建日志；不要删除或重新生成现有密钥来尝试解决升级问题。

INSTALL_FAILED_UPDATE_INCOMPATIBLE：签名不兼容。优先使用原签名重新编译；不要直接卸载、adb shell pm clear 或清除 SecureStorage。存在未知提交必须先确认原 UUID 和业务结果。需要更换签名时，仅在待确认提交全部处理、必要设置妥善记录且获得现场授权后，才安排迁移或重装。

INSTALL_FAILED_VERSION_DOWNGRADE：查看新旧 versionCode，使用不低于已安装版本的版本。正式发布递增 versionCode，版本管理见原手册。不要靠改 APK 文件名改变版本号。

## 9. 真机性能对照

- 设置页性能诊断查看构建标记应为 Release/非Debug。
- 选择 XTEST、合法测试单与不同箱码，先热身再清空采样，连续盘点至少30箱，复制报告。
- Debug 与 Release QA 保持同设备、网络、服务端、相近数据条件。
- 记录帧时间、内存、扫码和提交 P50/P95；UI 刷新计时不是实际帧时间，网络与 SQL 也计入整体耗时。

~~~powershell
adb shell dumpsys gfxinfo com.dreamer.stockcount.pad reset
# 完成操作后
adb shell dumpsys gfxinfo com.dreamer.stockcount.pad framestats
adb shell dumpsys meminfo com.dreamer.stockcount.pad
adb logcat -d -s AndroidRuntime
~~~

不要公开可能含业务信息的现场日志。

## 10. 常见问题

| 现象 | 处理 |
|---|---|
| 找不到 maui-android | dotnet workload install maui-android |
| SDK/JDK 缺失 | 修复 Visual Studio Android 工作负载或 SDK/JDK 配置 |
| 找不到 Signed.apk | 检查 Release 编译输出、签名参数和文件时间 |
| adb unauthorized | 解锁设备并允许 USB 调试 |
| more than one device | adb -s 序列号 |
| APK 签名不匹配 | 比较证书指纹，保留原密钥和待确认数据 |
| 安装后闪退 | adb logcat，检查 Release/AOT/裁剪兼容问题 |

## 11. GitHub Actions 与正式发布边界

现有 pda-android-build.yml 在没有正式 Secrets 或 PR 构建时使用 AndroidKeyStore=false 编译 Release QA 并核验图标，但不上传该 QA APK 为正式签名 Release 工件。当前统一 GitHub Release 流程仍要求持久正式签名。本手册仅增加本地 QA 编译、归档和安装指导，不改变 CI、打包脚本或发布策略。

正式签名版见 [Release 正式签名与 ADB 手册](StockCount-Release-APK-ADB-Guide.md)，以及 [Android 签名说明](ANDROID_RELEASE_SIGNING.md)。性能测试口径见 [PDA 性能计划](pda/PDA_PERFORMANCE_PLAN.md)。

官方依据：[.NET Android 生成属性](https://learn.microsoft.com/zh-cn/dotnet/android/building-apps/build-properties)、[MAUI 命令行发布](https://learn.microsoft.com/zh-cn/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0)、[apksigner](https://developer.android.com/tools/apksigner)、[ADB](https://developer.android.com/tools/adb)。

本手册依据当前仓库与官方文档编写，未在用户的 Windows 或 PDA 上实际执行。所有版本、路径、签名与设备状态以现场验证为准。
