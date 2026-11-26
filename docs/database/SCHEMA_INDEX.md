---
title: "StockCount — 七表结构与中文字段字典索引（5 张业务表 + 2 张字典表）"
aliases:
  - "StockCount · docs / database / SCHEMA_INDEX"
created: 2026-11-23
updated: 2026-11-25
status: reference
area: database
tags:
  - StockCount
  - database
  - docs
up: "[[docs/database/00-数据库知识地图]]"
---

# StockCount — 七表结构与中文字段字典索引（5 张业务表 + 2 张字典表）

> **2026-11-23 归档。** 只包含 Sage X3 字段字典、SQL 查询与字段解释，不提交真实生产/测试业务数据。**正式帐套：`[sagex3].[COB]`；测试帐套：`[sagex3].[XTEST]`**。本文是字段证据与开发索引，不能替代真实数据库权限与运行时核验。

## 七张表的证据覆盖

早期业务表清单曾重复写入 `XCOUTH`，结合盘点场景按 `XCOUTH + XCOUTD + XLABELDTL + STOLOC + ITMMASTER` 五张业务表整理；**2026-11-23 新增 `ATABZON` 和 `ATEXTE` 两张 Sage X3 字典表**，故现有共七张表。

| 表 | 用途 | 已取得的物理列证据 | 中文标题来源 | 详细字段清单 |
| --- | --- | --- | --- | --- |
| `XCOUTH` | 库存盘点单表头 | **COB 16/16** 列、类型、可空已实测 | COB.ATABZON / ATEXTE | [16 列完整映射](schema/XCOUTH.md) |
| `XCOUTD` | 库存盘点明细 | **COB 19/19** 列、类型、可空已实测 | COB.ATABZON / ATEXTE | [19 列完整映射](schema/XCOUTD.md) |
| `XLABELDTL` | 箱标签与完整二维码 | **COB 45/45** 列、类型、可空已实测 | COB.ATABZON / ATEXTE | [45 列完整映射](schema/XLABELDTL.md) |
| `STOLOC` | 库位主数据 | **XTEST 41/41** 列、物理类型、存储字节、可空已实测；其中 numeric 精度/scale 尚待查询 | XTEST.ATABZON / ATEXTE，其中 39 个有中文标题 | [41 列完整映射](schema/STOLOC.md) |
| `ITMMASTER` | 物料主数据、品名与规格 | **197 条 X3 字段字典已实测，但完整 SQL 物理列清单尚未回传** | COB.ATABZON / ATEXTE | [197 条字典及物理核验方法](schema/ITMMASTER.md) |
| `ATABZON` | X3 逻辑字段字典、字段定义与标题编号 | **常用字段名已由既有 SQL 验证；自身完整物理列/类型未收到回传** | 自身字典 + ATEXTE（如有） | [字段与关联关系](schema/ATABZON.md) |
| `ATEXTE` | X3 多语言字段标题文本 | **NUMERO_0、LAN_0、TEXTE_0 等查询字段已验证；自身完整物理列/类型未收到回传** | 自身字典（如有） | [字段与关联关系](schema/ATEXTE.md) |

**请不要把 197 条 ITMMASTER X3 字典码误认作 197 个实际 SQL Server 物理列。** 该表的完整物理结构须运行下面 SQL，再按实际回传结果补齐。不同帐套可能有结构差异（`CUNTYP_0` 已发现跨 Schema 转换溢出），不能只凭正式帐套数据断言测试帐套字段完全相同。

## 可直接使用的 SQL（全部只读）

**范围说明**：原来的 `inspect_five_tables.sql` 和其他两份业务 SQL 保持专门处理五张业务表；新增两份脚本专门处理 `ATABZON` 和 `ATEXTE`，避免把业务表与字典表的数据抽样混淆。

| SQL 脚本 | 内容 | 范围 |
| --- | --- | --- |
| [inspect_five_tables.sql](../../sql/read_only/inspect_five_tables.sql) | 依据 `sys.columns` 实际列关联 `ATABZON + ATEXTE`，输出五表全部字段、类型、长度、精度、小数位、可空、中文普通/长标题、菜单与 `MS_Description`，并检查**主键/唯一索引、默认值、外键、触发器、检查约束** | **同时查询 COB 和 XTEST 元数据**；能补齐 ITMMASTER 实际物理列 |
| [select_five_tables_chinese_aliases.sql](../../sql/read_only/select_five_tables_chinese_aliases.sql) | 动态按 SQL 真实物理列生成 `[中文标题（字段名）]`，分别 `TOP 20` 查询五表，支持按单号、箱号/二维码、物料、站点、仓库、库位筛选 | **默认 XTEST**，可手工设为 COB |
| [business_queries.sql](../../sql/read_only/business_queries.sql) | 盘点单列表、指定单据明细与数量、标签+物料、库位、物料资料、五表业务关联，共六组查询 | **默认 XTEST**，可手工设为 COB；各查询最多 TOP 30 |
| [inspect_dictionary_tables.sql](../../sql/read_only/inspect_dictionary_tables.sql) | **ATABZON / ATEXTE 完整物理列 + 中文短/普通/长标题 + 类型、长度、可空、索引、触发器等** | 同时检查 COB / XTEST，只读元数据 |
| [query_dictionary_tables.sql](../../sql/read_only/query_dictionary_tables.sql) | ATABZON 字段代码、标题号与 ATEXTE 中文/英文关联、两字典表有限行 `SELECT *` | 默认 XTEST；选择7张表之一、最多 TOP 100 |
  
## 2026-11-25 · 各表内嵌业务数据查询与码值 CASE

七个字段字典页面均新增「业务数据查询 SQL（中文别名）」：常用业务字段、全部物理列、原始码值与中文映射、现场本地菜单查询。已核实物理列的 XCOUTH（16）、XCOUTD（19）、XLABELDTL（45）、STOLOC（41）使用静态 SELECT，合计121个物理字段；ITMMASTER / ATABZON / ATEXTE 通过 sys.columns 动态枚举，不把字典定义当作物理列。

- [单表全部物理列 + 中文别名 + 本地菜单 CASE 生成器](../../sql/read_only/select_table_with_enum_aliases.sql)：默认 XTEST、TOP 20，允许手动选择七表及 COB；输出原值、实际菜单码值、中英文含义和生成的可复用 SELECT。第0项菜单元信息不参与翻译；空白或未知码值不猜测。
- 静态映射证据：STOLOC 的 OCPCOD/LOKSTA/CUNLOKFLG 来自 XTEST 实测；XCOUTH.CUNSTA 与 XLABELDTL.STA 已知部分来自 COB 实测；跨 Schema 映射需现场确认。
- 所有示例 SQL 均为只读诊断，状态文字不改变 V1 业务准入规则；未在企业 SQL Server 中重新执行验证。
- 2026-11-24 COB 只读核验中，站点+库位未发现重复，非空盘点明细均可在表头站点找到匹配；但存在仓库不一致记录。因此诊断优先用站点+库位关联，仓库作为独立比较条件；不将当前快照当成唯一索引或历史业务授权的证据。

## 字典表关系与常用字段关联

```text
ATABZON.CODFIC_0  = X3 表代码（例如 XCOUTH）
ATABZON.CODZONE_0 = X3 字段代码（例如 QTYSTU，去掉 SQL 的 _0）
ATABZON.NOCOURT_0 = ATEXTE.NUMERO_0（普通标题）
ATABZON.NOABREG_0 = ATEXTE.NUMERO_0（短标题）
ATABZON.NOLONG_0  = ATEXTE.NUMERO_0（长标题）
ATEXTE.LAN_0      = 'CHI' / 'ENG'（按语言筛选）
ATABZON.NOLIB_0   = APLSTD 的本地菜单号，**不是 ATEXTE 短标题号**
```

下列是五张业务表之间的字段连接：

```text
XCOUTH.CUNNUM_0   = XCOUTD.CUNNUM_0          -- 一对多盘点单/明细
XCOUTD.BOXNUM_0   = XLABELDTL.PID_0         -- 箱码/标签ID
XLABELDTL.ITMREF_0 = ITMMASTER.ITMREF_0      -- 产品编码
STOLOC.STOFCY_0 + STOLOC.WRH_0 + STOLOC.LOC_0
  对应 XCOUTH.STOFCY_0 + XCOUTH.WRH_0 + XCOUTD.LOC_0
   -- 库位建议组合匹配；需按真实业务确认
```

- **盘点单全部展示**：V1 列表不以 `CUNSTA_0` 过滤；是否允许写某单是独立规则。
- **标签全部状态都允许盘点**：`XLABELDTL.STA_0` 不作为准入白名单，历史 `IN (1,7,8)` 规则废弃。
- **实盘数量人工输入一次、同时保存两列同值**：`XCOUTD.QTYSTU_0 = XCOUTD.QTYPCU_0 = 人工实盘数量`，不读标签 `QTY_0` 或二维码 `QT` 作为实盘数。**`PCUSTUCOE_0` 和 `PCU_0` 的写入一致性尚待 XTEST 验证**。
- **绝不盲目拼接生产 INSERT**：`AUUID_0`、`ROWID`、`UPDTICK_0`、非空审计字段和业务写入机制先核实。
- **不要以当前标签数据倒推历史实盘**：标签库位、标签当前数量都可能在盘点后变动。

## 继续补齐 ITMMASTER 的真实物理字段

1. 在有数据库读取权限的管理端执行 [inspect_five_tables.sql](../../sql/read_only/inspect_five_tables.sql)；**另执行 [inspect_dictionary_tables.sql](../../sql/read_only/inspect_dictionary_tables.sql) 补齐字典两表真实物理字段与类型**。
2. 仅回传**字段元数据**（表名/SQL字段名/类型/字节长度/数字精度/小数位/中文标题），**不要上传生产物料数据行、账号或内网地址到公开 GitHub**。
3. 以 `ITMMASTER` 实际物理列清单替换其“物理字段待核”状态，并核验测试 `XTEST` 与正式 `COB` 的差异。
4. 对 `STOLOC` 数字列补齐 `precision/scale`；检查两帐套字段是否一致，完善 API 和 Dapper 实际映射。

## 关联

- [核心字段与 DTO 关系](../DATABASE_MAPPING.md)
- [库存盘点需求](../../requirements.md)
- [AI 规范](../../AGENTS.md)
- [API 契约](../API_CONTRACT.md)
- [安全与帐套](../SECURITY_AND_ENVIRONMENTS.md)

---

## 🧭 知识库导航

- 主题索引：[[docs/database/00-数据库知识地图|数据库知识地图]] · [Markdown](00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](../00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 数据流：[[docs/DATABASE_MAPPING|字段映射]] · [Markdown](../DATABASE_MAPPING.md) · [[docs/API_CONTRACT|API 契约]] · [Markdown](../API_CONTRACT.md)


- [当前业务 SQL 实现字典](BUSINESS_SQL_REFERENCE.md) · [运行时只读核验](../../sql/read_only/runtime_business_queries.sql)
