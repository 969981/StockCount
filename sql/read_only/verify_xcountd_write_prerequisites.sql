/*
StockCount — XTEST XCOUTD preflight, READ ONLY.
Run in an approved SQL Server session, inspect the output with the Sage X3 owner
before setting "Schema Reviewed" in the Tauri Windows service console.
No INSERT/UPDATE/DELETE/DDL is issued here.
*/
USE [sagex3];
SET NOCOUNT ON;

-- 1. Physical columns, nullability, IDENTITY and defaults
SELECT C.column_id AS [序号], C.name AS [字段], TY.name AS [类型],
       C.max_length AS [字节数], C.precision AS [精度],
       C.scale AS [小数位], C.is_nullable AS [允许NULL],
       C.is_identity AS [自动生成ROWID],
       DF.definition AS [字段默认值]
FROM sys.columns C
JOIN sys.types TY ON TY.user_type_id=C.user_type_id
LEFT JOIN sys.default_constraints DF ON DF.object_id=C.default_object_id
WHERE C.object_id=OBJECT_ID(N'[XTEST].[XCOUTD]')
ORDER BY C.column_id;

-- 2. Current triggers, constraints, indexes: do not bypass ERP behavior.
SELECT TR.name AS [触发器],TR.is_disabled AS [已禁用],M.definition AS [定义]
FROM sys.triggers TR
LEFT JOIN sys.sql_modules M ON M.object_id=TR.object_id
WHERE TR.parent_id=OBJECT_ID(N'[XTEST].[XCOUTD]');

SELECT IDX.name AS [索引],IDX.is_unique AS [唯一],
       IDX.is_primary_key AS [主键],
       IC.key_ordinal AS [索引位置],C.name AS [字段]
FROM sys.indexes IDX
JOIN sys.index_columns IC ON IC.object_id=IDX.object_id AND IC.index_id=IDX.index_id
JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
WHERE IDX.object_id=OBJECT_ID(N'[XTEST].[XCOUTD]')
ORDER BY IDX.name,IC.key_ordinal;

-- 3. Native detail identifiers and write permissions (no auxiliary table).
SELECT HAS_PERMS_BY_NAME(N'XTEST.XCOUTD',N'OBJECT',N'INSERT') AS [允许测试明细写入],
       HAS_PERMS_BY_NAME(N'XTEST.XCOUTD',N'OBJECT',N'SELECT') AS [允许测试明细查询],
       COL_LENGTH(N'XTEST.XCOUTD',N'AUUID_0') AS [请求识别码字节长度];

-- 4. Label unit distribution for research only. Count in STU: detail PCU=STU,
--    factor=1 and QTYSTU=QTYPCU regardless of original label PCU/factor.
SELECT TOP (20)
       L.STU_0 AS [存货单位],L.PCU_0 AS [包装单位],
       L.PCUSTUCOE_0 AS [换算系数],COUNT_BIG(*) AS [标签条数]
FROM [XTEST].[XLABELDTL] L
GROUP BY L.STU_0,L.PCU_0,L.PCUSTUCOE_0
ORDER BY COUNT_BIG(*) DESC;

-- 5. Do not alter old duplicate rows; compare counts before deciding rules.
SELECT TOP (20) D.CUNNUM_0 AS [盘点单号],D.BOXNUM_0 AS [重复箱码],
       COUNT_BIG(*) AS [出现次数]
FROM [XTEST].[XCOUTD] D
GROUP BY D.CUNNUM_0,D.BOXNUM_0
HAVING COUNT_BIG(*)>1
ORDER BY COUNT_BIG(*) DESC;

-- 6. Header states are raw codes; only 0 is enabled by this version's writer.
SELECT H.CUNSTA_0 AS [盘点单原始状态],COUNT_BIG(*) AS [单据数量]
FROM [XTEST].[XCOUTH] H
GROUP BY H.CUNSTA_0
ORDER BY H.CUNSTA_0;
