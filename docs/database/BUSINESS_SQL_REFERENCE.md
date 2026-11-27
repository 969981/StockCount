---
title: "StockCount 业务 SQL 实现字典"
aliases: ["业务SQL","运行时SQL字典","盘点业务SQL"]
created: 2026-11-26
updated: 2026-11-26
status: reference
area: database
tags: [StockCount, database, documentation]
up: "[[docs/database/00-数据库知识地图]]"
---

# StockCount 业务 SQL 实现字典

> 源码基线：`805ce006b2e365e28727fce49159f29e083c7c7e`（PR #45 合并后）。本文列出实际 Sage X3 业务访问 SQL，而非从表名推测；源文件模板中的 `{schema}` 由服务端白名单转换为 [XTEST] 或 [COB]，其余动态段在各节说明。文档不是写库操作脚本。只读核验请用 [runtime_business_queries.sql](../../sql/read_only/runtime_business_queries.sql)，默认 XTEST，使用合成占位值；既有 [business_queries.sql](../../sql/read_only/business_queries.sql) 是业务排查扩展查询，不是所有运行时语句的逐字复制。

## 1. 业务与 SQL 对照

| ID | 使用场景 | API / 方法 | 表 | 类型 |
| --- | --- | --- | --- | --- |
| Q01 | 数据库就绪 | GET /ready | 无 | SELECT 1 |
| Q02 | 盘点单列表 | GET /inventory-counts | XCOUTH | 分页 |
| Q03 | 盘点单详情 | GET /inventory-counts/{countNumber} | XCOUTH | 精确 |
| Q04 | 库位扫码及提交复核 | POST /locations/resolve / SubmitAsync | STOLOC | 聚合 |
| Q05 | 箱码解析 | POST /labels/resolve | XLABELDTL、ITMMASTER、XCOUTD | TOP 2 |
| Q06 | 旧版已盘记录 | GET /inventory-counts/{countNumber}/items | XCOUTD | OFFSET |
| Q07 | 新版历史总数与上界 | GET /inventory-counts/{countNumber}/history | XCOUTD | COUNT_BIG、MAX |
| Q08 | 新版历史分页 | 同上 | XCOUTD | ROWID keyset |
| Q09 | 原 UUID 结果查询及重放 | GET /submissions/{requestId} / SubmitAsync | XCOUTD | TOP 2 |
| W01 | 同箱事务互斥 | SubmitAsync | sp_getapplock | 事务锁 |
| W02 | ROWID 自增核验 | SubmitAsync | sys.columns | 元数据 |
| W03 | 事务锁定盘点单 | SubmitAsync | XCOUTH | UPDLOCK/HOLDLOCK |
| W04 | 写入前标签复核 | SubmitAsync | XLABELDTL | TOP 2 |
| W05 | 同帐套全单防重 | SubmitAsync | XCOUTD | 锁定 EXISTS |
| W06 | 保存实盘 | SubmitAsync | XCOUTD | INSERT |

API 路径均以前缀 /api/v1 开头（/ready 除外）。当前应用不 UPDATE/DELETE ERP 盘点数据、不创建或关闭盘点单、不调整标签或库存。日志 SQLite 的维护不属于 ERP 业务写入。

## 2. 读取类 SQL（Q01–Q09）

### Q01 数据库连接

```sql
SELECT 1;
```

仅检测连接，不证明表访问、写入或 ERP 业务兼容。

### Q02 盘点单列表

```sql
SELECT H.CUNNUM_0 AS CountNumber, H.CUNDES_0 AS Description,
       H.STOFCY_0 AS SiteCode, H.WRH_0 AS WarehouseCode,
       H.CUNDAT_0 AS CountDate, CAST(H.CUNSTA_0 AS int) AS StatusCode,
       CAST(H.CUNTYP_0 AS nvarchar(30)) AS CountType
FROM {schema}.[XCOUTH] H
WHERE (@Search IS NULL
       OR CHARINDEX(@Search,H.CUNNUM_0)>0
       OR CHARINDEX(@Search,H.CUNDES_0)>0)
ORDER BY H.CUNDAT_0 DESC,H.CUNNUM_0 DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
```

参数 Search（单号/名称包含匹配，可空）、Skip（偏移）、Take（请求页大小+1）；API 1–100，PDA 每页100。所有 CUNSTA_0 状态可见；是否可提交由服务端授权策略决定，不从状态码过滤。

### Q03 盘点单详情

```sql
SELECT H.CUNNUM_0 AS CountNumber,H.CUNDES_0 AS Description,
       H.STOFCY_0 AS SiteCode,H.WRH_0 AS WarehouseCode,
       H.CUNDAT_0 AS CountDate,CAST(H.CUNSTA_0 AS int) AS StatusCode,
       CAST(H.CUNTYP_0 AS nvarchar(30)) AS CountType
FROM {schema}.[XCOUTH] H
WHERE H.CUNNUM_0=@CountNumber;
```

CountNumber = 当前单号，最长30字符。原生状态与类型只显示，不作为写入白名单。

### Q04 库位校验

```sql
SELECT COUNT_BIG(*) AS TotalMatches,
       COUNT_BIG(CASE WHEN (@CheckSite=0 OR S.STOFCY_0=@Site)
                           AND (@CheckWarehouse=0 OR S.WRH_0=@Warehouse)
                      THEN 1 END) AS ScopedMatches,
       MIN(CASE WHEN (@CheckSite=0 OR S.STOFCY_0=@Site)
                 AND (@CheckWarehouse=0 OR S.WRH_0=@Warehouse)
                THEN S.LOC_0 END) AS LocationCode
FROM {schema}.[STOLOC] S
WHERE S.LOC_0=@Location;
```

参数 Location（扫码 LC. 去前缀后实际库位，最长10）、Site/Warehouse（当前单头）、CheckSite/CheckWarehouse（本帐套两个开关）。四个环境开关默认 false；TotalMatches=0 不存在；ScopedMatches=0 不匹配已启用范围；两维都开启且 ScopedMatches>1 才拒绝歧义。不是用标签当前库位代替实盘库位。

### Q05 扫箱读取

```sql
SELECT TOP (2) L.PID_0 AS BoxNumber,L.ITMREF_0 AS ItemCode,
       I.YITMDES4_0 AS ItemName,I.ITMDES2_0 AS Specification,
       L.LOT_0 AS LotNumber,L.STU_0 AS StockUnit,
       L.QTY_0 AS LabelQty,CAST(L.STA_0 AS int) AS LabelStatusCode,
       CAST(CASE WHEN EXISTS (
        SELECT 1 FROM {schema}.[XCOUTD] D
        WHERE D.BOXNUM_0=L.PID_0
    ) THEN 1 ELSE 0 END AS bit) AS AlreadyRecordedInCount
FROM {schema}.[XLABELDTL] L
LEFT JOIN {schema}.[ITMMASTER] I ON I.ITMREF_0=L.ITMREF_0
WHERE L.QRCOD_0=@Value;
```

先完整 QR 精确查；零匹配且结构化 ID. 字段唯一合法时才把 WHERE 换为 L.PID_0=@Value；TOP(2) 多条为歧义，不能退回继续猜测。STA_0 不参与过滤。AlreadyRecordedInCount 是兼容旧命名，实际含义是本帐套任意盘点单存在箱号。显示品名 I.YITMDES4_0、规格 I.ITMDES2_0，STU 来源 L.STU_0；L.QTY_0 只作参考。

### Q06 旧版已盘明细

```sql
SELECT CONVERT(nvarchar(40),D.ROWID) AS DetailId,
       D.CUNNUM_0 AS CountNumber,D.BOXNUM_0 AS BoxNumber,
       D.LOC_0 AS LocationCode,D.ITMREF_0 AS ItemCode,
       D.LOT_0 AS LotNumber,D.PCU_0 AS PackageUnit,
       D.QTYPCU_0 AS QuantityPcu,D.QTYSTU_0 AS QuantityStu,
       D.CREDATTIM_0 AS CreatedAt
FROM {schema}.[XCOUTD] D
WHERE D.CUNNUM_0=@CountNumber
ORDER BY D.ROWID DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
```

CountNumber、Skip、Take=页大小+1；PDA 旧版50条。保留接口兼容。

### Q07 新版历史真实总数

```sql
SELECT CONVERT(varchar(40), MAX(D.ROWID)) AS UpperRowId,
       COUNT_BIG(*) AS TotalCount,
       COUNT_BIG(CASE WHEN (@BoxSearch IS NULL OR CHARINDEX(@BoxSearch,D.BOXNUM_0)>0)
                 AND (@Location IS NULL OR D.LOC_0=@Location) THEN 1 END) AS FilteredCount
FROM {schema}.[XCOUTD] D
WHERE D.CUNNUM_0=@CountNumber;
```

CountNumber、BoxSearch（包含，最多20字符）、Location（精确，最多10字符）；COUNT_BIG 是行数不是去重箱数，返回 TotalCount/FilteredCount/UpperRowId（字符串）。只在第一页读取统计。

### Q08 新版历史 ROWID 游标

```sql
SELECT TOP (@Take) CONVERT(varchar(40),D.ROWID) AS DetailId,
       D.CUNNUM_0 AS CountNumber,D.BOXNUM_0 AS BoxNumber,
       D.LOC_0 AS LocationCode,D.ITMREF_0 AS ItemCode,
       D.LOT_0 AS LotNumber,D.PCU_0 AS PackageUnit,
       D.QTYPCU_0 AS QuantityPcu,D.QTYSTU_0 AS QuantityStu,
       D.CREDATTIM_0 AS CreatedAt
FROM {schema}.[XCOUTD] D
WHERE D.CUNNUM_0=@CountNumber
  AND D.ROWID<=CONVERT(numeric(38,0),@UpperRowId)
  -- 仅续页包含：
  AND D.ROWID<CONVERT(numeric(38,0),@BeforeRowId)
  AND (@BoxSearch IS NULL OR CHARINDEX(@BoxSearch,D.BOXNUM_0)>0)
  AND (@Location IS NULL OR D.LOC_0=@Location)
ORDER BY D.ROWID DESC;
```

首页省略 BeforeRowId 条件，续页用上一页末尾的 ROWID；Take=51（PDA），输出50条。ROWID numeric(38,0) 不转 long/decimal；游标范围指纹绑定帐套、单号和过滤条件，不是访问授权。首页统计及分页不是严格事务快照；其他设备高 ROWID 新增在刷新后显示。详见 [[docs/pda/PDA 已盘记录分页与搜索优化]]。

### Q09 原请求结果查询

```sql
SELECT TOP (2) D.BOXNUM_0 AS BoxNumber,D.LOC_0 AS LocationCode,
       CONVERT(nvarchar(40),D.ROWID) AS DetailId,
       D.PCU_0 AS StockUnit,D.QTYSTU_0 AS SavedQtyStu,D.QTYPCU_0 AS SavedQtyPcu
FROM {schema}.[XCOUTD] D
WHERE D.CUNNUM_0=@CountNumber
  AND D.AUUID_0=CONVERT(binary(16),@RequestId)
  AND D.PDAID_0=N'StockCount';
```

CountNumber 和 RequestId（uniqueidentifier）。提交事务内 D 后加 WITH (UPDLOCK,HOLDLOCK)；PDAID_0=StockCount，使用与 INSERT 一致的 SQL binary(16) 转换。不把别的箱号记录当成原 UUID 成功；重放还核对箱号、库位和两数量。历史旧随机 UUID 记录仍受箱号防重约束。

## 3. 事务 SQL（W01–W06，仅源码说明，禁止手工执行）

以下不是可直接执行的提交脚本。必须走 API 的身份、环境、结构、审计、标签、库位、数量和幂等校验；**不要复制 INSERT 到 SSMS 手工录入。** 事务 IsolationLevel.Serializable，锁和查询在同一个 SQL 连接/事务内；先取锁、查询原请求，已确认同载荷则返回原结果，否则继续。

### W01 同箱应用锁

```sql
DECLARE @result int;
EXEC @result=sys.sp_getapplock
    @Resource=@LockResource, @LockMode=N'Exclusive',
    @LockOwner=N'Transaction', @LockTimeout=10000;
SELECT @result;
```

LockResource = StockCount:Box: + SHA256(schema + U+001F + boxNumber)，不包含盘点单号，环境隔离。返回负数拒绝提交。

### W02 ROWID 自增核验

```sql
SELECT COUNT(*) FROM sys.columns C
WHERE C.object_id=OBJECT_ID(@TableName) AND C.name=N'ROWID'
  AND C.is_identity=1;
```

TableName 为白名单表名；不是 IDENTITY 时拒绝。实际触发器、字段、权限仍需现场核验。

### W03 锁定单头

```sql
SELECT H.STOFCY_0 AS SiteCode,H.WRH_0 AS WarehouseCode
FROM {schema}.[XCOUTH] H WITH (UPDLOCK,HOLDLOCK)
WHERE H.CUNNUM_0=@CountNumber;
```

当前单号存在即可，所有已有单据状态可提交（2026-11-25 规则）；仍受后端与数据库授权约束。

### W04 事务内标签复核

```sql
SELECT TOP (2) L.PID_0 AS BoxNumber,L.ITMREF_0 AS ItemCode,
       L.LOT_0 AS LotNumber,L.STU_0 AS StockUnit
FROM {schema}.[XLABELDTL] L
WHERE L.QRCOD_0=@Value;
```

仅零匹配时按合法 ID 改为 PID_0=@Value；验证数据库 canonical PID 与请求箱号完全一致、物料和批次长度及 STU。

### W05 当前帐套全表防重

```sql
SELECT CAST(CASE WHEN EXISTS (
    SELECT 1 FROM {schema}.[XCOUTD] D WITH (UPDLOCK,HOLDLOCK)
    WHERE D.BOXNUM_0=@BoxNumber
) THEN 1 ELSE 0 END AS bit);
```

不限制单号、日期、仓库、站点和单据状态。历史重复不删除、不改写；XTEST 与 COB 各自独立。已确认原 UUID 的重放先于防重，避免把正常幂等重放当成重复。

### W06 实盘插入（仅供审计阅读）

```sql
INSERT INTO {schema}.[XCOUTD]
    (UPDTICK_0,CUNNUM_0,PALNUM_0,BOXNUM_0,ITMREF_0,
     LOT_0,LOC_0,ECCVALMAJ_0,PCU_0,QTYPCU_0,PCUSTUCOE_0,
     QTYSTU_0,PDAID_0,CREDATTIM_0,UPDDATTIM_0,AUUID_0,
     CREUSR_0,UPDUSR_0)
OUTPUT CONVERT(nvarchar(40),INSERTED.ROWID)
VALUES
    (1,@CountNumber,N'',@BoxNumber,@ItemCode,
     @LotNumber,@Location,N'',@StockUnit,@Quantity,@UnitFactor,
     @Quantity,N'StockCount',GETDATE(),GETDATE(),
     CONVERT(binary(16),@RequestId),@AuditUser,@AuditUser);
```


| 写入字段 | 数据来源/参数 |
| --- | --- |
| CUNNUM_0 | 当前单号 nvarchar(30) |
| BOXNUM_0 | 标签 PID nvarchar(20) |
| ITMREF_0 | 标签物料 nvarchar(20) |
| LOT_0 | 标签批次 nvarchar(30) |
| LOC_0 | 人工扫描库位 nvarchar(10) |
| PCU_0 | 标签 STU nvarchar(3) |
| QTYPCU_0、QTYSTU_0 | 同一个人工实盘值 numeric(28,13)，允许0，非负，小数最多13位，整数最多15位 |
| PCUSTUCOE_0 | 1，numeric(18,7) |
| AUUID_0 | 原 RequestId uniqueidentifier → binary(16) |
| PDAID_0 | StockCount |
| CREUSR_0、UPDUSR_0 | 审计用户 nvarchar(5)，不是个人登录身份 |
| CREDATTIM_0、UPDDATTIM_0 | GETDATE() |
| PALNUM_0、ECCVALMAJ_0 | 空字符串 |
| UPDTICK_0 | 1 |
| ROWID | 数据库 IDENTITY 生成，通过 OUTPUT 返回字符串 |

成功后 Commit；异常尝试 Rollback，未知结果用原 UUID 查询，绝不另起新请求。现行实现不需要创建辅助幂等表；历史迁移 sql/migrations/001_stockcount_submission.sql 不属于当前运行路径，不执行。完整事务代码：[SqlInventoryWriteRepository.cs](../../src/StockCount.Infrastructure/SqlInventoryWriteRepository.cs)。

## 4. 排查入口与现有 SQL

- [当前运行时只读核验 SQL](../../sql/read_only/runtime_business_queries.sql)：中文业务别名、测试帐套默认、带示例参数；只读，不模拟事务锁或执行 INSERT。
- [五表业务查询](../../sql/read_only/business_queries.sql)、[五表中文字段](../../sql/read_only/select_five_tables_chinese_aliases.sql)、[码值列及 CASE 映射](../../sql/read_only/select_table_with_enum_aliases.sql)。
- [七表字段索引](SCHEMA_INDEX.md)、[字段映射](../DATABASE_MAPPING.md)、[写入流程](../WRITE_FLOW_IMPLEMENTATION.md)、[API 契约](../API_CONTRACT.md)。
- 原始实现：[SqlInventoryReadRepository.cs](../../src/StockCount.Infrastructure/SqlInventoryReadRepository.cs)、[BoxDuplicateQuery.cs](../../src/StockCount.Infrastructure/BoxDuplicateQuery.cs)、[LocationLookupQuery.cs](../../src/StockCount.Infrastructure/LocationLookupQuery.cs)、[HistoryReadQuery.cs](../../src/StockCount.Infrastructure/HistoryReadQuery.cs)。

现场 SQL Server 未连接，本文未执行任何业务查询或 DDL/DML；索引和性能以现场只读执行计划为准。部署、扫码和恢复操作见 [软件使用手册](../USER_GUIDE.md)。


## 2026-11-27 W07 数量修正（新增路径，前文只 INSERT 为旧版）

以下为 SqlInventoryWriteRepository 中新路径的参数化核心 SQL，非可独立执行的生产脚本。先同帐套 BOXNUM_0 TOP(2) WITH (UPDLOCK,HOLDLOCK) 取完整原记录；按事务校验版本、单号、库位、标签、单位及权限，再执行：

```sql
UPDATE {schema}.[XCOUTD]
SET QTYSTU_0=@Qty,QTYPCU_0=@Qty,UPDDATTIM_0=GETDATE(),UPDUSR_0=@Audit,
    UPDTICK_0=UPDTICK_0+1
WHERE ROWID=CONVERT(numeric(38,0),@DetailId) AND CUNNUM_0=@Count
  AND BOXNUM_0=@BoxNumber AND LOC_0=@LocationCode AND UPDTICK_0=@Tick
  AND QTYSTU_0=@QuantityStu AND QTYPCU_0=@QuantityPcu AND PCUSTUCOE_0=1;
```

同事务核验修改行及不可变字段，INSERT StockCountOperationReceipt（原请求/更新回执）；GET 恢复优先该表。迁移仅提供 XTEST 审批脚本，未执行；UPDTICK_0+1 必须由 DBA 独立确认。旧只读诊断脚本不执行 UPDATE。详见 [[docs/pda/PDA 同库位数量更新与库位冲突]]。
