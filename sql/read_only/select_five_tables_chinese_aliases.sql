/*
StockCount — 五张表“全部真实物理列 + X3 中文标题”抽样查询（只读）
输出 5 组 TOP(@RowLimit) 业务数据，字段标题形如 [产品（ITMREF_0）]，
从 sys.columns 读取实际列，再根据选定 Schema 的 ATABZON/ATEXTE 加中文别名。
默认仅抽样 20 行，不应在生产执行无上限 SELECT *。
默认 XTEST，切换正式只能手工设 COB；本脚本不会修改数据。
*/
USE [sagex3];
SET NOCOUNT ON;

DECLARE @SchemaName sysname = N'XTEST';   -- 仅 XTEST / COB
DECLARE @RowLimit int = 20;               -- 1..100
DECLARE @CountNumber nvarchar(30) = NULL; -- 可填写现有盘点单号
DECLARE @BoxPid nvarchar(50) = NULL;      -- 标签 PID
DECLARE @FullQR nvarchar(200) = NULL;     -- 完整二维码
DECLARE @ItemCode nvarchar(20) = NULL;    -- 物料号
DECLARE @SiteCode nvarchar(5) = NULL;     -- 站点
DECLARE @WarehouseCode nvarchar(5) = NULL; -- 仓库
DECLARE @LocationCode nvarchar(20) = NULL; -- 库位

IF @SchemaName NOT IN (N'COB', N'XTEST')
    THROW 51011, N'仅支持 COB (正式) 或 XTEST (测试) 的只读查询', 1;
IF @RowLimit IS NULL OR @RowLimit < 1 OR @RowLimit > 100
    THROW 51012, N'@RowLimit 必须在 1 至 100 之间', 1;

DECLARE @Tables TABLE (No int PRIMARY KEY, TableName sysname NOT NULL);
INSERT INTO @Tables (No, TableName)
VALUES (1, N'XCOUTH'), (2, N'XCOUTD'), (3, N'XLABELDTL'),
       (4, N'STOLOC'), (5, N'ITMMASTER');

DECLARE @n int = 1, @table sysname, @objectId int;
DECLARE @columns nvarchar(max), @metaSql nvarchar(max),
        @readSql nvarchar(max), @whereSql nvarchar(max), @orderSql nvarchar(max);

WHILE @n <= 5
BEGIN
    SELECT @table = TableName FROM @Tables WHERE No = @n;
    SET @objectId = OBJECT_ID(QUOTENAME(@SchemaName)+N'.'+QUOTENAME(@table), N'U');
    IF @objectId IS NULL
    BEGIN
        PRINT N'未找到 '+@SchemaName+N'.'+@table+N'，跳过该表';
        SET @n += 1;
        CONTINUE;
    END;

    -- sys.columns 是物理字段真源，中文标题从当前 Schema 的 X3 字典动态获取。
    SET @metaSql = N'
    SELECT @ColsOut = STUFF((
      SELECT N'','' + CHAR(13) + CHAR(10)
             + N''    T.'' + QUOTENAME(C.name)
             + N'' AS '' + QUOTENAME(LEFT(
                 COALESCE(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(4000), TX.TEXTE_0))), N''''),
                          N''无字典中文标题'')
                 + N''（'' + C.name + N''）'', 128))
      FROM sys.columns AS C
      CROSS APPLY (
          SELECT PATINDEX(N''%[^0-9]%'', REVERSE(C.name)) AS P
      ) AS S
      CROSS APPLY (
          SELECT CASE WHEN S.P > 1
                    AND SUBSTRING(C.name, LEN(C.name)-S.P+1, 1)=N''_''
                 THEN LEFT(C.name, LEN(C.name)-S.P)
                 ELSE C.name END AS X3Code
      ) AS B
      OUTER APPLY (
          SELECT TOP (1) ZZ.NOCOURT_0
          FROM ' + QUOTENAME(@SchemaName) + N'.[ATABZON] AS ZZ
          WHERE RTRIM(ZZ.CODFIC_0) = @TableName
            AND RTRIM(ZZ.CODZONE_0) = B.X3Code
          ORDER BY ZZ.CODZONE_0
      ) AS Z
      LEFT JOIN ' + QUOTENAME(@SchemaName) + N'.[ATEXTE] AS TX
        ON TX.NUMERO_0=Z.NOCOURT_0 AND TX.LAN_0=N''CHI''
      WHERE C.object_id=@ObjectId
      ORDER BY C.column_id
      FOR XML PATH(''''), TYPE
    ).value(''.'', ''nvarchar(max)''), 1, 1, N'''');';

    SET @columns=NULL;
    EXEC sys.sp_executesql @metaSql,
         N'@ObjectId int, @TableName sysname, @ColsOut nvarchar(max) OUTPUT',
         @ObjectId=@objectId, @TableName=@table, @ColsOut=@columns OUTPUT;

    IF NULLIF(@columns,N'') IS NULL
    BEGIN
        PRINT N'未能生成 '+@table+N' 的真实字段列表，跳过';
        SET @n += 1;
        CONTINUE;
    END;

    SET @whereSql=N' WHERE 1=1';
    SET @orderSql=N'';

    IF @table IN (N'XCOUTH', N'XCOUTD') AND @CountNumber IS NOT NULL
        SET @whereSql += N' AND T.[CUNNUM_0]=@CountNumber';

    IF @table=N'XLABELDTL'
    BEGIN
        IF @BoxPid IS NOT NULL SET @whereSql += N' AND T.[PID_0]=@BoxPid';
        IF @FullQR IS NOT NULL SET @whereSql += N' AND T.[QRCOD_0]=@FullQR';
    END;

    IF @table=N'ITMMASTER' AND @ItemCode IS NOT NULL
        SET @whereSql += N' AND T.[ITMREF_0]=@ItemCode';

    IF @table IN (N'STOLOC',N'XCOUTH')
    BEGIN
        IF @SiteCode IS NOT NULL SET @whereSql += N' AND T.[STOFCY_0]=@SiteCode';
        IF @WarehouseCode IS NOT NULL SET @whereSql += N' AND T.[WRH_0]=@WarehouseCode';
    END;

    IF @table=N'STOLOC' AND @LocationCode IS NOT NULL
        SET @whereSql += N' AND T.[LOC_0]=@LocationCode';

    IF @table=N'XCOUTH'
        SET @orderSql=N' ORDER BY T.[CUNDAT_0] DESC, T.[CUNNUM_0] DESC';
    IF @table=N'XCOUTD'
        SET @orderSql=N' ORDER BY T.[CREDATTIM_0] DESC';
    IF @table=N'STOLOC'
        SET @orderSql=N' ORDER BY T.[STOFCY_0], T.[WRH_0], T.[LOC_0]';

    SET @readSql =
        N'SELECT TOP (@Take)' + CHAR(13) + CHAR(10)
        + @columns + CHAR(13) + CHAR(10)
        + N'FROM '+QUOTENAME(@SchemaName)+N'.'+QUOTENAME(@table)+N' AS T'
        + @whereSql + @orderSql + N';';

    PRINT N'===== '+@SchemaName+N'.'+@table+N'：真实全字段 + 中文别名 =====';

    EXEC sys.sp_executesql @readSql,
         N'@Take int, @CountNumber nvarchar(30), @BoxPid nvarchar(50),
           @FullQR nvarchar(200), @ItemCode nvarchar(20), @SiteCode nvarchar(5),
           @WarehouseCode nvarchar(5), @LocationCode nvarchar(20)',
         @Take=@RowLimit, @CountNumber=@CountNumber, @BoxPid=@BoxPid,
         @FullQR=@FullQR, @ItemCode=@ItemCode, @SiteCode=@SiteCode,
         @WarehouseCode=@WarehouseCode, @LocationCode=@LocationCode;

    SET @n += 1;
END;
