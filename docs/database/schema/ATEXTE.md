---
title: "ATEXTE — Sage X3 多语言标题文本表"
aliases:
  - "StockCount · docs / database / schema / ATEXTE"
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

# ATEXTE — Sage X3 多语言标题文本表

> **证据状态：既有 SQL 已确认以下字段能参与字典连接，当前尚未取得 `ATEXTE` 自身全部 SQL Server 物理列元数据。** 不把已知的 3 个关键列误称为“全表只有3列”。2026-11-23。

## 一、表用途与关系

`ATEXTE` 是 Sage X3 的文本/语言翻译数据来源，字段的普通/短/长标题由 `ATABZON` 的文本编号指向不同 `ATEXTE.NUMERO_0` 记录；`CHI` 常用于中文、`ENG` 用于英文。

```text
ATABZON.NOCOURT_0   ─┐
ATABZON.NOABREG_0   ├─→ ATEXTE.NUMERO_0
ATABZON.NOLONG_0    ┘
                          + ATEXTE.LAN_0 = 'CHI' / 'ENG'
                          → ATEXTE.TEXTE_0 标题文字
```

**注意**：短标题用 `NOABREG_0`，不是 `NOLIB_0`。后者连接 `APLSTD` 本地菜单编号。只依靠 `NUMERO_0` 不足以选对语言；实际字段类型、索引与物理唯一约束请用元数据查询确认。

## 二、此前 SQL 已使用过的关键字段

| 已确认可被已有查询引用的字段 | 功能说明（不是官方中文标题的声称） | 使用条件 |
| --- | --- | --- |
| `NUMERO_0` | 标题/文本编号 | 与 `ATABZON.NOCOURT_0` / `NOABREG_0` / `NOLONG_0` 连接 |
| `LAN_0` | 语言代码 | `CHI`、`ENG`（视企业安装情况） |
| `TEXTE_0` | 语言文本 | 展示 `CHI` 的中文标题，`ENG` 的英文标题 |

**其它物理列、SQL Server 类型、长度、精度、小数位、可空与索引均待现场回传；不能根据上述三列反推出表总列数。**

## 三、业务使用规范

- StockCount 用它生成字段标题**仅供开发/诊断**；不要用标题文字作 JSON DTO 属性、SQL 列名或唯一 ID。
- 字典标题与 SQL Server 的 `MS_Description` 是不同来源。若中文标题为空、缺少语言行或字典中没有字段，保留真实物理列，并明确标记“无中文标题”。
- 物料业务“规格”对应 `ITMMASTER.ITMDES2_0` 的企业字典是“描述 2”；不能因为前端显示名称不同就覆盖原字典事实。
- 不把 `ATEXTE` 误作仓库业务数据表，更不能为了补充翻译而在生产库修改标题。
- 由于公共仓库安全约束，实例查询使用 `TOP` 和按指定业务表过滤，示例仅含合成值。

## 四、可执行只读 SQL

- [物理字段结构、长度与索引（COB、XTEST）](../../../sql/read_only/inspect_dictionary_tables.sql)
- [ATABZON → ATEXTE 中文/英文短普通长标题关联、少量样本查询](../../../sql/read_only/query_dictionary_tables.sql)
- [七表字段总索引](../SCHEMA_INDEX.md)

## 业务数据查询 SQL（中文别名）

2026-11-25 补充。以下均为只读，默认抽样 TOP (20)，字段使用 `中文业务别名（物理字段名）`；说明性别名不等于 X3 官方字典标题。原始码值和 CASE 翻译并列，转换不代表业务准入。切换 Schema 前核实真实列和菜单。

### 1. 常用业务字段

```sql
SELECT TOP (20)
    T.NUMERO_0 AS [文本编号（NUMERO_0）],
    T.LAN_0 AS [语言代码（LAN_0）],
    T.TEXTE_0 AS [文本内容（TEXTE_0）],
    CASE T.LAN_0 WHEN N'CHI' THEN N'中文' WHEN N'ENG' THEN N'英文' ELSE N'其他语言' END AS [语言名称]
FROM sagex3.XTEST.ATEXTE AS T
WHERE T.LAN_0 IN (N'CHI', N'ENG')
ORDER BY NUMERO_0, LAN_0;
```

### 2. 全部物理字段（动态 SQL）

此表完整物理列尚无现场回传，不能把逻辑字典或常用字段当成全列。以下直接读取 `sys.columns` 生成真实列的中文别名，并从对应 Schema 的 APLSTD 生成 CASE；缺中文标题保留字段名。脚本会输出实际生成的 SQL 和数据样本。

```sql
/*
StockCount — 单表全物理列中文别名 + 当前 Schema 本地菜单 CASE（只读）
SQL Server；默认 XTEST；仅限七表。无字典标题保留物理字段名。
先输出菜单映射及生成 SQL，再执行 TOP 抽样；不修改业务库。
*/
USE [sagex3];
SET NOCOUNT ON;
DECLARE @SchemaName sysname = N'XTEST'; -- COB 正式 / XTEST 测试
DECLARE @TableName sysname = N'ATEXTE'; -- 七表之一
DECLARE @RowLimit int = 20;
IF @SchemaName IS NULL OR @SchemaName NOT IN (N'COB',N'XTEST')
    THROW 51011, N'仅允许 COB 或 XTEST', 1;
IF @TableName IS NULL OR @TableName NOT IN
    (N'XCOUTH',N'XCOUTD',N'XLABELDTL',N'STOLOC',N'ITMMASTER',N'ATABZON',N'ATEXTE')
    THROW 51012, N'仅支持七张已归档的表', 1;
IF @RowLimit IS NULL OR @RowLimit NOT BETWEEN 1 AND 100
    THROW 51013, N'行数限 1..100', 1;
DECLARE @S nvarchar(258) = QUOTENAME(@SchemaName);
DECLARE @Obj int = OBJECT_ID(@S+N'.'+QUOTENAME(@TableName),N'U');
IF @Obj IS NULL THROW 51014, N'目标表不存在或无元数据权限', 1;

DECLARE @Fields TABLE (
    Ord int PRIMARY KEY, Col sysname, Caption nvarchar(4000), Menu int
);
DECLARE @Meta nvarchar(max) = N'
SELECT C.column_id, C.name,
    COALESCE(NULLIF(T.Caption,N''''), N''无字典中文标题''),
    CASE WHEN Z.MenuCount=1 THEN Z.Menu END
FROM sys.columns C
CROSS APPLY (SELECT PATINDEX(N''%[^0-9]%'', REVERSE(C.name)) AS P) P
CROSS APPLY (SELECT CASE WHEN P.P>1 AND
    SUBSTRING(C.name,LEN(C.name)-P.P+1,1)=N''_''
    THEN LEFT(C.name,LEN(C.name)-P.P) ELSE C.name END AS Code) B
OUTER APPLY (
    SELECT MIN(ZZ.NOCOURT_0) AS TitleId,
           MIN(CASE WHEN ZZ.CODTYP_0=N''M'' AND ZZ.NOLIB_0>0 THEN ZZ.NOLIB_0 END) AS Menu,
           COUNT(DISTINCT CASE WHEN ZZ.CODTYP_0=N''M'' AND ZZ.NOLIB_0>0 THEN ZZ.NOLIB_0 END) AS MenuCount
    FROM '+@S+N'.ATABZON ZZ
    WHERE ZZ.CODFIC_0=@TableName AND ZZ.CODZONE_0=B.Code
) Z
OUTER APPLY (
    SELECT MIN(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(4000),TX.TEXTE_0))),N'''')) AS Caption
    FROM '+@S+N'.ATEXTE TX
    WHERE TX.NUMERO_0=Z.TitleId AND TX.LAN_0=N''CHI''
) T
WHERE C.object_id=@Obj;';
INSERT INTO @Fields EXEC sys.sp_executesql @Meta,
    N'@Obj int,@TableName sysname',@Obj=@Obj,@TableName=@TableName;

-- 对缺少自身字典标题的常用系统/字典字段提供说明性别名，而非官方标题。
UPDATE @Fields SET Caption=CASE Col
    WHEN N'ROWID' THEN N'系统行标识'
    WHEN N'UPDTICK_0' THEN N'更新计数'
    WHEN N'CODFIC_0' THEN N'表代码'
    WHEN N'CODZONE_0' THEN N'字段代码'
    WHEN N'CODTYP_0' THEN N'逻辑类型'
    WHEN N'NOCOURT_0' THEN N'普通标题编号'
    WHEN N'NOABREG_0' THEN N'短标题编号'
    WHEN N'NOLONG_0' THEN N'长标题编号'
    WHEN N'NOLIB_0' THEN N'本地菜单编号'
    WHEN N'NUMERO_0' THEN N'文本编号'
    WHEN N'LAN_0' THEN N'语言代码'
    WHEN N'TEXTE_0' THEN N'文本内容'
    ELSE Caption END
WHERE Caption=N'无字典中文标题';

DECLARE @MenuRows TABLE (Menu int, Code int, Lang nvarchar(10), Meaning nvarchar(4000));
DECLARE @MenuSql nvarchar(max)=N'
SELECT LANCHP_0,LANNUM_0,LAN_0,
       NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(4000),LANMES_0))),N'''')
FROM '+@S+N'.APLSTD
WHERE LANNUM_0>0 AND LAN_0 IN (N''CHI'',N''ENG'')
AND LANCHP_0 IN (SELECT NOLIB_0 FROM '+@S+N'.ATABZON
    WHERE CODFIC_0=@TableName AND CODTYP_0=N''M'' AND NOLIB_0>0);';
INSERT INTO @MenuRows EXEC sys.sp_executesql @MenuSql,
    N'@TableName sysname',@TableName=@TableName;

SELECT F.Col AS [物理字段], F.Menu AS [本地菜单],
       M.Code AS [码值], M.Lang AS [语言], M.Meaning AS [菜单含义]
FROM @Fields F LEFT JOIN @MenuRows M ON M.Menu=F.Menu
WHERE F.Menu IS NOT NULL ORDER BY F.Ord,M.Code,M.Lang;

DECLARE @Cols nvarchar(max)=N'', @Ord int=0, @Col sysname,
        @Caption nvarchar(4000),@Menu int,@Cases nvarchar(max),@Alias nvarchar(258);
WHILE EXISTS (SELECT 1 FROM @Fields WHERE Ord>@Ord)
BEGIN
    SELECT TOP (1) @Ord=Ord,@Col=Col,@Caption=Caption,@Menu=Menu
    FROM @Fields WHERE Ord>@Ord ORDER BY Ord;
    -- 保留列名后缀，避免同一标题的多列混淆。
    SET @Alias=QUOTENAME(LEFT(@Caption,CASE WHEN LEN(@Col)<125 THEN 125-LEN(@Col) ELSE 0 END)+N'（'+LEFT(@Col,125)+N'）');
    SET @Cols+=CASE WHEN @Cols=N'' THEN N'' ELSE N','+CHAR(10) END+
        N'T.'+QUOTENAME(@Col)+N' AS '+@Alias;
    IF @Menu IS NOT NULL
    BEGIN
        -- 仅采用唯一、非空中文菜单文字；不使用第0项元信息，不猜测冲突值。
        SELECT @Cases=(
            SELECT N' WHEN '+CONVERT(nvarchar(20),M.Code)+N' THEN N'''+
                   REPLACE(M.Meaning,N'''',N'''''')+N''''
            FROM (
                SELECT Code,MIN(Meaning) AS Meaning
                FROM @MenuRows WHERE Menu=@Menu AND Lang=N'CHI' AND Meaning IS NOT NULL
                GROUP BY Code HAVING COUNT(DISTINCT Meaning)=1
            ) M ORDER BY M.Code FOR XML PATH(''),TYPE
        ).value('.','nvarchar(max)');
        SET @Cols+=N','+CHAR(10)+
            CASE WHEN NULLIF(@Cases,N'') IS NULL THEN N'CASE WHEN T.'+QUOTENAME(@Col)+N' IS NULL THEN N''空值'' ELSE N''未核实码值'' END'
                 ELSE N'CASE TRY_CONVERT(int,T.'+QUOTENAME(@Col)+N')'+@Cases+
                      N' ELSE N''未知或未命名码值'' END' END
            +N' AS '+QUOTENAME(LEFT(@Caption,CASE WHEN LEN(@Col)<120 THEN 120-LEN(@Col) ELSE 0 END)+N'含义（'+LEFT(@Col,120)+N'）');
    END;
END;
DECLARE @Sql nvarchar(max)=N'SELECT TOP (@Take) '+@Cols+
    N' FROM '+@S+N'.'+QUOTENAME(@TableName)+N' T;';
SELECT @Sql AS [生成的全字段中文别名及CASE查询];
EXEC sys.sp_executesql @Sql,N'@Take int',@Take=@RowLimit;

```

### 3. 码值字典与核验

菜单章节编号和标题文本编号是关联键，不是业务状态码。语言示例 CHI/ENG、逻辑类型 M 的解释来自已有字典用法；其他编码不强行猜测。

```sql
SELECT DISTINCT
    Z.CODZONE_0 AS [字段代码], Z.NOLIB_0 AS [本地菜单编号],
    M.LANNUM_0 AS [码值], M.LAN_0 AS [语言],
    M.LANMES_0 AS [状态含义]
FROM sagex3.XTEST.ATABZON Z
LEFT JOIN sagex3.XTEST.APLSTD M
    ON M.LANCHP_0 = Z.NOLIB_0
   AND M.LAN_0 IN (N'CHI', N'ENG') AND M.LANNUM_0 > 0
WHERE Z.CODFIC_0 = N'ATEXTE'
  AND Z.CODTYP_0 = N'M' AND Z.NOLIB_0 > 0
ORDER BY Z.CODZONE_0, Z.NOLIB_0, M.LANNUM_0, M.LAN_0;
```

全部菜单的动态 CASE（包括尚未手工核实的枚举）使用 [select_table_with_enum_aliases.sql](../../../sql/read_only/select_table_with_enum_aliases.sql)，将 `@TableName` 设为 `ATEXTE`。脚本仅按现场非空且无冲突的中文字典生成 CASE，不使用菜单第0项；未知码值保留原值并标明未知。SQL 尚未在企业数据库执行验收，不提交实际业务数据。


---

## 🧭 知识库导航

- 主题索引：[[docs/database/00-数据库知识地图|数据库知识地图]] · [Markdown](../00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../../../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](../../00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 元数据与 SQL：[[docs/database/SCHEMA_INDEX|七表字典索引]] · [Markdown](../SCHEMA_INDEX.md) · [[docs/DATABASE_MAPPING|业务字段映射]] · [Markdown](../../DATABASE_MAPPING.md)
