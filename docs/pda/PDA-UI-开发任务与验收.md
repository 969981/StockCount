---
aliases: ["PDA UI 开发任务与验收", "StockCount PDA UI QA"]
title: "StockCount PDA APK UI 交互实施任务与验收清单"
created: 2026-11-23
updated: 2026-11-23
status: implemented-unverified
type: implementation-backlog
area: pda
tags: [StockCount, MAUI, PDA, tasks, UX, QA]
up: "[[docs/pda/PDA-UI-UX-设计规范与交互状态]]"
---

# PDA UI 开发任务 · V1

> **2026-11-23 实施进度**：已提交确认的 HTML 原型对应四页 XAML、扫码核心状态机、持久待确认 UUID、真实已盘接口和 Android CI 上传 APK 工件的源码。列表、扫码、设置、历史页面已编码；MVVM 彻底重构、海康硬件适配、编译及真实 ERP 端到端测试仍待验证。下列任务含已编码但未验收项目，**不得将源码提交视为测试通过**。设计主文件：[[docs/pda/PDA-UI-UX-设计规范与交互状态|UI/交互规范]] · [Markdown](PDA-UI-UX-设计规范与交互状态.md)。

## 本轮源码提交进度（静态核对，非运行验收）

| 状态 | 交付内容 | 入口 |
| --- | --- | --- |
| 已编码 | 列表卡片、搜索、分页、帐套标识、加载空态 | `CountListPage.xaml(.cs)` |
| 已编码 | 扫码页三段布局、固定底部数量/提交、只读箱字段 | `CountingPage.xaml(.cs)` |
| 已编码 | 扫码队列和忙碌提示、重复箱弹窗、草稿覆盖确认 | `CountingPage.xaml.cs` + `CountScanWorkflow.cs` |
| 已编码 | 确认成功立即清箱、不清库位；400ms非阻塞 Toast | `CountScanWorkflow.AcknowledgeSuccessAsync` |
| 已编码 | UUID/原始二维码/数量加密持久化与重启恢复 | `SecurePendingSubmissionStore.cs` + 列表恢复入口 |
| 已编码 | 已盘记录真实 GET 分页 | `HistoryPage.xaml(.cs)` |
| 已编码 | 帐套/HTTPS API/设备 Key 设置、切环境保护 | `SettingsPage.xaml(.cs)` |
| 已编码 | Core 扫码与持久化业务单测、新 APK CI 工件上传 | `CountScanWorkflowTests.cs` + `pda-android-build.yml` |
| 待验收 | MAUI/Android 真正构建、运行截图、海康扫码广播、连续扫码和 API/DB 联调 | Windows CI + 海康 PDA + XTEST/COB |
| 后续增强 | 完整 MVVM 化、未提交普通草稿的安全暂存、网络断连视觉与蜂鸣 | P1/技术债 |

## P0：稳定可用的扫码盘点闭环

| 任务ID | 开发任务 | 涉及文件/新组件 | 完成标准 |
| --- | --- | --- | --- |
| PDA-UI-01 | 建立 UI 设计 Token、统一按钮/卡片/字号，适配320–480dp | Resources/Styles/Colors.xaml, Styles.xaml, App.xaml | 无远程字体；52–56dp主按钮；可点击区域≥48dp；深浅文字对比可读 |
| PDA-UI-02 | 盘点单列表重新排版，增加环境/网络状态和加载/空态 | CountListPage.xaml, CountListViewModel | 全状态显示；后台搜索/分页；Test/COB持续可辨；canSubmit原因可见 |
| PDA-UI-03 | 主盘点页面改为三段Grid，固定底部数量与提交 | CountingPage.xaml, QuantitySubmitBar.xaml | 320dp宽/IME弹出仍能看见数量与提交；不滚动丢失主按钮 |
| PDA-UI-04 | ViewModel 化扫码与提交（不在 View 中管理业务真值） | CountingViewModel, CountScanWorkflow | 不可手动填库位；箱详情只读；无效扫描不保留旧箱 |
| PDA-UI-05 | 草稿保护：数量填好又扫另一箱/库位要确认 | CountScanWorkflow, ConfirmDiscardDraft | 默认继续当前箱；除用户确认，不覆盖已填数量 |
| PDA-UI-06 | 提交确认后立即清除箱和数量 | CountScanWorkflow, CountingPage, SuccessToast | API确认→清理草稿与箱信息→显示400ms Toast，同时可扫码；单号/名称/库位始终保留 |
| PDA-UI-07 | 独立提交状态与原UUID持久化恢复 | IPendingSubmissionStore, Android secure storage, ViewModel | 超时/重启恢复同环境、同单号、箱、库位、数量、原UUID；不重复 INSERT |
| PDA-UI-08 | 工业硬件扫码桥接、串行消费/可见排队 | IScannerService, Android BroadcastReceiver, MockScanner | no silent drop; Rx仅接收；真机验证Action/Extra/扫码键/CRLF；Debug 与 Release 盘点页均无模拟扫码入口 |
| PDA-UI-09 | 统一后端错误码和界面提示，含重复箱/数量空/网络未知 | ApiClient, AlertService, ErrorMapper | 指定文案精准一致，重复箱不填充，网络未知不清空；错误仅在用户确认后继续扫码 |
| PDA-UI-10 | 设置页更换端点、密钥和帐套的保护 | SettingsPage, SettingsViewModel | Production进入提示一次；环境切换不串上下文；有未知UUID不能直接丢草稿；API地址和密钥安全保存 |
| PDA-UI-11 | 真实 Android 编译、单元/UI/E2E验收 | tests, CI, Android 真机 | dotnet build成功、无XAML报错；端到端测试成功；仅受控帐套进行实际写入 |

## P1：效率与运营

| ID | 内容 | 依赖 |
| --- | --- | --- |
| PDA-UI-12 | 本单已盘记录只读列表 + 搜索/分页 | 后端 GET /api/v1/inventory-counts/{countNumber}/items |
| PDA-UI-13 | 扫码反馈蜂鸣/振动策略 | 确认海康机型和Android策略 |
| PDA-UI-14 | 扫码设备诊断 / 广播参数配置 | 工业设备实测 |
| PDA-UI-15 | 横屏、大字体、手套模式扩展 | 当前主竖屏成功验收 |

## 关键交互测试（Core / ViewModel）

| 场景ID | 输入/动作 | 预期 |
| --- | --- | --- |
| UX-01 | 无库位扫描完整箱码 | 请先扫描盘点库位！ + OK，无箱数据 |
| UX-02 | 扫库位 LOC-DEMO01、再扫 LOC-DEMO02 | 库位更新到 LOC-DEMO02 |
| UX-03 | 扫完整未盘二维码 | 展示箱/物料/批次/单位，数量空 |
| UX-04 | 重复箱 | 该箱号已扫描！ + OK，不填箱 |
| UX-05 | 数量留空点击提交 | 提示箱内数量为必填项，不发POST |
| UX-06 | 手工填99，QR参考数100 | POST人工99；两数据库数量列99 |
| UX-07 | 已填99又扫描第二箱 | 原草稿保留，确认弹窗默认继续当前箱 |
| UX-08 | 提交HTTP201 | 立即清箱并显示灰色半透明 Toast；无需等待消失即可扫下一箱，单号/库位不变 |
| UX-09 | 相同UUID重试HTTP200 | 显示成功，不产生第二条明细 |
| UX-10 | HTTP超时/断网后App重启 | 保留原UUID；先GET查询原结果，拒绝生成新UUID |
| UX-11 | 修改环境/主机名时仍有UnknownOutcome | 阻断无提示切换，提示先恢复或确认 |
| UX-12 | 在高频扫码时连续收到3条扫描事件 | 不静默吞掉后续扫描，队列有界且反馈明确 |
| UX-13 | XTEST / COB 分别可写 | 环境展示持续准确，API分别控制 canSubmit，绝不跨帐套 |
| UX-14 | 库位扫描后展示品名规格长文本 | 关键数量/提交始终可触达，长文本可查看详情 |
| UX-15 | Android后退/关闭当前页仍有未提交草稿 | 提示继续盘点/暂存，不直接丢数据 |

## 实施顺序

建议依次交付：**01/03 视觉基础** → **04/05/06 扫码状态正确性** → **07/08 弱网与硬件** → **02/09/10 可用性** → **11 编译+真机+测试**。UI 实现与后端表结构写入规则必须解耦，不能用前端绕过 API 的错误码。

Core 已新增草稿确认、成功显示后清箱、重启模拟恢复同 UUID 等测试**源码**，但未取得真实 dotnet test 执行通过结果；Android 真机断电/杀进程复现同样待进行。

## AI 编程任务交接模板

每个 PR / commit 需注明：任务ID；需求 P-xx；修改的XAML/ViewModel/Core/API文件；真实运行的 build/test 命令与结果；真机/Mock 使用范围；是否改动 Test/Production 写入行为；风险与待办。必须说明是否真正验证了海康扫码、二维码解析、连续扫描和未知UUID恢复。

## 关联

- [[docs/pda/PDA-UI-UX-设计规范与交互状态|UI/交互规范]] · [Markdown](PDA-UI-UX-设计规范与交互状态.md)
- [[docs/TEST_PLAN|全局测试]] · [Markdown](../TEST_PLAN.md)
- [[docs/IMPLEMENTATION_PLAN|全局研发计划]] · [Markdown](../IMPLEMENTATION_PLAN.md)
- [MAUI工程](../../src/StockCount.Pad/README.md)

## 2026-11-25 V2 紧凑布局（用户确认 HTML 原型）

| 验收项 | 要求 |
|---|---|
| PDA-V2-01 | 48dp导航、紧凑表头和库位、平面分区，移除常驻数量说明 |
| PDA-V2-02 | 6个字段对齐，长值换行；320–480dp下可滚动阅读 |
| PDA-V2-03 | 数量框25sp、单位内嵌右侧、底栏固定；扫码完成后可聚焦 |
| PDA-V2-04 | 整数/小数、真实提交、重复检查及未知结果保护保持原样 |
| PDA-V2-05 | 400ms Toast 不阻塞扫码，成功清箱保留库位 |
| PDA-V2-06 | 实体扫码与IME下数量框/按钮可操作；无模拟扫码入口 |

源码实现后须取得Android构建和真实设备视觉/操作验收，不能以HTML预览替代。
