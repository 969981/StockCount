---
title: "StockCount API · 运行与联调"
aliases: ["StockCount API RUN", "ASP.NET Core 盘点 API"]
tags: [StockCount, ASPNETCore, SQLServer, HTTP]
status: active
---

# StockCount.Api — 启动、联调与部署（.NET 10）

> 更新：2026-11-23。**已创建 .NET 10 只读 API + 受控 XTEST 事务写入与幂等查询源码。尚未在当前环境编译、运行或连接 SQL Server。** 这里的命令供 Windows 开发机验证；只有执行成功后才能说已经运行。正式 COB / 测试 XTEST 均可独立启用受控事务写入，两套帐套首次均默认关闭；需要单独配置、数据库核验与现场批准。

## 1. 已有代码与范围

- 项目：`StockCount.Api`（Minimal API）、`StockCount.Contracts`（DTO）、`StockCount.Core`（环境白名单）、`StockCount.Infrastructure`（Dapper/SqlClient）。
- 已写接口：`GET /health`、`GET /ready`、`GET /api/v1/inventory-counts`、`GET /api/v1/inventory-counts/{countNumber}`、`POST /api/v1/locations/resolve`、`POST /api/v1/labels/resolve`、`GET /api/v1/inventory-counts/{countNumber}/items`。
- 写入与幂等：`POST /api/v1/inventory-counts/{countNumber}/items` 已有真正 SQL Server 事务及同单同箱防重，Test 需要 DB 迁移和管理员显式启用；Production 仍默认403。`GET /api/v1/submissions/{requestId}?countNumber=...` 查询同库持久化幂等记录。
- 只允许请求头 `X-StockCount-Environment: Test|Production`，服务端固定映射 `XTEST`/`COB`，所有值参数化；生产写入开关默认 `false`。

## 2. Windows 开发机准备

安装 .NET 10 SDK 和 Visual Studio 2026 / VS Code（任选）；SQL Server 2016+ 的可用网络与**最小权限只读账号**由企业内部运维提供。`dotnet` 属于开发机依赖，PDA 上不需 SDK。

```powershell
git clone https://github.com/969981/StockCount.git
cd StockCount
dotnet --info
dotnet restore StockCount.Backend.slnx
dotnet build StockCount.Backend.slnx -c Debug
```

**注意**：根目录 `StockCount.slnx` 现在包含后端、Avalonia Windows 管理器和 MAUI Android PDA；完整生成需安装 MAUI Android workload。如只调试 API 且不安装 Android SDK，应使用 `dotnet build StockCount.Backend.slnx -c Debug`。Visual Studio API 调试请使用 `StockCount.Api (HTTP)`，`http://127.0.0.1:5080`，不要选 IIS Express/HTTPS；详情见 [Visual Studio HTTP 调试指南](../../docs/VISUAL_STUDIO_HTTP_DEBUG.md)。

## 3. 本地配置 SQL Server（不要提交真值）

两帐套同属数据库 `sagex3`：正式为 Schema `COB`，测试为 `XTEST`，两套服务可共用受控数据库连接，但推荐实施只读 SQL 权限及业务隔离。

```powershell
# 所有尖括号内都是占位符，不是真实数据库信息
$env:ConnectionStrings__SageX3 = "Server=<SQL_SERVER>;Database=sagex3;User ID=<READONLY_USER>;Password=<PASSWORD>;Encrypt=True;TrustServerCertificate=False"
```

建议由企业内部 CA 为 SQL Server 提供受信任的 TLS 证书，不要为连通性而默认关闭服务器证书校验。该环境变量仅设置在当前终端会话；公开仓库不能保存任何实际连接串。

## 4. 启动与实际测试

```powershell
dotnet run --project src/StockCount.Api/StockCount.Api.csproj --no-launch-profile --urls http://127.0.0.1:5080
```

新开一个 PowerShell 终端：

```powershell
$h = @{ "X-StockCount-Environment" = "Test" }
Invoke-RestMethod "http://127.0.0.1:5080/health"
Invoke-RestMethod "http://127.0.0.1:5080/ready" -Headers $h
Invoke-RestMethod "http://127.0.0.1:5080/api/v1/inventory-counts?pageSize=20" -Headers $h
```

- `/health` 返回 Running 只证明进程活着（没有连接数据库）；`/ready` 返回数据库 Reachable 才代表从本进程能执行 SELECT 1。
- 分页请求使用 `pageSize=1..100`、`cursor` 为 `nextCursor` 返回的**数值偏移量字符串**，不会按 `CUNSTA_0` 状态预过滤；这种 OFFSET 分页在实时新增/修改单据时可能出现页边界变化。
- `countType` 在 SQL 中明确 CAST 成 nvarchar，以兼容 XTEST/COB 的类型差异。
- POST 库位校验支持现场确认的 `LC.` + 库位值协议（例如 `LC.DEMO-01`），兼容纯库位值。先去掉一次 LC 前缀，再校验 1–10 字符，按当前帐套 STOLOC.LOC_0 检查存在性；站点及仓库匹配取决于对应帐套的配置，默认均关闭。返回数据库库位值，不含 LC 前缀；区分格式错误、不存在、仓库不匹配、重复记录与查询失败。
- POST 标签解析按完整 `XLABELDTL.QRCOD_0` 过滤，并关联 ITMMASTER 品名/规格，**不按 `STA_0` 过滤**；标签原数量仅作参考，不是实盘输入。
- 如无正确连接串或 SQL 不可达，`/ready` 返回 503；读接口的异常会由 ASP.NET Core 异常处理中间件转换为服务器错误，不能当成正常业务数据。

## 5. 示例扫码接口（使用合成数据）

```powershell
$body = @{ countNumber="COUNT-DEMO-001"; rawQrCode="ID.BOX-DEMO-01@@PT.DOC-01@@LN.1000@@MT.ITEM-001@@LT.LOT-01@@QT.100@@PG.125" } | ConvertTo-Json
Invoke-RestMethod "http://127.0.0.1:5080/api/v1/labels/resolve" -Method POST -Headers $h -ContentType "application/json" -Body $body
```

该测试请求在真实数据库没有虚构标签时会返回 404，这是正常结果；不要把合成数据误当生产真实单据。

## 6. 生产部署（在 API 完成安全门禁之后）

```powershell
dotnet publish src/StockCount.Api/StockCount.Api.csproj -c Release -r win-x64 --self-contained false -o ./publish/api
```

运行机器需安装 .NET 10 ASP.NET Core Runtime。正式使用推荐 IIS 或受控反向代理进行 HTTPS 终止、内网/VPN 访问限制、受控设备认证、审计、备份与健康探测；当前尚未实现 Windows Service 托管，不应直接将开发 localhost HTTP 扩展暴露到公网或工厂全网。

**注意**：`src/StockCount.Api/appsettings.json` 的 AllowedHosts 仅为 localhost/127.0.0.1。实际域名/反向代理部署前要按运维批准修改 Host allowlist，并在可信代理正确转发 Host/HTTPS 信息。API 内不包含 SQL 凭据或设备口令。

## 7. 受控 XTEST 写入启用步骤

运行前先由 DBA 执行只读前检 [verify_xcountd_write_prerequisites.sql](../../sql/read_only/verify_xcountd_write_prerequisites.sql)，并检查 XTEST.XCOUTD.ROWID 是 IDENTITY，旧 ERP 触发器/字段/审计语义兼容。然后在批准窗口执行 [001_stockcount_submission.sql](../../sql/migrations/001_stockcount_submission.sql) 一次性建立同事务幂等表。

推荐由 Tauri 管理器填写审计用户编码，并根据现场批准分别启用 XTEST/COB 写入（不能从 PDA 开启）。独立 CLI 模式可使用以下配置选项，但只能在已验证的测试库使用：

```powershell
$env:STOCKCOUNT_ENABLE_TEST_WRITES = "true"
$env:STOCKCOUNT_XTEST_SCHEMA_REVIEWED = "true"
$env:STOCKCOUNT_X3_AUDIT_USER = "PDA" # 仅示例：由现场 X3 负责人批准
# 经过 COB 独立核验和现场授权后，才能选择启用正式帐套写入：
$env:STOCKCOUNT_COB_SCHEMA_REVIEWED = "true"
$env:STOCKCOUNT_ENABLE_PRODUCTION_WRITES = "true"
```

仅实际完成 SQL Server COMMIT 后返回 HTTP 201，复用同 UUID 且相同载荷返回200，未知结果必须以原 UUID 调用 GET /api/v1/submissions/{requestId}?countNumber=... 查询。完整文档见 [WRITE_FLOW_IMPLEMENTATION.md](../../docs/WRITE_FLOW_IMPLEMENTATION.md)。

**正式环境可以单独启用写入，但不能继承测试帐套的结构核验结果。** DBA 必须运行 [COB 只读前检](../../sql/read_only/verify_cob_write_prerequisites.sql)，确认生产表结构、X3 事务行为及账号权限，然后在 Tauri 管理界面勾选 COB 独立核验/启用开关。

## 8. 下一阶段未完成

- `StockCount.Pad` Android MAUI 页面/扫码广播源码已添加，尚需实际 Android SDK 编译与海康 PDA 真机联调。
- `XTEST`/`COB` 的真实库结构核验、只读 SQL 联调与两环境差异测试。
- 真实 XTEST / COB 插入适配器均已编码，**现场仍需分别确认** X3 原生服务与直接 INSERT 的适配差异、ROWID IDENTITY、审计字段/触发器和 SQL Server 事务；不符合条件必须拒绝。
- 同单同箱锁与 SQL Server 同事务幂等表已编码，尚未完成两个 PDA 的并发测试、历史复盘规则审批及单位 1:1 验证；STU/PCU 两列强制同值。
- 编译、单测、API 自动化测试和真实设备/SQL Server 集成测试：**本次未执行，不能标为通过**。

相关文件：[根需求](../../requirements.md)、[API 契约](../../docs/API_CONTRACT.md)、[环境安全](../../docs/SECURITY_AND_ENVIRONMENTS.md)、[字段映射](../../docs/DATABASE_MAPPING.md)。

## API 请求与响应明细日志

业务 API 的每次请求完成后，写入一条 `StockCount.Api.Logging.ApiExchangeLogging` SQLite 日志：来源 IP/端口、HTTP 方法及路由、帐套、路径/查询入参、允许清单内的 JSON 入参和响应、HTTP 状态、请求耗时及关联编号。正文每侧限 8192 字节，超限或非 JSON 不记录原文；敏感请求头、未知 JSON 字段不写入。已接收的拒绝请求仍记录状态，未经读取的请求正文显示未读取。记录不改变请求/响应字节，不在公网暴露日志接口。旧日志与清理规则不变。真实业务日志必须按企业权限管理，不得上传公开仓库。

## 中文 Swagger 接口文档

API 启动后，浏览器打开 `http://127.0.0.1:5080` 自动跳转中文 Swagger；也可访问 `/docs`、`/swagger`，OpenAPI JSON 位于 `/openapi/v1.json`。局域网监听启用后，使用后台电脑实际局域网 IP。文档和脚本内置，无需互联网/CDN，无需数据库在线即可浏览。文档仅允许直接私有 IPv4 或本机回环来源，业务 API 的设备认证和写入授权不变。在线执行仅支持 GET；POST 包含请求结构及返回说明，但不在文档内直接执行。内部关闭接口不公开。请求头 `X-StockCount-Environment` 选择 Test/Production，默认测试示例 Test；旧版认证使用可选 `X-StockCount-Device-Key`。禁止公网映射。

### 库位一致性校验（可选）

独立环境变量：`STOCKCOUNT_XTEST_CHECK_SITE`、`STOCKCOUNT_XTEST_CHECK_WAREHOUSE`、`STOCKCOUNT_COB_CHECK_SITE`、`STOCKCOUNT_COB_CHECK_WAREHOUSE`，值为 `true` 时启用，默认 `false`。扫码和提交事务内复核共用相同参数化查询。只校验已启用的维度，库位必须存在，两个维度同时启用时保留精确范围重复检查；写入授权、事务、幂等、标签/数量等规则不变。配置在 API 启动时读取；变更后重启 API。
