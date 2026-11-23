# StockCount · XCOUTD 事务写入与提交结果查询

> 2026-11-23：已提交真实 SQL Server INSERT/事务、幂等记录查询及 MAUI 扫码流程代码；**尚未在实际 XTEST 数据库中执行验证，也未取得编译通过结果**。Production 继续默认禁写。

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

POST /api/v1/inventory-counts/1712474746529/items

请求头：X-StockCount-Environment: Test，X-StockCount-Device-Key: <PDA令牌>，Idempotency-Key: <每笔提交固定的UUID>。

```json
{"rawQrCode":"ID.1794814541901|1@@PT.PTHG01260700005@@LN.1000@@MT.A10000087@@LT.L26070146@@QT.100@@PG.100","boxNumber":"1794814541901|1","countedLocation":"0700Q0001","countedQtyStu":99}
```

箱内数量改成 nullable decimal，**未填写会返回 400 COUNT_QUANTITY_REQUIRED**，人工输入99时保存 QTYSTU_0=QTYPCU_0=99，不会从二维码 QT.100 带入。数量为0当前允许，负数或 numeric(28,13) 超出范围拒绝；PCS 整数要求尚待业务确认。标签 STA_0 无任何白名单限制。

写入前后端重查盘点单（当前仅 CUNSTA_0=0 允许）、库位、完整二维码及箱号；只支持物料标签 STU=PCU、系数=1 的 1:1 单位场景，其他返回 409 UNIT_MAPPING_UNSUPPORTED。不能自动换算两数量列。

## 原子事务、重复与幂等

后端使用一个 SQL Server Serializable 事务和 sp_getapplock 跨 API 进程锁定环境+盘点单+箱码；先查持久化请求，命中且载荷哈希相同就返回原结果（HTTP 200）；同 UUID 换了内容返回 409。之后在锁内再次查询 XCOUTD 的同单同箱记录，有重复即 409 BOX_ALREADY_RECORDED，提示“该箱号已扫描！”。

通过审核后 INSERT XCOUTD，再在**同一事务** INSERT dbo.StockCountSubmission 幂等记录，COMMIT 成功才返回 HTTP 201 status=Committed。ROWID 从数据库生成并作为字符串返回；所有 NOT NULL 业务列显式赋值、AUUID/时间在 SQL Server 生成。PALNUM_0 和 ECCVALMAJ_0 填空、PDAID_0 填 StockCount、审计用户由现场配置，**这些真实 X3 映射尚未现场审批**。若 XCOUTD.ROWID 不是 IDENTITY，接口返回 ERP_SCHEMA_UNSUPPORTED，禁止猜测 MAX(ROWID)+1。

幂等记录表必须与 ERP 表在同一 SQL Server 数据库：SQLite server-logs.db **只用于程序日志，不能用于原子提交幂等**。迁移为 [001_stockcount_submission.sql](../sql/migrations/001_stockcount_submission.sql)，仅 DBA 备份并授权后执行一次；API 不在启动时自动创建或改动表结构。

GET /api/v1/submissions/{requestId}?countNumber=1712474746529 要携带同一环境/设备请求头：200 代表确认已保存，404 代表当前未查到已提交记录，503 代表结果无法确认。404 后重试也必须使用原 UUID；503 时绝不能据此推断保存失败。

## 启用 XTEST 测试写入（默认关闭）

1. DBA 在 XTEST 执行 [verify_xcountd_write_prerequisites.sql](../sql/read_only/verify_xcountd_write_prerequisites.sql) 只读检查，核对 19 列、ROWID IDENTITY、ERP 触发器、PCU/STU 和审计规则。
2. 在授权维护窗口执行 SQL Server 幂等迁移 [001_stockcount_submission.sql](../sql/migrations/001_stockcount_submission.sql)，给 API 账号最小权限：XTEST 五表必要 SELECT + XTEST.XCOUTD INSERT + dbo.StockCountSubmission SELECT/INSERT；不要为测试账号赋 COB INSERT。
3. Windows Tauri 后端管理器数据库设置页填写 X3 审计用户（1–5字符）、勾选“XTEST结构已确认”和“允许 XTEST 写入”，停止并重新启动 C# Sidecar。
4. 前台 MAUI 可选择 Test 环境扫码、人工输入数量提交；正式 Production 始终禁写，桌面 UI 不暴露生产开关。
5. 在 XTEST 对照检查 201/200/409/503、并发、失败回滚、STA 任意状态、同一请求 UUID 重放，业务方验收后再考虑独立的生产变更。

## 客户端流程

MAUI 的 CountScanWorkflow 与 CountingPage 已接入海康广播、API 扫码、预检查重复和提交：未扫库位扫箱 → 弹窗“请先扫描盘点库位！”；重复 → 弹窗“该箱号已扫描！”且不填充；数量留空不能提交；成功 → “提交成功！”约一秒后清箱数据及数量，不清单号、名称、库位。网络结果未知时保留内容与请求 UUID，通过 GET 查询。

**尚未覆盖**：Android 系统杀进程后的草稿/UUID 持久恢复、实际海康设备广播参数与 MAUI 真机验证；网络外部老 PDA/X3 写入渠道与本 API 的并发一致性也需现场测试。

## 错误码

| HTTP | code | 含义 |
| --- | --- | --- |
| 400 | COUNT_QUANTITY_REQUIRED | 未填写数量 |
| 400 | COUNT_QUANTITY_INVALID | 数量无效 |
| 409 | BOX_ALREADY_RECORDED | 当前单箱码已盘 |
| 409 | IDEMPOTENCY_PAYLOAD_MISMATCH | 同 UUID 不同请求 |
| 409 | COUNT_NOT_WRITABLE | 该状态不允许写入 |
| 409 | UNIT_MAPPING_UNSUPPORTED | 单位系数不兼容 |
| 403 | PRODUCTION_WRITE_DISABLED | 正式帐套写入关闭 |
| 503 | ERP_WRITE_UNAVAILABLE | 未启用 XTEST 写入 |
| 503 | SCHEMA_NOT_APPROVED | 现场尚未核准字段/单位与写入逻辑 |
| 503 | ERP_SCHEMA_UNSUPPORTED | X3 表结构不满足适配器条件 |
| 503 | SUBMISSION_OUTCOME_UNKNOWN | 提交结果未知，保持草稿 |
| 404 | SUBMISSION_NOT_FOUND | 无已提交记录，不能生成新 UUID 盲重试 |

**验收状态**：代码已提交、静态对照完成；实际编译、真机运行、SQL Server XTEST 写入/回滚、多个 PDA 并发测试和正式业务验收均待执行。
