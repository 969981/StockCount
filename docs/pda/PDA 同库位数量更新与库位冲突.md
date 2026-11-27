---
title: "PDA 同库位数量更新与库位冲突"
aliases: ["PDA-UPDATE-01～11", "PDA 复盘数量更新"]
created: 2026-11-27
updated: 2026-11-27
status: implemented-unverified
area: pda
tags: [StockCount, PDA, SQL, transaction]
---

# PDA 同库位数量更新与库位冲突

## 已确认业务规则（PDA-UPDATE-01）

用户确认“托盘码”指当前扫描的 XCOUTD.LOC_0，不是 PALNUM_0。按当前帐套全表查 BOXNUM_0；无记录新增，同单同库位唯一记录允许更新数量，跨库位/跨单/多条历史记录拒绝，不允许移动箱、自动去重或新增跨单重复。XTEST 与 COB 独立。原数量只显示，必须人工输入新数量，允许0；两数量列同值，物料/批次/单位/系数不兼容时禁止更新。原有单据状态规则保持。

## API/SQL（PDA-UPDATE-02～05）

- 旧 POST /labels/resolve 不传 locationCode 时保留全帐套重复布尔值。传 locationCode 则新增 decision，mode 为 New、Update、LocationConflict、CountConflict、Ambiguous、Unsupported 或 UpdateDisabled；存在唯一记录时附 ROWID 字符串、原单号/库位/数量/单位/修改时间、SHA256 版本快照。
- 新增仍走 POST /inventory-counts/{countNumber}/items；旧客户端不会自动更新。PATCH /inventory-counts/{countNumber}/items/{detailId} 请求含 rawQrCode、boxNumber、countedLocation、countedQtyStu、expectedVersion 和 Idempotency-Key。
- 更新事务 Serializable；获取同原新增路径的帐套+canonical PID 应用锁；重新核验单头、库位、标签、全表唯一记录、ROWID、精确单号/库位/箱码、物料/批次/单位/系数、原版本。只有一行命中才更新 QTYSTU_0、QTYPCU_0、UPDDATTIM_0、UPDUSR_0、经审核的 UPDTICK_0+1。读取更新后行复验数量、版本及保留字段。不改 ROWID、AUUID、PALNUM、LOC、创建信息，不改标签/库存/表头。
- 操作流水 StockCountOperationReceipt 与更新在同一 SQL Server 事务，主键为 (CountNumber,RequestId)，保存操作类型、更新请求指纹、旧/新数量、ROWID、位置、审计用户及 UTC 记录时间。更新重放先查不可变回执，指纹不同拒绝。部署流水后新增也在同事务记录回执。对已有 StockCount 明细，首次更新前若无回执则归档当时原数量及原生 UUID；这是更新前快照，不证明历史外部修改前的首次提交数量。原 AUUID 永远不改。非 StockCount 旧 UUID 不冒充请求标识。
- GET /submissions/{requestId}?countNumber=... 在同一个 Serializable 事务中优先流水、再回退原生 UUID；新增和更新均返回 operation、locationCode。PDA SecureStorage 保留原快照/数量/UUID；网络未知阻止覆盖，恢复查询核对帐套、单号、箱号、库位、操作、数量和更新 ROWID。已确认版本冲突需重新扫描，绝不静默用最新版本重试。
- 更新独立开关：StockCount:Write:TestQuantityUpdatesReviewed / StockCount:Write:ProductionQuantityUpdatesReviewed 默认 false，原新增开关/结构审核/审计用户同时有效才可更新。独立批准项不由 Avalonia 自动新增授权继承。未部署流水表时拒绝更新。旧版数据库未启用更新时仍可新增。启用后不可删除流水、撤销其 SELECT 权限或回退旧后端；应备份并保留所有回执。SQL 账号须能看到表元数据。尚未执行真实 SQL。

## PDA（PDA-UPDATE-06～09）

新增蓝色提交、更新橙色“更新盘点”及原数量，新数量留空，仍使用现有键盘自动滚动和紧凑操作栏；冲突红色持久卡片显示原单号、库位、箱码、数量、修改时间，禁用数量/提交，不弹出键盘。普通红色提示沿用2秒及失败音1000ms震动。更新成功“更新成功！”及成功音200ms，收键盘、清箱保留库位。未知结果保留确认流程。已盘记录仍只读/默认关闭、原 ROWID 不变所以总行数不变；最近一次 StockCount 更新由操作流水给出旧→新及 UTC 时间，不推断外部更新历史。

## 部署与验收（PDA-UPDATE-10～11）

1. 仅在隔离 XTEST，用 DBA 核验物理字段、索引、触发器、X3 原生 UPDTICK_0 是否允许 +1、审计字段与业务副作用、单位约束、权限及备份。
2. 人工审核并执行 sql/migrations/002_stockcount_quantity_update_receipts_XTEST.sql（只建 XTEST 辅助表，不修改 ERP 表）；不执行旧001脚本。审批后单独授予 SELECT/INSERT 辅助表及 XCOUTD 指定字段 UPDATE 权限。不得授予回执 UPDATE/DELETE。
3. 新 API/PDA 同时部署。管理员通过 API 进程配置设置 StockCount__Write__TestQuantityUpdatesReviewed=true（Windows PowerShell 当前进程环境变量，启动管理器/服务前设置；必须确认实际 API 子进程继承配置）。默认 false；未增加桌面配置界面。COB 独立审核及迁移，不自动启用。
4. XTEST 用合成箱码验证新增100、同库位改120、原 ROWID/AUUID/LOC/PALNUM 不变、两数量列120、总行数不变；不同库位/单号/多条、单位异常均拒绝。两台设备持相同旧版本仅一台成功；旧 UUID 查询仍返回原回执；更新 UUID 重放及后来再次更新后回放仍返回原更新结果；异载荷冲突；断网/进程死亡按原 UUID 查询或重试；禁用及缺流水拒绝。验证触发器、事务回滚及 DBA 审计。
5. 真机验证更新按钮、空输入、冲突卡片、音量/震动/2秒、键盘、长库位及窄屏、已盘分页刷新。CI 单测/编译不是 XTEST 实库或真机验收。未批准 COB 不开放更新。

关联：[[docs/API_CONTRACT]]、[[docs/database/BUSINESS_SQL_REFERENCE]]、[[docs/pda/00-PDA扫码知识地图]]。
