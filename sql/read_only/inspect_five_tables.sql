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
    LEFT JOIN ' + QUOTENAME(@schema) + N'.[ATEXTE] AS TL
       ON TL.NUMERO_0 = Z.NOLONG_0 AND TL.LAN_0 = N''CHI''
    LEFT JOIN ' + QUOTENAME(@schema) + N'.[ATEXTE] AS TS
       ON TS.NUMERO_0 = Z.NOLIB_0 AND 1=0 -- 不将菜单号当作短标题文字编号
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
    -- ATABZON 的短标题应使用对应的短标题编号字段，且不同定制库可能不同；
    -- 不在无法确认实际列名时硬接菜单号。将其固定置空，普通/长标题仍可完整返回。
    SET @sql = REPLACE(@sql,
      N'TS.TEXTE_0 AS [X3中文短标题]', N'CAST(NULL AS nvarchar(4000)) AS [X3中文短标题_源字段待核]');
    SET @sql = REPLACE(@sql,
      N'    LEFT JOIN ' + QUOTENAME(@schema) + N'.[ATEXTE] AS TS
       ON TS.NUMERO_0 = Z.NOLIB_0 AND 1=0 -- 不将菜单号当作短标题文字编号
', N'');

    PRINT N'===== '+@schema+N'：5 张表实际物理字段与中文标题 =====';
    EXEC sys.sp_executesql @sql, N'@SourceSchema sysname', @SourceSchema=@schema;
    SET @i += 1;
END;
