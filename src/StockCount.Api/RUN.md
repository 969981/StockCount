# StockCount.Api — 启动、联调与部署（.NET 10）

> 更新：2026-11-23。**已创建 .NET 10 只读 API 源码骨架，未在当前环境实际编译、运行或连接 SQL Server。** 这里的命令供 Windows 开发机验证；只有执行成功后才能说已经运行。正式数据库仍保持只读，任何生产写入都未实现。

## 1. 已有代码与范围

- 项目：`StockCount.Api`（Minimal API）、`StockCount.Contracts`（DTO）、`StockCount.Core`（环境白名单）、`StockCount.Infrastructure`（Dapper/SqlClient）。
- 已写接口：`GET /health`、`GET /ready`、`GET /api/v1/inventory-counts`、`GET /api/v1/inventory-counts/{countNumber}`、`POST /api/v1/locations/resolve`、`POST /api/v1/labels/resolve`、`GET /api/v1/inventory-counts/{countNumber}/items`。
- 写入防护：`POST /api/v1/inventory-counts/{countNumber}/items` 对 Production 返回 403，对 Test 返回 503，**无真实写入**；`GET /api/v1/submissions/{requestId}` 也返回 503（幂等查询尚未实现）。
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

这个 `.slnx` 当前只有非 Android 的四个后端项目，单独构建不需要安装 MAUI Android workload。

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

## 7. 下一阶段未完成

- `StockCount.Pad` Android 工程和真实海康扫码广播联调。
- `XTEST`/`COB` 的真实库结构核验、只读 SQL 联调与两环境差异测试。
- 数据库真实写入通道（优先经验证的 Sage X3 服务）、ROWID/AUUID/审计字段处理及事务。
- 同单同箱防重是否允许复盘、持久化幂等/双设备并发、STU/PCU 两列同值与单位系数的受控验证。
- 编译、单测、API 自动化测试和真实设备/SQL Server 集成测试：**本次未执行，不能标为通过**。

相关文件：[根需求](../../requirements.md)、[API 契约](../../docs/API_CONTRACT.md)、[环境安全](../../docs/SECURITY_AND_ENVIRONMENTS.md)、[字段映射](../../docs/DATABASE_MAPPING.md)。
