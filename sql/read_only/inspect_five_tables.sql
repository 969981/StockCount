/*
StockCount | 5 张表完整物理字段 + Sage X3 中文字典的只读查询
表：XCOUTH、XCOUTD、XLABELDTL、STOLOC、ITMMASTER
Schema：COB(正式)、XTEST(测试)
执行方式：在 SQL Server 查询窗口运行，全程只读。
证据区别：sys.columns 是实际物理列；ATABZON/ATEXTE 是 X3 字典标题；
            扩展属性 MS_Description 可能为空，且 X3 字典代码不一定是物理列。
不读取业务数据，不使用 INSERT / UPDATE / DELETE / DDL。
*/
USE [sagex3];
SET NOCOUNT ON;

DECLARE @Schemas TABLE (Seq int PRIMARY KEY, SchemaName sysname NOT NULL);
INSERT INTO @Schemas (Seq, SchemaName)
VALUES (1, N'COB'), (2, N'XTEST');

DECLARE @i int = 1, @schema sysname, @sql nvarchar(max);

WHILE @i <= 2
BEGIN
    SELECT @schema = SchemaName FROM @Schemas WHERE Seq = @i;
    IF @schema NOT IN (N'COB', N'XTEST')
        THROW 51001, N'仅允许受控的 COB / XTEST Schema。', 1;

    -- Schema 仅来自本文件中固定白名单，不能来自客户端。
    SET @sql = N'
    SELECT
        @SourceSchema AS [Schema名称],
        TB.name AS [表名],
        C.column_id AS [字段序号],
        C.name AS [SQL物理字段],
        B.X3Code AS [X3字段代码],
        TY.name AS [SQL数据类型],
        CASE
          WHEN TY.name IN (N''nvarchar'',N''nchar'')
            THEN CASE WHEN C.max_length = -1 THEN N''MAX''
                      ELSE CONVERT(nvarchar(20), C.max_length / 2) END
          WHEN TY.name IN (N''varchar'',N''char'',N''varbinary'',N''binary'')
            THEN CASE WHEN C.max_length = -1 THEN N''MAX''
                      ELSE CONVERT(nvarchar(20), C.max_length) END
          ELSE NULL END AS [字符或二进制长度],
        C.max_length AS [SQL存储字节],
        C.precision AS [数字精度],
        C.scale AS [小数位],
        C.is_nullable AS [允许NULL_1是],
        C.is_identity AS [自增_1是],
        C.is_computed AS [计算列_1是],
        TN.TEXTE_0 AS [X3中文普通标题],
        TL.TEXTE_0 AS [X3中文长标题],
        TS.TEXTE_0 AS [X3中文短标题],
        Z.CODTYP_0 AS [X3逻辑类型],
        Z.NOLIB_0 AS [本地菜单号],
        Z.LONG_0 AS [X3定义长度],
        Z.DIME_0 AS [X3维度],
        Z.LIEN_0 AS [X3关联表],
        TRY_CONVERT(nvarchar(4000), EP.value) AS [SQLServer_MS_Description],
        CASE
          WHEN Z.CODZONE_0 IS NULL THEN N''物理列没有匹配X3字段字典''
          WHEN NULLIF(LTRIM(RTRIM(TRY_CONVERT(nvarchar(4000),TN.TEXTE_0))),N'''') IS NULL
            THEN N''匹配X3字段但无中文普通标题''
          ELSE N''有X3中文标题'' END AS [中文标题证据状态]
    FROM sys.tables AS TB
    JOIN sys.schemas AS SC ON SC.schema_id = TB.schema_id
    JOIN sys.columns AS C ON C.object_id = TB.object_id
    JOIN sys.types AS TY ON TY.user_type_id = C.user_type_id
    CROSS APPLY (
        SELECT PATINDEX(N''%[^0-9]%'', REVERSE(C.name)) AS P
    ) AS X
    CROSS APPLY (
        SELECT CASE
           WHEN X.P > 1
            AND SUBSTRING(C.name, LEN(C.name) - X.P + 1, 1) = N''_''
           THEN LEFT(C.name, LEN(C.name) - X.P)
           ELSE C.name END AS X3Code
    ) AS B
    OUTER APPLY (
        SELECT TOP (1) ZZ.*
        FROM ' + QUOTENAME(@schema) + N'.[ATABZON] AS ZZ
        WHERE RTRIM(ZZ.CODFIC_0) = TB.name
          AND RTRIM(ZZ.CODZONE_0) = B.X3Code
        ORDER BY ZZ.CODZONE_0
    ) AS Z
    LEFT JOIN ' + QUOTENAME(@schema) + N'.[ATEXTE] AS TN
       ON TN.NUMERO_0 = Z.NOCOURT_0 AND TN.LAN_0 = N''CHI''
    LEFT JOIN ' + QUOTENAME(@schema) + N'.[ATEXTE] AS TS
       ON TS.NUMERO_0 = Z.NOABREG_0 AND TS.LAN_0 = N''CHI''
    LEFT JOIN ' + QUOTENAME(@schema) + N'.[ATEXTE] AS TL
       ON TL.NUMERO_0 = Z.NOLONG_0 AND TL.LAN_0 = N''CHI''
    LEFT JOIN sys.extended_properties AS EP
       ON EP.major_id = C.object_id AND EP.minor_id = C.column_id
      AND EP.class = 1 AND EP.name = N''MS_Description''
    WHERE SC.name = @SourceSchema
      AND TB.name IN (N''XCOUTH'',N''XCOUTD'',N''XLABELDTL'',N''STOLOC'',N''ITMMASTER'')
    ORDER BY CASE TB.name
        WHEN N''XCOUTH'' THEN 1 WHEN N''XCOUTD'' THEN 2
        WHEN N''XLABELDTL'' THEN 3 WHEN N''STOLOC'' THEN 4 ELSE 5 END,
        C.column_id;
    ';
    -- 已依据已保存的 2026-11-19 SQL05 证据确认：NOABREG_0 为短标题文本号，
    -- NOLIB_0 是本地菜单号，绝不能误用于 ATEXTE 的短标题关联。
    PRINT N'===== '+@schema+N'：5 张表实际物理字段与中文标题 =====';
    EXEC sys.sp_executesql @sql, N'@SourceSchema sysname', @SourceSchema=@schema;
    SET @i += 1;
END;

/*
补充结构核验：索引、唯一约束、默认值、外键、触发器与检查约束。
下面所有语句都是 SELECT，仍只读取系统元数据。
*/
-- 2. 主键/唯一索引/普通索引及所包含的字段；一行表示索引中的一个字段
SELECT
    S.name AS [Schema], T.name AS [表名],
    I.name AS [索引名称],
    I.type_desc AS [索引类型],
    I.is_primary_key AS [主键_1是],
    I.is_unique AS [唯一_1是],
    I.is_unique_constraint AS [唯一约束_1是],
    I.is_disabled AS [禁用_1是],
    I.filter_definition AS [过滤条件],
    IC.key_ordinal AS [键顺序],
    IC.is_included_column AS [Include列_1是],
    C.name AS [字段名]
FROM sys.tables AS T
JOIN sys.schemas AS S ON S.schema_id=T.schema_id
JOIN sys.indexes AS I ON I.object_id=T.object_id AND I.index_id>0
LEFT JOIN sys.index_columns AS IC ON IC.object_id=I.object_id AND IC.index_id=I.index_id
LEFT JOIN sys.columns AS C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
WHERE S.name IN (N'COB',N'XTEST')
  AND T.name IN (N'XCOUTH',N'XCOUTD',N'XLABELDTL',N'STOLOC',N'ITMMASTER')
ORDER BY S.name,T.name,I.name,IC.key_ordinal,IC.index_column_id;

-- 3. 默认值与计算字段定义；不代表 X3 业务服务的实际赋值规则
SELECT
    S.name AS [Schema],T.name AS [表名],C.name AS [字段],
    D.name AS [默认约束名],D.definition AS [默认值定义],
    CC.definition AS [计算列定义]
FROM sys.tables AS T
JOIN sys.schemas AS S ON S.schema_id=T.schema_id
JOIN sys.columns AS C ON C.object_id=T.object_id
LEFT JOIN sys.default_constraints AS D ON D.object_id=C.default_object_id
LEFT JOIN sys.computed_columns AS CC ON CC.object_id=C.object_id AND CC.column_id=C.column_id
WHERE S.name IN (N'COB',N'XTEST')
  AND T.name IN (N'XCOUTH',N'XCOUTD',N'XLABELDTL',N'STOLOC',N'ITMMASTER')
  AND (D.object_id IS NOT NULL OR CC.column_id IS NOT NULL)
ORDER BY S.name,T.name,C.column_id;

-- 4. 物理外键（逻辑业务关联不一定在 DB 中有外键）
SELECT
    PS.name AS [来源Schema],PT.name AS [来源表],
    FK.name AS [外键名],PC.name AS [来源字段],
    RS.name AS [目标Schema],RT.name AS [目标表],RC.name AS [目标字段],
    FK.is_disabled AS [禁用_1是]
FROM sys.foreign_key_columns AS FKC
JOIN sys.foreign_keys AS FK ON FK.object_id=FKC.constraint_object_id
JOIN sys.tables AS PT ON PT.object_id=FKC.parent_object_id
JOIN sys.schemas AS PS ON PS.schema_id=PT.schema_id
JOIN sys.columns AS PC ON PC.object_id=PT.object_id AND PC.column_id=FKC.parent_column_id
JOIN sys.tables AS RT ON RT.object_id=FKC.referenced_object_id
JOIN sys.schemas AS RS ON RS.schema_id=RT.schema_id
JOIN sys.columns AS RC ON RC.object_id=RT.object_id AND RC.column_id=FKC.referenced_column_id
WHERE PS.name IN (N'COB',N'XTEST')
  AND PT.name IN (N'XCOUTH',N'XCOUTD',N'XLABELDTL',N'STOLOC',N'ITMMASTER')
ORDER BY PS.name,PT.name,FK.name,FKC.constraint_column_id;

-- 5. 触发器、检查约束：仅返回元数据/定义，不执行触发器
SELECT S.name AS [Schema],T.name AS [表名],
       TR.name AS [触发器],TR.is_disabled AS [禁用_1是],
       TR.is_instead_of_trigger AS [替代触发器_1是]
FROM sys.triggers AS TR
JOIN sys.tables AS T ON T.object_id=TR.parent_id
JOIN sys.schemas AS S ON S.schema_id=T.schema_id
WHERE S.name IN (N'COB',N'XTEST')
  AND T.name IN (N'XCOUTH',N'XCOUTD',N'XLABELDTL',N'STOLOC',N'ITMMASTER')
ORDER BY S.name,T.name,TR.name;

SELECT S.name AS [Schema],T.name AS [表名],CK.name AS [检查约束名],
       CK.definition AS [检查条件],CK.is_disabled AS [禁用_1是]
FROM sys.check_constraints AS CK
JOIN sys.tables AS T ON T.object_id=CK.parent_object_id
JOIN sys.schemas AS S ON S.schema_id=T.schema_id
WHERE S.name IN (N'COB',N'XTEST')
  AND T.name IN (N'XCOUTH',N'XCOUTD',N'XLABELDTL',N'STOLOC',N'ITMMASTER')
ORDER BY S.name,T.name,CK.name;
