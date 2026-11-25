---
title: "StockCount · XCOUTD 事务写入与提交结果查询"
aliases: ["StockCount 事务写入", "XCOUTD 写入"]
tags: [StockCount, SQLServer, WriteFlow, QA]
status: implemented-awaiting-erp-validation
---

# StockCount · XCOUTD 事务写入与提交结果查询

> 2026-11-23：已提交真实 SQL Server INSERT/事务、幂等记录查询及 MAUI 扫码流程代码；**尚未在实际 XTEST / COB 数据库中执行验证，也未取得编译通过结果**.XTEST 与 COB 都具备独立授权写入能力，默认均关闭。

## API 实现状态

| API | 作用 | 状态 |
| --- | --- | --- |
| GET /api/v1/inventory-counts | 查询所有已有盘点单 | 已编码 |
| GET /api/v1/inventory-counts/{countNumber} | 单据详情 | 已编码 |
| POST /api/v1/locations/resolve | 按站点、仓库、库位校验 | 已编码 |
| POST /api/v1/labels/resolve | 完整二维码匹配 XLABELDTL、ITMMASTER，并预检查重复 | 已编码 |
| POST /api/v1/inventory-counts/{countNumber}/items | 真实 XTEST.XCOUTD INSERT、事务和防重 | 已编码，管理员启用后才执行 |
| GET /api/v1/submissions/{requestId}?countNumber=... | 持久化已提交结果查询 | 已编码，须先装部署表 |

## 业务请求

POST /api/v1/inventory-counts/COUNT-DEMO-001/items

请求头：X-StockCount-Environment: Test，X-StockCount-Device-Key: <PDA令牌>，Idempotency-Key: <每笔提交固定的UUID>。

```json
{"rawQrCode":"ID.BOX-DEMO-001@@PT.DOC-DEMO-001@@LN.1000@@MT.ITM-DEMO-01@@LT.LOT-01@@QT.100@@PG.100","boxNumber":"BOX-DEMO-001","countedLocation":"0700Q0001","countedQtyStu":99}
```

箱内数量改成 nullable decimal，**未填写会返回 400 COUNT_QUANTITY_REQUIRED**，人工输入99时保存 QTYSTU_0=QTYPCU_0=99，不会从二维码 QT.100 带入。数量为0当前允许，负数或 numeric(28,13) 超出范围拒绝；PCS 整数要求尚待业务确认。标签 STA_0 无任何白名单限制。

写入前后端重查盘点单（存在即可，不限制 CUNSTA_0）、库位、完整二维码及箱号；按标签 STU 盘点：XCOUTD.PCU_0=XLABELDTL.STU_0，PCUSTUCOE_0=1，两数量列均为人工实盘数。不再限制原标签 PCU 或系数；仅 STU 无效返回 409 UNIT_MAPPING_UNSUPPORTED。不能自动换算两数量列。

## 原子事务、重复与幂等

后端使用一个 SQL Server Serializable 事务和 sp_getapplock 跨 API 进程锁定帐套 Schema+箱码（不含盘点单号）；先查持久化请求，命中 XCOUTD.AUUID_0 且箱号、库位和两数量列一致就返回原结果（HTTP 200）；不一致返回 409。之后在锁内再次查询当前帐套 XCOUTD 的任意盘点单同箱记录，有重复即 409 BOX_ALREADY_RECORDED，提示“该箱号已扫描！”。

通过审核后仅 INSERT XCOUTD，COMMIT 成功才返回 HTTP 201 status=Committed。ROWID 从数据库生成并作为字符串返回；所有 NOT NULL 业务列显式赋值、AUUID 使用原请求 UUID 经 SQL Server binary(16) 转换，时间仍由 SQL Server 生成。PALNUM_0 和 ECCVALMAJ_0 填空、PDAID_0 填 StockCount、审计用户由现场配置，**这些真实 X3 映射尚未现场审批**。若 XCOUTD.ROWID 不是 IDENTITY，接口返回 ERP_SCHEMA_UNSUPPORTED，禁止猜测 MAX(ROWID)+1。

不新增辅助幂等表；SQLite server-logs.db 仅用于程序日志。旧迁移脚本不再需要，保留为历史文件，不自动创建或删除任何数据库对象。

GET /api/v1/submissions/{requestId}?countNumber=COUNT-DEMO-001 要携带同一环境/设备请求头：200 代表确认已保存，404 代表当前未查到已提交记录，503 代表结果无法确认。404 后重试也必须使用原 UUID；503 时绝不能据此推断保存失败。

## 启用 XTEST / COB 双帐套写入（各自默认关闭）

1. DBA 在 XTEST 执行 [verify_xcountd_write_prerequisites.sql](../sql/read_only/verify_xcountd_write_prerequisites.sql) 只读检查，核对 19 列、ROWID IDENTITY、ERP 触发器、PCU/STU 和审计规则。
2. 确认 API 账号具有必要的 SELECT 和 XCOUTD INSERT 权限，并在 XTEST 核验 AUUID_0 是否由触发器重写、ROWID 自增及重复箱号保护。无需创建其他表。
3. Windows Tauri 后端管理器数据库设置页填写 X3 审计用户（1–5字符），独立勾选 XTEST 的结构已确认/允许写入。若要启用正式帐套，另外执行 [COB 只读前检](../sql/read_only/verify_cob_write_prerequisites.sql)、核验 COB 触发器/ROWID/单位/账号权限，并勾选 COB 结构已确认/允许写入。保存后停止并重新启动 C# Sidecar。
4. 前台 MAUI 可选择 Test 或 Production，扫码与人工数量提交使用相同接口，服务端根据帐套独立开关决定是否写入 `XTEST.XCOUTD` 或 `COB.XCOUTD`。
5. 先在 XTEST 验证 201/200/409/503、并发和失败回滚；在专门批准窗口再对 COB 使用受控单据做端到端验证。现场还需评估其他旧 ERP 写入渠道产生的并发冲突。

## 客户端流程

MAUI 的 CountScanWorkflow 与 CountingPage 已接入海康广播、API 扫码、预检查重复和提交：未扫库位扫箱 → 弹窗“请先扫描盘点库位！”；重复 → 弹窗“该箱号已扫描！”且不填充；数量留空不能提交；成功 → 确认成功立即清箱数据及数量，显示400ms灰色半透明非阻塞 Toast，不清单号、名称、库位。网络结果未知时保留内容与请求 UUID，通过 GET 查询。

**尚未覆盖**：Android 系统杀进程后的草稿/UUID 持久恢复、实际海康设备广播参数与 MAUI 真机验证；网络外部老 PDA/X3 写入渠道与本 API 的并发一致性也需现场测试。

## 错误码

| HTTP | code | 含义 |
| --- | --- | --- |
| 400 | COUNT_QUANTITY_REQUIRED | 未填写数量 |
| 400 | COUNT_QUANTITY_INVALID | 数量无效 |
| 409 | BOX_ALREADY_RECORDED | 当前单箱码已盘 |
| 409 | IDEMPOTENCY_PAYLOAD_MISMATCH | 同 UUID 不同请求 |
| 409 | COUNT_NOT_WRITABLE | 旧版兼容错误码，新版不按状态拒绝 |
| 409 | UNIT_MAPPING_UNSUPPORTED | 标签存货单位无效（兼容原错误码） |
| 403 | PRODUCTION_WRITE_DISABLED | COB 端未开启独立写入开关 |
| 503 | ERP_WRITE_UNAVAILABLE | 未启用 XTEST 写入 |
| 503 | SCHEMA_NOT_APPROVED | 现场尚未核准字段/单位与写入逻辑 |
| 503 | ERP_SCHEMA_UNSUPPORTED | X3 表结构不满足适配器条件 |
| 503 | SUBMISSION_OUTCOME_UNKNOWN | 提交结果未知，保持草稿 |
| 404 | SUBMISSION_NOT_FOUND | 无已提交记录，不能生成新 UUID 盲重试 |

**验收状态**：XTEST / COB 双帐套写入代码已提交；实际编译、真机运行、两套 SQL Server 的写入/回滚、多个 PDA 并发测试和正式业务验收均待执行。

现场确认依据：[2026-11-25 盘点单位映射研究](database/COUNT_UNIT_MAPPING_RESEARCH_2026-11-25.md)。

2026-11-25 PDA 成功反馈调整：服务端确认提交后立即完成本地草稿清理，显示400ms Toast，不等待提示消失；下一箱扫码可立即继续。未知结果保护与重复箱弹窗保持不变。

2026-11-25：所有 CUNSTA_0 状态均允许提交，含已结束/未知状态；事务仍核对 XCOUTH 存在及站点、仓库上下文，不修改表头状态。

### 2026-11-25 跨盘点单防重

扫码与最终写入统一按 BOXNUM_0 查询当前 Schema 的全表，不限制日期、表头状态、仓库或站点，包含历史明细、非 StockCount 创建的记录。保留 alreadyRecordedInCount 和 BOX_ALREADY_RECORDED，旧 PDA 无需更新；同环境同单原 UUID 成功重放仍优先返回原结果。XTEST 与 COB 不互相阻断。不修改历史记录、数据库结构及其它系统写入规则。应用锁以 Schema+箱号为粒度，SQL Serializable 与 UPDLOCK/HOLDLOCK 检查覆盖写入竞争；旧版 API 不应与新版长期并行，外部写入系统不受应用锁统一约束。全表防重可能需要 BOXNUM_0 前导索引，须先只读核验执行计划并经 DBA 审批，不自动创建索引。

验收：XTEST 单A先扫入箱X提交，再在单B扫码应提示该箱号已扫描；两台设备预先扫码同一未录入箱并在不同单同时提交，应只有一笔成功，另一笔重复拒绝或安全重试后拒绝；同单同UUID同载荷重放应返回原结果；COB 与 XTEST 各自隔离。实际 SQL 并发及硬件测试未执行。
