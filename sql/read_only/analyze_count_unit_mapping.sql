/*
2026-11-25: historical stock-count unit mapping evidence.
Read-only. No DDL/DML, no status filter. XTEST and COB are independent.
1. Label distribution; 2. historical count rows; 3. join evidence;
4. check join coverage and duplicate-label ambiguity; 5. inspect one box.
*/
USE [sagex3];

-- 1. All label packaging/stock-unit combinations and legacy rejection counts.
WITH L AS (
    SELECT N'XTEST' AS Env, ITMREF_0, STU_0, PCU_0, PCUSTUCOE_0
    FROM XTEST.XLABELDTL
    UNION ALL
    SELECT N'COB', ITMREF_0, STU_0, PCU_0, PCUSTUCOE_0
    FROM COB.XLABELDTL
)
SELECT Env AS [帐套], STU_0 AS [存货单位], PCU_0 AS [标签包装单位],
       PCUSTUCOE_0 AS [标签转换系数], COUNT_BIG(*) AS [箱标签数],
       COUNT(DISTINCT ITMREF_0) AS [物料数],
       CASE WHEN STU_0=PCU_0 AND PCUSTUCOE_0=1 AND LTRIM(RTRIM(STU_0))<>N''
            THEN N'旧规则允许' ELSE N'旧规则拒绝' END AS [旧校验结果]
FROM L GROUP BY Env,STU_0,PCU_0,PCUSTUCOE_0
ORDER BY Env,[箱标签数] DESC;

-- 2. Historical detail quantities: no need to join current labels.
WITH D AS (
    SELECT N'XTEST' AS Env, PCU_0, PCUSTUCOE_0, QTYSTU_0,QTYPCU_0 FROM XTEST.XCOUTD
    UNION ALL
    SELECT N'COB', PCU_0, PCUSTUCOE_0, QTYSTU_0,QTYPCU_0 FROM COB.XCOUTD
)
SELECT Env AS [帐套],PCU_0 AS [盘点包装单位],PCUSTUCOE_0 AS [盘点转换系数],
       COUNT_BIG(*) AS [明细数],
       SUM(CONVERT(bigint,CASE WHEN QTYSTU_0=QTYPCU_0 THEN 1 ELSE 0 END)) AS [两数量相同],
       SUM(CONVERT(bigint,CASE WHEN QTYSTU_0<>QTYPCU_0 THEN 1 ELSE 0 END)) AS [两数量不同]
FROM D GROUP BY Env,PCU_0,PCUSTUCOE_0
ORDER BY Env,[明细数] DESC;

-- 3. Reproduce the exact historical COB analysis (374 groups on 2026-11-25).
SELECT D.PCU_0 AS [盘点明细包装单位],L.STU_0 AS [箱标签存货单位],
       L.PCU_0 AS [箱标签包装单位],L.PCUSTUCOE_0 AS [箱标签转换系数],
       D.PCUSTUCOE_0 AS [盘点明细转换系数],COUNT_BIG(*) AS [记录数]
FROM COB.XCOUTD D
INNER JOIN COB.XLABELDTL L ON D.BOXNUM_0=L.PID_0
GROUP BY D.PCU_0,L.STU_0,L.PCU_0,L.PCUSTUCOE_0,D.PCUSTUCOE_0
ORDER BY [记录数] DESC;

-- 4. Independently verify matching coverage; equal total counts alone
-- do not exclude unmatched details and multiply matched labels.
WITH L AS (
    SELECT N'COB' AS Env,PID_0,COUNT_BIG(*) AS Matches FROM COB.XLABELDTL GROUP BY PID_0
    UNION ALL
    SELECT N'XTEST',PID_0,COUNT_BIG(*) FROM XTEST.XLABELDTL GROUP BY PID_0
), D AS (
    SELECT N'COB' AS Env,BOXNUM_0 FROM COB.XCOUTD
    UNION ALL
    SELECT N'XTEST',BOXNUM_0 FROM XTEST.XCOUTD
)
SELECT D.Env AS [帐套],COUNT_BIG(*) AS [明细总数],
       SUM(CONVERT(bigint,CASE WHEN L.Matches IS NULL THEN 1 ELSE 0 END)) AS [未关联标签],
       SUM(CONVERT(bigint,CASE WHEN L.Matches=1 THEN 1 ELSE 0 END)) AS [唯一关联标签],
       SUM(CONVERT(bigint,CASE WHEN L.Matches>1 THEN 1 ELSE 0 END)) AS [多重关联标签]
FROM D LEFT JOIN L ON D.Env=L.Env AND D.BOXNUM_0=L.PID_0
GROUP BY D.Env;

-- 5. List material groups rejected by the previous rule (both environments).
WITH L AS (
    SELECT N'XTEST' AS Env,ITMREF_0,STU_0,PCU_0,PCUSTUCOE_0 FROM XTEST.XLABELDTL
    UNION ALL
    SELECT N'COB',ITMREF_0,STU_0,PCU_0,PCUSTUCOE_0 FROM COB.XLABELDTL
), I AS (
    SELECT N'XTEST' AS Env,ITMREF_0,YITMDES4_0,ITMDES2_0 FROM XTEST.ITMMASTER
    UNION ALL
    SELECT N'COB',ITMREF_0,YITMDES4_0,ITMDES2_0 FROM COB.ITMMASTER
)
SELECT L.Env AS [帐套],L.ITMREF_0 AS [物料],I.YITMDES4_0 AS [品名],I.ITMDES2_0 AS [规格],
       L.STU_0 AS [存货单位],L.PCU_0 AS [标签包装单位],L.PCUSTUCOE_0 AS [标签系数],
       COUNT_BIG(*) AS [箱标签数],
       CASE WHEN LTRIM(RTRIM(L.STU_0))=N'' THEN N'存货单位无效'
            WHEN LTRIM(RTRIM(L.PCU_0))=N'' THEN N'原包装单位为空'
            WHEN L.STU_0<>L.PCU_0 THEN N'原包装与存货单位不同'
            ELSE N'原标签系数不为1' END AS [旧规则拒绝原因]
FROM L LEFT JOIN I ON I.Env=L.Env AND I.ITMREF_0=L.ITMREF_0
WHERE L.STU_0<>L.PCU_0 OR L.PCUSTUCOE_0<>1
   OR LTRIM(RTRIM(L.STU_0))=N'' OR LTRIM(RTRIM(L.PCU_0))=N''
GROUP BY L.Env,L.ITMREF_0,I.YITMDES4_0,I.ITMDES2_0,L.STU_0,L.PCU_0,L.PCUSTUCOE_0
ORDER BY L.Env,[箱标签数] DESC;

-- 6. Single box diagnostic (fill in the actual PID).
DECLARE @BoxNumber nvarchar(20)=N'';
SELECT L.PID_0 AS [箱码],L.ITMREF_0 AS [物料],I.YITMDES4_0 AS [品名],
       L.STU_0 AS [存货单位],L.PCU_0 AS [标签包装单位],
       L.PCUSTUCOE_0 AS [标签转换系数],L.QTY_0 AS [标签数量],
       L.STU_0 AS [新版盘点包装单位],CONVERT(numeric(18,7),1) AS [新版盘点系数]
FROM XTEST.XLABELDTL L LEFT JOIN XTEST.ITMMASTER I ON I.ITMREF_0=L.ITMREF_0
WHERE L.PID_0=@BoxNumber;
