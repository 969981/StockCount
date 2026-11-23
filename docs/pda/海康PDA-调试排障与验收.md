# 海康 PDA 扫码调试、ADB 排障与 StockCount 验收

> [!WARNING] 安全
> 仅在有权限的目标设备和测试环境调试。不要把真实二维码、业务箱码、生产数据库信息、内网地址、设备唯一标识或 Logcat 原文提交到公开 GitHub。此页是**可执行的排障手册，不是已完成测试记录**。

## 1. 前置条件与 ADB

1. 在 PDA 系统设置启用开发者选项、USB 调试；Windows 安装匹配的 ADB 驱动，使用目标 PDA 数据线连接。
2. Windows PowerShell 检查：

```powershell
adb devices -l
adb shell getprop ro.build.version.release
adb shell getprop ro.product.model
adb shell pm list packages | findstr /I "hikrobotics pdaservice"
adb shell dumpsys package com.hikrobotics.pdaservice
```

3. 如果设备显示 `unauthorized`，在 PDA 上批准调试授权；如果 `offline`，检查驱动、USB 模式、数据线和 Android 授权。设备型号、扫描服务软件包版本和 Android SDK/API 等信息只在本地核对，不上传敏感序列号。
4. 确认系统扫码应用里服务已启用，输出模式已切 `BROADCASTMODE`，单次扫描按需选 `SINGLEMODE` / 3000ms。

## 2. 从硬件到业务分层排查

| 层级 | 检查方式 | 典型问题 |
| --- | --- | --- |
| 扫描引擎 | 实体扫码键、模组红光、识别声 | 模组/实体键/扫码服务未工作 |
| 输出模式 | PDA 扫描助手设置 | 焦点覆盖模式下 Receiver 收不到事件 |
| 广播配置 | Action `com.service.scanner.data`、Extra `ScanCode` | 厂商设置改了 Action/Extra，大小写不匹配 |
| Android 接收器 | 进入作业页、注册与注销日志 | Android 13+ exported 标志、页面生命周期、重复注册 |
| StockCount 解析 | `LC`/箱二维码、字段长度、完整原始内容 | 二维码被前缀/截取功能破坏 |
| API | 环境路由、标签/库位查询、网络 | 扫码成功但后端查无标签 |
| 写入 | 人工数、幂等 ID、XTEST 受控回查 | 扫描成功 ≠ ERP 实盘保存成功 |

## 3. 设备扫描服务自测（仅在测试 PDA 上）

**配置命令，不是软件触发实体键**：

```powershell
# 启动扫描服务：需要已安装原厂 com.hikrobotics.pdaservice。
adb shell am broadcast -a com.service.scanner.start -n com.hikrobotics.pdaservice/com.pda.service.broadcast.LaunchReceiver

# 将数据输出方式设置为 BROADCASTMODE（可能影响该 PDA 上其他扫码 App）
adb shell am broadcast -a com.service.scanner.outputmode -n com.hikrobotics.pdaservice/com.pda.service.broadcast.ServiceControlReceiver --es outputMode BROADCASTMODE

# 按实体扫描键，使用无业务数据的测试二维码验证。
```

Android 版本/厂商权限可能限制 `adb shell am broadcast` 对目标接收器的调用；这些命令是**实验用的配置尝试**，并不保证每个设备/固件都允许。不要在生产作业现场未经批准更改共享服务设置。

## 4. 观察 Logcat（不记录真实条码）

```powershell
adb logcat -v time | findstr /I "StockCount Scanner Receiver"
```

接收器应只记录：Action 是否匹配、Extras 是否存在、条码长度/格式、状态机阶段及错误码；**不能打印原始 `ScanCode`、用户输入数量或 API 凭据**。可在 Mock 模式使用 `ID.BOX-DEMO-01@@QT.100` 等合成码。

如果只有 TextEntry 接收到条码而 `OnReceive` 一直没有事件，优先查输出模式；如果广播到了但内容为空，查 `ScanCode` 大小写或自定义 `broad_code`；如果硬件可读码但 API 报标签不存在，排查 ERP 绑定而不是扫描引擎。

## 5. Mock 广播与真实读码的区别

```powershell
# 合成广播，**只验证本应用 Receiver 的代码路径**
adb shell am broadcast -a com.service.scanner.data --es ScanCode "ID.BOX-DEMO-01@@QT.100" --es ScanCodeType "QR"
```

第三方模拟广播未必能到达未 exported 的动态 Receiver；收到模拟广播**并不能证明海康扫码服务/实体键已联调成功**。真实验收必须通过实体扫描键/设备扫描引擎完成，并重新查固件配置。

## 6. StockCount PDA 验收矩阵

| 场景 | 正确行为 |
| --- | --- |
| 无登录启动 | 自动进入现有盘点单列表；可切 Test/Production |
| 选测试帐套 | XTEST 单据全部可见，包括不同状态 |
| 无库位扫箱 | 提示先扫库位，不填箱信息 |
| 扫库位 | 按 `STOFCY_0 + WRH_0 + LOC_0` 业务定位，不只用 LOC |
| 扫原始箱 QR | 保留 `@@`、`|`、中文信息，读出物料/批次 |
| `STA_0=4/9` | 不能仅因标签状态拒绝盘点 |
| 标签数量100、人工实盘99 | `QTYSTU_0=QTYPCU_0=99`；QR QT=100 仅参考 |
| 连续扫不同箱码 | 不串单、不覆盖当前未提交草稿 |
| 连扫同一箱码 | UI 轻量防抖；服务端持久幂等、防重 |
| 数量输入时按扫描键 | 数量文本框不应被扫描内容覆盖 |
| 后端超时 | 保留箱码/库位/数量及原 RequestId，不误报成功 |
| 前后台切换 | 无重复 Receiver，不漏订阅恢复 |
| Production 切换 | 持续可见环境提示；后端正式写入默认关闭 |
| 安卓系统升级 | 重新验证 Receiver 注册/厂商广播兼容 |

## 7. 现场记录模板

```text
设备型号：<已核对>
Android 版本 / API：<已核对>
扫描服务版本：<已核对>
扫描输出模式：BROADCASTMODE / 其他
接收 Action：<已核对>
条码 Extra：<已核对>
码制 Extra：<已核对>
实体按键：通过/失败
MAUI 接收：通过/失败
XTEST 标签查询：通过/失败
XTEST 受控写入：通过/失败/未授权
问题与脱敏日志：<仅错误码/时间>
```

核对 [[海康PDA-广播协议与接入总览]]、[[海康PDA-MAUI实现与示例]] 与 [[TEST_PLAN]]。
