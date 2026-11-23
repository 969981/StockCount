# StockCount.Api — 启动、联调与部署（.NET 10）

> 更新：2026-11-23。**已创建 .NET 10 只读 API + 受控 XTEST 事务写入与幂等查询源码。尚未在当前环境编译、运行或连接 SQL Server。** 这里的命令供 Windows 开发机验证；只有执行成功后才能说已经运行。正式数据库仍保持只读，Tauri Sidecar 管理端的正式 COB 写入默认关闭；测试 XTEST 写入需单独配置和审批。

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
dotnet restore StockCount.slnx
dotnet build StockCount.slnx -c Debug
```

这个 `.slnx` 当前是非 Android 的后端和纯 C# 单测项目（MAUI Android 单独编译），单独构建不需要安装 MAUI Android workload。

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
- POST 库位校验当前**只支持扫码文本恰好为 STOLOC.LOC_0**，且使用表头站点+仓库+库位复合定位；`LC.` 等现场协议仍需在 PDA 真机验证后加入解析器，不能擅自截掉前缀。
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

推荐由 Tauri 管理器填写审计用户编码并启用 XTEST 写入（不能从 PDA 开启）。独立 CLI 模式可使用以下配置选项，但只能在已验证的测试库使用：

```powershell
$env:STOCKCOUNT_ENABLE_TEST_WRITES = "true"
$env:STOCKCOUNT_XTEST_SCHEMA_REVIEWED = "true"
$env:STOCKCOUNT_X3_AUDIT_USER = "PDA" # 仅示例：由现场 X3 负责人批准
```

仅实际完成 SQL Server COMMIT 后返回 HTTP 201，复用同 UUID 且相同载荷返回200，未知结果必须以原 UUID 调用 GET /api/v1/submissions/{requestId}?countNumber=... 查询。完整文档见 [WRITE_FLOW_IMPLEMENTATION.md](../../docs/WRITE_FLOW_IMPLEMENTATION.md)。

**正式环境绝不可直接使用测试帐套的启用流程。** 生产需要独立的变更审批/账户权限审计；Tauri 管理界面没有生产启用按钮。

## 8. 下一阶段未完成

- `StockCount.Pad` Android MAUI 页面/扫码广播源码已添加，尚需实际 Android SDK 编译与海康 PDA 真机联调。
- `XTEST`/`COB` 的真实库结构核验、只读 SQL 联调与两环境差异测试。
- 真实 XTEST 插入适配器已编码，**现场仍需确认** X3 原生服务与直接 INSERT 的适配差异、ROWID IDENTITY、审计字段/触发器和 SQL Server 事务；不符合条件必须拒绝。
- 同单同箱锁与 SQL Server 同事务幂等表已编码，尚未完成两个 PDA 的并发测试、历史复盘规则审批及单位 1:1 验证；STU/PCU 两列强制同值。
- 编译、单测、API 自动化测试和真实设备/SQL Server 集成测试：**本次未执行，不能标为通过**。

相关文件：[根需求](../../requirements.md)、[API 契约](../../docs/API_CONTRACT.md)、[环境安全](../../docs/SECURITY_AND_ENVIRONMENTS.md)、[字段映射](../../docs/DATABASE_MAPPING.md)。
