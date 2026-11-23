# StockCount

> 基于 .NET 10 的工业 Android PDA 库存盘点应用与 ASP.NET Core API，连接既有 Sage X3 / SQL Server 盘点数据。

**当前阶段：V1 统一需求与架构规范已经建立；业务代码、APK、生产写入接口尚未实现/验收。**

## 一期产品范围

- **无应用内用户登录**。启动后自动进入盘点单列表，设置页可切换 **Production（正式帐套 `sagex3.COB`）/ Test（测试帐套 `sagex3.XTEST`）**。
- 展示当前帐套中**全部已有盘点单**（采用分页加载，不按状态预先隐藏）；选择一张单据进入盘点。
- 先扫描实际库位，再扫描箱标签二维码；查询物料、批次、单位并**人工输入一次 STU 实盘数量**，由服务端校验并**同值保存到 `XCOUTD.QTYSTU_0` 与 `XCOUTD.QTYPCU_0`**。
- **不按 `XLABELDTL.STA_0` 拦截盘点**；状态仍在接口中返回、可显示和审计。
- 一期**不创建/关闭盘点单**，不修改 `XLABELDTL.QTY_0`，不执行库存调整/ERP 过账。
- 端到端写入须先在 XTEST 受控验证；正式写入默认保持关闭，完成验收后单独启用。

## 规范入口（AI / 人类开发者共同遵守）

| 文件 | 权威范围 |
| --- | --- |
| [requirements.md](requirements.md) | 最新产品需求、行为和验收 |
| [architecture.md](architecture.md) | 分层设计、状态与环境隔离 |
| [AGENTS.md](AGENTS.md) | 全仓 AI 编程硬性约束 |
| [OPENAI.md](OPENAI.md) | OpenAI / Codex 工作规范与执行模板 |
| [docs/TECH_STACK.md](docs/TECH_STACK.md) | 语言、依赖、项目结构和构建约定 |
| [docs/PROJECT_GUIDE.md](docs/PROJECT_GUIDE.md) | 项目说明、职责、开发交付流程 |
| [docs/DATABASE_MAPPING.md](docs/DATABASE_MAPPING.md) | 核心字段/DTO 映射与业务风险 |
| [docs/database/SCHEMA_INDEX.md](docs/database/SCHEMA_INDEX.md) | **七表字段与 SQL 总索引**（五张盘点业务表 + `ATABZON`/`ATEXTE` 字典表；未核实物理列如实标注） |
| [docs/API_CONTRACT.md](docs/API_CONTRACT.md) | V1 API 契约草案、错误和幂等 |
| [docs/SCANNER_INTEGRATION.md](docs/SCANNER_INTEGRATION.md) | Android 工业扫描接入规范 |
| [docs/SECURITY_AND_ENVIRONMENTS.md](docs/SECURITY_AND_ENVIRONMENTS.md) | 无登录时的网络安全和正式/测试隔离 |
| [docs/TEST_PLAN.md](docs/TEST_PLAN.md) | 测试矩阵与上线门禁 |
| [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | 前后端 AI 分阶段任务、完成定义 |
| [docs/DECISIONS.md](docs/DECISIONS.md) | 已定规则与仍待决策事项 |

### 规范优先级

用户最近明确确认的要求 > `requirements.md` / `docs/DECISIONS.md` > `architecture.md` / `docs/DATABASE_MAPPING.md` > 其他专题文档。**已确认事实、设计目标、待验证假设须分开**；不得将旧 APK 的行为当成新 StockCount 的强制实现。

## 建议解决方案结构（待创建源码）

```text
StockCount.slnx
src/
  StockCount.Pad/             # .NET MAUI Android
  StockCount.Api/             # ASP.NET Core Web API
  StockCount.Contracts/       # HTTP DTO
  StockCount.Core/            # 盘点业务规则与接口
  StockCount.Infrastructure/  # SQL Server/Dapper 与 Sage X3 适配
tests/
  StockCount.Core.Tests/
  StockCount.Api.Tests/
  StockCount.Pad.Tests/
docs/
```

## 数据库七表文档和只读 SQL

- [七表字段结构和中文标题总索引](docs/database/SCHEMA_INDEX.md)：已确认四张业务表 16 + 19 + 45 + 41 列物理元数据，另有 ITMMASTER 197 条 X3 字典标题；`ATABZON` / `ATEXTE` 的完整物理列需用新脚本查询。
- [五张业务表物理列及中文标题](sql/read_only/inspect_five_tables.sql)；[两张字典表完整结构](sql/read_only/inspect_dictionary_tables.sql)；[ATABZON + ATEXTE 中英文标题关联](sql/read_only/query_dictionary_tables.sql)；[五表中文别名抽样](sql/read_only/select_five_tables_chinese_aliases.sql)；[业务关联 SQL](sql/read_only/business_queries.sql)。

## 重要约束

本仓库为公开仓库。**严禁提交真实数据库密码、连接字符串、API 凭据、内网 IP/域名、真实箱码明细、敏感现场日志或客户原始业务数据**。示例必须使用占位符/合成数据。仅客户端切换环境**不等于后端写入授权**。开发和联调优先 XTEST，生产默认只读。

> 具体状态解释、PCU/STU 单位系数兼容性、库位码协议、同单防重和 X3 必填列写入策略尚有未决点；详见 [决策表](docs/DECISIONS.md)。