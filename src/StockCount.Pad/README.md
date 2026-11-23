# StockCount.Pad · Android (.NET MAUI 10)

> 2026-11-23：首版扫码页面、API 客户端及海康扫码广播桥接源代码已提交。仍未完成实际 MAUI Android 编译、设备测试或在线部署，不能认作已发布 APK。

## APK UI 设计与实施

- [工业 PDA UI 与扫码交互设计规范](../../docs/pda/PDA-UI-UX-设计规范与交互状态.md)：页面层级、竖屏线框、视觉 Token、Scan UI 状态、数量/提交反馈以及弱网恢复。
- [可执行的 MAUI UI 改造任务与验收矩阵](../../docs/pda/PDA-UI-开发任务与验收.md)：P0/P1 开发拆分、API依赖、交互测试用例。
- **现有基础 UI 并非新版设计完成稿**：目前成功路径在显示成功提示前已清箱；扫描处理中直接 return 可能丢事件；离线草稿/原UUID尚未持久化。新版设计要求先提示1秒后清箱、草稿防覆盖、断网恢复。

## Windows 构建

1. 安装 .NET 10 SDK、Android SDK、MAUI Android workload 和 adb。
2. 在仓库根目录：

```powershell
dotnet workload install maui-android
dotnet restore src/StockCount.Pad/StockCount.Pad.csproj
dotnet build src/StockCount.Pad/StockCount.Pad.csproj -f net10.0-android -c Debug
```

仓库独立 CI 文件为 [.github/workflows/pda-android-build.yml](../../.github/workflows/pda-android-build.yml)。这不是正式签名 APK；正式安装包必须由企业提供签名证书和设备发放策略，不能上传私钥到公开仓库。

## 已有页面

- `CountListPage`：可分页的全部已有盘点单列表，支持服务端搜索，不按状态隐藏单据。
- `CountingPage`：显示只读单号、名称、库位以及箱信息；人工编辑唯一 STU 数量；现有源码是提交确认后立刻清箱再显示约1秒提示；规划重构后才改为提示消失再清空（保留单号、名称、库位）。
- `SettingsPage`：可选择 Test/Production、填写/保存 API HTTPS 地址及设备 Key（SecureStorage），不含数据库连接字符串或 SQL 凭据。
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
- Android 系统杀进程后的离线草稿与提交 UUID 持久化/恢复（当前仅会话内存状态）。
- 保存过程的设备审计标识和本地日志策略。
- API XTEST 实际写入/rollback/重复并发验收；XTEST/COB 写入均由服务端分别授权（默认均关闭），现场联调尚未完成。

详细步骤：[写入工作流](../../docs/WRITE_FLOW_IMPLEMENTATION.md) · [API 契约](../../docs/API_CONTRACT.md) · [安全与环境](../../docs/SECURITY_AND_ENVIRONMENTS.md)。
