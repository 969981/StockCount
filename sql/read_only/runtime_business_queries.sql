-- StockCount: current read-only business diagnostics, baseline 805ce006b2e365e28727fce49159f29e083c7c7e
-- Run in SSMS using a read-only account. Default XTEST. No ERP DML/DDL, no transaction locks.
-- Replace synthetic parameters locally. Never commit actual company data.
USE [sagex3];
SET NOCOUNT ON;
DECLARE @Schema sysname=N'XTEST';
DECLARE @CountNumber nvarchar(30)=N'COUNT-DEMO-001';
DECLARE @Search nvarchar(100)=NULL;
DECLARE @BoxNumber nvarchar(20)=N'BOX-DEMO-001';
DECLARE @FullQR nvarchar(200)=N'ID.BOX-DEMO-001@@QT.100';
DECLARE @Location nvarchar(10)=N'LOC-DEMO01';
DECLARE @BoxSearch nvarchar(20)=NULL;
DECLARE @HistoryLocation nvarchar(10)=NULL;
DECLARE @RequestId uniqueidentifier=NULL;
DECLARE @CheckSite bit=0, @CheckWarehouse bit=0;
DECLARE @Take int=51, @Skip int=0;
DECLARE @UpperRowId varchar(40)=NULL, @BeforeRowId varchar(40)=NULL;
-- First page: both cursor values NULL. Continuation: copy previous upper and last returned row ID.
IF @Schema IS NULL OR @Schema NOT IN (N'XTEST',N'COB')
    THROW 51030,N'仅允许 XTEST 或 COB',1;
IF @Take NOT BETWEEN 1 AND 101 OR @Skip<0
    THROW 51031,N'分页参数不合法',1;
IF (@UpperRowId IS NOT NULL AND TRY_CONVERT(numeric(38,0),@UpperRowId) IS NULL)
 OR (@BeforeRowId IS NOT NULL AND (@UpperRowId IS NULL OR TRY_CONVERT(numeric(38,0),@BeforeRowId) IS NULL))
    THROW 51032,N'游标不合法',1;
DECLARE @H nvarchar(300)=QUOTENAME(@Schema)+N'.[XCOUTH]',
        @D nvarchar(300)=QUOTENAME(@Schema)+N'.[XCOUTD]',
        @L nvarchar(300)=QUOTENAME(@Schema)+N'.[XLABELDTL]',
        @S nvarchar(300)=QUOTENAME(@Schema)+N'.[STOLOC]',
        @I nvarchar(300)=QUOTENAME(@Schema)+N'.[ITMMASTER]';
DECLARE @Sql nvarchar(max)=N'
-- Q01: connection
SELECT 1 AS [连接测试];
-- Q02: list (no status filter)
SELECT H.CUNNUM_0 AS [盘点单号],H.CUNDES_0 AS [名称],H.STOFCY_0 AS [站点],H.WRH_0 AS [仓库],
       H.CUNDAT_0 AS [盘点日期],H.CUNSTA_0 AS [原始状态码],H.CUNTYP_0 AS [原始类型码]
FROM '+@H+N' H
WHERE @Search IS NULL OR CHARINDEX(@Search,H.CUNNUM_0)>0 OR CHARINDEX(@Search,H.CUNDES_0)>0
ORDER BY H.CUNDAT_0 DESC,H.CUNNUM_0 DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
-- Q03: header and context
SELECT H.CUNNUM_0 AS [盘点单号],H.CUNDES_0 AS [名称],H.STOFCY_0 AS [站点],H.WRH_0 AS [仓库],
       H.CUNDAT_0 AS [盘点日期],H.CUNSTA_0 AS [原始状态码],H.CUNTYP_0 AS [原始类型码]
FROM '+@H+N' H WHERE H.CUNNUM_0=@CountNumber;
DECLARE @Site nvarchar(5),@Warehouse nvarchar(5);
SELECT @Site=H.STOFCY_0,@Warehouse=H.WRH_0 FROM '+@H+N' H WHERE H.CUNNUM_0=@CountNumber;
-- Q04: scope
SELECT COUNT_BIG(*) AS [库位全部匹配数],
 COUNT_BIG(CASE WHEN (@CheckSite=0 OR S.STOFCY_0=@Site) AND (@CheckWarehouse=0 OR S.WRH_0=@Warehouse) THEN 1 END) AS [范围匹配数],
 MIN(CASE WHEN (@CheckSite=0 OR S.STOFCY_0=@Site) AND (@CheckWarehouse=0 OR S.WRH_0=@Warehouse) THEN S.LOC_0 END) AS [匹配库位]
FROM '+@S+N' S WHERE S.LOC_0=@Location;
-- Q05: exact QR then diagnostic PID (separate inspection, not automatic fallback)
SELECT TOP(2) L.PID_0 AS [箱码],L.ITMREF_0 AS [物料],I.YITMDES4_0 AS [品名],I.ITMDES2_0 AS [规格],
 L.LOT_0 AS [批号],L.STU_0 AS [存货单位],L.QTY_0 AS [标签数量_参考],L.STA_0 AS [原始状态码],
 CAST(CASE WHEN EXISTS(SELECT 1 FROM '+@D+N' D WHERE D.BOXNUM_0=L.PID_0) THEN 1 ELSE 0 END AS bit) AS [本帐套已盘]
FROM '+@L+N' L LEFT JOIN '+@I+N' I ON I.ITMREF_0=L.ITMREF_0 WHERE L.QRCOD_0=@FullQR;
SELECT TOP(2) L.PID_0 AS [箱码],L.ITMREF_0 AS [物料],I.YITMDES4_0 AS [品名],I.ITMDES2_0 AS [规格],
 L.LOT_0 AS [批号],L.STU_0 AS [存货单位],L.QTY_0 AS [标签数量_参考],L.STA_0 AS [原始状态码],
 CAST(CASE WHEN EXISTS(SELECT 1 FROM '+@D+N' D WHERE D.BOXNUM_0=L.PID_0) THEN 1 ELSE 0 END AS bit) AS [本帐套已盘]
FROM '+@L+N' L LEFT JOIN '+@I+N' I ON I.ITMREF_0=L.ITMREF_0 WHERE L.PID_0=@BoxNumber;
-- W05 diagnostic: no lock, shows cross-count duplicate rows
SELECT TOP(@Take) CONVERT(varchar(40),D.ROWID) AS [行标识],D.CUNNUM_0 AS [盘点单号],
 D.BOXNUM_0 AS [箱码],D.LOC_0 AS [实盘库位],D.QTYSTU_0 AS [实盘数],D.CREDATTIM_0 AS [创建时间]
FROM '+@D+N' D WHERE D.BOXNUM_0=@BoxNumber ORDER BY D.ROWID DESC;
-- Q06: legacy OFFSET
SELECT CONVERT(varchar(40),D.ROWID) AS [行标识],D.CUNNUM_0 AS [盘点单号],D.BOXNUM_0 AS [箱码],
 D.LOC_0 AS [库位],D.ITMREF_0 AS [物料],D.LOT_0 AS [批号],D.PCU_0 AS [单位],
 D.QTYPCU_0 AS [包装数量],D.QTYSTU_0 AS [存货数量],D.CREDATTIM_0 AS [创建时间]
FROM '+@D+N' D WHERE D.CUNNUM_0=@CountNumber ORDER BY D.ROWID DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
-- Q07: actual counts, upper ROWID
IF @UpperRowId IS NULL
BEGIN
 SELECT @UpperRowId=CONVERT(varchar(40),MAX(D.ROWID)) FROM '+@D+N' D WHERE D.CUNNUM_0=@CountNumber;
 SELECT @UpperRowId AS [首页ROWID上界],COUNT_BIG(*) AS [总记录数],
 COUNT_BIG(CASE WHEN (@BoxSearch IS NULL OR CHARINDEX(@BoxSearch,D.BOXNUM_0)>0)
 AND (@HistoryLocation IS NULL OR D.LOC_0=@HistoryLocation) THEN 1 END) AS [筛选记录数]
 FROM '+@D+N' D WHERE D.CUNNUM_0=@CountNumber;
END;
-- Q08: keyset; diagnostic optional predicate instead of two separate runtime templates
SELECT TOP(@Take) CONVERT(varchar(40),D.ROWID) AS [行标识],D.CUNNUM_0 AS [盘点单号],
 D.BOXNUM_0 AS [箱码],D.LOC_0 AS [库位],D.ITMREF_0 AS [物料],D.LOT_0 AS [批号],
 D.PCU_0 AS [单位],D.QTYPCU_0 AS [包装数量],D.QTYSTU_0 AS [存货数量],D.CREDATTIM_0 AS [创建时间]
FROM '+@D+N' D WHERE D.CUNNUM_0=@CountNumber
 AND D.ROWID<=CONVERT(numeric(38,0),@UpperRowId)
 AND (@BeforeRowId IS NULL OR D.ROWID<CONVERT(numeric(38,0),@BeforeRowId))
 AND (@BoxSearch IS NULL OR CHARINDEX(@BoxSearch,D.BOXNUM_0)>0)
 AND (@HistoryLocation IS NULL OR D.LOC_0=@HistoryLocation) ORDER BY D.ROWID DESC;
-- Q09: original UUID, only when explicitly supplied
IF @RequestId IS NOT NULL
SELECT TOP(2) D.BOXNUM_0 AS [箱码],D.LOC_0 AS [库位],CONVERT(varchar(40),D.ROWID) AS [行标识],
 D.PCU_0 AS [单位],D.QTYSTU_0 AS [存货数量],D.QTYPCU_0 AS [包装数量]
FROM '+@D+N' D WHERE D.CUNNUM_0=@CountNumber AND D.AUUID_0=CONVERT(binary(16),@RequestId)
 AND D.PDAID_0=N''StockCount'';
-- W02: metadata only
SELECT C.name AS [字段],C.is_identity AS [是否自增] FROM sys.columns C
 WHERE C.object_id=OBJECT_ID(@DetailTable) AND C.name=N''ROWID'';
';
EXEC sys.sp_executesql @Sql,
 N'@CountNumber nvarchar(30),@Search nvarchar(100),@BoxNumber nvarchar(20),@FullQR nvarchar(200),@Location nvarchar(10),@BoxSearch nvarchar(20),@HistoryLocation nvarchar(10),@RequestId uniqueidentifier,@CheckSite bit,@CheckWarehouse bit,@Take int,@Skip int,@UpperRowId varchar(40),@BeforeRowId varchar(40),@DetailTable nvarchar(300)',
 @CountNumber=@CountNumber,@Search=@Search,@BoxNumber=@BoxNumber,@FullQR=@FullQR,@Location=@Location,
 @BoxSearch=@BoxSearch,@HistoryLocation=@HistoryLocation,@RequestId=@RequestId,@CheckSite=@CheckSite,
 @CheckWarehouse=@CheckWarehouse,@Take=@Take,@Skip=@Skip,@UpperRowId=@UpperRowId,@BeforeRowId=@BeforeRowId,@DetailTable=@D;
