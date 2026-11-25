---
title: "Sage X3 / SQL Server 数据库字段映射规范"
aliases:
  - "StockCount · docs / DATABASE_MAPPING"
created: 2026-11-23
updated: 2026-11-23
status: reference
area: database
tags:
  - StockCount
  - database
  - docs
up: "[[docs/database/00-数据库知识地图]]"
---

# Sage X3 / SQL Server 数据库字段映射规范

- 版本：V1（2026-11-23）
- **七表结构 / 中文标题 / 可执行只读 SQL**：参见 [数据库七表字段索引](database/SCHEMA_INDEX.md)（业务五表 + `ATABZON` / `ATEXTE`）。目前 XCOUTH16 / XCOUTD19 / XLABELDTL45 / STOLOC41 列实际物理证据齐全；ITMMASTER 已有 197 条中文**字典代码**；另据 2026-06-30 历史 `COB.ITMMASTER` DDL 为 **258 个物理字段**，但今日现网结构仍待重新查询。
- 本文来源：此前经只读 SQL 核实的 `sagex3.COB` 物理列/字典与 `XTEST` 的单据/明细统计；**不是 DDL**，不自动授权任何写库操作。
- **重要**：生产 `sagex3.COB`、测试 `sagex3.XTEST` 均为 `sagex3` 数据库下的 Schema；`COBTEST2` 曾发现，但不纳入 V1 环境切换。对 XTEST 的具体字段类型需独立核实，不能照抄 COB。

## 1. 表关系与取数

```text
XCOUTH (盘点单头; CUNNUM_0 唯一)
  1 ── N
XCOUTD (盘点明细; CUNNUM_0; BOXNUM_0)
         │ 以 BOXNUM_0 = XLABELDTL.PID_0 查询标签
         ▼
XLABELDTL (箱标签; PID_0 唯一, QRCOD_0 完整二维码)
         │ 以 ITMREF_0 查询物料主数据
         ▼
ITMMASTER (产品主数据; 品名/规格)
```

**逻辑 JOIN 已由样例支持，但不等于数据库已创建外键**。当前标签的 `LOC_0/QTY_0` 可能在盘点后变化，不可把今天的标签值当作历史盘点时的真值。

## 2. XCOUTH — 盘点单表头（COB 物理结构已核验）

| 原字段 | SQL Server 类型（COB） | 中文/业务语义 | DTO 建议 |
| --- | --- | --- | --- |
| `CUNNUM_0` | nvarchar(30) | 盘点单号；有唯一索引 | `countNumber: string` |
| `CUNDES_0` | nvarchar(30) | 标题/盘点单名称 | `description` |
| `STOFCY_0` | nvarchar(5) | 地点/库存站点 | `siteCode` |
| `WRH_0` | nvarchar(5) | 仓库 | `warehouseCode` |
| `LOC_0` | nvarchar(20) | 表头库位；不代表现场每箱实盘库位 | `headerLocationCode` |
| `CUNDAT_0` | datetime | 盘点日期 | `countDate` |
| `CUNSTA_0` | tinyint | 单据状态；0可盘点、中文名称未确定 | `statusCode` |
| `CUNTYP_0` | nvarchar(10) | 盘点类型；**按字符串传输** | `countType` |
| `PDAID_0` | nvarchar(50) | PDA 标识；业务含义仍待核 | 不必对用户展示 |
| `CREDATTIM_0` | datetime | 创建日期时间 | `createdAt` |
| `UPDDATTIM_0` | datetime | 更新日期时间 | `updatedAt` |
| `CREUSR_0` / `UPDUSR_0` | nvarchar(5) | X3 用户代码 | `createdBy` / `updatedBy`（如合规） |
| `UPDTICK_0` | int | X3 系统列 | 服务端内部 |
| `AUUID_0` | binary(16) | 唯一标识系统列 | 服务端内部 |
| `ROWID` | numeric(38,0) | 物理行标识 | 服务端内部 |

**不允许**在列表 SQL 中硬编码 `CUNSTA_0=0` 来过滤：V1 要显示所有单据。表头 `CUNSTA_0=0` 可提交的业务许可曾明确，其余码值可写规则仍待核。X3 菜单2722在 COB 字典中 1=创建中、2=待盘点、3=已结束；不能把 XTEST 客户端的“正常”直接当作码值文本。

### Schema 差异实证

对 COB/COBTEST2/XTEST 的 `XCOUTH` 做 `UNION ALL` 时，`CUNTYP_0='230628001'` 被隐式转换到 `INT1/tinyint` 发生溢出。因此**跨 Schema 汇总时 `CAST(CUNTYP_0 AS NVARCHAR(30))`**，每套源表独立验证列类型；DTO `countType` 一律字符串，不能用 `byte`。

## 3. XCOUTD — 盘点单明细（COB 物理结构已核验）

| 原字段 | SQL 类型（COB） | 含义 / 保存规则 | DTO |
| --- | --- | --- | --- |
| `ROWID` | numeric(38,0) | 行标识/主键唯一；生成机制待证 | `detailId`（服务端映射） |
| `CUNNUM_0` | nvarchar(30) | 所属盘点单号 | `countNumber` |
| `BOXNUM_0` | nvarchar(20) | 箱码；来自标签 `PID_0` | `boxNumber` |
| `PALNUM_0` | nvarchar(20) | 托盘号 | `palletNumber` |
| `ITMREF_0` | nvarchar(20) | 产品编码 | `itemCode` |
| `LOT_0` | nvarchar(30) | 批次 | `lotNumber` |
| `LOC_0` | nvarchar(10) | **现场扫码实际库位**（V1目标） | `countedLocation` |
| `PCU_0` | nvarchar(3) | 盘点计量单位 | `XLABELDTL.STU_0` |
| **`QTYPCU_0`** | **numeric(28,13)** | **同一人工实盘数；必须与 `QTYSTU_0` 完全相等** | `quantityPcu = countedQtyStu`（后端派生） |
| `PCUSTUCOE_0` | numeric(18,7) | 盘点转换系数 | 固定 `1` |
| **`QTYSTU_0`** | **numeric(28,13)** | **人工实盘数的目标列，且等于 `QTYPCU_0`** | **`countedQtyStu: decimal`** |
| `ECCVALMAJ_0` | nvarchar(20) | X3 业务辅助字段；语义待核 | 服务端内部 |
| `PDAID_0` | nvarchar(50) | 设备/PDA 关联标识；待核 | 审计上下文（方案待定） |
| `CREDATTIM_0` / `UPDDATTIM_0` | datetime | 明细创建/更新时间 | `createdAt` / `updatedAt` |
| `CREUSR_0` / `UPDUSR_0` | nvarchar(5) | 用户字段，V1 无登录需定义受控服务账号/审计 | 服务端内部 |
| `UPDTICK_0` | int | X3 系统列 | 服务端内部 |
| `AUUID_0` | binary(16) | 系统唯一标识 | 服务端内部 |

**已验证索引现状**：COB 表头 `CUNNUM_0` 唯一、标签 `PID_0` 唯一；`XCOUTD` 仅 `ROWID` 作为唯一主键，**未发现 (`CUNNUM_0`, `BOXNUM_0`) 唯一约束**。COB 曾查到两组同单重复箱号，不能改历史数据来“修好”或直接添加复合索引。

**最新 V1 业务要求（2026-11-23）**：只让操作员输入一次数量，后端将**相同 `decimal` 值**写进 `QTYSTU_0` 与 `QTYPCU_0`，两者必须相等。历史 COB `XCOUTD` 的 22,936 行也全部满足 `QTYPCU_0=QTYSTU_0`、系数=1；**2026-11-25 用户已确认盘点明细系数固定为1、PCU取标签STU**；现场实际写入仍待验收。

## 4. XLABELDTL — 箱标签表（COB 关键字段）

| 原字段 | 类型（COB） | 用途 / 注意 |
| --- | --- | --- |
| `PID_0` | nvarchar(50) | 标签唯一ID；扫码箱号来源 |
| `QRCOD_0` | nvarchar(200) | **完整原始二维码**；可用于精确查找 |
| `ITMREF_0` | nvarchar(20) | 产品编码；联 `ITMMASTER.ITMREF_0` |
| `LOT_0` | nvarchar(20) | 批号 |
| `STU_0` | nvarchar(3) | 存货单位；人工输入数量的计量单位 |
| `PCU_0` | nvarchar(3) | 包装单位 |
| `PCUSTUCOE_0` | numeric(18,7) | 原标签包装与存货单位系数；盘点不使用此值 |
| `QTY_0` | numeric(28,13) | **读取时的标签数量**，不是实际盘点数量 |
| `LOC_0` | nvarchar(20) | 标签当前库位；不是现场扫描库位 |
| `STA_0` | tinyint | 标签状态码；**V1 不按 STA_0 限制盘点** |
| `PALNUM_0` | nvarchar(30) | 标签识别码1/托码相关线索；不盲认与明细 PALNUM 相同 |
| `VCRNUM_0` | nvarchar(20) | 来源凭证号 |
| `VCRLIN_0` | int | 来源行号 |
| `VCRNUMORI_0` | nvarchar(20) | 原始凭证号 |
| `VCRLINORI_0` | int | 原始凭证行号 |
| `CREDATTIM_0` / `UPDDATTIM_0` | datetime | 创建/更新时点，非全部移动日志 |

标签状态菜单 6012 的可读文本：4=失效、9=已发货；1/7/8 的中文码值并未从菜单核得。**标签状态规则已经被用户更改为不限制**，不能沿用过去 `IN (1,7,8)`。

### 原二维码解析（格式示例为合成内容）

```text
ID.BOX-EXAMPLE-01@@PT.DOC00001@@LN.1000@@MT.ITEM001@@LT.LOT01@@QT.100@@PG.125
```

| 段 | 已验证/可推断关系 | 证据限制 |
| --- | --- | --- |
| `ID` | `XLABELDTL.PID_0` | 标签箱码 |
| `PT` | `XLABELDTL.VCRNUM_0` | 来源凭证号（含PT的样本） |
| `WO` | 可能为工单字段 | 对不同来源的凭证语义需独立核 |
| `LN` | `XLABELDTL.VCRLIN_0` | 来源行号 |
| `MT` | `XLABELDTL.ITMREF_0` | 产品编码 |
| `LT` | `XLABELDTL.LOT_0` | 批次 |
| `QT` | 生成二维码时的数量 | **不等于实盘量**，也可能不等于后来的标签当前数量 |
| `PG` | 含义未知 | 有过 PG≠QT 样例，不能解释为 QT |
| `LC.` | PDA 库位扫码前缀（现场需求已确认） | 提取前缀后的库位值，按 STOLOC.LOC_0 查询；展示与 XCOUTD.LOC_0 写入均不含 LC 前缀 |

扫码操作逻辑应先以完整 `QRCOD_0` 查找，或在可靠解析并核验后以 `PID_0` 查找；不能只信客户端拆出的字段、也不能自动用 QT 作为数量。

## 5. ITMMASTER — 物料主数据

| 字段 | 业务作用 | 类型证据 |
| --- | --- | --- |
| `ITMREF_0` | 以 `XLABELDTL.ITMREF_0` 关联 | 确认字段，物理长度应在目标 Schema 进一步探测 |
| `YITMDES4_0` | 品名（企业自定义列） | X3 字典中文标题已核 |
| `ITMDES2_0` | 描述2；PDA 业务显示为“规格” | 字典标题已核 |

JOIN 可能依赖 ERP 物料组织/多语言约束；不可未经验证作“必有且只一行”的假设。

## 6. 库位主数据 STOLOC 的精确关联

- **完整 41 列与 X3 中文标题**：[STOLOC 字段字典](database/schema/STOLOC.md)；库位主键/唯一性不能仅凭列名推断。
- 2026-11-23 的 XTEST 主数据分析显示，`STOFCY_0 + WRH_0 + LOC_0` 组合不重复，但 `LOC_0` 单独跨仓库复用；该历史证据不等于必须强制仓库一致。2026-11-25 现场业务确认：XTEST / COB 分别配置站点、仓库一致性校验（四项默认关闭）；始终校验对应帐套的 `STOLOC.LOC_0` 是否存在。仅开启站点时使用 `STOFCY_0 + LOC_0`；仅开启仓库时使用 `WRH_0 + LOC_0`；两项开启时保留联合定位及重复记录拒绝。任一关闭时按满足条件的记录是否存在判定，不要求唯一，因为 XCOUTD 仅保存库位编码。扫码和提交事务内复核使用同一规则。
- `STOLOC.LOKSTA_0`、`CUNLOKFLG_0` 等为原始代码，不能因中文标签直接将数值 1 当作“冻结/正在盘点”为真。
- [ITMMASTER 197 条 X3 中文字典](database/schema/ITMMASTER.md) 不是物理表全字段；历史 DDL 为 258 个物理字段（不等于现网）；[查询两帐套真实物理列 SQL](../sql/read_only/inspect_five_tables.sql)。

## 7. 从页面到实际表的端到端映射

| UI / API 语义 | 查询来源 | 保存目标 |
| --- | --- | --- |
| 环境 | Pad 受控枚举 | 后端选 COB 或 XTEST（**不写进 X3 字段**） |
| 单号/名称/站点/仓库 | `XCOUTH.CUNNUM_0/CUNDES_0/STOFCY_0/WRH_0` | 明细只保存 CUNNUM_0 关联 |
| 当前实盘库位 | 现场扫描并后端核库位 | **`XCOUTD.LOC_0`** |
| 原始箱 QR | 原始扫描字符串 / 标签 QRCOD_0 | X3 明细**无同名 QRCOD_0 字段**；如要审计用独立受控记录 |
| 箱码 | `XLABELDTL.PID_0` | `XCOUTD.BOXNUM_0` |
| 产品、批次 | `XLABELDTL.ITMREF_0/LOT_0` | `XCOUTD.ITMREF_0/LOT_0` |
| 品名、规格 | `ITMMASTER.YITMDES4_0/ITMDES2_0` | 仅显示，不必重复入库 |
| 存货单位 STU | `XLABELDTL.STU_0` | UI 显示 STU，写入 XCOUTD.PCU_0；原标签 PCU/系数不参与校验，**不得据此改变实盘数** |
| 标签数量 | `XLABELDTL.QTY_0`、二维码 QT | **只读参考，不写实盘** |
| 手填实盘数量 | Pad **仅一次**输入 `decimal` | **`XCOUTD.QTYSTU_0` 与 `XCOUTD.QTYPCU_0`，同时写入相同值** |
| 原始标签状态 | `XLABELDTL.STA_0` | 供展示/诊断，不做状态准入 |

## 8. 安全防线、证据边界与先决条件

- **长度风险**：标签 PID(50) → 明细 BOXNUM(20)，标签/表头 LOC(20) → 明细 LOC(10)；超过必须提示并拒绝，不得截断。
- **Schema 差异**：COB 与 XTEST 的 `CUNTYP_0` 有过转换冲突；部署前对每个环境扫描 `INFORMATION_SCHEMA.COLUMNS`、索引、触发器、默认值，写入路径不可假定一致。
- **重复箱码**：原生产库有历史同单重复；服务端防重范围和复盘规则待最终批准。
- **历史事实**：测试库 XTEST 11 张历史单据，23,197 条明细（10 张有数据、1 张空单），每单不同箱码数=条数；不能证明全部由人工扫码生成。
- **动态库存**：历史标签当前值变化不能作为历史异常的直接证据；QR QT 是编码原数。
- **写入阻断**：`ROWID/AUUID_0/UPDTICK_0`、审计字段、**两数量列同值与 `PCUSTUCOE_0`/`PCU_0` 约束**及 X3 核准服务实现全部确认前，禁止自动直接 INSERT 正式 COB。

## 9. 只读核验 SQL（部署前执行，结果不提交到仓库）

```sql
-- 逐帐套核对表头关键字段（只读）
SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME,
       DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
FROM [sagex3].INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA IN (N'COB', N'XTEST')
  AND TABLE_NAME IN (N'XCOUTH', N'XCOUTD', N'XLABELDTL', N'STOLOC', N'ITMMASTER')
ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;

-- 跨帐套仅统计表头，不用未显式 CAST 的 UNION ALL 类型推断
SELECT N'Production' AS Environment, COUNT_BIG(*) AS CountHeaders FROM [sagex3].[COB].[XCOUTH]
UNION ALL
SELECT N'Test', COUNT_BIG(*) FROM [sagex3].[XTEST].[XCOUTH];
```

参见 [requirements.md](../requirements.md)、[architecture.md](../architecture.md)、[docs/DECISIONS.md](DECISIONS.md)。

**实际可执行的五张业务表 SQL**：[完整物理结构 + 中文普通/长标题（COB/XTEST）](../sql/read_only/inspect_five_tables.sql) · [五表真实全字段中文别名抽样](../sql/read_only/select_five_tables_chinese_aliases.sql) · [六组常用业务查询](../sql/read_only/business_queries.sql)。

**两张 X3 字典表**： [ATABZON 字段映射](database/schema/ATABZON.md) · [ATEXTE 多语言标题](database/schema/ATEXTE.md) · [真实物理结构/索引查询](../sql/read_only/inspect_dictionary_tables.sql) · [中英文标题关联查询](../sql/read_only/query_dictionary_tables.sql)。

---

## 🧭 知识库导航

- 主题索引：[[docs/database/00-数据库知识地图|数据库知识地图]] · [Markdown](database/00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 数据流：[[docs/DATABASE_MAPPING|字段映射]] · [Markdown](DATABASE_MAPPING.md) · [[docs/API_CONTRACT|API 契约]] · [Markdown](API_CONTRACT.md)

最新单位映射决议及证据见 [2026-11-25 盘点单位映射研究](database/COUNT_UNIT_MAPPING_RESEARCH_2026-11-25.md)。
