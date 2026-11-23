# 海康 PDA 扫码广播协议与接入总览

> [!IMPORTANT] 证据分级
> **厂商协议**：《读码模组扫描服务广播协议开发指南 V1.0.1》（已有资料，章节 2.1–2.31）；**历史项目记录**：OutsourceFlow.Pad 针对海康 MV-IDP5204、Android 11 的扫码适配；**StockCount 状态**：拟复用协议，尚未对当前设备固件重新实机验证，本文不是已经运行成功的 StockCount 源码。

## 1. 接入目标

- **设备**：海康工业 Android PDA，既有目标型号 `MV-IDP5204`（历史记录的 Android 11 设备）；不保证所有固件/Android 版本采用相同广播配置。
- **方案**：由厂商内置扫描服务控制读码模组，扫码完成后发送 Android Broadcast，`.NET 10 + .NET MAUI` 的 `BroadcastReceiver` 接收，转为 `IScannerService` 事件；客户端只调用 StockCount API，**不直接连接 SQL Server**。
- **输入**：手持 PDA 实体扫码键触发读码；`com.service.scanner.start` 是**启动扫描服务**，不是软件按钮“扫描一次”命令。
- **输出**：二维码原始字符串 + 可选码制；由 StockCount 的库位/箱码解析器区分，不把扫码动作误判为 API 提交成功。

## 2. 原厂 V1.0.1 协议的关键命令

| 功能 | Android Intent Action | 显式目标组件 | 原厂规定的参数/说明 |
| --- | --- | --- | --- |
| 启动扫码服务 | `com.service.scanner.start` | `LaunchReceiver` | `FLAG_INCLUDE_STOPPED_PACKAGES` |
| 停止扫码服务 | `com.service.scanner.stop` | `LaunchReceiver` | 不应在每个箱码完成后反复停服务 |
| 扫描模式 | `com.service.scanner.scanmode` | `ServiceControlReceiver` | `scanMode=SINGLEMODE`, `singleTime=3000` ms；另支持 `MANUALMODE` / `CONTINUOUSMODE` |
| 输出模式 | `com.service.scanner.outputmode` | `ServiceControlReceiver` | **`outputMode=BROADCASTMODE`**；原厂默认 `FOCUSANDCOVERMODE` |
| 接收解码事件 | `com.service.scanner.data` | 本应用动态注册的 Receiver | `ScanCode`（内容）、`ScanCodeType`（码制），两者可经厂家设置更名 |
| 重复过滤 | `com.service.scanner.repeated.filter` | `ServiceControlReceiver` | `scanCodeFilterRepeated`；StockCount 必须独立做幂等 |
| 字符编码 | `com.service.scanner.charset` | `ServiceControlReceiver` | 按原厂支持值配置，参见完整手册；不可直接写 .NET 枚举名称 |
| 蜂鸣器 | `com.service.scanner.voice` | `ServiceControlReceiver` | `scanVoice`（bool），**只表示模组识别，不表示盘点成功** |
| 震动 | `com.service.scanner.shock` | `ServiceControlReceiver` | `scanShock`（bool） |
| 修改数据广播 Action | `com.service.scanner.config.broad.action` | `ServiceControlReceiver` | `broad_action`；默认 `com.service.scanner.data` |
| 修改条码 Extra 名称 | `com.service.scanner.config.broad.code` | `ServiceControlReceiver` | `broad_code`；默认 `ScanCode` |

上表 `LaunchReceiver` 对应完整类 `com.pda.service.broadcast.LaunchReceiver`；`ServiceControlReceiver` 对应 `com.pda.service.broadcast.ServiceControlReceiver`；服务包均为 `com.hikrobotics.pdaservice`（原厂 V1.0.1，非当前设备实测）。

> [!WARNING] 命名大小写
> 原厂的参数表会写 `scanCode` / `scanCodeType`，但同一手册第 2.19 节的 **`getStringExtra("ScanCode")`、`getStringExtra("ScanCodeType")`** 示例采用大写首字母。历史项目使用后者。真实设备允许重定义 Action/Extra，最终必须以当前固件及扫码设置核对，不得盲目统一大小写。

## 3. 默认接入方案

```text
MV-IDP5204 实体扫描键
  → com.hikrobotics.pdaservice / 读码模组
  → outputMode=BROADCASTMODE
  → Intent Action=com.service.scanner.data
  → ScannerBroadcastReceiver (GetStringExtra)
  → HikvisionBroadcastScannerService / IScannerService
  → StockCount.Pad ViewModel 状态机
  → 先扫库位 → 扫箱码 → 显示标签/物料
  → 人工输入 STU 数量 → API 保存
  → XCOUTD.QTYSTU_0 = XCOUTD.QTYPCU_0 = 人工数
```

**StockCount V1 业务限制**：不做登录、不新建/关闭盘点单；`XLABELDTL.STA_0` 无论何值都不以状态作为盘点禁入条件；正式 COB/测试 XTEST 由 API 受控切换，扫码服务不直接选择 SQL Schema。

## 4. 原始条码与扫描模式

- 原厂输出可分 `FOCUSMODE`、`FOCUSANDCOVERMODE`、`BROADCASTMODE`、`CLIPBOARDMODE`。为了避免扫码内容误进入正在编辑的人工数量框，**StockCount 首选 BROADCASTMODE**。
- 建议 `SINGLEMODE`、`singleTime=3000`（源于原厂示例），但不强制推断其它模式无法工作。
- **不要**在厂商服务中随意开启前缀/后缀过滤、字符剪切、位置截取；二维码内部 `@@`、`|` 等分隔符必须保留，平台层仅移除扫码终止符 CR/LF。
- 扫描 `LC.` 只是曾见到的库位前缀示例，不代表所有场景都合法；扫码原文交给后端校验，库位按站点 + 仓库 + 库位匹配。
- `ScanCodeType` 是设备读码制式信息，**不是** Sage X3 的 `XLABELDTL.STA_0` 或盘点单 `CUNSTA_0` 状态码。
- 当扫码硬件配置为键盘/焦点输出时，Receiver 收不到 `com.service.scanner.data` 不一定代表扫描枪坏；先检查输出模式。

## 5. 源与核验状态

| 内容 | 证据状态 |
| --- | --- |
| `com.service.scanner.start`、服务包、控制组件、BROADCASTMODE、`com.service.scanner.data` | **原厂 V1.0.1 手册第 2.1、2.14、2.19 节明确记载** |
| `MV-IDP5204 / Android 11` 与 MAUI BroadcastReceiver 方案 | **既有 OutsourceFlow 项目记录**；原始最终版源码未找回 |
| 上述协议在当前 StockCount 目标 PDA 固件上工作 | **待实测** |
| 软件按钮模拟实体扫码键、Android 13/14 广播权限兼容 | **待实测/待厂商确认** |

技术示例参见 [[海康PDA-MAUI实现与示例]]，现场验证参见 [[海康PDA-调试排障与验收]]。原厂手册存于私有资料库（不复制进公开 GitHub），文档已注明版本和章节，尊重原始资料分发权限。
