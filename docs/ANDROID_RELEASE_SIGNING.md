---
title: StockCount · Android Debug / Release APK 与持久签名
created: 2026-11-23
status: code-complete-requires-signing-secrets
tags: [StockCount, Android, Release, MAUI, Signing, GitHubActions]
---

# Android 双版本 APK：Debug 与 Release

> 重要：Debug 和 Release 是两种 **.NET MAUI 编译配置**；Release APK 只有使用长期保留的私有 keystore 签名后，才是可持续更新的可安装版本。构建脚本不会把无签名 APK、临时签名 APK 或 Debug 签名包冒充 Release。

## 一、产物和区别

| APK 文件 | 配置 | 签名 | 建议用途 |
| --- | --- | --- | --- |
| `StockCount-PDA-v<VERSION>-Android-Debug-Signed.apk` | `Debug` | Android 调试 keystore（可能因构建机变化） | 调试 UI、海康广播、测试帐套联调 |
| `StockCount-PDA-v<VERSION>-Android-Release-Signed.apk` | `Release` | 企业/项目长期保存的独立正式 keystore | 受控现场测试，正式签名发布前仍需 SQL/硬件验收 |

两者默认 **相同的包 ID** `com.dreamer.stockcount.pad`，但签名不同，因此不能直接相互覆盖安装；切换前需要处理所有未知提交 UUID、备份必要资料。系统可能要求卸载旧包，卸载会删除 MAUI SecureStorage 和本地待确认草稿。后续同类型 Release 升级，必须使用**同一 keystore 与 alias**，并提高 Android versionCode。本次版本号已增加至 `ApplicationVersion=3`。

## 二、生成并长期备份 Release keystore（只做一次）

安装 JDK 和 Android SDK 后，打开 Windows PowerShell 7：

```powershell
# 选定仓库之外的安全目录，不要把 JKS 放进 Git 工作区
New-Item -ItemType Directory D:\StockCountSecrets -Force
keytool -genkeypair -v -keystore D:\StockCountSecrets\stockcount-release.jks -alias stockcount -keyalg RSA -keysize 3072 -validity 10000
```

`keytool` 会交互询问 keystore 密码、证书信息和 Key 密码。自行设置强密码，**不要把密码写入文档、脚本、仓库、Issues 或终端聊天记录**。将 `.jks` 和密码保存在独立加密备份中，最好有第二份离线备份。不要每次重新生成 keystore，否则 APK 以后可能无法原地升级。

## 三、在 GitHub 配置四个 Actions Secrets

打开仓库 `969981/StockCount` → **Settings → Secrets and variables → Actions → New repository secret**。新建：

| Secret 名称 | 值 |
| --- | --- |
| `STOCKCOUNT_ANDROID_KEYSTORE_BASE64` | JKS 文件的 Base64（单行文本） |
| `STOCKCOUNT_ANDROID_KEY_ALIAS` | 例如 `stockcount`（实际 Alias） |
| `STOCKCOUNT_ANDROID_STORE_PASS` | keystore 密码 |
| `STOCKCOUNT_ANDROID_KEY_PASS` | 私钥 Alias 的密码 |

可以在本地读取 JKS 字节、转换为 Base64 后直接写入 GitHub Secret（需要安装并登录 GitHub CLI `gh`）：

```powershell
$file = 'D:\StockCountSecrets\stockcount-release.jks'
[Convert]::ToBase64String([IO.File]::ReadAllBytes($file)) | gh secret set STOCKCOUNT_ANDROID_KEYSTORE_BASE64 -R 969981/StockCount
```

其它三个值推荐直接在 GitHub 设置网页里录入，避免密码出现在 shell 历史中。**请不要公开 Base64 内容**，Base64 不是加密方式，它依旧是完整的私钥文件。脚本只在 GitHub Runner 的临时目录中恢复 keystore，作业结束删除文件；签名口令经 MSBuild 的 `env:` 变量引用，不以明文 `-p` 参数写入命令行。

## 四、Actions 自动双版编译与 Releases

- [PDA Android Build](../.github/workflows/pda-android-build.yml)：始终编译 Debug，并检查 Release 配置；只有四个 Secrets 均齐全时才额外生成 `StockCount-PDA-Android-Release-Signed-APK` 工件。
- [StockCount GitHub Release](../.github/workflows/publish-stockcount.yml)：新版本 `v0.2.0-preview.3` 需要同时具有 **Debug APK + Release APK + Windows Setup.exe + Windows Portable.zip** 和 `SHA256SUMS.txt`，否则**不创建不完整版本**。
- 由于未获得本仓库正式 keystore，也没有权限自行设置仓库 Secrets，**此轮仅完成代码与流程接入，不声称 GitHub Releases 已含可安装 Release APK**。配置 Secrets 后，可在 Actions 页面手动运行 `StockCount Release - Android Debug and Release APK, Setup, Portable`，并检查日志与 Release 资产。
- GitHub Secrets 不会提供给来自 fork 的 Pull Request，外部 PR 只验证 Release 编译路径，不得要求读取正式私钥。

## 五、PowerShell 本地构建

先在当前 PowerShell **进程环境变量**中配置密码（仅该终端会话，关闭后失效）：

```powershell
# Read-Host 隐藏输入；密码不会作为 dotnet 构建参数出现
$storeSecure = Read-Host 'JKS keystore 密码' -AsSecureString
$keySecure   = Read-Host 'Alias key 密码' -AsSecureString
$env:STOCKCOUNT_ANDROID_STORE_PASS = [System.Net.NetworkCredential]::new('', $storeSecure).Password
$env:STOCKCOUNT_ANDROID_KEY_PASS = [System.Net.NetworkCredential]::new('', $keySecure).Password

# 一次构建 Debug + Release 双 APK
pwsh -File .\scripts\build-local.ps1 -Target Android -AndroidVariant Both -Version 0.2.0-local -ReleaseKeyStore D:\StockCountSecrets\stockcount-release.jks -ReleaseKeyAlias stockcount

# 完成后清除当前终端的明文环境变量
Remove-Item Env:STOCKCOUNT_ANDROID_STORE_PASS, Env:STOCKCOUNT_ANDROID_KEY_PASS -ErrorAction SilentlyContinue
```

也可只生成 Release 版本：

```powershell
pwsh -File .\scripts\build-local.ps1 -Target Android -AndroidVariant Release -Version 0.2.0-local -ReleaseKeyStore D:\StockCountSecrets\stockcount-release.jks -ReleaseKeyAlias stockcount
```

实际调用 [build-android-release.ps1](../scripts/build-android-release.ps1) 执行 `dotnet publish -f net10.0-android -c Release -p:AndroidPackageFormats=apk`、持久密钥签名、`apksigner verify --print-certs` 和 SHA256 核验。没有密钥/密码时直接失败，不静默降级。

## 六、安装和验收

```powershell
adb devices
pwsh -File .\scripts\deploy-local.ps1 -Target Android -PackagePath .\dist\local\StockCount-PDA-v0.2.0-local-Android-Release-Signed.apk
```

如遇 `INSTALL_FAILED_UPDATE_INCOMPATIBLE`，通常是从 Debug 切换到不同正式签名；脚本不会自动卸载，以避免丢失 SecureStorage 和待确认的提交信息。Android 上要验证启动、MAUI XAML、海康扫描广播、HTTPS API、断网后原 UUID 恢复与受控 XTEST/COB 请求；CI 编译通过不等于现场认证完成。

微软官方签名参考：[Publish a .NET MAUI Android app using the command line](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0) 和 [.NET for Android build properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties)。
