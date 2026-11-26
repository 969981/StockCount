---
title: "StockCount PDA APK · UI 与交互设计规范 V1"
aliases: ["PDA UI UX", "StockCount PDA 交互规范"]
created: 2026-11-23
updated: 2026-11-23
status: implemented-needs-build-and-device-qa
type: design-spec
area: pda
tags: [StockCount, PDA, MAUI, UX, scanning]
up: "[[docs/pda/00-PDA扫码知识地图]]"
---

# StockCount · 工业 Android PDA UI 与交互规范

> 本文最初是设计规划；2026-11-23 已按用户确认的 HTML 原型提交 MAUI XAML 页面及状态机改造，但不是已运行 APK 的界面截图。设计基于仓库现有 MAUI 页面、CountScanWorkflow 和 2026-11-23 已确认的业务要求。全部样例均为合成数据，公开仓库不使用真实企业单号、箱码和凭据。

## 1. 产品目标和设计假设

- 使用者：仓库现场人员，单手持工业安卓 PDA、实体扫码键高频扫描，现场网络可能抖动。
- 核心路径：无登录 → 选 Test(XTEST)/Production(COB) → 选择已有 XCOUTH → 扫实际库位 → 扫完整箱码 → 查询 XLABELDTL + ITMMASTER → 人工填箱内 STU 数量 → API 写 XCOUTD → 立即清箱 → 400ms 非阻塞 Toast → 连续下一箱。
- 技术：继续使用 .NET 10、MAUI、XAML；Windows 管理器的 Tauri 2 只负责后端管理，不放进 PDA APK。
- 推荐视觉：「工业清爽效率型」，白底、浅灰卡片、蓝色操作、明确的文字状态，而非大面积高饱和渐变。
- 布局基准：按 360×640dp 竖屏设计，兼容 320–480dp 宽度、系统字体放大、Android 软件键盘和厂商实体按键。**设备实际屏幕尺寸、Android 版本及广播 Action/Extra 未经真机确认**。
- 固定规则：盘点库位只能扫码；人工数量必须录入，不能将标签 QT 值自动填充；QTYSTU_0 = QTYPCU_0 = 人工数量；XLABELDTL.STA_0 不限制标签查询；XTEST 和 COB 两帐套分别授权后均可写入。
- V1 不做：登录/注册、盘点单创建或关闭、直接修改已盘记录、离线写 ERP、让 PDA 输入 SQL Server 连接串。

## 2. 页面树

~~~text
StockCount（无需登录）
├─ 盘点单列表：帐套+网络状态 / 搜索 / 刷新 / 分页
│  └─ 选择已有盘点单
│     └─ 扫码盘点：表头 / 库位 / 箱码明细 / 人工数量 / 固定提交按钮
│        └─ 已盘记录：本单只读分页列表（P1，需新 GET items API）
└─ 连接设置：API 地址 / 帐套 / 设备密钥 / 连接检测 / 扫码诊断
~~~

主导航保持极简。建议不放固定底部四 Tab：PDA 上真正高频的是「扫码盘点」，底部空间应留给数量和提交键。现有 NavigationPage 可继续用；若后续页面增多，再统一迁移 MAUI Shell。

### 2.1 盘点单列表

- 标题「选择盘点单」，右侧设置入口；明显显示「测试 · XTEST」或「正式 · COB」，并显示「已连接」「连接中断」文字。
- 搜索框按单号和名称搜索；所有盘点单均展示，不根据 XCOUTH.CUNSTA_0 过滤；服务端支持 pageSize/cursor 逐页加载。
- 卡片主信息：单号（显著）、盘点名称（最多2行）、站点/仓库、日期、原始状态和可提交状态。原始状态无经过校验的字典映射时展示「状态 0」，不得擅自翻译。
- 服务端 canSubmit=false：仍可点击查看，但清晰提示不可提交原因，不在客户端自行越权打开。
- Loading：骨架条；空列表：「当前帐套暂无盘点单」+ 重试；连接失败：「连接失败」+ 重试；点击卡片后按单号进入盘点页。
- 切换 Test/Production 必须重置选单、库位、箱信息与分页状态，先处理任何未确认提交。

### 2.2 核心：扫码盘点页（V2 已确认）

2026-11-25 用户已确认 HTML 交互原型，正式 MAUI 使用平面分区、细线分隔、高密度字段表、固定数量提交栏，取消大圆角信息卡片及常驻数量说明。

~~~text
‹  扫码盘点                    ▤ 已盘记录
测试 · XTEST                     等待扫码
────────────────────────────────────
COUNT-DEMO-001                     可提交
示例盘点                      A01 / WH01
────────────────────────────────────
盘点库位                          已确认
LOC-DEMO01                     重扫可切换
────────────────────────────────────
当前箱信息                         已读取
箱码            BOX-DEMO-001
产品编码        ITM-DEMO-01
品名            示例物料
规格            DEMO-SPEC
批号            LOT-01
存货单位        PCS
────────────────────────────────────
箱内数量 *
[ 99                                PCS ]
[              提交盘点                 ]
~~~

- 顶部导航48dp，帐套条最小30dp，表头两行；正式帐套保留琥珀色环境标识，权限标签依据服务端 canSubmit。
- 库位区25sp，浅蓝灰背景，扫码修改，不开放手工录入。
- 当前箱字段使用72dp标签列、8dp列距、最小31dp行高、13sp字体及细分隔线。长值按字符换行，中间区域允许滚动；未扫码时为简洁文字空态。
- 固定底栏：12dp水平内边距；数量25sp、输入最小高度58dp（按字体测量扩展）、独立48dp清空按钮与单位列，原生下划线透明化；提交最小高度54dp、垂直内边距10dp、圆角4dp、主色#175AB7。窄屏及IME下保持操作可达。
- 删除“箱内数量必须人工填写；二维码中的QT…”说明块；仍要求人工输入，标签参考数不代填。空态及错误才显示必要提示。允许非负小数，后端有效小数最多13位。
- 扫箱后待输入框恢复可用再请求聚焦；成功立即清理已提交草稿及箱信息，400ms灰色半透明非阻塞Toast；保留盘点单、库位，继续扫码。
- 保留真实的待确认提交面板、重复箱错误弹窗及只读原因。原型的模拟扫码按钮不进入APK。
- 当前原型的已盘记录页面仅供流程演示；本轮重构范围为扫码盘点页，历史页保留已合并的数量格式优化。
- 验收：Android 320/360/390/480dp、长字段、字体缩放、软键盘弹出、连续扫码、真实后台的失败/未知结果状态。HTML 预览与原生 Android 运行截图分别验收。


### 2.3 已盘记录（已编码，待真机验收）

由「扫码盘点」页面顶部更多菜单进入本单只读明细：箱码、库位、实盘数量、提交时间。支持筛选、分页、刷新；无编辑/删除按钮。API 的 GET submissions/{requestId} 只能确认一个已知 UUID，**不能当作已盘明细列表**；新增本单列表需要 GET inventory-counts/{countNumber}/items API。

### 2.4 连接设置页

- 可配置 API HTTPS 地址、Test/Production、设备访问密钥；不出现 SQL Server 地址或密码。
- 设备密钥录入使用安全输入框与 MAUI SecureStorage，留空保留原配置；替换密钥提供明确操作。
- 「测试连接」分开显示：API 可达；SQL 数据库 /ready 可达；本盘点单是否允许提交。SELECT 1 不能证明 ERP 表有 INSERT 权限。
- 改帐套、改服务器地址、改密钥前处理未提交草稿与未知提交，避免串帐套。
- 调试模式显示扫码广播接收状态；不得将企业原始二维码明文上传 GitHub 或记录到普通日志。

## 3. UI 状态机和事件约束

| 状态 | 界面内容 | 扫码行为 | 提交 |
| --- | --- | --- | --- |
| AwaitLocation | 库位为空，提示先扫库位 | 库位 → ResolveLocation；箱码 → 指定弹窗 | 禁用 |
| ResolvingLocation | 库位验证中 | 受控排队，不能静默丢扫码 | 禁用 |
| AwaitBox | 库位确认，等待箱码 | 箱码 → ResolveLabel；库位 → 可切换 | 禁用 |
| ResolvingBox | 读取中 | 防止双请求、重复按键 | 禁用 |
| AwaitQuantity | 展示只读箱信息，数量空 | 新箱/库位先检查是否会丢草稿 | 禁用 |
| ReadyToSubmit | 人工数量有效 | 保护本箱草稿 | 启用 |
| Submitting | 主按钮「正在提交…」 | 禁止新提交和覆盖本箱 | Loading |
| Submitted | 中央灰色半透明「提交成功！」400ms | 立即清箱，不清单/库位 | 提示不拦截触摸/扫码 |
| Duplicate | 「该箱号已扫描！」+ OK | 点 OK 关闭并继续扫码 | 禁用 |
| UnknownOutcome | 「提交结果待确认」 | 保留原 UUID/草稿，不切环境 | 查结果/同 UUID 重试 |
| Offline | 「连接中断」 | 不尝试离线写 ERP | 禁用或明确手工重试 |

扫码统一经过平台 IScannerService：规范化 CR/LF、保留二维码内部 ID/@@/|，通过 API 判定真实库位和标签。不能只通过字符长度推断库位有效，也不允许 XLABELDTL.STA_0 状态筛选。

**草稿保护（必须修正已有实现）**：已扫箱且输入数量后，又扫新箱或新库位，不应直接覆盖。先保留新扫码内容，弹窗「当前箱尚未提交，继续扫描会丢弃已输入数量」；默认按钮「继续当前箱」，次按钮「丢弃并处理新扫码」。若存在 UnknownOutcome，则不允许丢弃或切换帐套，先确认原 UUID 的提交结果。

**高频扫码**：收到扫码后立即显示「处理中」，串行消费事件；短时间重复发送相同字符串应做输入事件级去抖，但不同箱码不能被无提示丢弃。当前 CountingPage 的 if (_busy) return 会静默忽略输入，需升级为容量有限队列或提示「正在处理，请稍后重扫」的反馈。

### 3.1 核心业务的中文提示

| 场景 | 文案 | 展示方式 |
| --- | --- | --- |
| 成功扫库位 | 库位已更新 | 非阻塞小提示，约0.8秒 |
| 先扫箱、库位空 | **请先扫描盘点库位！** | 模态窗+OK |
| 当前单重复箱码 | **该箱号已扫描！** | 模态窗+OK，禁止填箱 |
| 找不到箱 | 未找到该箱标签，请重新扫描 | 模态窗+OK |
| 数量未填 | **箱内数量为必填项！** | 数量框下错误+焦点；可补弹窗 |
| 正在提交 | 正在提交… | 固定按钮 Loading，防双击 |
| 提交成功 | **提交成功！** | 立即清箱，居中半透明提示400ms，不阻塞扫码 |
| 提交结果未知 | 提交结果待确认，请先检查结果 | 持久警示栏，不能自动消失 |
| 后端未授权写入 | 当前帐套未开启写入，请联系管理员 | 模态窗+OK |
| 网络断开 | 服务连接异常，请检查网络 | 持久连接状态+重试 |

成功顺序必须是：HTTP 201 或同 UUID 重放200（status=Committed）→ 清理本地已确认草稿并清空箱码/物料/规格/批号/单位/数量 → 等待下一箱，同时显示可配置非阻塞 Toast（默认2000ms）；始终保留盘点单号/名称/盘点库位。

### 3.2 断网与未知提交结果

- 提交前生成并稳定保存 Idempotency-Key(UUID)，连同原环境、单号、箱、库位、数量保留到恢复草稿中。
- 如果 HTTP 断开、超时或服务器返回 UNKNOWN，不等于失败。保持所有输入，调用 GET /api/v1/submissions/{requestId}?countNumber=...。
- 200确认写入则执行成功流程；404表示未查到已提交记录，可**以原 UUID**重试；503或无响应则继续保留草稿并重新查询。
- 增加本地受保护持久化存储（P0）：应用被 Android 系统杀掉或断电后，重新启动可恢复未知提交 UUID。建议由 Android Keystore 保护本地草稿加密密钥，保存最小字段至应用沙盒加密数据库/文件；该功能已采用 `SecurePendingSubmissionStore` 在 POST 前写入 Android SecureStorage；真机杀进程恢复测试仍未完成。
- 不提供自动离线向 ERP 写入；不要记录完整企业扫码字符串到公开控制台日志。

## 4. 视觉 Token 与空间规范

| 项目 | 标准 | 说明 |
| --- | --- | --- |
| Primary | #376BEE | 主操作和焦点 |
| 页面背景 | #F6F8FC | 保持清晰 |
| 卡片 | #FFFFFF / 1dp 浅边框 | 低干扰 |
| 主文字 | #17233B | 箱码、库位、数量 |
| 次级文字 | #68788F | 标签、说明 |
| Success | #16855D | 已验证 / 成功 |
| Warning | #B96B14 | COB 正式 / 待确认 |
| Error | #C73535 | 错误 / 重复 |
| 间距 | 4/8/12/16/24dp | 统一 |
| 圆角 | 8–12dp | 控件简洁 |
| 页面标题 | 20–22sp | 可读性 |
| 库位 / 数量主值 | 22–28sp | 高频识别 |
| 一般正文 | 14–16sp | 字段值 |
| 辅助文本 | 12–13sp | 不靠颜色传递关键含义 |
| 点击区域 | 至少48×48dp；主按钮52–56dp高 | 戴手套/单手点击 |
| 外边距 | 12–16dp | 窄屏适配 |

使用 Android 自带字体和本地矢量图标，离线可显示；支持系统字体缩放、无障碍阅读文本以及软键盘安全区。遵循 Android 开发者的建议：可点击控件至少48×48dp；不只用颜色表示状态。

## 5. MAUI 架构与改造映射

| 现有文件 | 当前情况 | 设计改动 |
| --- | --- | --- |
| CountListPage.xaml | 基础 CollectionView + 搜索 | 统一环境横幅、状态角标、完整卡片、加载/空态 |
| CountingPage.xaml | 全屏 ScrollView | 三段 Grid + 固定 QuantitySubmitBar + 滚动信息 |
| CountingPage.xaml.cs | 事件式控制、直接操作 x:Name | 提取 CountingViewModel 与命令绑定 |
| CountScanWorkflow.cs | 库位/箱解析与提交逻辑已有 | 明确状态、草稿确认、扫码事件队列、确认成功后立即清理 |
| MainActivity.cs / ScanFeed.cs | 硬编码扫码广播 | IScannerService + AndroidAdapter + MockScannerService，现场核验广播 |
| StockCountApiClient.cs | 已有后端 API | 错误码映射、超时状态、提交 UUID 的本地恢复 |
| SettingsPage.xaml | 基本连接设置 | 连接分级诊断、切帐套草稿保护、扫码诊断 |
| 已盘记录 | 未实现列表 | 新只读页面并补 GET items API（P1） |

建议新增 ViewModels/CountListViewModel.cs、ViewModels/CountingViewModel.cs、ViewModels/SettingsViewModel.cs、Models/ScanUiState.cs、Services/IScannerService.cs、Services/IPendingSubmissionStore.cs、Controls/EnvironmentHeader.xaml、Controls/QuantitySubmitBar.xaml 和 Resources/Styles/Colors.xaml。

MVVM 可使用 CommunityToolkit.Mvvm；页面只订阅 ViewModel 状态，广播接收器不直接做网络调用，VisualStateManager 统一 Normal/Busy/Error/Disabled 等控件视觉状态。避免引入复杂的 WebView 组件栈。

## 6. 优先级和验收标准

**P0 首版必须做完**：盘点单列表/环境状态；库位优先/只读显示；扫描箱信息；重复箱弹窗；数量必填且未预填；固定提交按钮；双帐套的 canSubmit 及明确环境标识；未知结果保留 UUID 和恢复；新扫码不能静默覆盖数量；确认成功立即清箱保留库位，提示不阻塞扫码；真实海康扫码器 + 键盘/导航事件验收。

**P1 可后续交付**：本单已盘记录（需后端 GET items）、设备蜂鸣和振动设置、扫码诊断、提交历史摘要、横屏与超大字增强。

**必过场景**：
1. 无库位直接扫箱 → 指定提示且不填箱；有效库位切换会在验证后更新。
2. 扫有效未盘箱 → 显示6项只读详情，数量为空，二维码 QT=100 不会被视为已盘100。
3. 重复箱 → 指定「该箱号已扫描！」，点 OK 后继续扫其他箱，保留库位。
4. 人工输入99 → 后端返回 Committed 后立即清箱，显示400ms非阻塞提示；盘点单/库位仍保留；数据库两数量列都等于99。
5. 有未提交数量99再扫新箱 → 必须确认放弃，不得静默丢草稿。
6. 网络断开已发起提交 → 保留旧 UUID；APP 重启恢复并查结果，不产生双写。
7. Test/Production 切换 → 旧环境选单和库位不串到新环境，服务器独立写入开关生效。
8. 屏幕320dp宽 / 大字体 / 软件键盘弹出 → 数量和提交可触达；实体扫码广播无丢失或重复自动提交。
9. 已盘记录只读，后台未实现 GET items 时隐藏入口或显示未提供，不能误把单个 UUID 查询当已盘列表。

## 7. 参考与导航

- [[requirements|产品需求]] · [Markdown](../../requirements.md)
- [[docs/API_CONTRACT|API 契约]] · [Markdown](../API_CONTRACT.md)
- [[docs/WRITE_FLOW_IMPLEMENTATION|写入和幂等]] · [Markdown](../WRITE_FLOW_IMPLEMENTATION.md)
- [[docs/SCANNER_INTEGRATION|海康扫码]] · [Markdown](../SCANNER_INTEGRATION.md)
- [[docs/pda/PDA-UI-开发任务与验收|实施任务]] · [Markdown](PDA-UI-开发任务与验收.md)
- [当前 MAUI 盘点页](../../src/StockCount.Pad/CountingPage.xaml)
- [Core 扫码状态机](../../src/StockCount.Core/CountScanWorkflow.cs)

外部设计依据：
- Android Developer 无障碍触控区域：https://developer.android.com/guide/topics/ui/accessibility/apps
- .NET MAUI Shell Navigation：https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/shell/navigation?view=net-maui-10.0
- .NET MAUI CollectionView：https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/collectionview?view=net-maui-10.0

> 首先锁定 UX 交互和接受标准，再重构 XAML；不得因 UI 美化改变扫码权限、正式/测试环境隔离、两数量列同值和提交幂等语义。

### 2026-11-25 真机数量区裁切修复

现场照片显示数量输入区的原生 Android 下划线与外框重叠，数字与文字存在遮挡反馈。仅调整扫码页底部：Entry 不再限制固定高度，最小58dp、垂直居中，Android 保留字体内边距并设置8dp上下内边距；独立清空键与单位列，避免覆盖数字；提交按钮最小54dp、上下10dp内边距，支持字体缩放。清空按钮随输入可用性和非空状态启用，不改变业务校验。保持键盘 Numeric、小数、草稿保护、固定底栏及可配置非阻塞 Toast（默认2000ms）。需真机核验数字0–9、小数点、占位文字、PCS/其他单位、中文按钮、软件键盘弹出和字体缩放；源码修复并不等于真机验收通过。


## 2026-11-26 PDA 键盘与成功提示

- [[docs/pda/PDA 键盘避让与提交成功提示配置|键盘避让与提交成功提示配置]] · [Markdown](PDA%20%E9%94%AE%E7%9B%98%E9%81%BF%E8%AE%A9%E4%B8%8E%E6%8F%90%E4%BA%A4%E6%88%90%E5%8A%9F%E6%8F%90%E7%A4%BA%E9%85%8D%E7%BD%AE.md) — IME Resize、固定操作栏、默认2秒及可配置时长；源码待 CI/真机验收。
