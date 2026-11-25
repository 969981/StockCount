---
title: "ATABZON — Sage X3 字段定义字典"
aliases:
  - "StockCount · docs / database / schema / ATABZON"
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

# ATABZON — Sage X3 字段定义字典

> **证据状态：已核实用途和下列查询涉及的列名；未取得 `ATABZON` 自身全部 SQL Server 物理列的现场输出**。此文档不是“已完成物理表结构实测”的声明。根据已保存的 COB/XTEST `ATABZON + ATEXTE` 中文标题查询、Sage X3 字典导出脚本整理。2026-11-23。

## 一、表用途

`ATABZON` 保存 X3 表/字段的**逻辑元数据**，不保存具体盘点数量或标签库存。常用关系：

```text
ATABZON.CODFIC_0   → X3 表代码（例如 XCOUTH、XCOUTD、STOLOC）
ATABZON.CODZONE_0  → X3 字段代码（例如 QTYSTU、PID；一般不含物理后缀 _0）
ATABZON.NOCOURT_0  → ATEXTE.NUMERO_0（普通标题）
ATABZON.NOABREG_0  → ATEXTE.NUMERO_0（短标题）
ATABZON.NOLONG_0   → ATEXTE.NUMERO_0（长标题）
ATABZON.NOLIB_0    → APLSTD.LANCHP_0（本地菜单；不是字段标题）
ATEXTE.LAN_0='CHI' → 中文翻译；'ENG' → 英文翻译
```

**注意**：标准字典定义不等于 SQL 物理表字段、数据库索引或 HTTP DTO 必填属性；同名字段可能有多个物理下标 `_0` / `_1`。

## 二、已在历史只读 SQL 使用过的关键字段

**下表为已知字段代码与功能解释，不代表全列数，不提供未经实测的 SQL 数据类型/长度、可空性或真实中文标题。** 真正完整物理结构见 [元数据查询](../../../sql/read_only/inspect_dictionary_tables.sql)，待用户回传实际结果后补齐逐列类型与官方中文标题。

| SQL 物理字段（既有查询中出现） | 功能说明（业务解释，非实测官方中文标题） | 证据 |
| --- | --- | --- |
| `CODFIC_0` | X3 业务表代码 | SQL05 字段标题提取 |
| `CODZONE_0` | 字段代码（通常剥除 `_0` 后的基础名） | SQL05 字段标题提取 |
| `CODTYP_0` | X3 逻辑字段类型 | SQL05 |
| `NOCOURT_0` | 普通标题的文本编号 | SQL05 |
| `NOABREG_0` | 短标题的文本编号 | SQL05 |
| `NOLONG_0` | 长标题的文本编号 | SQL05 |
| `NOLIB_0` | 本地菜单章节号；枚举文字需另外查 APLSTD | SQL05 |
| `LONG_0` | X3 定义长度；不等于 SQL Server 字节长度 | XTEST.STOLOC 中文对照 |
| `DIME_0` | X3 字段维度 | XTEST.STOLOC 中文对照 |
| `LIEN_0` | X3 关联表代码 | XTEST.STOLOC 中文对照 |
| `OBLIG_0` | X3 字段链接/强制关联类标识；不能直接理解为 API 必填 | 字典导出脚本 |
| `CODACT_0` | X3 活动代码 | 字典导出脚本 |

**本表真实列数、其余列、SQL 物理类型、精度/长度/NULL 属性尚未验证，不能编造。**

## 三、与 StockCount 开发的关系

- 后端取 `XCOUTH`、`XCOUTD`、`XLABELDTL`、`STOLOC`、`ITMMASTER` 等字段中文标题时，按当前帐套对应 Schema 的 `ATABZON` 关联 `ATEXTE`。
- `CODFIC_0 + CODZONE_0` 是**逻辑字典匹配条件**；并非证实 SQL Server 存在此组合的唯一索引。
- `CUNSTA_0` 的状态描述不能单凭 `NOCOURT_0` 推断，菜单需另读 `APLSTD`；且 StockCount V1 **所有盘点单可浏览**。
- 标签 `STA_0` 的码值是否允许盘点**不受字典枚举限制**：StockCount V1 已取消状态白名单。
- 客户端不用直接调用这张表；仅后端/开发时的元数据核查读取。

## 四、配套只读 SQL

- [COB/XTEST 的 ATABZON 与 ATEXTE 全部物理列、数据类型、长度、精度、可空、索引检查](../../../sql/read_only/inspect_dictionary_tables.sql)
- [按业务表获取 X3 字段代码、短/普通/长中文标题、英文标题的字典关联查询](../../../sql/read_only/query_dictionary_tables.sql)
- [原五张业务表的完整结构](../SCHEMA_INDEX.md)

所有 SQL 仅限只读；不要将企业字典的原始大量文本/全部语种内容提交公开仓库。

## 业务数据查询 SQL（中文别名）

2026-11-25 补充。以下均为只读，默认抽样 TOP (20)，字段使用 `中文业务别名（物理字段名）`；说明性别名不等于 X3 官方字典标题。原始码值和 CASE 翻译并列，转换不代表业务准入。切换 Schema 前核实真实列和菜单。

### 1. 常用业务字段

```sql
SELECT TOP (20)
    T.CODFIC_0 AS [业务表代码（CODFIC_0）],
    T.CODZONE_0 AS [字段代码（CODZONE_0）],
    T.CODTYP_0 AS [X3逻辑类型（CODTYP_0）],
    T.NOCOURT_0 AS [普通标题编号（NOCOURT_0）],
    T.NOABREG_0 AS [短标题编号（NOABREG_0）],
    T.NOLONG_0 AS [长标题编号（NOLONG_0）],
    T.NOLIB_0 AS [本地菜单编号（NOLIB_0）],
    T.LONG_0 AS [X3字段长度（LONG_0）],
    T.DIME_0 AS [维度（DIME_0）],
    T.LIEN_0 AS [关联表（LIEN_0）],
    T.OBLIG_0 AS [链接标识（OBLIG_0）],
    T.CODACT_0 AS [活动代码（CODACT_0）],
    CASE WHEN T.CODTYP_0=N'M' AND T.NOLIB_0>0 THEN N'本地菜单字段' ELSE N'其他逻辑类型或无菜单' END AS [菜单字段识别]
FROM sagex3.XTEST.ATABZON AS T
WHERE T.CODFIC_0 = N'XCOUTH'
ORDER BY CODFIC_0, CODZONE_0;
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
DECLARE @TableName sysname = N'ATABZON'; -- 七表之一
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
WHERE Z.CODFIC_0 = N'ATABZON'
  AND Z.CODTYP_0 = N'M' AND Z.NOLIB_0 > 0
ORDER BY Z.CODZONE_0, Z.NOLIB_0, M.LANNUM_0, M.LAN_0;
```

全部菜单的动态 CASE（包括尚未手工核实的枚举）使用 [select_table_with_enum_aliases.sql](../../../sql/read_only/select_table_with_enum_aliases.sql)，将 `@TableName` 设为 `ATABZON`。脚本仅按现场非空且无冲突的中文字典生成 CASE，不使用菜单第0项；未知码值保留原值并标明未知。SQL 尚未在企业数据库执行验收，不提交实际业务数据。


---

## 🧭 知识库导航

- 主题索引：[[docs/database/00-数据库知识地图|数据库知识地图]] · [Markdown](../00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../../../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](../../00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 元数据与 SQL：[[docs/database/SCHEMA_INDEX|七表字典索引]] · [Markdown](../SCHEMA_INDEX.md) · [[docs/DATABASE_MAPPING|业务字段映射]] · [Markdown](../../DATABASE_MAPPING.md)
