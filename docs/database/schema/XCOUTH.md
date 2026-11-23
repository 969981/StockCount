---
title: "XCOUTH — 库存盘点单表头完整字段字典"
aliases:
  - "StockCount · docs / database / schema / XCOUTH"
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

# XCOUTH — 库存盘点单表头完整字段字典

> **来源**：用户于 2026-11-19 在 Sage X3 SQL Server 的 **`sagex3.COB`（正式 Schema）** 实际执行并回传的字段元数据与 `ATABZON + ATEXTE (LAN_0='CHI')` 对照结果；本表物理列为**已实测**，不是猜测。字段中文普通/短/长标题忠实保留企业字典原文。**不要将本页 COB 结构不经核实直接推定为 XTEST 结构。**
>
> **数量**：16 列；源 SQL CSV 格式包含 SQL Server 物理数据类型、是否可空、X3 字段中文标题。本仓库不存真实业务明细数据。
>
> [返回五表索引](../SCHEMA_INDEX.md) · [读取/中文别名 SQL](../../../sql/read_only/select_five_tables_chinese_aliases.sql) · [全字段结构字典 SQL](../../../sql/read_only/inspect_five_tables.sql) · [业务查询 SQL](../../../sql/read_only/business_queries.sql)

## 1. 全部 16 列：类型与企业中文标题

| 序号 | SQL 字段 | SQL Server 实际类型 | 可空 | X3 中文普通标题 | X3 中文短标题 | X3 中文长标题 | 本地菜单 | 字典匹配结果 |
| ---: | --- | --- | :---: | --- | --- | --- | ---: | --- |
| 1 | `UPDTICK_0` | `int` | 否 | — | — | — | — | 未匹配（通常系统列） |
| 2 | `CUNNUM_0` | `nvarchar(30)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 3 | `STOFCY_0` | `nvarchar(5)` | 否 | 地点 | 地点 | 地点 | 0 | 已匹配且有中文 |
| 4 | `CUNDAT_0` | `datetime` | 否 | 盘点日期 | 盘点日期 | 盘点日期 | 0 | 已匹配且有中文 |
| 5 | `CUNSTA_0` | `tinyint` | 否 | 状态 | 状态 | 状态 | 2722 | 已匹配且有中文 |
| 6 | `CUNDES_0` | `nvarchar(30)` | 否 | 标题 | 标题 | 标题 | 0 | 已匹配且有中文 |
| 7 | `PDAID_0` | `nvarchar(50)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 8 | `CUNTYP_0` | `nvarchar(10)` | 否 | 盘点类型 | 盘点类型 | 盘点类型 | 0 | 已匹配且有中文 |
| 9 | `WRH_0` | `nvarchar(5)` | 否 | 仓库 | 仓库 | 仓库 | 0 | 已匹配且有中文 |
| 10 | `LOC_0` | `nvarchar(20)` | 否 | 库位 | 库位 | — | 0 | 已匹配且有中文 |
| 11 | `CREDATTIM_0` | `datetime` | 否 | 日期时间 | 日期时间 | 日期时间 | 0 | 已匹配且有中文 |
| 12 | `UPDDATTIM_0` | `datetime` | 否 | 日期时间 | 日期时间 | 日期时间 | 0 | 已匹配且有中文 |
| 13 | `AUUID_0` | `binary(16)` | 否 | 唯一识别码 | 唯一识别码 | 唯一识别码 | 0 | 已匹配且有中文 |
| 14 | `CREUSR_0` | `nvarchar(5)` | 否 | 用户 | 用户 | 用户 | 0 | 已匹配且有中文 |
| 15 | `UPDUSR_0` | `nvarchar(5)` | 否 | 用户 | 用户 | 用户 | 0 | 已匹配且有中文 |
| 16 | `ROWID` | `numeric(38,0)` | 否 | — | — | — | — | 未匹配（通常系统列） |

## 2. 使用规则与核验边界

- **无中文标题 ≠ 字段不存在**：有些字段企业字典标题为空、`ROWID` / `UPDTICK_0` 等系统列未进入字典，不得自动给它们冒充官方中文名。
- **全部业务查询请以选择的 Schema 为准**。正式 `COB`、测试 `XTEST` 由服务端受控枚举映射，APK 不直接执行 SQL。
- 数字字段的精度、scale 已在列类型中明确；`CUNTYP_0` 在不同 Schema 可能有类型差异（此前 `UNION ALL` 导致 INT1 溢出），跨环境统一映射为字符串。


- **最新 V1 规则**：盘点单列表展示所有 `CUNSTA_0` 状态；是否允许提交需另行定义；V1 不创建、不关闭盘点单。

---

## 🧭 知识库导航

- 主题索引：[[docs/database/00-数据库知识地图|数据库知识地图]] · [Markdown](../00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../../../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](../../00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 数据流：[[docs/DATABASE_MAPPING|字段映射]] · [Markdown](../../DATABASE_MAPPING.md) · [[docs/API_CONTRACT|API 契约]] · [Markdown](../../API_CONTRACT.md)
