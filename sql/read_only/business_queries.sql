/*
StockCount 五张表常用业务查询（只读，默认 XTEST）
XCOUTH/ XCOUTD / XLABELDTL / STOLOC / ITMMASTER。
支持固定白名单的 COB / XTEST；绝不从外部接收任意 Schema。
只查询数据，不创建、不关闭盘点单，不修改标签/库存。
当参数为 NULL 时，返回限制条数的样例；大表也使用 TOP 上限。
*/
USE [sagex3];
SET NOCOUNT ON;

DECLARE @Schema sysname = N'XTEST'; -- 可选择 N'COB' 正式，或 N'XTEST' 测试
DECLARE @Take int = 30;             -- 1..100，禁止无上限导出
DECLARE @CountNumber nvarchar(30) = NULL;  -- 填指定盘点单号时额外看明细
DECLARE @BoxPid nvarchar(50) = NULL;       -- 按箱标签 ID
DECLARE @FullQR nvarchar(200) = NULL;      -- 按完整二维码
DECLARE @ItemCode nvarchar(20) = NULL;     -- 按物料编码
DECLARE @SiteCode nvarchar(5) = NULL;      -- 按站点
DECLARE @WarehouseCode nvarchar(5) = NULL; -- 按仓库
DECLARE @LocationCode nvarchar(20) = NULL; -- 按库位

IF @Schema NOT IN (N'COB',N'XTEST')
    THROW 51020,N'只允许固定 COB / XTEST Schema',1;
IF @Take IS NULL OR @Take<1 OR @Take>100
    THROW 51021,N'@Take 只能在 1..100 范围',1;

DECLARE @H nvarchar(300)=QUOTENAME(@Schema)+N'.[XCOUTH]';
DECLARE @D nvarchar(300)=QUOTENAME(@Schema)+N'.[XCOUTD]';
DECLARE @L nvarchar(300)=QUOTENAME(@Schema)+N'.[XLABELDTL]';
DECLARE @S nvarchar(300)=QUOTENAME(@Schema)+N'.[STOLOC]';
DECLARE @I nvarchar(300)=QUOTENAME(@Schema)+N'.[ITMMASTER]';

DECLARE @Sql nvarchar(max)=N'
-- 1. 盘点单：不按 CUNSTA_0 过滤（V1 展示所有现有盘点单）
SELECT TOP (@Take)
  H.CUNNUM_0 AS [盘点单号],
  H.CUNDES_0 AS [盘点单名称],
  H.STOFCY_0 AS [库存站点],
  H.WRH_0 AS [仓库],
  H.LOC_0 AS [表头库位],
  H.CUNDAT_0 AS [盘点日期],
  H.CUNSTA_0 AS [原始状态码],
  CAST(H.CUNTYP_0 AS nvarchar(30)) AS [盘点类型_字符串],
  H.CREDATTIM_0 AS [创建日期时间],
  H.UPDDATTIM_0 AS [修改日期时间]
FROM '+@H+N' AS H
WHERE (@SiteCode IS NULL OR H.STOFCY_0=@SiteCode)
  AND (@WarehouseCode IS NULL OR H.WRH_0=@WarehouseCode)
ORDER BY H.CUNDAT_0 DESC, H.CUNNUM_0 DESC;

-- 2. 某盘点单的明细（仅指定 @CountNumber 时执行）
IF @CountNumber IS NOT NULL
BEGIN
  SELECT TOP (@Take)
    D.CUNNUM_0 AS [盘点单号],
    D.BOXNUM_0 AS [箱号],
    D.PALNUM_0 AS [托盘号],
    D.ITMREF_0 AS [产品],
    D.LOT_0 AS [批次],
    D.LOC_0 AS [实际盘点库位],
    D.PCU_0 AS [包装单位],
    D.QTYPCU_0 AS [实盘包装数量],
    D.QTYSTU_0 AS [实盘存货数量],
    D.PCUSTUCOE_0 AS [包装存货单位系数],
    D.CREDATTIM_0 AS [记录创建时间]
  FROM '+@D+N' AS D
  WHERE D.CUNNUM_0=@CountNumber
  ORDER BY D.CREDATTIM_0 DESC;

  SELECT H.CUNNUM_0 AS [盘点单号],
         COUNT(D.CUNNUM_0) AS [明细条数],
         COUNT(DISTINCT D.BOXNUM_0) AS [不同箱码数量],
         MIN(D.CREDATTIM_0) AS [最早明细时间],
         MAX(D.CREDATTIM_0) AS [最晚明细时间]
  FROM '+@H+N' AS H
  LEFT JOIN '+@D+N' AS D ON D.CUNNUM_0=H.CUNNUM_0
  WHERE H.CUNNUM_0=@CountNumber
  GROUP BY H.CUNNUM_0;
END;

-- 3. 箱标签关联产品，标签状态 STA_0 只展示，不作为盘点准入过滤
SELECT TOP (@Take)
  L.PID_0 AS [箱码标签ID],
  L.QRCOD_0 AS [完整二维码],
  L.ITMREF_0 AS [产品编码],
  I.YITMDES4_0 AS [品名],
  I.ITMDES2_0 AS [描述2_界面规格],
  L.LOT_0 AS [批次],
  L.STU_0 AS [存货单位],
  L.PCU_0 AS [包装单位],
  L.PCUSTUCOE_0 AS [包装存货系数],
  L.QTY_0 AS [标签当前数量_非实盘],
  L.LOC_0 AS [标签当前库位_非实盘],
  L.STA_0 AS [标签状态原始码]
FROM '+@L+N' AS L
LEFT JOIN '+@I+N' AS I ON I.ITMREF_0=L.ITMREF_0
WHERE (@BoxPid IS NULL OR L.PID_0=@BoxPid)
  AND (@FullQR IS NULL OR L.QRCOD_0=@FullQR)
  AND (@ItemCode IS NULL OR L.ITMREF_0=@ItemCode);

-- 4. 库位主数据：站点+仓库+库位复合定位，不能只用 LOC 判断全局唯一
SELECT TOP (@Take)
  S.STOFCY_0 AS [库存站点],
  S.WRH_0 AS [仓库],
  S.LOC_0 AS [库位],
  S.YLOCDES1_0 AS [库位名称],
  S.LOCTYP_0 AS [库位类型],
  S.LOCCAT_0 AS [库位种类],
  S.OCPCOD_0 AS [占用原始码],
  S.LOKSTA_0 AS [冻结状态原始码],
  S.CUNLOKFLG_0 AS [进行中盘点原始码],
  S.PCU_0 AS [包装单位],
  S.QTYPCU_0 AS [包装单位数量],
  S.UPDDATTIM_0 AS [修改日期时间]
FROM '+@S+N' AS S
WHERE (@SiteCode IS NULL OR S.STOFCY_0=@SiteCode)
  AND (@WarehouseCode IS NULL OR S.WRH_0=@WarehouseCode)
  AND (@LocationCode IS NULL OR S.LOC_0=@LocationCode)
ORDER BY S.STOFCY_0, S.WRH_0, S.LOC_0;

-- 5. 产品主数据关键列（其他全部物理列请用 select_five_tables_chinese_aliases.sql）
SELECT TOP (@Take)
  I.ITMREF_0 AS [产品编码],
  I.YITMDES4_0 AS [品名],
  I.ITMDES2_0 AS [描述2_业务规格]
FROM '+@I+N' AS I
WHERE (@ItemCode IS NULL OR I.ITMREF_0=@ItemCode);

-- 6. 指定盘点单的五表业务联查：只有给定单号才返回
-- 实盘数量 = XCOUTD 两列；标签当前值不是历史盘点时点快照。
IF @CountNumber IS NOT NULL
BEGIN
  SELECT TOP (@Take)
    H.CUNNUM_0 AS [盘点单号],
    H.CUNDES_0 AS [盘点单名称],
    H.STOFCY_0 AS [盘点站点],
    H.WRH_0 AS [盘点仓库],
    D.BOXNUM_0 AS [已盘箱码],
    D.LOC_0 AS [实际盘点库位],
    S.YLOCDES1_0 AS [实际库位名称],
    D.ITMREF_0 AS [已盘物料],
    I.YITMDES4_0 AS [品名],
    I.ITMDES2_0 AS [描述2_业务规格],
    D.LOT_0 AS [实盘记录批次],
    D.QTYSTU_0 AS [实盘存货数量],
    D.QTYPCU_0 AS [实盘包装数量],
    L.STA_0 AS [标签当前状态码_参考],
    L.QTY_0 AS [标签当前数量_参考],
    L.LOC_0 AS [标签当前库位_参考],
    D.CREDATTIM_0 AS [明细创建时间]
  FROM '+@H+N' AS H
  JOIN '+@D+N' AS D ON D.CUNNUM_0=H.CUNNUM_0
  LEFT JOIN '+@L+N' AS L ON L.PID_0=D.BOXNUM_0
  LEFT JOIN '+@S+N' AS S
    ON S.STOFCY_0=H.STOFCY_0
   AND S.WRH_0=H.WRH_0
   AND S.LOC_0=D.LOC_0
  LEFT JOIN '+@I+N' AS I ON I.ITMREF_0=D.ITMREF_0
  WHERE H.CUNNUM_0=@CountNumber
  ORDER BY D.CREDATTIM_0 DESC;
END;';

EXEC sys.sp_executesql @Sql,
     N'@Take int, @CountNumber nvarchar(30), @BoxPid nvarchar(50),
       @FullQR nvarchar(200), @ItemCode nvarchar(20), @SiteCode nvarchar(5),
       @WarehouseCode nvarchar(5), @LocationCode nvarchar(20)',
     @Take=@Take, @CountNumber=@CountNumber,
     @BoxPid=@BoxPid, @FullQR=@FullQR,
     @ItemCode=@ItemCode, @SiteCode=@SiteCode,
     @WarehouseCode=@WarehouseCode, @LocationCode=@LocationCode;
