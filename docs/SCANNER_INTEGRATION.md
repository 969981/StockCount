# Android 工业 PDA 扫码接入规范

状态：设备适配方案，真实型号与固件扫描广播 Action/Extra **必须在现场再验证**。2026-11-23。

## 1. 适配方式

首选设备扫描服务通过 Android Broadcast/Intent 向应用发送扫码结果。MAUI 的 Android 平台项目接收系统事件，再通过抽象接口上报到共享 ViewModel；**不是通过摄像头扫描、也不能假定 TextBox 焦点等于扫码事件**。

此前另一个工业 PDA 项目记录了海康 MV-IDP5204 / 扫码服务广播路线，该方案可借鉴，但需在 StockCount 实际目标设备上确认。

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
    "BroadcastAction": "<DEVICE_VERIFIED_ACTION>",
    "BarcodeExtraKey": "<DEVICE_VERIFIED_EXTRA_KEY>",
    "Charset": "UTF-8",
    "DuplicateSuppressMilliseconds": 800
  }
}
```

这些值只是**可配置键名与参考节流参数**，不是已在当前设备验证的协议。先获取 PDA 型号、Android 版本、扫描服务版本与真机 adb/logcat，再根据厂商文档核对 Action 和 Extra Key。默认拒绝未知 Action，不接收非预期来源的敏感广播。

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