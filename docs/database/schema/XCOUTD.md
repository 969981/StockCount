---
title: "XCOUTD — 库存盘点明细表完整字段字典"
aliases:
  - "StockCount · docs / database / schema / XCOUTD"
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

# XCOUTD — 库存盘点明细表完整字段字典

> **来源**：用户于 2026-11-19 在 Sage X3 SQL Server 的 **`sagex3.COB`（正式 Schema）** 实际执行并回传的字段元数据与 `ATABZON + ATEXTE (LAN_0='CHI')` 对照结果；本表物理列为**已实测**，不是猜测。字段中文普通/短/长标题忠实保留企业字典原文。**不要将本页 COB 结构不经核实直接推定为 XTEST 结构。**
>
> **数量**：19 列；源 SQL CSV 格式包含 SQL Server 物理数据类型、是否可空、X3 字段中文标题。本仓库不存真实业务明细数据。
>
> [返回五表索引](../SCHEMA_INDEX.md) · [读取/中文别名 SQL](../../../sql/read_only/select_five_tables_chinese_aliases.sql) · [全字段结构字典 SQL](../../../sql/read_only/inspect_five_tables.sql) · [业务查询 SQL](../../../sql/read_only/business_queries.sql)

## 1. 全部 19 列：类型与企业中文标题

| 序号 | SQL 字段 | SQL Server 实际类型 | 可空 | X3 中文普通标题 | X3 中文短标题 | X3 中文长标题 | 本地菜单 | 字典匹配结果 |
| ---: | --- | --- | :---: | --- | --- | --- | ---: | --- |
| 1 | `UPDTICK_0` | `int` | 否 | — | — | — | — | 未匹配（通常系统列） |
| 2 | `CUNNUM_0` | `nvarchar(30)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 3 | `PALNUM_0` | `nvarchar(20)` | 否 | 托盘号 | 托盘号 | — | 0 | 已匹配且有中文 |
| 4 | `BOXNUM_0` | `nvarchar(20)` | 否 | 箱号 | 箱号 | 箱号 | 0 | 已匹配且有中文 |
| 5 | `ITMREF_0` | `nvarchar(20)` | 否 | 产品 | 产品 | 产品 | 0 | 已匹配且有中文 |
| 6 | `LOT_0` | `nvarchar(30)` | 否 | 批次 | 批次 | 批次 | 0 | 已匹配且有中文 |
| 7 | `LOC_0` | `nvarchar(10)` | 否 | 库位 | 库位 | 库位 | 0 | 已匹配且有中文 |
| 8 | `ECCVALMAJ_0` | `nvarchar(20)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 9 | `PCU_0` | `nvarchar(3)` | 否 | 单位 | 包装单位 | 包装单位 | 0 | 已匹配且有中文 |
| 10 | `QTYPCU_0` | `numeric(28,13)` | 否 | 包装单位数量 | 包装单位数量 | 包装单位数量 | 0 | 已匹配且有中文 |
| 11 | `PCUSTUCOE_0` | `numeric(18,7)` | 否 | 包装/存货单位系数 | 包装/存货单位系数 | 包装/存货单位系数 | 0 | 已匹配且有中文 |
| 12 | `QTYSTU_0` | `numeric(28,13)` | 否 | 存货单位数量 | 存货单位数量 | 存货单位数量 | 0 | 已匹配且有中文 |
| 13 | `PDAID_0` | `nvarchar(50)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 14 | `CREDATTIM_0` | `datetime` | 否 | 日期时间 | 日期时间 | 日期时间 | 0 | 已匹配且有中文 |
| 15 | `UPDDATTIM_0` | `datetime` | 否 | 日期时间 | 日期时间 | 日期时间 | 0 | 已匹配且有中文 |
| 16 | `AUUID_0` | `binary(16)` | 否 | 唯一识别码 | 唯一识别码 | 唯一识别码 | 0 | 已匹配且有中文 |
| 17 | `CREUSR_0` | `nvarchar(5)` | 否 | 用户 | 用户 | 用户 | 0 | 已匹配且有中文 |
| 18 | `UPDUSR_0` | `nvarchar(5)` | 否 | 用户 | 用户 | 用户 | 0 | 已匹配且有中文 |
| 19 | `ROWID` | `numeric(38,0)` | 否 | — | — | — | — | 未匹配（通常系统列） |

## 2. 使用规则与核验边界

- **无中文标题 ≠ 字段不存在**：有些字段企业字典标题为空、`ROWID` / `UPDTICK_0` 等系统列未进入字典，不得自动给它们冒充官方中文名。
- **全部业务查询请以选择的 Schema 为准**。正式 `COB`、测试 `XTEST` 由服务端受控枚举映射，APK 不直接执行 SQL。
- 数字字段的精度、scale 已在列类型中明确；`CUNTYP_0` 在不同 Schema 可能有类型差异（此前 `UNION ALL` 导致 INT1 溢出），跨环境统一映射为字符串。
- **最新 V1 规则**：一次人工实盘数同时写入 `QTYSTU_0` 与 `QTYPCU_0`，两者同值；`PCUSTUCOE_0`/`PCU_0` 与 X3 服务的兼容性仍待验证，不得擅自生产裸 INSERT。
- 已知正式表没有 `CUNNUM_0 + BOXNUM_0` 复合唯一约束，历史曾存在同单重复。

## 业务数据查询 SQL（中文别名）

2026-11-25 补充。以下均为只读，默认抽样 TOP (20)，字段使用 `中文业务别名（物理字段名）`；说明性别名不等于 X3 官方字典标题。原始码值和 CASE 翻译并列，转换不代表业务准入。切换 Schema 前核实真实列和菜单。

### 1. 常用业务字段

```sql
SELECT TOP (20)
    T.CUNNUM_0 AS [盘点单号（CUNNUM_0）],
    T.PALNUM_0 AS [托盘号/识别码1（PALNUM_0）],
    T.BOXNUM_0 AS [箱码（BOXNUM_0）],
    T.ITMREF_0 AS [产品编码（ITMREF_0）],
    T.LOT_0 AS [批号（LOT_0）],
    T.LOC_0 AS [库位（LOC_0）],
    T.PCU_0 AS [包装单位（PCU_0）],
    T.QTYPCU_0 AS [包装单位数量（QTYPCU_0）],
    T.PCUSTUCOE_0 AS [包装/存货单位系数（PCUSTUCOE_0）],
    T.QTYSTU_0 AS [实盘存货单位数量（QTYSTU_0）],
    T.PDAID_0 AS [PDA标识（PDAID_0）],
    T.CREDATTIM_0 AS [创建时间（CREDATTIM_0）]
FROM sagex3.COB.XCOUTD AS T
ORDER BY CREDATTIM_0 DESC, ROWID DESC;
```

### 2. 全部 19 个物理字段（静态 SQL）

以下按已实测 COB 物理字段顺序展开，CASE 为附加显示列，不替换原始值。

```sql
SELECT TOP (20)
    T.UPDTICK_0 AS [更新计数（UPDTICK_0）],
    T.CUNNUM_0 AS [盘点单号（CUNNUM_0）],
    T.PALNUM_0 AS [托盘号/识别码1（PALNUM_0）],
    T.BOXNUM_0 AS [箱码（BOXNUM_0）],
    T.ITMREF_0 AS [产品编码（ITMREF_0）],
    T.LOT_0 AS [批号（LOT_0）],
    T.LOC_0 AS [库位（LOC_0）],
    T.ECCVALMAJ_0 AS [ECCVALMAJ（含义待核）（ECCVALMAJ_0）],
    T.PCU_0 AS [包装单位（PCU_0）],
    T.QTYPCU_0 AS [包装单位数量（QTYPCU_0）],
    T.PCUSTUCOE_0 AS [包装/存货单位系数（PCUSTUCOE_0）],
    T.QTYSTU_0 AS [实盘存货单位数量（QTYSTU_0）],
    T.PDAID_0 AS [PDA标识（PDAID_0）],
    T.CREDATTIM_0 AS [创建时间（CREDATTIM_0）],
    T.UPDDATTIM_0 AS [修改时间（UPDDATTIM_0）],
    T.AUUID_0 AS [唯一识别码（AUUID_0）],
    T.CREUSR_0 AS [创建用户（CREUSR_0）],
    T.UPDUSR_0 AS [修改用户（UPDUSR_0）],
    T.ROWID AS [系统行标识（ROWID）]
FROM sagex3.COB.XCOUTD AS T
ORDER BY CREDATTIM_0 DESC, ROWID DESC;
```

### 3. 码值字典与核验

已归档 19 列没有非零本地菜单号；不把数量、托盘号、批次、PDA 标识或系统行号当作枚举。若环境字典变化，以以下查询为准。

```sql
SELECT DISTINCT
    Z.CODZONE_0 AS [字段代码], Z.NOLIB_0 AS [本地菜单编号],
    M.LANNUM_0 AS [码值], M.LAN_0 AS [语言],
    M.LANMES_0 AS [状态含义]
FROM sagex3.COB.ATABZON Z
LEFT JOIN sagex3.COB.APLSTD M
    ON M.LANCHP_0 = Z.NOLIB_0
   AND M.LAN_0 IN (N'CHI', N'ENG') AND M.LANNUM_0 > 0
WHERE Z.CODFIC_0 = N'XCOUTD'
  AND Z.CODTYP_0 = N'M' AND Z.NOLIB_0 > 0
ORDER BY Z.CODZONE_0, Z.NOLIB_0, M.LANNUM_0, M.LAN_0;
```

全部菜单的动态 CASE（包括尚未手工核实的枚举）使用 [select_table_with_enum_aliases.sql](../../../sql/read_only/select_table_with_enum_aliases.sql)，将 `@TableName` 设为 `XCOUTD`。脚本仅按现场非空且无冲突的中文字典生成 CASE，不使用菜单第0项；未知码值保留原值并标明未知。SQL 尚未在企业数据库执行验收，不提交实际业务数据。


---

## 🧭 知识库导航

- 主题索引：[[docs/database/00-数据库知识地图|数据库知识地图]] · [Markdown](../00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../../../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](../../00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 数据流：[[docs/DATABASE_MAPPING|字段映射]] · [Markdown](../../DATABASE_MAPPING.md) · [[docs/API_CONTRACT|API 契约]] · [Markdown](../../API_CONTRACT.md)
