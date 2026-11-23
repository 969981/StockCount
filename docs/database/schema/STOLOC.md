---
title: "STOLOC — 库位主数据完整字段字典（XTEST 已实测）"
aliases:
  - "StockCount · docs / database / schema / STOLOC"
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

# STOLOC — 库位主数据完整字段字典（XTEST 已实测）

> **来源**：2026-11-23 用户针对 `sagex3.XTEST.STOLOC` 的 SQL Server 实测元数据、`ATABZON` + `ATEXTE` 中文标题；**41/41 SQL 物理列已确认、39/41 有 X3 中文长标题**。41 列 `MS_Description` 全部为空（不代表 Sage X3 无中文标题）。没有在本公开仓库收录用户的真实库位数据行。
>
> **类型注意**：来源的长度是 **`sys.columns.max_length` 的 SQL Server 字节数**；为阅读方便，`nvarchar` 显示为等价字符容量（字节 ÷ 2），而 `numeric` 精度/小数位**无法从字节数推出**，须通过所附动态 SQL 读取 `precision/scale`。所有 41 列在已实测 XTEST 的 `is_nullable=0`。
>
> [五表索引](../SCHEMA_INDEX.md) · [全物理列/中文字典查询 SQL](../../../sql/read_only/inspect_five_tables.sql) · [中文别名查询 SQL](../../../sql/read_only/select_five_tables_chinese_aliases.sql)

## 全部 41 列

| 序号 | 字段 | SQL 类型（可确认部分） | 存储字节 | 可空 | X3 中文长标题 | X3 类型 | X3 字段长度 | 维度 | 关联表 |
| ---: | --- | --- | ---: | --- | --- | --- | ---: | ---: | --- |
| 1 | `UPDTICK_0` | `int` | 4 | 否 | — | — | — | — | — |
| 2 | `STOFCY_0` | `nvarchar(5)` | 10 | 否 | 地点 | FCY | 0.0000 | 1 | FACILITY |
| 3 | `LOC_0` | `nvarchar(10)` | 20 | 否 | 库位 | EMP | 0.0000 | 1 | — |
| 4 | `WRH_0` | `nvarchar(5)` | 10 | 否 | 仓库 | WRH | 0.0000 | 1 | WAREHOUSE |
| 5 | `OCPCOD_0` | `tinyint` | 1 | 否 | 占用 | M | 15.0000 | 1 | — |
| 6 | `LOCTYP_0` | `nvarchar(5)` | 10 | 否 | 库位类型 | TLO | 0.0000 | 1 | TABLOCTYP |
| 7 | `LOCCAT_0` | `tinyint` | 1 | 否 | 库位种类 | M | 15.0000 | 1 | — |
| 8 | `LOCNUMFMT_0` | `nvarchar(10)` | 20 | 否 | 库位格式 | EMP | 0.0000 | 1 | — |
| 9 | `PPSSEQ_0` | `nvarchar(10)` | 20 | 否 | 建议序列 | EMP | 0.0000 | 1 | — |
| 10 | `MONITMFLG_0` | `tinyint` | 1 | 否 | 单一产品 | M | 4.0000 | 1 | — |
| 11 | `DEDFLG_0` | `tinyint` | 1 | 否 | 专用的 | M | 4.0000 | 1 | — |
| 12 | `REAFLG_0` | `tinyint` | 1 | 否 | 可补货的 | M | 4.0000 | 1 | — |
| 13 | `FRGMGTMOD_0` | `tinyint` | 1 | 否 | 解除管理 | M | 15.0000 | 1 | — |
| 14 | `TEMLTI_0` | `smallint` | 2 | 否 | 延迟期间 | C | 3.0000 | 1 | — |
| 15 | `FILMGTFLG_0` | `tinyint` | 1 | 否 | 容量管理 | M | 4.0000 | 1 | — |
| 16 | `AUZSST_0` | `nvarchar(20)` | 40 | 否 | 授权的子状态 | A | 20.0000 | 1 | — |
| 17 | `AVADAT_0` | `datetime` | 8 | 否 | 可用日期 | D | 0.0000 | 1 | — |
| 18 | `AVAHOU_0` | `nvarchar(6)` | 12 | 否 | 可用时间 | HM | 0.0000 | 1 | — |
| 19 | `ALLUSR_0` | `nvarchar(5)` | 10 | 否 | 保留用户 | A | 5.0000 | 1 | — |
| 20 | `ALLDAT_0` | `datetime` | 8 | 否 | 保留日期 | D | 0.0000 | 1 | — |
| 21 | `ALLHOU_0` | `nvarchar(6)` | 12 | 否 | 保留期间 | HM | 0.0000 | 1 | — |
| 22 | `MAXAUZWEI_0` | `numeric（精度/标度待查）` | 9 | 否 | 最大重量 | WEI | 0.0000 | 1 | — |
| 23 | `WID_0` | `smallint` | 2 | 否 | 宽度 | C | 4.0000 | 1 | — |
| 24 | `HEI_0` | `smallint` | 2 | 否 | 高度 | C | 4.0000 | 1 | — |
| 25 | `DTH_0` | `smallint` | 2 | 否 | 深度 | C | 4.0000 | 1 | — |
| 26 | `LOKSTA_0` | `tinyint` | 1 | 否 | 冻结的 | M | 4.0000 | 1 | — |
| 27 | `CUNLOKFLG_0` | `tinyint` | 1 | 否 | 进行中盘点 | M | 4.0000 | 1 | — |
| 28 | `PCU_0` | `nvarchar(3)` | 6 | 否 | 包装单位 | UOM | 0.0000 | 1 | TABUNIT |
| 29 | `PCUSTUCOE_0` | `numeric（精度/标度待查）` | 9 | 否 | 包装/存货单位系数 | COE | 0.0000 | 1 | — |
| 30 | `QTYPCU_0` | `numeric（精度/标度待查）` | 13 | 否 | 包装单位数量 | QTY | 0.0000 | 1 | — |
| 31 | `MAXQTYPCU_0` | `numeric（精度/标度待查）` | 13 | 否 | 最大PAC数量 | QTY | 0.0000 | 1 | — |
| 32 | `EXPNUM_0` | `int` | 4 | 否 | 导出编号 | L | 8.0000 | 1 | — |
| 33 | `CREDAT_0` | `datetime` | 8 | 否 | 创建日期 | D | 0.0000 | 1 | — |
| 34 | `CREUSR_0` | `nvarchar(5)` | 10 | 否 | 创建用户 | A | 5.0000 | 1 | — |
| 35 | `UPDDAT_0` | `datetime` | 8 | 否 | 修改日期 | D | 0.0000 | 1 | — |
| 36 | `UPDUSR_0` | `nvarchar(5)` | 10 | 否 | 修改用户 | A | 5.0000 | 1 | — |
| 37 | `CREDATTIM_0` | `datetime` | 8 | 否 | 日期时间 | ADATIM | 0.0000 | 1 | — |
| 38 | `UPDDATTIM_0` | `datetime` | 8 | 否 | 日期时间 | ADATIM | 0.0000 | 1 | — |
| 39 | `AUUID_0` | `binary` | 16 | 否 | 唯一识别码 | AUUID | 0.0000 | 1 | — |
| 40 | `YLOCDES1_0` | `nvarchar(10)` | 20 | 否 | 库位名称 | A | 10.0000 | 1 | — |
| 41 | `ROWID` | `numeric（精度/标度待查）` | 17 | 否 | — | — | — | — | — |

> `MAXQTYPCU_0` 的**中文普通标题**是“最大包装单位数量”，中文**长标题**为“最大PAC数量”；本表保留原来源“长标题”。`UPDTICK_0` 与 `ROWID` 没有 X3 标题，请勿声称这两个系统字段有官方中文标题。

## 库位查询与 StockCount 设计提示

- 库位的**完整业务定位**至少包含 `STOFCY_0`（地点）、`WRH_0`（仓库）、`LOC_0`（库位），**不能只依赖 LOC 全局唯一**。
- 2026-11-23 用户在 XTEST 执行 12 列简版库位列表后提供 13,156 行样本结果，其站点+仓库+库位组合不重复，但单独 LOC 存在跨站点/仓库复用；**不将真实业务行提交到公开仓库**。
- `YLOCDES1_0` 中文名“库位名称”，来源中的实测 13,156 行该值为空；UI 应仍支持名称字段为空的情况。
- `LOKSTA_0`（冻结的）、`CUNLOKFLG_0`（进行中盘点）、`OCPCOD_0`（占用）为原始代码值，**不能单凭字段中文名就将 1 解释为 true**。
- V1 写入目标是 `XCOUTD.LOC_0 nvarchar(10)`，扫描库位无法容纳时拒绝写入，不可截断。

SQL 位置：[全字段中文别名读数](../../../sql/read_only/select_five_tables_chinese_aliases.sql) · [关键业务只读 SQL](../../../sql/read_only/business_queries.sql)。

---

## 🧭 知识库导航

- 主题索引：[[docs/database/00-数据库知识地图|数据库知识地图]] · [Markdown](../00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../../../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](../../00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 元数据与 SQL：[[docs/database/SCHEMA_INDEX|七表字典索引]] · [Markdown](../SCHEMA_INDEX.md) · [[docs/DATABASE_MAPPING|业务字段映射]] · [Markdown](../../DATABASE_MAPPING.md)
