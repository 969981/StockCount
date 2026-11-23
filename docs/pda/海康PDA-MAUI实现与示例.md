---
title: "海康 PDA · .NET MAUI Android 扫码适配技术说明与 C# 示例"
aliases:
  - "StockCount · docs / pda / 海康PDA-MAUI实现与示例"
created: 2026-11-23
updated: 2026-11-23
status: reference
area: pda
tags:
  - StockCount
  - pda
  - docs
up: "[[docs/pda/00-PDA扫码知识地图]]"
---

# 海康 PDA · .NET MAUI Android 扫码适配技术说明与 C# 示例

> [!NOTE] 适用范围
> 以下代码是依据海康原厂广播协议 V1.0.1 与旧项目记录重建的**实现示例**，**不是找回的原项目最终源码，也未声称已经编译/安装/真机跑通**。开发 Agent 必须按目标 SDK 版本验证 `RegisterReceiver` 的 API、生命周期和权限。

## 1. 组件分层

```text
src/StockCount.Pad/
├─ Platforms/Android/Scanning/
│  ├─ HikScannerProtocol.cs         # 常量/显式目标组件
│  ├─ HikScannerConfigurator.cs     # 配置扫描服务输出/模式
│  └─ ScannerBroadcastReceiver.cs   # Android Intent → 纯扫码数据
├─ Services/Scanning/
│  ├─ IScannerService.cs            # 前端共享协议
│  ├─ HikvisionBroadcastScannerService.cs
│  └─ MockScannerService.cs
└─ ViewModels/InventoryCountScanViewModel.cs
```

- `Platforms/Android` 只能做设备与广播适配；不能写 SQL、发 X3 业务请求。
- `IScannerService` 负责扫码事件传递，ViewModel 不 import `Android.Content`。
- 生产实例为 `HikvisionBroadcastScannerService`；设计/单测用 `MockScannerService`，不要用焦点文本框冒充真实硬件适配。
- 服务端才执行站点/库位/标签校验和幂等保存。

## 2. Android 原厂 Action 与目标组件

```csharp
#if ANDROID
using Android.Content;

internal static class HikScannerProtocol
{
    public const string ServicePackage = "com.hikrobotics.pdaservice";
    public const string LaunchReceiver = "com.pda.service.broadcast.LaunchReceiver";
    public const string ControlReceiver = "com.pda.service.broadcast.ServiceControlReceiver";
    public const string BarcodeAction = "com.service.scanner.data";
    public const string BarcodeExtra = "ScanCode";
    public const string BarcodeTypeExtra = "ScanCodeType";
}
#endif
```

### 2.1 配置输出模式、单次扫描

```csharp
#if ANDROID
using Android.Content;

internal static class HikScannerConfigurator
{
    public static void StartAndConfigure(Context context)
    {
        var start = new Intent("com.service.scanner.start");
        start.AddFlags(ActivityFlags.IncludeStoppedPackages);
        start.SetComponent(new ComponentName(
            HikScannerProtocol.ServicePackage, HikScannerProtocol.LaunchReceiver));
        context.SendBroadcast(start);

        var output = new Intent("com.service.scanner.outputmode");
        output.SetComponent(new ComponentName(
            HikScannerProtocol.ServicePackage, HikScannerProtocol.ControlReceiver));
        output.PutExtra("outputMode", "BROADCASTMODE");
        context.SendBroadcast(output);

        var mode = new Intent("com.service.scanner.scanmode");
        mode.SetComponent(new ComponentName(
            HikScannerProtocol.ServicePackage, HikScannerProtocol.ControlReceiver));
        mode.PutExtra("scanMode", "SINGLEMODE");
        mode.PutExtra("singleTime", 3000);
        mode.PutExtra("intervalTime", 3000);
        context.SendBroadcast(mode);
    }
}
#endif
```

**说明**：`StartAndConfigure` 发送的是配置命令，**不是**强制扫描一次。启停整机扫描服务可能影响同设备其它业务 App；首次进入作业页只在需要时配置，**不要离开一页就随意停止系统服务**。

### 2.2 Android BroadcastReceiver 示例

```csharp
#if ANDROID
using Android.Content;

internal sealed class ScannerBroadcastReceiver : BroadcastReceiver
{
    public event Action<string, string?>? Received;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != HikScannerProtocol.BarcodeAction) return;

        var raw = intent.GetStringExtra(HikScannerProtocol.BarcodeExtra);
        if (string.IsNullOrEmpty(raw)) return;

        // 只移除传输终止符，不改变二维码内原始内容和业务前后缀。
        var code = raw.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(code)) return;

        Received?.Invoke(
            code, intent.GetStringExtra(HikScannerProtocol.BarcodeTypeExtra));
    }
}
#endif
```

**广播接收器只作轻量数据搬运**，不要在 `OnReceive` 中同步进行网络访问、等待数据库事务、弹模态 UI。事件应被调度到 MAUI 主线程/ViewModel 的异步处理队列，防止 ANR、UI 卡死。

## 3. 注册/注销与 Android 13+ 兼容

- 在**作业页可接收扫码的生命周期**注册；离开/暂停页面则注销，防止重复监听和内存泄漏。选单、设置页无需把广播输入给实盘数量框。
- Android 13+ 的动态广播 `RegisterReceiver` exported 标志 API 与 Android 11 不同。厂商服务属于**外部应用**，通常需要按平台要求使用 `RECEIVER_EXPORTED` 才能接收非系统广播；具体 MAUI/AndroidX API 签名与设备广播行为须在选用 .NET 10 workload 后编译和真机核实。
- 如果不得不声明 exported Receiver，就要承认第三方 App **可能伪造**同名 Action 和 Extras；不能把“我收到了广播”视为可信业务授权，后端始终重新核验二维码/环境/箱号/盘点单。
- 接收器注册状态建议设计为**可重复调用且只生效一次**，强制成对注销；页面回前台需恢复监听，应用进程销毁后重新注册。
- 页面在 `Saving` 状态时暂不接收新一箱扫码；网络超时保留原单、库位、箱号、实盘数和幂等 ID。
- 前端防抖（例如 800ms）仅抑制设备重复事件，**不能代替服务端幂等**。

## 4. 跨平台接口与 ViewModel

```csharp
public sealed record ScannerRead(
    string RawText,
    string? Symbology,
    DateTimeOffset ReceivedAt);

public interface IScannerService
{
    event EventHandler<ScannerRead>? BarcodeReceived;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
```

推荐事件分配：

```text
无盘点单 → 提示“请选择盘点单”
已选单，无库位 → 扫 LC 库位，后端按站点/仓库/库位核查
库位已确认 → 扫箱二维码，将完整 RawText 发 POST /labels/resolve
查到标签 → 展示物料名称/批次/单位，人工数量框仍为空
人工输入数量 99 → 调用 POST /inventory-counts/{id}/items
后端保存成功 → QTYSTU_0=99 且 QTYPCU_0=99
→ 清空该箱表单，保留盘点单与库位
```

- `XLABELDTL.STA_0` 仅展示/审计，**不得做 1/7/8 状态白名单**。
- `QRCOD_0` 内的 `QT` 是二维码参考值，**不是**用户输入的实盘数；不将 `ScanCodeType` 当标签状态。
- 页面状态由 ViewModel 管理，而不是设备接收器；输入状态异常时不悄悄删除用户草稿。
- 正式/测试环境切换必须隔离 API 请求/缓存及未知提交状态，不能将之前的 ScanCode 在新环境后台自动提交。

## 5. 伪造与隐私边界

- 不把 `ScanCode` 全量写到生产日志；用长度/格式/散列或合成条码进行诊断。
- 客户端拒绝未注册状态的扫描事件，并只接受预设 Action；这种过滤是体验防线，不是鉴权。服务端负责最终授权。
- Android 部分设备还可能将条码通过键盘模式发送，必须检查 `BROADCASTMODE`，不要把输入框中的字符误当安全扫码来源。

## 6. 验证清单

[ ] .NET 10 目标 Android 编译通过；[ ] 目标海康设备物理扫描成功；[ ] Action/Extras 与固件一致；[ ] Android 11 / 升级后注册策略验证；[ ] 数量框不会被扫码覆盖；[ ] 后端超时不丢草稿；[ ] 两数量列同值写入；[ ] STA 任意状态仍可进入标签解析。

下一步见 [[海康PDA-调试排障与验收]] 和 [[SCANNER_INTEGRATION]]。

---

## 🧭 知识库导航

- 主题索引：[[docs/pda/00-PDA扫码知识地图|PDA 扫码知识地图]] · [Markdown](00-PDA%E6%89%AB%E7%A0%81%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](../00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 全链路：[[docs/SCANNER_INTEGRATION|扫码总规范]] · [Markdown](../SCANNER_INTEGRATION.md) · [[docs/pda/海康PDA-调试排障与验收|真机验证]] · [Markdown](%E6%B5%B7%E5%BA%B7PDA-%E8%B0%83%E8%AF%95%E6%8E%92%E9%9A%9C%E4%B8%8E%E9%AA%8C%E6%94%B6.md)
