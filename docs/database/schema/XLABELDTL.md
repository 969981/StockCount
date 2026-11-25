---
title: "XLABELDTL — 箱标签 / 二维码明细表完整字段字典"
aliases:
  - "StockCount · docs / database / schema / XLABELDTL"
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

# XLABELDTL — 箱标签 / 二维码明细表完整字段字典

> **来源**：用户于 2026-11-19 在 Sage X3 SQL Server 的 **`sagex3.COB`（正式 Schema）** 实际执行并回传的字段元数据与 `ATABZON + ATEXTE (LAN_0='CHI')` 对照结果；本表物理列为**已实测**，不是猜测。字段中文普通/短/长标题忠实保留企业字典原文。**不要将本页 COB 结构不经核实直接推定为 XTEST 结构。**
>
> **数量**：45 列；源 SQL CSV 格式包含 SQL Server 物理数据类型、是否可空、X3 字段中文标题。本仓库不存真实业务明细数据。
>
> [返回五表索引](../SCHEMA_INDEX.md) · [读取/中文别名 SQL](../../../sql/read_only/select_five_tables_chinese_aliases.sql) · [全字段结构字典 SQL](../../../sql/read_only/inspect_five_tables.sql) · [业务查询 SQL](../../../sql/read_only/business_queries.sql)

## 1. 全部 45 列：类型与企业中文标题

| 序号 | SQL 字段 | SQL Server 实际类型 | 可空 | X3 中文普通标题 | X3 中文短标题 | X3 中文长标题 | 本地菜单 | 字典匹配结果 |
| ---: | --- | --- | :---: | --- | --- | --- | ---: | --- |
| 1 | `UPDTICK_0` | `int` | 否 | — | — | — | — | 未匹配（通常系统列） |
| 2 | `TYP_0` | `tinyint` | 否 | — | — | — | 1 | 字典匹配但无中文标题 |
| 3 | `VCRNUM_0` | `nvarchar(20)` | 否 | 凭证 | 凭证 | 凭证号 （Rec No， Del N | 0 | 已匹配且有中文 |
| 4 | `VCRLIN_0` | `int` | 否 | 凭证行号 | 行号 | 凭证行号 | 0 | 已匹配且有中文 |
| 5 | `ITMREF_0` | `nvarchar(20)` | 否 | 产品 | 产品 | 产品 | 0 | 已匹配且有中文 |
| 6 | `ITMDES1_0` | `nvarchar(150)` | 否 | 描述1 | 描述 | 描述1 | 0 | 已匹配且有中文 |
| 7 | `ITMDES2_0` | `nvarchar(150)` | 否 | 描述 2 | 描述 2 | 描述 2 | 0 | 已匹配且有中文 |
| 8 | `ITMDES3_0` | `nvarchar(150)` | 否 | 描述 3 | 描述 3 | 描述 3 | 0 | 已匹配且有中文 |
| 9 | `STU_0` | `nvarchar(3)` | 否 | 存货单位 | 存货单位 | 存货单位 | 0 | 已匹配且有中文 |
| 10 | `QTY_0` | `numeric(28,13)` | 否 | 数量 | 数量 | 数量 | 0 | 已匹配且有中文 |
| 11 | `PCU_0` | `nvarchar(3)` | 否 | 单位 | 包装单位 | 包装单位 | 0 | 已匹配且有中文 |
| 12 | `PCUSTUCOE_0` | `numeric(18,7)` | 否 | 包装/存货单位系数 | 包装/存货单位系数 | 包装/存货单位系数 | 0 | 已匹配且有中文 |
| 13 | `LOT_0` | `nvarchar(20)` | 否 | 批次 | 批次 | 批次 | 0 | 已匹配且有中文 |
| 14 | `LOC_0` | `nvarchar(20)` | 否 | 库位 | 库位 | 库位 | 0 | 已匹配且有中文 |
| 15 | `QRCOD_0` | `nvarchar(200)` | 否 | QR Code | QR Code | QR Code | 0 | 已匹配且有中文 |
| 16 | `STA_0` | `tinyint` | 否 | 标签状态 | 标签状态 | 标签状态 | 6012 | 已匹配且有中文 |
| 17 | `PID_0` | `nvarchar(50)` | 否 | 标签ID | 标签ID | 标签ID | 0 | 已匹配且有中文 |
| 18 | `IPTDAT_0` | `datetime` | 否 | 过账日期 | 过账 | 过账日期 | 0 | 已匹配且有中文 |
| 19 | `BPRNUM_0` | `nvarchar(15)` | 否 | 供应商 | 供应商 | 供应商 | 0 | 已匹配且有中文 |
| 20 | `BPRNAM_0` | `nvarchar(100)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 21 | `VCRNUMORI_0` | `nvarchar(20)` | 否 | 原始凭证 | 源凭证 | 源凭证号 | 0 | 已匹配且有中文 |
| 22 | `VCRLINORI_0` | `int` | 否 | 原始凭证行号 | 原始行 | 源凭证货品号 | 0 | 已匹配且有中文 |
| 23 | `CFGNUM_0` | `nvarchar(50)` | 否 | 配置号 | — | — | 0 | 已匹配且有中文 |
| 24 | `CFGCOD_0` | `nvarchar(50)` | 否 | 配置代码 | 配置代码 | 配置代码 | 0 | 已匹配且有中文 |
| 25 | `AFLG1_0` | `nvarchar(30)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 26 | `AFLG2_0` | `nvarchar(30)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 27 | `DFLG1_0` | `numeric(14,3)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 28 | `DFLG2_0` | `numeric(14,3)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 29 | `COPNBR_0` | `int` | 否 | 打印次数 | 打印次数 | — | 0 | 已匹配且有中文 |
| 30 | `CREDATTIM_0` | `datetime` | 否 | 日期时间 | 日期时间 | 日期时间 | 0 | 已匹配且有中文 |
| 31 | `UPDDATTIM_0` | `datetime` | 否 | 日期时间 | 日期时间 | 日期时间 | 0 | 已匹配且有中文 |
| 32 | `AUUID_0` | `binary(16)` | 否 | 唯一识别码 | 唯一识别码 | 唯一识别码 | 0 | 已匹配且有中文 |
| 33 | `CREUSR_0` | `nvarchar(5)` | 否 | 用户 | 用户 | 用户 | 0 | 已匹配且有中文 |
| 34 | `UPDUSR_0` | `nvarchar(5)` | 否 | 用户 | 用户 | 用户 | 0 | 已匹配且有中文 |
| 35 | `DES_0` | `nvarchar(150)` | 否 | 描述 | 描述 | 描述 | 0 | 已匹配且有中文 |
| 36 | `CLASS_0` | `nvarchar(200)` | 否 | 班次 | 班次 | 班次 | 0 | 已匹配且有中文 |
| 37 | `PALNUM_0` | `nvarchar(30)` | 否 | 识别码1 | 货盘管理 | 识别码1 | 0 | 已匹配且有中文 |
| 38 | `CTRNUM_0` | `nvarchar(10)` | 否 | 识别码2 | 识别码 2 | 识别码2 | 0 | 已匹配且有中文 |
| 39 | `QTYWEU_0` | `numeric(28,13)` | 否 | 重量 | 重量 | 重量 | 0 | 已匹配且有中文 |
| 40 | `SLO_0` | `nvarchar(50)` | 否 | 子批次 | 子批次 | 子批次 | 0 | 已匹配且有中文 |
| 41 | `SERNUM_0` | `nvarchar(50)` | 否 | 序列 | 序列 | 序列 | 0 | 已匹配且有中文 |
| 42 | `BPSLOT_0` | `nvarchar(30)` | 否 | 供应商批次 | 供应商批次 | 供应商批次 | 0 | 已匹配且有中文 |
| 43 | `ENAFLG_0` | `tinyint` | 否 | 激活的 | 激活的 | 选择业务 | 1 | 已匹配且有中文 |
| 44 | `STOSTA_0` | `nvarchar(30)` | 否 | — | — | — | 0 | 字典匹配但无中文标题 |
| 45 | `ROWID` | `numeric(38,0)` | 否 | — | — | — | — | 未匹配（通常系统列） |

## 2. 使用规则与核验边界

- **无中文标题 ≠ 字段不存在**：有些字段企业字典标题为空、`ROWID` / `UPDTICK_0` 等系统列未进入字典，不得自动给它们冒充官方中文名。
- **全部业务查询请以选择的 Schema 为准**。正式 `COB`、测试 `XTEST` 由服务端受控枚举映射，APK 不直接执行 SQL。
- 数字字段的精度、scale 已在列类型中明确；`CUNTYP_0` 在不同 Schema 可能有类型差异（此前 `UNION ALL` 导致 INT1 溢出），跨环境统一映射为字符串。

- **最新 V1 规则**：`STA_0` **不参与盘点准入限制**，包括 4、9；`QTY_0` 是当前标签数量，不等于人工实盘数；`QRCOD_0` 为完整原始二维码。

## 业务数据查询 SQL（中文别名）

2026-11-25 补充。以下均为只读，默认抽样 TOP (20)，字段使用 `中文业务别名（物理字段名）`；说明性别名不等于 X3 官方字典标题。原始码值和 CASE 翻译并列，转换不代表业务准入。切换 Schema 前核实真实列和菜单。

### 1. 常用业务字段

```sql
SELECT TOP (20)
    T.PID_0 AS [箱码（PID_0）],
    T.QRCOD_0 AS [完整二维码（QRCOD_0）],
    T.ITMREF_0 AS [产品编码（ITMREF_0）],
    T.ITMDES1_0 AS [描述1（ITMDES1_0）],
    T.ITMDES2_0 AS [描述2（规格）（ITMDES2_0）],
    T.ITMDES3_0 AS [描述 3（ITMDES3_0）],
    T.LOT_0 AS [批号（LOT_0）],
    T.LOC_0 AS [库位（LOC_0）],
    T.STU_0 AS [存货单位（STU_0）],
    T.QTY_0 AS [数量（QTY_0）],
    T.PCU_0 AS [包装单位（PCU_0）],
    T.PCUSTUCOE_0 AS [包装/存货单位系数（PCUSTUCOE_0）],
    T.STA_0 AS [标签状态码（STA_0）],
    CASE T.STA_0
        WHEN 1 THEN N'码值1（名称未确认）'
        WHEN 4 THEN N'失效'
        WHEN 7 THEN N'码值7（名称未确认）'
        WHEN 8 THEN N'码值8（名称未确认）'
        WHEN 9 THEN N'已发货'
        ELSE N'未知或未命名码值'
    END AS [标签状态（STA_0）],
    T.TYP_0 AS [TYP码（用途待核）（TYP_0）],
    CASE T.TYP_0
        WHEN 1 THEN N'否'
        WHEN 2 THEN N'是'
        ELSE N'未知或未命名码值'
    END AS [TYP菜单文字（TYP_0）],
    T.ENAFLG_0 AS [激活标志码（ENAFLG_0）],
    CASE T.ENAFLG_0
        WHEN 1 THEN N'否'
        WHEN 2 THEN N'是'
        ELSE N'未知或未命名码值'
    END AS [激活菜单文字（ENAFLG_0）],
    T.PALNUM_0 AS [托盘号/识别码1（PALNUM_0）],
    T.VCRNUM_0 AS [凭证（VCRNUM_0）],
    T.VCRLIN_0 AS [凭证行号（VCRLIN_0）],
    T.VCRNUMORI_0 AS [原始凭证（VCRNUMORI_0）],
    T.VCRLINORI_0 AS [原始凭证行号（VCRLINORI_0）],
    T.CREDATTIM_0 AS [创建时间（CREDATTIM_0）],
    T.UPDDATTIM_0 AS [修改时间（UPDDATTIM_0）]
FROM sagex3.COB.XLABELDTL AS T
ORDER BY PID_0;
```

### 2. 全部 45 个物理字段（静态 SQL）

以下按已实测 COB 物理字段顺序展开，CASE 为附加显示列，不替换原始值。

```sql
SELECT TOP (20)
    T.UPDTICK_0 AS [更新计数（UPDTICK_0）],
    T.TYP_0 AS [TYP码（用途待核）（TYP_0）],
    CASE T.TYP_0
        WHEN 1 THEN N'否'
        WHEN 2 THEN N'是'
        ELSE N'未知或未命名码值'
    END AS [TYP菜单文字（TYP_0）],
    T.VCRNUM_0 AS [凭证（VCRNUM_0）],
    T.VCRLIN_0 AS [凭证行号（VCRLIN_0）],
    T.ITMREF_0 AS [产品编码（ITMREF_0）],
    T.ITMDES1_0 AS [描述1（ITMDES1_0）],
    T.ITMDES2_0 AS [描述2（规格）（ITMDES2_0）],
    T.ITMDES3_0 AS [描述 3（ITMDES3_0）],
    T.STU_0 AS [存货单位（STU_0）],
    T.QTY_0 AS [数量（QTY_0）],
    T.PCU_0 AS [包装单位（PCU_0）],
    T.PCUSTUCOE_0 AS [包装/存货单位系数（PCUSTUCOE_0）],
    T.LOT_0 AS [批号（LOT_0）],
    T.LOC_0 AS [库位（LOC_0）],
    T.QRCOD_0 AS [完整二维码（QRCOD_0）],
    T.STA_0 AS [标签状态码（STA_0）],
    CASE T.STA_0
        WHEN 1 THEN N'码值1（名称未确认）'
        WHEN 4 THEN N'失效'
        WHEN 7 THEN N'码值7（名称未确认）'
        WHEN 8 THEN N'码值8（名称未确认）'
        WHEN 9 THEN N'已发货'
        ELSE N'未知或未命名码值'
    END AS [标签状态（STA_0）],
    T.PID_0 AS [箱码（PID_0）],
    T.IPTDAT_0 AS [过账日期（IPTDAT_0）],
    T.BPRNUM_0 AS [供应商（BPRNUM_0）],
    T.BPRNAM_0 AS [BPRNAM_0（字典标题待核）（BPRNAM_0）],
    T.VCRNUMORI_0 AS [原始凭证（VCRNUMORI_0）],
    T.VCRLINORI_0 AS [原始凭证行号（VCRLINORI_0）],
    T.CFGNUM_0 AS [配置号（CFGNUM_0）],
    T.CFGCOD_0 AS [配置代码（CFGCOD_0）],
    T.AFLG1_0 AS [AFLG1（含义待核）（AFLG1_0）],
    T.AFLG2_0 AS [AFLG2（含义待核）（AFLG2_0）],
    T.DFLG1_0 AS [DFLG1（含义待核）（DFLG1_0）],
    T.DFLG2_0 AS [DFLG2（含义待核）（DFLG2_0）],
    T.COPNBR_0 AS [打印次数（COPNBR_0）],
    T.CREDATTIM_0 AS [创建时间（CREDATTIM_0）],
    T.UPDDATTIM_0 AS [修改时间（UPDDATTIM_0）],
    T.AUUID_0 AS [唯一识别码（AUUID_0）],
    T.CREUSR_0 AS [创建用户（CREUSR_0）],
    T.UPDUSR_0 AS [修改用户（UPDUSR_0）],
    T.DES_0 AS [描述（DES_0）],
    T.CLASS_0 AS [班次（CLASS_0）],
    T.PALNUM_0 AS [托盘号/识别码1（PALNUM_0）],
    T.CTRNUM_0 AS [识别码2（CTRNUM_0）],
    T.QTYWEU_0 AS [重量（QTYWEU_0）],
    T.SLO_0 AS [子批次（SLO_0）],
    T.SERNUM_0 AS [序列（SERNUM_0）],
    T.BPSLOT_0 AS [供应商批次（BPSLOT_0）],
    T.ENAFLG_0 AS [激活标志码（ENAFLG_0）],
    CASE T.ENAFLG_0
        WHEN 1 THEN N'否'
        WHEN 2 THEN N'是'
        ELSE N'未知或未命名码值'
    END AS [激活菜单文字（ENAFLG_0）],
    T.STOSTA_0 AS [STOSTA（含义待核）（STOSTA_0）],
    T.ROWID AS [系统行标识（ROWID）]
FROM sagex3.COB.XLABELDTL AS T
ORDER BY PID_0;
```

### 3. 码值字典与核验

| 字段 | 菜单 | 码值 | 含义/证据 |
| --- | ---: | --- | --- |
| STA_0 | 6012 | 4、9 | 失效、已发货（COB 已实查） |
| STA_0 | 6012 | 1、7、8 | 历史存在，中文名称未确认 |
| TYP_0、ENAFLG_0 | 1 | 1、2 | 字典菜单编号来自 COB；否/是文案来自 XTEST 菜单1实测，COB 文案需复查。TYP 具体业务用途未知 |

V1 不按标签状态限制盘点；CASE 只供显示。STOSTA_0 是字符串，未确认枚举来源。

```sql
SELECT DISTINCT
    Z.CODZONE_0 AS [字段代码], Z.NOLIB_0 AS [本地菜单编号],
    M.LANNUM_0 AS [码值], M.LAN_0 AS [语言],
    M.LANMES_0 AS [状态含义]
FROM sagex3.COB.ATABZON Z
LEFT JOIN sagex3.COB.APLSTD M
    ON M.LANCHP_0 = Z.NOLIB_0
   AND M.LAN_0 IN (N'CHI', N'ENG') AND M.LANNUM_0 > 0
WHERE Z.CODFIC_0 = N'XLABELDTL'
  AND Z.CODTYP_0 = N'M' AND Z.NOLIB_0 > 0
ORDER BY Z.CODZONE_0, Z.NOLIB_0, M.LANNUM_0, M.LAN_0;
```

全部菜单的动态 CASE（包括尚未手工核实的枚举）使用 [select_table_with_enum_aliases.sql](../../../sql/read_only/select_table_with_enum_aliases.sql)，将 `@TableName` 设为 `XLABELDTL`。脚本仅按现场非空且无冲突的中文字典生成 CASE，不使用菜单第0项；未知码值保留原值并标明未知。SQL 尚未在企业数据库执行验收，不提交实际业务数据。


---

## 🧭 知识库导航

- 主题索引：[[docs/database/00-数据库知识地图|数据库知识地图]] · [Markdown](../00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../../../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](../../00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 数据流：[[docs/DATABASE_MAPPING|字段映射]] · [Markdown](../../DATABASE_MAPPING.md) · [[docs/API_CONTRACT|API 契约]] · [Markdown](../../API_CONTRACT.md)
