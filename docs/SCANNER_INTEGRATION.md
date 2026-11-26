---
title: "Android 工业 PDA 扫码接入规范"
aliases:
  - "StockCount · docs / SCANNER_INTEGRATION"
created: 2026-11-23
updated: 2026-11-26
status: reference
area: pda
tags:
  - StockCount
  - pda
  - docs
up: "[[docs/pda/00-PDA扫码知识地图]]"
---

# Android 工业 PDA 扫码接入规范

状态：设备适配方案，真实型号与固件扫描广播 Action/Extra **必须在现场再验证**。2026-11-23。

## 1. 适配方式

首选设备扫描服务通过 Android Broadcast/Intent 向应用发送扫码结果。MAUI 的 Android 平台项目接收系统事件，再通过抽象接口上报到共享 ViewModel；**不是通过摄像头扫描、也不能假定 TextBox 焦点等于扫码事件**。

此前另一个工业 PDA 项目记录了海康 MV-IDP5204 / 扫码服务广播路线，该方案可借鉴，但需在 StockCount 实际目标设备上确认。

## 2026-11-23 已实现的广播联调功能

- Android `MainActivity` 前台动态注册 `BroadcastReceiver`，扫码事件通过 `ScanFeed` 交给当前活动的 MAUI 页面（`CountingPage` / `SettingsPage`），不从接收器直接执行 HTTP。
- APK「设置 → 硬件扫码诊断」能够观察最近一次扫码的**本地时间、码格式、字符长度**；诊断事件中无原始条码内容，关闭页面停止订阅。
- APK 可输入当前型号使用的广播 **Action** 和内容 **Extra** 名称。点击「应用扫码参数」会校验格式、写入 Android Preferences，并触发 Android 重新注册 Receiver，无需重新打包或重启。
- 默认 Action `com.service.scanner.data`，Extra `ScanCode`；**现场实际值必须真机核实**，不同海康设备/固件可能不同。
- 当前接收器运行在前台并设为 Exported，以兼容外部扫码服务广播；因此广播本身**不是可信的授权或提交来源**，不可仅凭扫到码允许 ERP 写入。设备 Key、库位/标签数据库核验和幂等仍由 API 执行。当前 PDA → Windows 通道为受控局域网 HTTP，**不提供传输加密**；只能在可信隔离网络上使用。
- 现有 `CountScanWorkflow` 负责禁止覆盖已输入数量、重复箱提示及提交；尚未建立按 Action/Extra 来源 UID 的原厂签名校验和设备硬件真机验收。

## 2. 建议接口

```csharp
public record ScannerRead(string RawText, DateTimeOffset ReceivedAt);

public interface IScannerService
{
    event EventHandler<ScannerRead>? BarcodeReceived;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

实现：
- `HikvisionBroadcastScannerService`（Android 专属适配）
- `ScannerBroadcastReceiver`（接收 Intent）
- `MockScannerService`（本地和 CI）
- 如硬件仅支持 Keyboard Wedge，必须明确建立独立替代适配和焦点处理测试，不应偷偷在正常输入框里拼接扫码。

## 3. 扫描协议配置

```json
{
  "Scanner": {
    "Mode": "Broadcast",
    "BroadcastAction": "com.service.scanner.data",
    "BarcodeExtraKey": "ScanCode",
    "CodeTypeExtraKey": "ScanCodeType",
    "Charset": "UTF8",
    "DuplicateSuppressMilliseconds": 800
  }
}
```

以上 Action / Extras 是**海康原厂 V1.0.1 手册和旧项目记录的默认协议值**，并非当前 StockCount PDA 已经实测的配置；厂商允许自定义广播名与字段标签。`Charset=UTF8` 沿用厂家协议编码标识而非 .NET Enum。首次联调必须核对型号、Android 版本、扫描服务版本与实际 Extra。Receiver 只监听用户配置的 Action，读取对应 Extra；第三方应用仍可能发送伪造广播，所以此值**不是**任何访问授权，服务端必须再次校验。

## 4. 条码处理顺序

1. 平台层只清除扫码传输终止符（CR/LF），**不改写内部 `|`、`@@`、中文或大小写**。
2. 扫码总控根据协议/已验证前缀区分**库位码**与**箱二维码**。旧 PDA 曾识别 `LC` / `ID` / `T` / `SE` 前缀，这不是 StockCount 已审核全部支持的格式。
3. 库位码解析后调用 API 核实是否存在及属于目标盘点仓库；切库位前有未提交箱需确认。
4. 没有库位扫描箱码时直接提示，不发起箱查询、不回填物料。
5. 扫箱时把**完整原 QR 字符串**传 API，以标签返回为准；客户端不从 QR `QT` 自动填写实盘数。
6. `PG` 的业务含义未核，不得影响数量字段。
7. 保存中禁止扫码重入；Android 生命周期暂停/恢复正确注册与注销 Receiver，防止重复监听或内存泄漏。
8. 轻量前端节流只解决扫描枪重复事件，**不替代后端去重/幂等**。

## 5. 安全与异常

- 不打印完整二维码到公开日志；Debug 时使用合成条码或经过脱敏的数据。
- 超长输入、空输入、不支持前缀、乱码、重复事件给出明确可读错误。
- 扫描成功/失败可使用声音与震动，但必须可关闭，并且不能只用颜色区分。
- 手动数量输入应是数值键盘，不可被扫描广播写入。
- 键盘弹出后和切回页面后仍应能可靠接收硬件扫码。

## 6. 真机验收

| 场景 | 预期 |
| --- | --- |
| 连续不同箱码扫描 | 每个事件独立接收且顺序正确 |
| 重复物理扫码 | 节流体验正常，服务端仍保证不双写 |
| 无库位扫箱 | 提示，箱信息不填 |
| 有库位扫箱 | 原样传 `ID...` 完整二维码，显示标签数据 |
| 用户编辑数值时扫箱 | 事件不写入数量框、不覆盖未提交草稿 |
| 切后台/回前台 | Receiver 无重复注册、不漏恢复监听 |
| 切测试/正式 | 旧请求不回填到新环境 |
| 异常编码/超长码 | 用户可见错误，数据不截断 |

关联：[架构](../architecture.md) · [接口](API_CONTRACT.md) · [测试](TEST_PLAN.md)。

## 7. 海康 PDA 专项技术文档与知识地图

- [[docs/pda/00-PDA扫码知识地图|PDA 扫码知识地图]] · [Markdown](pda/00-PDA%E6%89%AB%E7%A0%81%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- [[docs/pda/海康PDA-广播协议与接入总览|海康原厂广播协议]] · [Markdown](pda/%E6%B5%B7%E5%BA%B7PDA-%E5%B9%BF%E6%92%AD%E5%8D%8F%E8%AE%AE%E4%B8%8E%E6%8E%A5%E5%85%A5%E6%80%BB%E8%A7%88.md)
- [[docs/pda/海康PDA-MAUI实现与示例|Android MAUI C# 接入]] · [Markdown](pda/%E6%B5%B7%E5%BA%B7PDA-MAUI%E5%AE%9E%E7%8E%B0%E4%B8%8E%E7%A4%BA%E4%BE%8B.md)
- [[docs/pda/海康PDA-调试排障与验收|ADB 真机调试与验收]] · [Markdown](pda/%E6%B5%B7%E5%BA%B7PDA-%E8%B0%83%E8%AF%95%E6%8E%92%E9%9A%9C%E4%B8%8E%E9%AA%8C%E6%94%B6.md)
- [[docs/pda/海康PDA-资料与核验清单|协议来源与证据等级]] · [Markdown](pda/%E6%B5%B7%E5%BA%B7PDA-%E8%B5%84%E6%96%99%E4%B8%8E%E6%A0%B8%E9%AA%8C%E6%B8%85%E5%8D%95.md)

这些示例不能替代真机验证；`start` 是启动扫描服务，**不是**模拟一次扫码。

---

## 🧭 知识库导航

- 主题索引：[[docs/pda/00-PDA扫码知识地图|PDA 扫码知识地图]] · [Markdown](pda/00-PDA%E6%89%AB%E7%A0%81%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 扫码专题：[[docs/pda/海康PDA-广播协议与接入总览|广播协议]] · [Markdown](pda/%E6%B5%B7%E5%BA%B7PDA-%E5%B9%BF%E6%92%AD%E5%8D%8F%E8%AE%AE%E4%B8%8E%E6%8E%A5%E5%85%A5%E6%80%BB%E8%A7%88.md) · [[docs/pda/海康PDA-MAUI实现与示例|MAUI 接入]] · [Markdown](pda/%E6%B5%B7%E5%BA%B7PDA-MAUI%E5%AE%9E%E7%8E%B0%E4%B8%8E%E7%A4%BA%E4%BE%8B.md)


## 扫码声音与震动专项（2026-11-26，规划）

- [[docs/pda/PDA 扫码业务声音与震动反馈改造方案|扫码成功/失败音效与震动改造方案]] · [Markdown](pda/PDA%20%E6%89%AB%E7%A0%81%E4%B8%9A%E5%8A%A1%E5%A3%B0%E9%9F%B3%E4%B8%8E%E9%9C%87%E5%8A%A8%E5%8F%8D%E9%A6%88%E6%94%B9%E9%80%A0%E6%96%B9%E6%A1%88.md) — PDA-FB-01～08、F01～F22 触发矩阵、独立合成音效与验收（规划）。


## 2026-11-26 PDA 键盘与成功提示

- [[docs/pda/PDA 键盘避让与提交成功提示配置|键盘避让与提交成功提示配置]] · [Markdown](pda/PDA%20%E9%94%AE%E7%9B%98%E9%81%BF%E8%AE%A9%E4%B8%8E%E6%8F%90%E4%BA%A4%E6%88%90%E5%8A%9F%E6%8F%90%E7%A4%BA%E9%85%8D%E7%BD%AE.md) — IME Resize、固定操作栏、默认2秒及可配置时长；源码待 CI/真机验收。


PDA-FB-09～16：普通扫码错误及重复箱码统一自动消失的业务提示，默认2秒，共用原设置。失败音 SoundPool + MediaPlayer 备用，媒体音量；原 UUID 未知、草稿确认仍阻塞。详见上方专项文档。
