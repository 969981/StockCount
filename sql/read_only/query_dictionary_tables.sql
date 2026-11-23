/*
StockCount — ATABZON / ATEXTE 只读查询：字段代码与中英文标题 + 原始字典样本
SQL Server / Sage X3 database=sagex3；默认 XTEST（测试帐套）。
使用白名单 Schema，严禁业务层透传任意 Schema。
输出：
1) ATABZON 字段定义 + ATEXTE 的普通/短/长中文标题及英文标题
2) ATEXTE 指定字段的原始中文/英文文本（最多 @Take）
3) 两表少量真实全物理字段 SELECT TOP(@Take)（便于诊断，别提交实际行到公开仓库）
请避免无范围的全量 ATEXTE 导出；字典文字可能包含客户自定义描述。
*/
USE [sagex3];
SET NOCOUNT ON;

DECLARE @Schema sysname = N'XTEST';        -- N'COB' 正式 / N'XTEST' 测试
DECLARE @FocusTable nvarchar(30)=N'XCOUTH';  -- 可改为下列 7 张表
DECLARE @Take int=30;                     -- 1..100
DECLARE @Lang nvarchar(3)=N'CHI';         -- CHI 或 ENG
IF @Schema NOT IN (N'COB', N'XTEST')
    THROW 51111, N'仅支持 COB 或 XTEST', 1;
IF @FocusTable NOT IN (N'XCOUTH',N'XCOUTD',N'XLABELDTL',N'STOLOC',
                       N'ITMMASTER',N'ATABZON',N'ATEXTE')
    THROW 51112, N'请选定受控的七张表之一', 1;
IF @Take NOT BETWEEN 1 AND 100
    THROW 51113, N'@Take 必须在1到100之间', 1;
IF @Lang NOT IN (N'CHI',N'ENG')
    THROW 51114, N'@Lang 必须是 CHI 或 ENG', 1;

DECLARE @Sql nvarchar(max) = N'
-- 1. 按实际字典代码查询全部标题（限制行数）
SELECT TOP(@Take)
   Z.CODFIC_0 AS [X3业务表],
   Z.CODZONE_0 AS [X3字段代码],
   Z.CODTYP_0 AS [X3逻辑类型],
   Z.LONG_0 AS [X3定义长度],
   Z.DIME_0 AS [字段维度],
   Z.LIEN_0 AS [关联表],
   Z.OBLIG_0 AS [强制关联标记_不等于API必填],
   Z.CODACT_0 AS [活动代码],
   Z.NOLIB_0 AS [本地菜单章节号],
   Z.NOCOURT_0 AS [普通标题文本编号],
   Z.NOABREG_0 AS [短标题文本编号],
   Z.NOLONG_0 AS [长标题文本编号],
   TC.TEXTE_0 AS [中文普通标题],
   TS.TEXTE_0 AS [中文短标题],
   TL.TEXTE_0 AS [中文长标题],
   TE.TEXTE_0 AS [英文普通标题]
FROM '+QUOTENAME(@Schema)+N'.[ATABZON] AS Z
LEFT JOIN '+QUOTENAME(@Schema)+N'.[ATEXTE] AS TC
  ON TC.NUMERO_0=Z.NOCOURT_0 AND TC.LAN_0=N''CHI''
LEFT JOIN '+QUOTENAME(@Schema)+N'.[ATEXTE] AS TS
  ON TS.NUMERO_0=Z.NOABREG_0 AND TS.LAN_0=N''CHI''
LEFT JOIN '+QUOTENAME(@Schema)+N'.[ATEXTE] AS TL
  ON TL.NUMERO_0=Z.NOLONG_0 AND TL.LAN_0=N''CHI''
LEFT JOIN '+QUOTENAME(@Schema)+N'.[ATEXTE] AS TE
  ON TE.NUMERO_0=Z.NOCOURT_0 AND TE.LAN_0=N''ENG''
WHERE RTRIM(Z.CODFIC_0)=@FocusTable
ORDER BY Z.CODZONE_0;

-- 2. 一小部分与选定业务表有关的原始 ATEXTE 文本
SELECT TOP(@Take)
    TXT.NUMERO_0 AS [标题编号],
    TXT.LAN_0 AS [语言],
    TXT.TEXTE_0 AS [原始标题文字]
FROM '+QUOTENAME(@Schema)+N'.[ATEXTE] AS TXT
WHERE TXT.LAN_0=@Lang
  AND EXISTS (
      SELECT 1 FROM '+QUOTENAME(@Schema)+N'.[ATABZON] AS Z
      WHERE RTRIM(Z.CODFIC_0)=@FocusTable
        AND (Z.NOCOURT_0=TXT.NUMERO_0
          OR Z.NOABREG_0=TXT.NUMERO_0
          OR Z.NOLONG_0=TXT.NUMERO_0))
ORDER BY TXT.NUMERO_0;

-- 3. 限定行数获取 ATABZON 的所有实际物理字段（仅调试）
SELECT TOP(@Take) * FROM '+QUOTENAME(@Schema)+N'.[ATABZON]
WHERE RTRIM(CODFIC_0)=@FocusTable
ORDER BY CODZONE_0;

-- 4. 限定行数获取 ATEXTE 的所有实际物理字段（仅关联的目标文本）
SELECT TOP(@Take) * FROM '+QUOTENAME(@Schema)+N'.[ATEXTE] AS TXT
WHERE TXT.LAN_0=@Lang AND EXISTS (
      SELECT 1 FROM '+QUOTENAME(@Schema)+N'.[ATABZON] AS Z
      WHERE RTRIM(Z.CODFIC_0)=@FocusTable
        AND (Z.NOCOURT_0=TXT.NUMERO_0
          OR Z.NOABREG_0=TXT.NUMERO_0
          OR Z.NOLONG_0=TXT.NUMERO_0))
ORDER BY TXT.NUMERO_0;
';

EXEC sys.sp_executesql @Sql,
    N'@FocusTable nvarchar(30),@Lang nvarchar(3),@Take int',
    @FocusTable=@FocusTable,@Lang=@Lang,@Take=@Take;
