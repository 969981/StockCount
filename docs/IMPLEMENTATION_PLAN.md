# StockCount 前后端 AI 编程执行计划

> V1 项目拆解，供独立前端/后端 AI Agent 分批实施。**此页列出的任务尚未自动执行。** 2026-11-23。

## 1. 研发原则

- **Contracts First**：先固化 DTO 和错误码，再让两端并行开发。
- **Read-only First**：优先真实查询、Mock 提交；正式环境的任何写操作均需额外门禁。
- **小 PR、可验证**：一个任务覆盖具体需求编号和实际测试；不能一句“全端已完成”却没编译。
- 真实数据库含私有业务资料，公开仓库不得保存真实输出；测试使用合成/脱敏样本。

## 2. 交付顺序与依赖

<details>
<summary>阶段 0 — 工程骨架与自动化</summary>

### T0-01 创建 .NET 10 solution / 项目骨架
- 所有项目启用 Nullable / ImplicitUsings；MAUI 项目仅 Android。
- 添加 Core、Contracts、Api、Infrastructure、Pad 及 xUnit 测试项目。
- 配置可复现 build / test，源代码格式化与 README。
- 完成定义：`dotnet restore`、`dotnet build`、`dotnet test` 真实执行成功；缺 Android workload 必须标明范围。

### T0-02 CI 与密钥安全
- CI 对 Core/Api/Contracts 默认构建测试；Android 单独 runner/workload。
- 加 `.gitignore` / Secret 扫描，忽略本地连接串、设备 APK 签名文件。
- 不在公开 PR 暴露 ERP 凭据。

</details>

## 3. 阶段 1 — 共享契约与只读后端

### T1-01 Contracts

- `EnvironmentKind: Production|Test`、`InventoryCountHeader`、`LabelDetails`、`ResolveLocation`、`CountItemRequest`、`SaveCountItemResult`、`ApiProblem`。
- 统一 JSON 命名及 decimal、日期和字符串 countType；添加 JSON 合同测试。
- **依赖**：[API_CONTRACT.md](API_CONTRACT.md)、[DATABASE_MAPPING.md](DATABASE_MAPPING.md)。

### T1-02 环境路由

- 服务端受信任静态映射 Production→COB、Test→XTEST；拒绝任意 Schema。
- 环境分离日志/缓存/幂等命名空间，`EnableProductionWrites=false`。
- 测试两套环境读取不同数据且绝不跨访问。

### T1-03 单据列表、详情 API

- `GET inventory-counts` 全状态、分页/排序/搜索；`GET {countNumber}` 显示真实状态、类型（string）、canSubmit。
- Core 中把“可查看”与“可提交”分别处理。
- 单元测试至少包含状态0、1；无隐式 `WHERE CUNSTA=0`。

### T1-04 标签与库位只读 API

- 查询 `XLABELDTL` + `ITMMASTER`，返回原始 `STA_0`，**不按状态阻断**。
- 完整二维码优先，可信解析 `ID` 兜底需复核，禁止用 QR QT 自动填实盘。
- 库位解析/存在/仓库校验先依据经核验字典；未知协议必须报可读错误。

## 4. 阶段 2 — MAUI PDA 前端

### T2-01 无登录壳 + 环境选择
- 启动显示全部盘点单；设置页只可切 Production/Test。
- 首次选择建议 Test；切帐套清上下文/阻止未知提交误转。
- 正式环境持续文字标识，服务器禁写时给出明显提示。

### T2-02 盘点单选择与列表
- 适配工业 PDA 小屏、单手、实体按键；搜索、刷新、分页、错误占位、加载状态。
- 完整显示单号、名称、日期、站点、仓库、状态/类型。

### T2-03 工业扫码适配
- `IScannerService` + BroadcastReceiver + MockScannerService。
- Action/Extra 配置化、真机 adb/logcat 验证；可处理 CR/LF 和二维码内部 `@@` 不破坏。
- 当前库位为空扫箱提示，不写数量输入框；取消扫码订阅时无内存泄漏。

### T2-04 箱信息、手工数量与提交体验
- 物料只读；标签当前数量与二维码原数量标注“参考”，**只显示一个人工实盘输入框**，初始留空。
- 手工输入 decimal，保存中锁定表单；失败保留所有信息，成功清箱保留库位单号。
- 不出现创建单、关闭单或账号登录入口。

## 5. 阶段 3 — 后端写入（只在批准后进入）

### T3-01 研究并验证真实 X3 写入规则
- 明确 `AddXCOUTD` 既有服务或批准的 ERP 通道，与裸 SQL 的差别。
- XTEST 独立核实 `ROWID/AUUID/UPDTICK/PCU/QTYPCU/PCUSTUCOE/CREUSR` 所有非空列及必要触发器。
- 查询两套 Schema 类型差异；准备不涉及正式库的数据回滚办法。
- **闸门：没有验证之前只提供 MockWriter，不得合并生产 INSERT。**

### T3-02 核准业务决策
- 盘点单其它状态提交准入、同单箱码去重/复盘、数量 0/小数/负数、库位规则签字。
- 在 Core 中实现单一规则真源，并通过 API 返回稳定 error code。

### T3-03 真正的事务与幂等
- 持久化 RequestId（环境 + 单号 + 客户请求ID），复用原响应，禁止未知结果双写。
- 原子防重或 X3 认可的业务序列化机制；两台设备并发测试。
- **提交唯一人工 STU 数值 → `XCOUTD.QTYSTU_0` 和 `XCOUTD.QTYPCU_0` 两列同值**；核实 `PCU_0` / `PCUSTUCOE_0` 是否允许这一组合，发生冲突就阻断而非静默换算。
- XTEST 受控回查成功/失败及审计；生产仍只读。

## 6. 阶段 4 — 端到端与发布

### T4-01 真实硬件 E2E
- 验证目标海康 PDA 的广播协议、Android 生命周期、扫码键、扫码与输入焦点、弱网恢复。
- 跑完 [TEST_PLAN.md](TEST_PLAN.md) T01–T30。

### T4-02 生产发布与审批
- 业务规则签字、生产只读核对、密钥/网络策略审核、回滚与告警准备。
- 仅运维授权打开 `EnableProductionWrites`，先低风险试运行，再扩大。
- 记录 APK 版本、API 版本、数据库 Schema 变更依赖；不因文档完成就宣称已上线。

## 7. 并行开发与交接

| 角色 | 可并行任务 | 必须等待 |
| --- | --- | --- |
| Frontend Agent | T2-01 到 T2-04（基于 Mock / Contracts） | T1-01 DTO 定稿 |
| Backend Agent | T1-02 到 T1-04，只读 API | T1-01 DTO 定稿 |
| Database/Integration Agent | 只读 Schema/索引/服务证据，T3-01 调查 | 任何实际写入需审批 |
| QA Agent | 测试矩阵、Mock、XTEST 受控回归 | 真机/库访问条件 |

## 8. AI 任务交付模板

每项任务 PR 必须标注：`T?-??` + `P-??`；修改文件、受影响 DTO/SQL、编译测试实际执行结果、环境/权限影响、仍在阻断的规则与下一任务。并行修改同一 Contracts 文件时先合并契约变更，避免客户端/后端定义不一致。

关联：[AGENTS](../AGENTS.md) · [OPENAI](../OPENAI.md) · [需求](../requirements.md) · [API](API_CONTRACT.md)。