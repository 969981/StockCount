/*
StockCount — ATABZON + ATEXTE 完整物理字段/中文标题/约束自检。
Schema: COB(正式), XTEST(测试); 均位于 sagex3 数据库。
全程只读，仅 SELECT 系统元数据和 X3 字典，不修改企业数据库。
返回：
  1) 两个 Schema 的两个字典表完整 sys.columns + X3 中文普通/短/长字段标题
  2) 索引/主键/唯一索引/列覆盖
  3) 外键、默认值、触发器及 CHECK
字典自描述条目是否存在、是否有 CHI 翻译由实际数据决定；
在当前聊天中尚未收到这两张表自己的 sys.columns 回传。
*/
USE [sagex3];
SET NOCOUNT ON;

DECLARE @Schemas TABLE (Seq int PRIMARY KEY, Name sysname NOT NULL);
INSERT INTO @Schemas(Seq, Name) VALUES (1, N'COB'), (2, N'XTEST');
DECLARE @n int=1, @schema sysname, @sql nvarchar(max);

WHILE @n<=2
BEGIN
    SELECT @schema=Name FROM @Schemas WHERE Seq=@n;
    IF @schema NOT IN (N'COB',N'XTEST')
        THROW 51101, N'仅允许固定的 COB / XTEST', 1;
    IF OBJECT_ID(QUOTENAME(@schema)+N'.[ATABZON]',N'U') IS NULL
        OR OBJECT_ID(QUOTENAME(@schema)+N'.[ATEXTE]',N'U') IS NULL
        THROW 51102, N'所选 Schema 缺失 ATABZON 或 ATEXTE', 1;

    SET @sql=N'
    SELECT
        @SchemaName AS [环境Schema],
        T.name AS [表名],
        C.column_id AS [字段序号],
        C.name AS [实际物理字段],
        TY.name AS [SQL数据类型],
        CASE WHEN TY.name IN (N''nvarchar'',N''nchar'')
             THEN CASE WHEN C.max_length=-1 THEN N''MAX'' ELSE CONVERT(nvarchar(20),C.max_length/2) END
             WHEN TY.name IN (N''varchar'',N''char'',N''binary'',N''varbinary'')
             THEN CASE WHEN C.max_length=-1 THEN N''MAX'' ELSE CONVERT(nvarchar(20),C.max_length) END
             ELSE NULL END AS [字符或二进制容量],
        C.max_length AS [SQL存储字节],
        C.precision AS [精度],
        C.scale AS [小数位],
        C.is_nullable AS [允许NULL_1是],
        C.is_identity AS [Identity_1是],
        C.is_computed AS [计算列_1是],
        B.BaseCode AS [X3字典字段代码],
        TC.TEXTE_0 AS [企业中文普通标题],
        TS.TEXTE_0 AS [企业中文短标题],
        TL.TEXTE_0 AS [企业中文长标题],
        TE.TEXTE_0 AS [企业英文普通标题],
        Z.CODTYP_0 AS [X3逻辑类型],
        Z.NOLIB_0 AS [X3本地菜单编号],
        Z.LONG_0 AS [X3定义长度],
        Z.DIME_0 AS [X3维度],
        TRY_CONVERT(nvarchar(4000),EP.value) AS [MS_Description],
        CASE WHEN Z.CODZONE_0 IS NULL THEN N''无自描述字段定义''
             WHEN TC.TEXTE_0 IS NULL THEN N''无中文普通标题''
             ELSE N''中文已匹配'' END AS [匹配状态]
    FROM sys.tables AS T
    JOIN sys.schemas AS S ON S.schema_id=T.schema_id
    JOIN sys.columns AS C ON C.object_id=T.object_id
    JOIN sys.types AS TY ON TY.user_type_id=C.user_type_id
    CROSS APPLY (
        SELECT PATINDEX(N''%[^0-9]%'',REVERSE(C.name)) AS P
    ) AS X
    CROSS APPLY (
        SELECT CASE WHEN X.P>1
                     AND SUBSTRING(C.name,LEN(C.name)-X.P+1,1)=N''_''
                    THEN LEFT(C.name,LEN(C.name)-X.P)
                    ELSE C.name END AS BaseCode
    ) AS B
    OUTER APPLY (
        SELECT TOP(1) ZZ.*
        FROM '+QUOTENAME(@schema)+N'.[ATABZON] AS ZZ
        WHERE RTRIM(ZZ.CODFIC_0)=T.name
          AND RTRIM(ZZ.CODZONE_0)=B.BaseCode
        ORDER BY ZZ.CODZONE_0
    ) AS Z
    LEFT JOIN '+QUOTENAME(@schema)+N'.[ATEXTE] AS TC
      ON TC.NUMERO_0=Z.NOCOURT_0 AND TC.LAN_0=N''CHI''
    LEFT JOIN '+QUOTENAME(@schema)+N'.[ATEXTE] AS TS
      ON TS.NUMERO_0=Z.NOABREG_0 AND TS.LAN_0=N''CHI''
    LEFT JOIN '+QUOTENAME(@schema)+N'.[ATEXTE] AS TL
      ON TL.NUMERO_0=Z.NOLONG_0 AND TL.LAN_0=N''CHI''
    LEFT JOIN '+QUOTENAME(@schema)+N'.[ATEXTE] AS TE
      ON TE.NUMERO_0=Z.NOCOURT_0 AND TE.LAN_0=N''ENG''
    LEFT JOIN sys.extended_properties AS EP
      ON EP.major_id=C.object_id AND EP.minor_id=C.column_id
     AND EP.class=1 AND EP.name=N''MS_Description''
    WHERE S.name=@SchemaName AND T.name IN (N''ATABZON'',N''ATEXTE'')
    ORDER BY CASE T.name WHEN N''ATABZON'' THEN 1 ELSE 2 END,C.column_id;';
    PRINT N'==== '+@schema+N' 字典表全部物理列与多语标题 ====';
    EXEC sys.sp_executesql @sql,N'@SchemaName sysname',@SchemaName=@schema;
    SET @n=@n+1;
END;

-- 索引、主键、唯一性（结构为实际 sys.indexes，不能从“字段名像ID”推测）
SELECT S.name AS [Schema],T.name AS [表],I.name AS [索引名],
       I.type_desc AS [索引类型],I.is_primary_key AS [主键],
       I.is_unique AS [唯一索引],I.is_disabled AS [已禁用],
       I.filter_definition AS [过滤谓词],
       IC.key_ordinal AS [索引字段顺序],
       IC.is_included_column AS [Include列],C.name AS [字段]
FROM sys.tables AS T
JOIN sys.schemas AS S ON S.schema_id=T.schema_id
JOIN sys.indexes AS I ON I.object_id=T.object_id AND I.index_id>0
LEFT JOIN sys.index_columns AS IC ON IC.object_id=I.object_id AND IC.index_id=I.index_id
LEFT JOIN sys.columns AS C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
WHERE S.name IN (N'COB',N'XTEST') AND T.name IN (N'ATABZON',N'ATEXTE')
ORDER BY S.name,T.name,I.name,IC.key_ordinal,IC.index_column_id;

-- 默认值与计算列
SELECT S.name AS [Schema],T.name AS [表],C.name AS [列],
       D.name AS [默认约束],D.definition AS [默认表达式],
       CC.definition AS [计算列表达式]
FROM sys.tables AS T
JOIN sys.schemas AS S ON S.schema_id=T.schema_id
JOIN sys.columns AS C ON C.object_id=T.object_id
LEFT JOIN sys.default_constraints AS D ON D.object_id=C.default_object_id
LEFT JOIN sys.computed_columns AS CC ON CC.object_id=C.object_id AND CC.column_id=C.column_id
WHERE S.name IN (N'COB',N'XTEST')
  AND T.name IN (N'ATABZON',N'ATEXTE')
  AND (D.object_id IS NOT NULL OR CC.column_id IS NOT NULL)
ORDER BY S.name,T.name,C.column_id;

-- 外键（可能没有；0 行不代表业务上没有逻辑关联）
SELECT PS.name AS [来源Schema],PT.name AS [来源表],
       FK.name AS [外键],PC.name AS [来源字段],
       RS.name AS [目标Schema],RT.name AS [目标表],RC.name AS [目标字段]
FROM sys.foreign_key_columns AS FC
JOIN sys.foreign_keys AS FK ON FK.object_id=FC.constraint_object_id
JOIN sys.tables AS PT ON PT.object_id=FC.parent_object_id
JOIN sys.schemas AS PS ON PS.schema_id=PT.schema_id
JOIN sys.columns AS PC ON PC.object_id=PT.object_id AND PC.column_id=FC.parent_column_id
JOIN sys.tables AS RT ON RT.object_id=FC.referenced_object_id
JOIN sys.schemas AS RS ON RS.schema_id=RT.schema_id
JOIN sys.columns AS RC ON RC.object_id=RT.object_id AND RC.column_id=FC.referenced_column_id
WHERE PS.name IN (N'COB',N'XTEST') AND PT.name IN (N'ATABZON',N'ATEXTE')
ORDER BY PS.name,PT.name,FK.name,FC.constraint_column_id;

-- 触发器与 CHECK（仅看元信息）
SELECT S.name AS [Schema],T.name AS [表],TR.name AS [触发器],
       TR.is_disabled AS [已禁用],TR.is_instead_of_trigger AS [INSTEAD_OF]
FROM sys.triggers AS TR
JOIN sys.tables AS T ON T.object_id=TR.parent_id
JOIN sys.schemas AS S ON S.schema_id=T.schema_id
WHERE S.name IN (N'COB',N'XTEST') AND T.name IN (N'ATABZON',N'ATEXTE')
ORDER BY S.name,T.name,TR.name;

SELECT S.name AS [Schema],T.name AS [表],CK.name AS [CHECK约束],
       CK.definition AS [表达式],CK.is_disabled AS [已禁用]
FROM sys.check_constraints AS CK
JOIN sys.tables AS T ON T.object_id=CK.parent_object_id
JOIN sys.schemas AS S ON S.schema_id=T.schema_id
WHERE S.name IN (N'COB',N'XTEST') AND T.name IN (N'ATABZON',N'ATEXTE')
ORDER BY S.name,T.name,CK.name;
