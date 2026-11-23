# StockCount.Pad · Android (.NET MAUI 10)

> 2026-11-23：根据已确认的 HTML 原型，已提交四页 MAUI XAML UI、真实 API 接入、受保护草稿恢复、扫码队列和 1 秒成功提示。**尚未取得真实 MAUI Android 构建成功、实机扫码或 ERP XTEST/COB 联调结果，不能宣称 APK 已成功发布。**

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
- `Platforms/Android/MainActivity.cs`：动态接收广播 `com.service.scanner.data`，从 `ScanCode` Extra 读取扫码文本，前台页面订阅事件；**这套广播参数来自此前的技术路线，尚需在实际海康 PDA 上验证**。

生产 Release 构建隐藏手工扫码调试输入，盘点库位只能由扫描服务事件写入。开发 Debug 模式允许使用模拟扫码框进行无设备联调。页面实际扫码状态由 Core 的 `CountScanWorkflow` 控制，纯 .NET 单测覆盖重复箱不填充、数量必填、状态未知保留 UUID、成功后清箱不清库位。

## 与 API 对接

设备从 Windows 后端 Tauri 管理器获取授权 API HTTPS 地址和设备 Key：

- 请求头 `X-StockCount-Environment` 只能使用 `Test` 或 `Production`。
- 请求头 `X-StockCount-Device-Key` 存储于 MAUI SecureStorage；切换测试/正式环境需重新选择盘点单。
- 提交时 `Idempotency-Key` 采用一个草稿一个 UUID，网络失败原 UUID 保留；状态查询 `GET /api/v1/submissions/{UUID}?countNumber=...`。
- 标签 `STA_0` 不过滤，完整二维码匹配 `XLABELDTL.QRCOD_0`；箱码由数据库返回 `XLABELDTL.PID_0`。

## 待验收和待增强

- 海康型号、Android 版本、广播 Action/Extra、PDA 实体扫码键的真机联调。
- Android 系统杀进程后的待确认提交 UUID 和草稿已通过 `SecurePendingSubmissionStore` 编码为加密持久化，**仍需真机杀进程/复测确认**。普通“尚未点击提交”的草稿未持久化，离开页面时会提示。
- 保存过程的设备审计标识和本地日志策略。
- API XTEST/COB 实际写入/回滚/并发验收尚未完成，两帐套均由服务端独立授权（首次默认关闭）。

详细步骤：[写入工作流](../../docs/WRITE_FLOW_IMPLEMENTATION.md) · [API 契约](../../docs/API_CONTRACT.md) · [安全与环境](../../docs/SECURITY_AND_ENVIRONMENTS.md)。
