/*
StockCount — 单表全物理列中文别名 + 当前 Schema 本地菜单 CASE（只读）
SQL Server；默认 XTEST；仅限七表。无字典标题保留物理字段名。
先输出菜单映射及生成 SQL，再执行 TOP 抽样；不修改业务库。
*/
USE [sagex3];
SET NOCOUNT ON;
DECLARE @SchemaName sysname = N'XTEST'; -- COB 正式 / XTEST 测试
DECLARE @TableName sysname = N'ITMMASTER'; -- 七表之一
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
