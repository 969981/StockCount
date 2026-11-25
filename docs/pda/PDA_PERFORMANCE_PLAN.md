---
title: "StockCount PDA 性能优化计划与实测基线"
aliases: ["PDA 性能优化"]
created: 2026-11-25
updated: 2026-11-25
status: implemented-unverified
type: implementation-backlog
area: pda
tags: [StockCount, PDA, performance, MAUI]
up: "[[docs/pda/PDA-UI-开发任务与验收]]"
---

# PDA 性能优化（PERF）

2026-11-25 用户确认：优先性能诊断、增量刷新、主线程减负和设备配置缓存。保持已确认的 V2 作业页布局及全部业务校验，不修改 ERP/API 契约。

## 基线审计

- Debug 构建现场主观感觉卡顿；无真实帧时间、CPU、GC 或分阶段耗时数据，不能将候选瓶颈认作实测原因。
- CountingPage 扫码前、响应后及 finally 多次全量刷新；静态权限和颜色重复创建。
- 每个 HTTP 请求读取 Preferences 和 SecureStorage；HttpClient 已为单例，不需另建连接池。
- 草稿 SecureStorage 读/写/删除必须保留，且每次 POST 前保存原 UUID。
- ScanFeed 在正常作业路径构建完整字符诊断；列表逐条集合通知和非编译绑定后续处理。

## 第一轮实现

| 任务 | 实现 | 验收 |
|---|---|---|
| PERF-001 | ClientPerformance 有界512阶段样本、最近值/P50/P95/最大值；设置页刷新、复制、清空 | Core 单测；真机复制报告 |
| PERF-002 | 按 Location/Box 值变化刷新字段；缓存颜色；静态权限初始化一次；按钮/输入框状态差异更新 | 核对 UI 视觉及全部状态路径 |
| PERF-003 | HTTP 服务层避免不必要的 UI 上下文恢复；草稿操作在线程池运行并串行、等待完成 | 检查原 UUID、恢复、失败路径 |
| PERF-004 | 已在 UI 线程直接分发；普通扫码仅保留轻量诊断，设置页才分析完整字符形态；有界队列不变 | 连续实体扫码及设置诊断 |
| PERF-005 | API 地址、帐套、设备密钥会话缓存；设置保存后更新快照，密钥失效重读；配置异常阻止后续业务请求 | 更换设置、空密钥、存储失败、帐套隔离 |

本轮不缓存待确认草稿，不改变其持久化顺序；不移除 SQL 重复检查、事务、UUID；不引入后台 fire-and-forget 写入。不更改正常扫码页面 XAML。

## 诊断指标边界

ScanQueue 从页面收到 ScanReceived 到队列处理；ScanTotal 从页面收到事件到处理结束（可能含弹窗人工等待），不代表扫码器解码时间。ScanWorkflow 含 Core 校验和 API；SubmitTotal 含本地处理、HTTP、确认清理以及可能的人工等待，不含输入数量时间。SubmitWorkflow 含草稿保存和 HTTP；ConfirmCleanup 含确认后草稿清理；RequestHeaders 含配置/密钥读取与锁等待；HttpResponse 含传输、后台处理和默认 HTTP 内容缓冲，失败也记录；JsonDecode 为反序列化阶段；DraftRead/Write/Clear 为本地操作阶段。UiRefresh 与 QuantityFocus 只统计同步代码执行，不是实际帧绘制或输入法完成时间。样本阶段嵌套，不能相加；512 为所有阶段合计，P95 是最近窗口的经验分位数。无结果标签，不把错误/人工等待样本当纯成功延迟。无持久日志、业务原文或密钥。

## 实测流程

1. 同一 PDA、同一 Wi-Fi、同一后台和 XTEST 测试单；记录型号、Android、APK 构建与签名。
2. 基线 Debug、Release 分开测试；先热身，清空诊断，各执行至少30箱合法不重复测试数据，复制报告。因正常重复箱规则，不反复提交同一箱。
3. 单独测试弱网/重复/未确认提交，清空采样后测，避免人工弹窗混入成功延迟基线。
4. 记录 adb shell dumpsys gfxinfo com.dreamer.stockcount.pad framestats、dumpsys meminfo，必要时 Perfetto；测帧时间与内存，不以 UiRefresh 代替。
5. 断网前后、重启恢复、换帐套、设置保存失败、原 UUID 重试、草稿丢失保护、整数/小数、400ms Toast 连续扫码均应回归。不得卸载含未确认草稿的旧 APK。
6. 目标：点击反馈≤50ms、正常作业扫码总P95≤200ms、提交P95≤300ms（包括网络/服务端，初步目标），提交确认后客户端清理≤100ms。未获得数据时不声称达标。

## 后续阶段

PERF-006 编译绑定和列表批量更新、PERF-007 JSON source generation、PERF-008 Release/Profile 基线、PERF-009 IME/实体键盘、PERF-010 500箱及8小时压力测试。根据实际数据决定优先级。SQL 与服务端性能另立专项，不在本轮修改。

当前仅源码实现及新增单测，Android 编译、真机运行及前后对照数据尚待验收。
