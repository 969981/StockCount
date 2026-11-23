# StockCount.Pad · Android (.NET MAUI 10)

> 2026-11-23：根据已确认的 HTML 原型，已提交四页 MAUI XAML UI、真实 API 接入、受保护草稿恢复、扫码队列和 1 秒成功提示。**尚未取得真实 MAUI Android 构建成功、实机扫码或 ERP XTEST/COB 联调结果，不能宣称 APK 已成功发布。**

**构建核实（2026-11-23）**：[GitHub Actions #46](https://github.com/969981/StockCount/actions/runs/45110321348) **成功**：MAUI Android Debug APK 编译和 APK 生成检查通过、29 项 Core 单测通过。发布到 GitHub Releases 的带版本名 APK 将由 [统一发布流水线](../../.github/workflows/publish-stockcount.yml) 负责，只上传 `*-Signed.apk`，不会发布未签名同名文件。海康真机/SQL Server 现场验收仍待完成。

## 本地编译和海康 PDA ADB 安装

- 使用 [本地 PowerShell 操作手册](../../docs/LOCAL_BUILD_DEPLOY.md) 与 `scripts/build-local.ps1 -Target Android` 编译带 Debug 签名的安装 APK。
- 使用 `scripts/deploy-local.ps1 -Target Android -PackagePath <APK实际路径> -DeviceSerial <adb序列号>` 安装到已授权的 Android PDA；发生签名不一致时不会自动卸载或删除 SecureStorage。
- `scripts/package-source.ps1` 打包的是整个仓库源代码而不是已编译 APK，产物在 `dist/source/`。
## APK UI 设计与实施

- [工业 PDA UI 与扫码交互设计规范](../../docs/pda/PDA-UI-UX-设计规范与交互状态.md)：页面层级、竖屏线框、视觉 Token、Scan UI 状态、数量/提交反馈以及弱网恢复。
- [可执行的 MAUI UI 改造任务与验收矩阵](../../docs/pda/PDA-UI-开发任务与验收.md)：P0/P1 开发拆分、API依赖、交互测试用例。
- **本轮已按确认的 HTML 原型重构**：盘点单卡片、扫码状态、顶部环境标识、固定数量/提交、只读历史和设置；成功先提示1000ms后清箱；扫码中使用有界队列和忙碌反馈；原 UUID/草稿在 POST 前加密保存。源码已提交，效果仍需编译与真机视觉验收。

## Windows 构建

1. 安装 .NET 10 SDK、Android SDK、MAUI Android workload 和 adb。
2. 在仓库根目录：

```powershell
dotnet workload install maui-android
dotnet restore src/StockCount.Pad/StockCount.Pad.csproj
dotnet build src/StockCount.Pad/StockCount.Pad.csproj -f net10.0-android -c Debug -p:AndroidPackageFormat=apk -p:EmbedAssembliesIntoApk=true
```

仓库 CI：[PDA Android Build](../../.github/workflows/pda-android-build.yml)。成功时上传 **StockCount-PDA-Android-Debug-APK** 工件（用于设备调试，非生产正式签名发行版）。实际成功状态需到 GitHub Actions 页面核验；正式发行签名证书由企业保管，不上传公开仓库。

## 已有页面

- `CountListPage`：可分页的全部已有盘点单列表，支持服务端搜索，不按状态隐藏单据。
- `CountingPage`：按 HTML 原型改成三段 Grid，底部固定数量与提交；扫码确认后展示只读箱信息；先显示1000ms成功蒙层后清箱，不清单号/名称/库位；支持草稿防覆盖、未确认结果查询/同UUID重试。
- `HistoryPage`：通过 `GET /api/v1/inventory-counts/{countNumber}/items` 读取真实已盘记录、分页、只读，不使用演示假数据。
- `SettingsPage`：原型式连接卡片与 Test/Production 切换；正式帐套首次切换确认；存在未确认提交时阻止变更环境/服务器/密钥，设备密钥保存在 SecureStorage。
- `Platforms/Android/MainActivity.cs`：前台动态注册扫描广播，默认 Action 为 `com.service.scanner.data`、Extra 为 `ScanCode`。**现在可在 APK「设置 → 硬件扫码诊断」修改这两个参数并立即生效**，无需重新编译；具体取值必须通过真实海康 PDA 的 SDK/设备广播测试核验。

生产 Release 构建隐藏手工扫码调试输入，盘点库位只能由扫描服务事件写入。开发 Debug 模式允许使用模拟扫码框进行无设备联调。页面实际扫码状态由 Core 的 `CountScanWorkflow` 控制，纯 .NET 单测覆盖重复箱不填充、数量必填、状态未知保留 UUID、成功后清箱不清库位。

## 与 API 对接

设备从 Windows 后端 Tauri 管理器获取授权 API HTTPS 地址和设备 Key：

- 请求头 `X-StockCount-Environment` 只能使用 `Test` 或 `Production`。
- 请求头 `X-StockCount-Device-Key` 存储于 MAUI SecureStorage；切换测试/正式环境需重新选择盘点单。
- 提交时 `Idempotency-Key` 采用一个草稿一个 UUID，网络失败原 UUID 保留；状态查询 `GET /api/v1/submissions/{UUID}?countNumber=...`。
- 标签 `STA_0` 不过滤，完整二维码匹配 `XLABELDTL.QRCOD_0`；箱码由数据库返回 `XLABELDTL.PID_0`。

## 海康 PDA 扫码现场诊断

1. 打开 APK「连接与设备设置」→「硬件扫码诊断」，查看当前广播 Action/Extra；默认是 `com.service.scanner.data` / `ScanCode`。
2. 使用 PDA 的物理扫码键对任意**测试条码**扫描；面板会显示是否收到事件、时间、格式类型和字符长度。**不会打印完整二维码**。
3. 如果未收到，按海康设备扫描服务设置或原厂文档填写正确的广播 Action、条码字符串 Extra 名称；点击「应用扫码参数」，接收器自动重新注册，再扫描验证。
4. 进入真实盘点界面，先扫测试库位、再扫箱码。后台 API 必须已连接、两套帐套写入开关须分别核验后开启。**扫描广播可被其他 Android 应用模拟，不能作为服务端授权的凭据**；服务端设备密钥、HTTPS 及业务 SQL 校验仍必需。
5. 如果因设备扫码服务使用键盘模拟而不是广播回传，请在设备扫描服务中切换为广播输出；不能以 Debug 的手输模拟成功作为海康真机扫码验收。

## APK 构建与工件

- GitHub Actions [StockCount MAUI PDA Android Build](../../.github/workflows/pda-android-build.yml) 现支持手动 `Run workflow`；先运行 Core 单元测试，再构建 APK、验证实际 APK 文件/打印 SHA256，成功后上传 `StockCount-PDA-Android-Debug-APK` 工件（保留 14 天）。
- 这只是 **Debug 签名的测试版安装包**，不代表 XTEST/COB 真库写入已验收。现场环境的 HTTPS 证书需要被 Android 信任；不要通过关闭证书校验来绕过。
- 安装验证示例：`adb install -r <实际生成的apk路径>`，再用 `adb logcat` 排查崩溃和广播事件，但不要将真实二维码或企业密钥原文上传公开 Issues。

## 待验收和待增强

- 海康型号、Android 版本、广播 Action/Extra、PDA 实体扫码键的真机联调。
- Android 系统杀进程后的待确认提交 UUID 和草稿已通过 `SecurePendingSubmissionStore` 编码为加密持久化，**仍需真机杀进程/复测确认**。普通“尚未点击提交”的草稿未持久化，离开页面时会提示。
- 保存过程的设备审计标识和本地日志策略。
- API XTEST/COB 实际写入/回滚/并发验收尚未完成，两帐套均由服务端独立授权（首次默认关闭）。

详细步骤：[写入工作流](../../docs/WRITE_FLOW_IMPLEMENTATION.md) · [API 契约](../../docs/API_CONTRACT.md) · [安全与环境](../../docs/SECURITY_AND_ENVIRONMENTS.md)。
