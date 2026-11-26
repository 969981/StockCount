---
title: "PDA 已盘记录分页与搜索优化"
aliases: ["已盘记录游标分页", "PDA 历史记录优化"]
created: 2026-11-26
updated: 2026-11-26
status: implemented-unverified
area: pda
tags: [StockCount, PDA, pagination, SQL, history]
up: "[[docs/pda/00-PDA扫码知识地图]]"
---

# PDA 已盘记录分页与搜索优化

需求 PDA-HISTORY-01～06：真实总数、自动分页、箱码搜索、库位筛选、ROWID 游标及大列表内存控制。原有 GET /items 及写入路径不变，新版 PDA 需同步升级 API。

## 1. 数据来源和分页

依据 [[docs/database/schema/XCOUTD]] 与 [[docs/DATABASE_MAPPING]]，XCOUTD.ROWID 为 numeric(38,0) 唯一主键；CUNNUM_0 nvarchar(30)、BOXNUM_0 nvarchar(20)、LOC_0 nvarchar(10)。使用字符串传输完整 ROWID、BigInteger 验证边界、SQL 参数转换 numeric(38,0)，不对 ROWID 列做字符串排序或使用 long/decimal 截断。Schema 由 Test/Production 白名单决定。

首页使用 COUNT_BIG(*)、条件 COUNT_BIG 和 MAX(ROWID) 取总行数、匹配行数和上界；分页 SELECT TOP(51) WHERE ROWID <= 上界，后续追加 ROWID < 上页最后一行。返回50条，额外一条确定是否还有数据。游标绑定环境、单号、箱码搜索及库位（SHA256 范围指纹，非签名/授权）；跨筛选复用拒绝。总数仅首页读取，不把历史重复箱号静默合并；因此单位是“条”而非唯一箱数。并发新增更高 ROWID 不会插入续页；刷新可看到新数据。非事务快照：低 ROWID 的迟到插入、删除、更新、两次 SELECT 之间的并发变化仍可能导致总数和列表略有差异，不宣称严格时间点快照。既有数据库索引未变。

## 2. PDA 交互

- 进入页默认加载50条，显示本单总行数和已加载/筛选结果数。
- 箱码包含搜索、库位精确输入筛选，组合 AND；点击查询生效，清空按钮恢复全部。未提交输入不自动触发网络。避免新搜索参数进入现有 query 日志白名单。
- 距末尾5条自动加载；同时最多一个有效续页请求。加载失败保留原数据、游标及重试按钮，停止自动重试。离开页面取消；刷新或改变筛选取消旧请求，忽略迟到响应。
- 单个窗口最多500行，达到上限不再自动拉取，点击“继续更早记录”后替换为下一窗口。保留累计进度；不把上万行全部缓存在设备。刷新回首段。按 DetailId 去重，不按箱号去重历史真实记录。
- 不新增写入/删除/撤销/复盘功能，不修改原扫码队列、数量、音效或 UUID。

## 3. 验收

| 项目 | 状态 |
| --- | --- |
| 新旧只读 API 隔离及完整 ROWID 游标 | 已编码，待 CI |
| SQL 参数与范围、真实总数及筛选 | 已编码，待真实只读 SQL 验收 |
| 自动加载、缓存上限、异常重试 | 已编码，待真机 |
| 38位游标/筛选绑定/分页边界单测 | 已编码，待 CI |
| Debug/Release APK、Windows API | 待 CI |
| XTEST/COB 实际查询执行计划、耗时 | 未执行，不修改生产索引 |

验收：空单、50/51条、500/501条、万条；历史同箱多行；刷新新记录；翻页期间其他设备新增更高 ROWID；筛选切换/断网/重试/返回；38位 ROWID；两个帐套隔离。先在 XTEST 只读验证 COUNT_BIG 与执行计划，若慢再单独评估获批索引；不执行 DDL。原始现场数据不提交公开仓库。

关联 [[docs/API_CONTRACT]] · [Markdown](../API_CONTRACT.md)
