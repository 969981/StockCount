---
title: "StockCount V1 REST API 契约草案"
aliases:
  - "StockCount · docs / API_CONTRACT"
created: 2026-11-23
updated: 2026-11-23
status: reference
area: development
tags:
  - StockCount
  - development
  - docs
up: "[[docs/development/00-研发规范地图]]"
---

# StockCount V1 REST API 契约草案

> **状态：只读 API、XCOUTD 受控事务写入与 SQL Server 持久幂等查询的源码均已提交，写入默认关闭；尚未执行 Windows 编译、XTEST 真库写入及并发回滚联调。** 使用 ASP.NET Core 10；客户端只发送业务 DTO，不指定任意 SQL 表/Schema。最后修订：2026-11-23。

> **0.1 实现说明**：当前代码使用 Minimal API，`cursor` 为非负数字偏移量字符串；`nextCursor` 可供继续加载下一页。只有目标帐套单独启用写入、通过其 Schema 核验且目标单据状态为已批准的 0 时 `canSubmit=true`。XTEST 与 COB 使用相同的 API 与事务流程，但具有独立启用开关，二者默认均 false。读取需要请求头 `X-StockCount-Environment: Test|Production`；`/health` 无需数据库，`/ready` 测试数据库连通。Windows 启动见 [RUN.md](../src/StockCount.Api/RUN.md)。


## 1. 基本约定

- API 根路径 `/api/v1`；JSON camelCase；UTF-8；时间使用 ISO 8601 并在服务端约定时区；数值以 JSON number 与后端 `decimal` 互通，禁止经过 JS double 做精确比较。
- 环境固定为 `Production` 或 `Test`。请求头示例：`X-StockCount-Environment: Test`。后端将它映射到可信 `COB` 或 `XTEST`；拒绝其他取值（400）。
- 每次响应应带 `environment`，保证客户端能核对环境；客户端切换到 Production 不会自行打开写入：服务端必须已独立授权 COB 写入并通过结构与权限核验；未启用时拒绝。
- `X-Request-Id`（或 `Idempotency-Key`）为 POST 保存明细的 UUID，不同业务提交使用新 ID，同一提交重试复用同一 ID。
- 列表使用 `pageSize` + `cursor`（或 `page`，实现时统一确定），展示全部数据可以连续翻页，不能隐式过滤状态。
- 错误采用 `application/problem+json`（Problem Details），扩展 `code`、`traceId`、`environment`；禁止泄漏数据库错误/凭据。

## 2. 端点总览

| 方法 | 路径 | 职责 | V1 |
| --- | --- | --- | --- |
| GET | `/health` | 服务健康/版本；不改变业务数据 | P0 |
| GET | `/api/v1/inventory-counts` | **全量可分页**的已有盘点单列表，允许搜索和排序 | P0 |
| GET | `/api/v1/inventory-counts/{countNumber}` | 单据详情、原始状态、是否允许提交及原因 | P0 |
| POST | `/api/v1/locations/resolve` | 解析/校验扫码库位 | P0 |
| POST | `/api/v1/labels/resolve` | 解析完整箱标签 QR，查物料等 | P0 |
| GET | `/api/v1/inventory-counts/{countNumber}/items` | 本单明细分页查询与重复提示 | P1 / 防重可内部实现 |
| POST | `/api/v1/inventory-counts/{countNumber}/items` | 保存人工实盘数、幂等/事务 | XTEST/COB 均已编码真实 SQL 事务，按帐套独立核验与显式启用 |
| GET | `/api/v1/submissions/{requestId}?countNumber=...` | 查询数据库持久提交记录，避免超时双写 | 查询原有 XCOUTD.AUUID_0，无需附加表 |

**没有** Create / Delete / Close / AdjustStock 端点；未来若追加须单独需求版本。

## 3. 核心 DTO（描述性示例）

### 3.1 盘点单列表

`GET /api/v1/inventory-counts?pageSize=50&search=...`

```json
{
  "environment": "Test",
  "items": [
    {
      "countNumber": "COUNT-DEMO-001",
      "description": "测试盘点",
      "siteCode": "A01",
      "warehouseCode": "WH01",
      "countDate": "2026-11-01",
      "statusCode": 0,
      "countType": "2",
      "canSubmit": true,
      "cannotSubmitReason": null
    }
  ],
  "nextCursor": null
}
```

要求：**可浏览所有状态**；`statusCode` 保留原始码，`countType` 使用字符串（XTEST/COB 类型不一致）。`canSubmit` 是受业务矩阵和服务端授权判断的独立字段；**`0` 允许盘点**、其他状态写入许可待明确。

### 3.2 校验库位

`POST /api/v1/locations/resolve`

```json
{
  "countNumber": "COUNT-DEMO-001",
  "rawCode": "LC.DEMO-01"
}
```

返回 `locationCode`、`siteCode`、`warehouseCode`、`isValid`、`message`。现场需求确认（2026-11-24）：识别 `LC.` 前缀并提取库位值，兼容直接传纯库位编码。长度约束针对提取值，最多 10 字符，不截断。返回 locationCode 为数据库库位值（不含协议前缀），最终写入 XCOUTD.LOC_0 同样使用该值。站点和仓库一致性按 XTEST / COB 的后台配置独立开启，默认均关闭；库位仍须存在于对应帐套，扫码与最终提交使用相同规则。仅在两项均开启时保留精确范围内的重复库位拒绝；任一关闭时，只要满足启用条件的库位存在即可（明细仅保存编码）。siteCode、warehouseCode 仍为盘点表头信息，不表示库位唯一归属。错误码：LOCATION_FORMAT_INVALID（422）、LOCATION_NOT_FOUND（404）、LOCATION_OUTSIDE_WAREHOUSE（422）、LOCATION_AMBIGUOUS（409）、LOCATION_LOOKUP_UNAVAILABLE（503）。

### 3.3 扫描标签

`POST /api/v1/labels/resolve`

```json
{
  "countNumber": "COUNT-DEMO-001",
  "rawQrCode": "ID.BOX-DEMO-01@@PT.DOC-01@@LN.1000@@MT.ITEM-001@@LT.LOT-01@@QT.100@@PG.125"
}
```

```json
{
  "environment": "Test",
  "boxNumber": "BOX-DEMO-01",
  "itemCode": "ITEM-001",
  "itemName": "示例产品",
  "specification": "示例规格",
  "lotNumber": "LOT-01",
  "stockUnit": "PCS",
  "labelQty": 100,
  "qrOriginalQty": 100,
  "labelStatusCode": 9,
  "alreadyRecordedInCount": false
}
```

**`labelStatusCode=9` 也应允许解析**；不依据 STA_0 设置业务拒绝码。数量 `labelQty` 与 `qrOriginalQty` 只供参考，前端实盘输入初始应为空。查箱信息时以服务器 `XLABELDTL` 与 `ITMMASTER` 为准，不将客户端拆出的字段当可信数据库事实。若暂无法解析某种二维码，明确返回错误而非虚构数量。

### 3.4 保存实盘数量

`POST /api/v1/inventory-counts/COUNT-DEMO-001/items`

请求头 `X-StockCount-Environment: Test`、`Idempotency-Key: <UUID>`

```json
{
  "rawQrCode": "ID.BOX-DEMO-01@@PT.DOC-01@@LN.1000@@MT.ITEM-001@@LT.LOT-01@@QT.100@@PG.125",
  "boxNumber": "BOX-DEMO-01",
  "countedLocation": "LOC-DEMO01",
  "countedQtyStu": 99
}
```

注意：`boxNumber` 只能作为交叉校验字段，服务器以标签解析真值为准。**请求仅有一个人工数量 `countedQtyStu=99`；服务端必须同值保存 `QTYSTU_0=99`、`QTYPCU_0=99`，禁止根据二维码 QT=100、当前标签数量或系数重算任一实盘数。** `PCU_0` / `PCUSTUCOE_0` 与该规则的兼容性需在 XTEST 验证。

成功响应（201 或幂等重试200，具体规范可在实现 PR 固化）：

```json
{
  "environment": "Test",
  "countNumber": "COUNT-DEMO-001",
  "boxNumber": "BOX-DEMO-01",
  "savedQtyStu": 99,
  "savedQtyPcu": 99,
  "stockUnit": "PCS",
  "detailId": "123456",
  "requestId": "<UUID>",
  "status": "Committed"
}
```

**强制**：只有完成实际数据库事务且幂等结果可查询才响应 `Committed`。真实 ID 可能是 numeric(38,0)，跨语言 DTO 用字符串避免精度丢失。未知结果需在 `GET /submissions/{requestId}` 中查验，参数需绑定当前环境且不能跨环境访问。

## 4. 幂等/并发契约

- 幂等唯一键推荐 `(Environment, CountNumber, RequestId)`；相同键 + 同一有效载荷重放返回原持久化结果。
- 同键但请求体内容不同 → 409 `IDEMPOTENCY_PAYLOAD_MISMATCH`。
- 同单箱码已存在 → 按最终确认的同单防重/复盘规则返回 409 `BOX_ALREADY_RECORDED` 或授权复盘响应。**不得静默新增重复行**。
- 在多个 API 进程/两台 PDA 同时提交时必须可靠；单机 Semaphore 不是业务一致性保障。
- 服务端启用写入前必须解决事务与 X3 数据副作用；接口定义本身不是写库授权。

## 5. 稳定错误码

| HTTP | code | 场景 |
| --- | --- | --- |
| 400 | `ENVIRONMENT_INVALID` | 环境非固定枚举 |
| 400 | `BARCODE_UNSUPPORTED` | 不认识/无法解析二维码类型 |
| 400 | `COUNT_QUANTITY_REQUIRED` | 箱内数量缺失，不能以默认0代替 |
| 400 | `COUNT_QUANTITY_INVALID` | 空值/精度/值域违反批准规则 |
| 404 | `COUNT_NOT_FOUND` | 没有该盘点单 |
| 404 | `LABEL_NOT_FOUND` | 完整二维码及可提取箱号均未匹配标签 |
| 409 | `LABEL_AMBIGUOUS` | 完整二维码或回退箱号匹配多条标签，拒绝任意选取 |
| 422 | `LOCATION_INVALID` | 库位不存在/不属于允许范围/超长 |
| 422 | `BOX_NUMBER_TOO_LONG` | PID 无法安全写入 BOXNUM(20) |
| 409 | `COUNT_NOT_WRITABLE` | 单据可浏览但不允许该状态提交 |
| 409 | `BOX_ALREADY_RECORDED` | 同单箱号重复，按批准规则 |
| 409 | `IDEMPOTENCY_PAYLOAD_MISMATCH` | 同 RequestId 重试不同载荷 |
| 403 | `PRODUCTION_WRITE_DISABLED` | COB 写入尚未由管理员在本机启用 |
| 503 | `ERP_WRITE_UNAVAILABLE` | 受控写入渠道不可用；不可假报成功 |

**不要定义 `LABEL_STATUS_FORBIDDEN` 作为因 STA_0 拒绝的规则。**

## 6. 实现与现场启用补充

- 事务与幂等：[WRITE_FLOW_IMPLEMENTATION.md](WRITE_FLOW_IMPLEMENTATION.md)；需要 DBA 单次部署 `sql/migrations/001_stockcount_submission.sql`，而非写入 SQLite 日志。
- POST 新提交成功 201，重复同 UUID/载荷为 200，`replayed=true`。GET 提交结果必须加 `?countNumber=...` 且携带同环境请求头。
- Tauri 管理器现有 **XTEST** 和 **COB** 两组独立“结构已核查/允许写入”开关；两者需独立核验 ROWID、单位规则、审计字段和 SQL 权限。后端按请求头 Test/Production 路由；服务未授权则禁止该帐套写入。
- 守住 `ROWID IDENTITY`、1:1 单位与合法单据状态等适配器先决条件，验证失败明确报错且不能假称提交成功。

## 7. 契约稳定性测试

- 统一 DTO snapshot / OpenAPI/schema 测试，C# 序列化一致；
- 客户端校验每次响应 `environment` 匹配当前设置；
- 数量 decimal 精度及历史 ID 不丢失；**保存响应的 `savedQtyStu` 与 `savedQtyPcu` 必须都等于唯一人工输入数**；
- 所有状态 0/1 的列表均可见；
- 标签 `STA_0=4/9` 不触发状态拒绝；
- 99 实盘不被 100 参考数量覆盖，且实际两列都为99；
- 网络断开重放同键得到同一结果。

关联：[需求](../requirements.md) · [架构](../architecture.md) · [字段映射](DATABASE_MAPPING.md) · [安全](SECURITY_AND_ENVIRONMENTS.md)。

---

## 🧭 知识库导航

- 主题索引：[[docs/development/00-研发规范地图|研发规范地图]] · [Markdown](development/00-%E7%A0%94%E5%8F%91%E8%A7%84%E8%8C%83%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)

### 箱标签匹配策略（现场更新）
扫码解析和提交事务共用 `LabelLookupPolicy`：优先以完整二维码匹配当前帐套 `XLABELDTL.QRCOD_0`；仅零匹配时提取唯一完整的 `ID.` 字段，以 `PID_0` 查询。ID 长度 1–20，仅允许 ASCII 字母数字及 `| - _ .`，不截断。多条匹配返回 409，数据库异常不触发回退；提交复核数据库返回箱号、重复箱、单位及实盘量，原扫码值及 UUID 保留。不按 STA_0 过滤。

### 2026-11-25 现场写入调整
不再依赖辅助幂等表。新提交在原有 XCOUTD.AUUID_0 存储请求 UUID（SQL Server binary(16) 转换），同帐套、同单号、同 UUID 且 PDAID_0=StockCount 查询原记录。重放核对箱号、库位及两数量列，不再保存完整请求哈希。正常新扫码及事务内仍按 CUNNUM_0 + BOXNUM_0 拒绝重复。旧版随机 AUUID 的记录无法按旧请求 UUID 追溯，仍受同单同箱防重保护。无需执行旧迁移脚本。

### 2026-11-25 盘点计量单位
提交以标签 STU 为唯一计量单位，XCOUTD.PCU_0=STU，PCUSTUCOE_0=1，QTYSTU_0=QTYPCU_0=人工输入数量。不再校验原标签 PCU 或系数。保留 409 UNIT_MAPPING_UNSUPPORTED 兼容码，仅用于存货单位无效。详见 [历史研究](database/COUNT_UNIT_MAPPING_RESEARCH_2026-11-25.md)。
