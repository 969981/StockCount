# StockCount — 系统架构

- **架构版本**：V1；2026-11-23
- **技术方向**：.NET 10 / C#、.NET MAUI Android、ASP.NET Core Web API、Dapper + SQL Server（Sage X3）
- **约束**：不在 PDA 内暴露数据库配置；业务逻辑由后端集中掌控

## 1. 系统边界

```text
┌────────────────────────────────────────────┐
│ StockCount.Pad · Android industrial PDA     │
│ MAUI Views / ViewModels / Scanner adapter  │
│ Environment selection / manual count input │
└───────────────────┬────────────────────────┘
                    │ HTTPS + JSON
                    │ environment=Production|Test (enum)
┌───────────────────▼────────────────────────┐
│ StockCount.Api · ASP.NET Core .NET 10      │
│ endpoint → validation → application service│
│ request identity / transaction / audit     │
└────────────┬─────────────────────┬─────────┘
             │                     │
       StockCount.Core       StockCount.Contracts
       rules & ports         request/response DTOs
             │
┌────────────▼───────────────────────────────┐
│ StockCount.Infrastructure · Dapper         │
│ env allowlist / X3 adapters / repositories │
│ SQL parameterization / transaction boundary│
└───────────────────┬────────────────────────┘
                    │ SQL Server (server only)
          ┌─────────┴──────────┐
          ▼                    ▼
       sagex3.COB         sagex3.XTEST
       正式 Schema         测试 Schema
```

**正式/测试同为数据库 `sagex3` 的两个 Schema**，不等于已经证明存在两台数据库服务器。物理连接可共享但权限隔离必须在服务端落实；部署时可分别设置不同主体与最小授权（优选）。

## 2. 解决方案与依赖方向

```text
StockCount.slnx
├── src/StockCount.Pad/                # MAUI Android / MVVM / 设备接入
├── src/StockCount.Api/                # ASP.NET Core API / DI / middleware
├── src/StockCount.Contracts/          # shared DTO / codes / API version
├── src/StockCount.Core/               # domain models, rules, ports
├── src/StockCount.Infrastructure/     # repositories / Dapper / X3 specifics
├── tests/StockCount.Core.Tests/
├── tests/StockCount.Api.Tests/
└── tests/StockCount.Pad.Tests/
```

依赖原则：Pad → Contracts；Api → Contracts + Core + Infrastructure；Infrastructure → Core；Core **不依赖** Pad、Api、SQL Server、MAUI；Contracts 不泄漏 SQL Server 对象或连接凭据。DTO 字段用业务语义命名，仓储映射集中到 Infrastructure。

## 3. 前端架构

- 仅提供 Android target；MAUI/XAML、CommunityToolkit.Mvvm 的 ObservableObject/RelayCommand。
- `IEnvironmentSelectionService`：持久化受控 `Production`/`Test` 枚举和本地设置；**切换环境应销毁当前盘点会话**，未确认提交需先确认状态，避免串库。
- `IScannerService`：抽象扫码事件，Android BroadcastReceiver 为实现；Mock 实现用于集成测试；不在 ViewModel 内操作 Intent。
- `IInventoryCountApiClient`：统一 HttpClientFactory / System.Text.Json、超时与网络错误映射，不把原始数据库错误显示给用户。
- `InventoryCountListViewModel`：列表分页、搜索、选择；**所有状态可见**。
- `InventoryCountScanViewModel`：单据上下文、当前库位、已解析标签、人工数量、请求 ID、错误与加载态。
- `LocalPendingSubmissionStore`：保存不确定结果的键与必要脱敏草稿（建议 SQLite）；不存凭据。
- 每个 API 请求显式带环境；不可只通过客户端隐藏的全局状态指示环境。

### 3.1 扫码状态机

```text
NoSheet → SheetChosen/NoLocation → LocationReady
 → ResolvingLabel → EditingCount → Saving
 → [Success → LocationReady] / [UnknownOrFailed → EditingCount]
```

- 无库位扫描箱码：禁止处理并提示。
- 保存中屏蔽新扫码、切库位和重复点击；不排队执行未知设备事件。
- 切库位/切单：如有草稿先确认处理；未知请求在确认前不得直接丢弃。
- 成功仅清除箱相关信息，保留 Sheet + Location；失败保留全部输入。
- 即使取消标签状态过滤，也应显示 STA_0 的原始码值而不以其决定能否点“提交”。

## 4. 业务边界（服务端权威）

`InventoryCountService` 负责：读取盘点单、判定是否可写、库位确认、标签解析、同单去重、数量校验、事务提交；后端必须复核前端的所有业务字段。

必备端口（示例，不要求名称逐字一致）：

```text
IInventoryCountRepository
ILabelRepository
IItemRepository
ILocationRepository
IInventoryCountWriter     // 已验证的写入适配器
IIdempotencyStore         // 原子唯一 RequestId / 结果
IAuditTrail
IEnvironmentResolver
```

库位必须来自扫码事件与服务端验证，不以当前 `XLABELDTL.LOC_0` 代替。实盘量来自人工 `countedQtyStu`，不得直接取 `QRCOD_0` 的 `QT` 或当前 `QTY_0`。

## 5. 数据隔离与危险写入护栏

- 由服务器**固定字典**解析 `Production → COB`、`Test → XTEST`；禁止接受 `schema`/`database`/`connectionString` 之类自由参数。
- Dapper 参数用于所有值；Schema 是通过可信枚举选择的**硬编码白名单常量**，不可把 `environment` 字符串直接拼 SQL。
- 生产默认只读：`EnableProductionWrites=false`；打开需运维授权、受控验收、审计留痕。生产数据库主体与测试主体尽量分开并最小授权。
- 发生网络重试、环境切换或进程恢复时，幂等键必须绑定 `(Environment, CountNumber, RequestId)`；若已返回成功则返回相同保存结果。
- 不通过旧系统的“GET 关单”或任何未知副作用接口测试生产写入。
- `COBTEST2` 存在，但未确认业务用途，**不出现在 V1 环境选项**。

## 6. 存储与写入事务（设计目标，未完成实测）

```text
POST CountItem
  -> validate environment/read-write permission
  -> validate sheet exists / permitted CUNSTA
  -> validate location and scanned label (STA_0: display only)
  -> validate decimal counted quantity / safe lengths
  -> check request idempotency and duplicate policy
  -> resolve PCU ↔ STU, populate mandatory X3 fields
  -> persist via approved X3 service/transaction boundary
  -> commit idempotency record + audit + detail in same logical transaction
  -> return detail identifier and immutable result
```

**关键未验证**：Sage X3 `ROWID/AUUID_0/UPDTICK_0` 自动赋值规则、是否有触发器/行号分配器、`PCU_0/QTYPCU_0/PCUSTUCOE_0` 的合法值、是否必须经 X3 官方服务写。即使表结构已确认，**不得直接假定使用原始 INSERT 就能安全写库**。先找既有写入通道，在测试库确认完整行为，再在代码中封装 `IInventoryCountWriter`。

### 6.1 幂等与并发

- 网络层生成客户端 `RequestId`（UUID），同一业务提交重试**复用原 ID**。
- 服务端维护原子幂等记录，键包括环境和盘点单；相同键与相同 payload 返回同一结果，payload 不同则冲突（409）。
- 同一盘点单的同箱去重目前是**建议规则、尚待业务批准**。数据库历史正式数据存在同单重复，不能直接给旧表添加唯一索引。并发保护建议在受控辅助表或可靠 X3 写入流程实现。
- Dapper 的事务、幂等记录与 X3 写入若不在同一 SQL 事务，必须设计补偿/查询机制，不能宣称天然原子。

## 7. API、错误与契约

在 [docs/API_CONTRACT.md](docs/API_CONTRACT.md) 中定义 V1 endpoint / 字段 / HTTP status / 幂等响应。统一 Problem Details（RFC 9457）风格及稳定业务 error code；对客户端返回可读中文消息，日志记录 correlation id，**不泄露 SQL/连接串/真实敏感字段**。

## 8. 容错、可观测与交付

- 健康检查与测试数据可用性、结构自检；服务启动时校验必需 Schema/列，失败应拒绝启用写入。
- 结构化日志：`environment`、`countNumber`（可局部遮蔽）、`requestId`、`correlationId`、耗时、错误码；正式环境避免输出原始二维码和明细全量。
- 并发、断网、跨环境、失败重试为 P0；不能用延时模拟真实确认。
- 测试顺序：Core unit → Api mock integration → XTEST read-only → XTEST isolated write → production read-only → approved production write。

## 9. 证据与假设

**已知**：COB 正式、XTEST 测试；两套有 XCOUTH/XCOUTD；表头 `CUNNUM_0` / 明细 `BOXNUM_0` / 标签 `PID_0` 及关键物理字段已实测。历史 `XTEST.XCOUTD` 11 单共 23,197 条，与“都通过人工扫码”不等同。

**待定**：盘点单其他状态可写性、真实库位码完整协议、单位换算和所有必填列、同单去重/复盘、认证替代的设备访问控制细节、后端采用 X3 业务服务还是数据库受控写入。

所有 AI 修改必须参照 [AGENTS.md](AGENTS.md) 和 [docs/DECISIONS.md](docs/DECISIONS.md)。