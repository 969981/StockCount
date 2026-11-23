# StockCount · 全仓 AI 编程规范

> 所有自动编程代理（OpenAI Codex / Copilot / Claude Code / Gemini CLI 等）必须在读代码前阅读本文。更新时间：2026-11-23。

## 0. 文档阅读顺序

1. [requirements.md](requirements.md) — **最新业务要求**。
2. [architecture.md](architecture.md) — 分层、环境隔离与事务。
3. [docs/DECISIONS.md](docs/DECISIONS.md) — 已决 / 未决及上线阻断。
4. 数据库集成**必须同时阅读** [docs/DATABASE_MAPPING.md](docs/DATABASE_MAPPING.md) 和 [docs/database/SCHEMA_INDEX.md](docs/database/SCHEMA_INDEX.md)（**七表结构：五张业务表 + ATABZON / ATEXTE**）；接口读 [docs/API_CONTRACT.md](docs/API_CONTRACT.md)，扫码读 [docs/SCANNER_INTEGRATION.md](docs/SCANNER_INTEGRATION.md)。
5. 检查真实源码与测试，**不得将文档规划当作已实现代码**。

## 1. 产品硬性约束

- **不实现登录**；无登录不等于无后端访问保护。
- 仅能切换 `Production` (`sagex3.COB`) / `Test` (`sagex3.XTEST`)。
- 展示当前环境**所有现存盘点单**，不因状态隐藏；是否允许对该单提交是另一条规则。
- **V1 不创建、编辑、关闭或删除盘点单**，不接旧 `AddXCOUTH` / `CloseXCOUTH`。
- 先扫库位后扫箱；**实盘数量只人工输入一次**；后端必须让 `XCOUTD.QTYSTU_0 = XCOUTD.QTYPCU_0 = 人工实盘值`，不得从 QR `QT` 或标签 `QTY_0` 自动填数。
- **`XLABELDTL.STA_0` 不作盘点准入白名单或黑名单**。旧 `STA_0 IN (1,7,8)` 已失效，即便值4或9也不能仅凭此拒绝盘点。
- 不自动调整 ERP 库存，不修改 `XLABELDTL.QTY_0` 或 `STA_0`。
- 生产写入默认禁用，经过受控业务验收与配置授权才可以开放。

## 2. 技术和依赖规范

- .NET 10 / C#；.NET MAUI Android；ASP.NET Core；Dapper + SQL Server；xUnit。
- 所有项目启用 `Nullable` 与 `ImplicitUsings`，使用 `.slnx`。
- Pad → Contracts；Api → Core + Contracts + Infrastructure；Infrastructure → Core；Core 不依赖 UI/SQL/ASP.NET Core。
- HTTP DTO 不暴露原始数据库账号，业务数量采用 `decimal`，禁止 `double` 表示实盘数量。
- 不硬编码环境 IP、密钥、生产连接串，不把用户输入直接拼作 Schema/SQL。
- 不引入不需要的微服务、账号系统、消息队列和管理后台。

## 3. 每次 AI 任务的执行步骤

1. 声明需求 ID、涉及模块和安全风险；检查当前仓库代码及测试。
2. 对事实做只读验证；不得靠表名推断字段的真实业务含义。
3. 小步变更，优先补测试，更新 DTO 与相关 Markdown。
4. 尽可能执行 `dotnet build`、`dotnet test`，如缺少 Android workload 需写明未能执行而非假报通过。
5. 审核改动，不静默修改已有用户代码、不擅自删除迁移数据。
6. 提交时汇报变更、命令与结果、风险、仍需人工审批的前置条件。

## 4. 数据库与环境硬性门禁

- SQL 值全部参数化；Schema 仅在后端由可信 enum 映射到固定常量，不能是任意客户端字符串。
- 生产权限最小化、默认只读，写操作需独立服务端开关和审计。
- 未经单独批准禁止执行生产数据库 `INSERT/UPDATE/DELETE/ALTER` 或添加唯一索引。
- 旧 X3 APK 的有副作用 GET 接口绝不可当健康检查。
- **STOLOC 库位查找必须结合站点、仓库和库位复合定位**，不能仅凭 `LOC_0` 推定全局唯一；ITMMASTER 现有197条是 X3 字典、不是已核实197列物理表结构。
- **元数据查询必须区分字典与物理列**：`ATABZON` 与 `ATEXTE` 本身完整结构未返回前，不得臆造它们的全部 SQL 字段、类型、索引；`NOABREG_0` 是短标题编号，`NOLIB_0` 是本地菜单号，不能混淆。读 `docs/database/schema/ATABZON.md` 和 `ATEXTE.md`。
- 不静默截断 `PID_0`、`LOC_0`；**两数量列必须完全相等**，`PCUSTUCOE_0`/`PCU_0` 与 X3 写入适配方式、ROWID/AUUID 的生成规则必须先核实。不得用未确认的换算系数改写人工数。
- 同单箱码去重是推荐方案、尚待最终业务确认；历史有同单重复，不可自行删改。
- `RequestId` 用于服务端幂等，网络超时/未知结果必须保留原请求 ID 和输入。

## 5. 代码质量、UI 与测试

- 输入、状态、环境校验全部以服务器为准；UI 快速提示不能替代事务验证。
- Scanner 接口与 Android Intent/广播解耦，提供 Mock；不要用普通文本框输入假装已支持硬件扫码。
- 保存中禁止二次提交及扫码覆盖；失败保留单据、库位、箱码、数量；成功只清当前箱字段。
- 必测：不登录、帐套切换不串数据、列表不漏状态、STA=4/9 可解析、**人工99使 QTYSTU_0=QTYPCU_0=99（不取 QR100）**、同单冲突与幂等、网络未知结果。
- 不依赖单进程锁做多设备并发保证；持久化/事务级保护必须有效。
- 生产写入规则未知时应 fail closed，不要用猜测的 0/1 或默认系数补列。

## 6. 公开仓库保密规范

这是公开仓库。**不得提交真实 DB 凭据、API Key、环境 IP/域名、供应商/客户信息、生产箱码/库存明细、现场日志、私有 APK 或原始 SQL 导出**。样例仅使用合成数据和 `<API_HOST>` 等占位符。

## 7. 分工

- 前端任务：Pad / Contracts / Mock tests；不实现直连 SQL。
- 后端任务：Api / Core / Infrastructure / Contracts / tests；先 XTEST 只读，然后受控写。
- DB 任务：只读字典/字段核验、映射、迁移建议；默认不修改 Sage X3 生产库。
- 共享 DTO 发生变更时前后端契约、测试与文档必须同步更新。

配合 [OPENAI.md](OPENAI.md) 和 [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) 使用。