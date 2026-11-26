---
title: "PDA 键盘避让与提交成功提示配置"
aliases: ["PDA 键盘遮挡", "PDA 提交成功提示", "PDA IME Resize"]
created: 2026-11-26
updated: 2026-11-26
status: implemented-unverified
area: pda
tags: [StockCount, PDA, Android, MAUI, IME, UX]
up: "[[docs/pda/00-PDA扫码知识地图]]"
---

# PDA 键盘避让与提交成功提示配置

## 背景与范围

2026-11-26 用户提供海康 PDA 真机现象：扫码后自动聚焦数量，系统数字键盘显示时提交按钮在键盘后面；必须收键盘或滚动才能提交。用户批准修复布局并将提交成功提示默认改为 2 秒且支持配置。现场照片包含真实业务内容，不进入公开仓库。基线：`2570096bc6aae4c62e02619c4f3d1e1e0285b204`。

## 实施

| 需求 | 变更 | 状态 |
| --- | --- | --- |
| PDA-IME-01 | App 使用 MAUI Android WindowSoftInputModeAdjust.Resize | 已编码 |
| PDA-IME-02 | CountingPage SafeAreaEdges=All；数量与提交同属固定底栏 | 已编码 |
| PDA-IME-03 | 上方详情保持星号行可滚动，底栏紧凑间距，不手算键盘高度 | 已编码 |
| PDA-IME-04 | Android 小屏/不同输入法/字体缩放 | 待设备验证 |
| PDA-IME-05 | 成功提示默认 2000ms，配置 500/1000/2000/3000/5000ms | 已编码 |
| PDA-IME-06 | 设置→盘点交互设置，立即持久化、恢复默认 | 已编码 |
| PDA-IME-07 | 单元测试：默认值、枚举配置、非法值回退、旧计时器失效 | 已编码待 CI |
| PDA-IME-08 | Debug/Release APK、真机 | 待 CI/设备 |

### 键盘避让

采用 .NET MAUI 10 官方 Resize + 页面 SafeAreaEdges=All。CountingPage 的根 Grid 保留 Auto,*,Auto,Auto，只有中间 ScrollView 占剩余高度。数量输入框、清空、单位及提交按钮仍在 ScrollView 外；底栏保持 58dp 输入和 54dp 提交最小高度。SettingsPage 同时启用 SafeAreaEdges=All，以适配全局 Resize。无手写屏幕像素、无重复 IME inset padding，不新增自定义键盘或自动回车提交。保留自动聚焦与原数量 decimal 校验。

参考：[MAUI Android 软键盘模式](https://learn.microsoft.com/en-us/dotnet/maui/android/platform-specifics/soft-keyboard-input-mode?view=net-maui-10.0)、[MAUI 10 安全区域](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/safe-area?view=net-maui-10.0)。

### 成功提示

- Preferences key: `StockCount.Interaction.SuccessToastDurationMs`，设备本地保存，独立于 API、帐套、声音与震动。
- 允许 500、1000、2000、3000、5000 ms；缺失或非法值回退 2000 ms。
- 原 Core AutoDismissMs 保留兼容字段，默认同步为 2000 ms；CountingPage 实际显示时读取客户端设置，不受 Core 默认覆盖。
- 仅在服务端确认提交且本机清理完成后显示非阻塞、可触摸穿透的 SuccessToast；原 UUID 去重与声音反馈不变。
- 每次成功递增版本；旧回调不能隐藏新 Toast，离开页面使回调失效。
- 不等待 2 秒、不持有队列、不阻塞下一箱，异常弹窗仍由人工确认。

## 验收

使用测试帐套、合成/经授权测试箱码；禁止通过生产真实写入验收布局。

1. 扫库位、扫箱、数字键盘自动显示；键盘不关闭即可直接点击完整提交按钮，不能依赖向下滑动。
2. 320–480dp、海康设备现用输入法及系统输入法、字体缩放和屏幕方向下确认数量、单位、清空、提交按钮可见；内容详情可滚动。
3. 数量 0、合法小数、空值、无效值、只读、提交忙碌时按钮语义保持原逻辑。
4. 默认 2s；逐一切换 0.5/1/2/3/5 秒，重启保持、恢复默认成功。
5. 连续两次确认成功的时间间隔小于第一次提示时长时，旧回调不可提前关闭新提示。
6. 服务器确认成功后清箱、保留库位、收起键盘并立即可扫码；断网未知保留原 UUID/草稿，不出现成功提示。
7. 构建验证 Android Debug/Release、Core 测试、三项原生音效资源与既有业务回归。

本轮仅改 PDA 展示与配置及 Core 默认提示值，不修改数据库、API、权限、实际库存数据或扫码协议；当前尚无真机截图和 CI 结果。

[[docs/pda/00-PDA扫码知识地图|PDA 知识地图]] · [Markdown](00-PDA扫码知识地图.md)


## 盘点交互设置追加项（2026-11-26）

`StockCount.Interaction.HistoryEnabled`：bool，默认 false，设备本地 Preferences；允许查看已盘记录开关即时保存，独立于成功提示时长，恢复2秒不改变历史开关。入口和历史页面均校验。详见 [[docs/pda/PDA 已盘记录分页与搜索优化|已盘记录专项]] · [Markdown](PDA%20%E5%B7%B2%E7%9B%98%E8%AE%B0%E5%BD%95%E5%88%86%E9%A1%B5%E4%B8%8E%E6%90%9C%E7%B4%A2%E4%BC%98%E5%8C%96.md)。


## 2026-11-26 业务提示统一（PDA-FB-09～16）

原成功提示时长扩展至普通业务错误，设置更名“业务提示显示时长”，保留原键和值。成功绿色、失败红色，触摸穿透且共享计时版本。普通扫码/数量/明确重复错误无需点击；原 UUID 未知、草稿丢弃、结构/存储等其他提交拒绝仍人工确认。详见 [[docs/pda/PDA 扫码业务声音与震动反馈改造方案]]。
