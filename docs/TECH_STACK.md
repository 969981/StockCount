# 技术栈与工程约定

**阶段：V1 规范，不代表依赖已安装或已构建。** 2026-11-23

## 1. 统一栈

| 部位 | 采用技术 | 说明 |
| --- | --- | --- |
| 客户端 | C#、.NET 10、.NET MAUI Android、XAML | 只支持 Android 的工业终端，不引入 WebView 主应用 |
| 视图状态 | MVVM + `CommunityToolkit.Mvvm` | ObservableProperty / RelayCommand |
| 扫码 | Android BroadcastReceiver + `IScannerService` | 海康扫码协议需真机确认，Mock 配置用于开发 |
| HTTP | `IHttpClientFactory`、`System.Text.Json` | API 版本化，CancellationToken 与错误码 |
| 客户端配置 | Preferences / SQLite | 环境、扫描设置、待确认请求；不可存 SQL 凭据 |
| API | ASP.NET Core 10 Minimal API 或 Controllers（二选一在首个实现 PR 固化） | OpenAPI、ProblemDetails、健康检查 |
| 领域 | `StockCount.Core` | 无 MAUI、ASP.NET 或 SQL 依赖 |
| SQL 访问 | `Microsoft.Data.SqlClient` + Dapper | 参数化、短连接、事务和独立仓储 |
| 数据源 | Sage X3 / SQL Server | `sagex3.COB` 正式、`sagex3.XTEST` 测试 |
| 测试 | xUnit；集成测试 Mock/隔离 XTEST | unit/contract/api/device test |
| 构建 | `.slnx`、`dotnet` CLI、GitHub Actions | SDK/MAUI workload 版本实际安装情况以 CI 记录为准 |

## 2. 项目约定

- `Nullable=enable`；`ImplicitUsings=enable`；公共 DTO 对可选字段显式 nullable，避免默认值掩盖缺失。
- `decimal` 用于 PCU、STU 和换算；SqlClient / Dapper 必须带真实 precision/scale 参数。不要先转 double 再比较。
- 环境 DTO 为显式 enum（JSON string），严禁客户端传任意 Schema 名。
- 服务端只有 Infrastructure 可以接触 SQL Server 表名和 X3 SQL 细节；Core 只使用业务模型。
- `DateTimeOffset` 用于 API 时间与操作事件，数据库 `datetime` 的时区解释需通过约定明确，不能擅自假设 UTC。
- 所有对写入有副作用的操作必须 POST（不能以 GET 触发副作用）。

## 3. 计划路径

```text
src/StockCount.Pad/
src/StockCount.Api/
src/StockCount.Contracts/
src/StockCount.Core/
src/StockCount.Infrastructure/
tests/StockCount.Core.Tests/
tests/StockCount.Api.Tests/
tests/StockCount.Pad.Tests/
StockCount.slnx
```

其中 `Pad.Tests` 必须采用可独立编译的 .NET 单测项目验证 ViewModel/适配抽象，真机 Android 测试另行实施；不要让 CI 在未安装 MAUI Android 工作负载的环境声称完成 APK 编译。

## 4. NuGet 与版本策略

统一采用 .NET 10 兼容的稳定 NuGet 包，锁定可复现版本（建议 `Directory.Packages.props`）；升级包需要先执行 build/test，并更新兼容性说明。未经验证不硬编码某个第三方包小版本到业务规范里。

## 5. 开发/构建命令（在工程建立后）

```bash
dotnet --info
dotnet workload list
dotnet restore StockCount.slnx
dotnet build StockCount.slnx -c Release
dotnet test StockCount.slnx -c Release
# Android SDK + MAUI workload 已配置才发布 APK：
dotnet publish src/StockCount.Pad/StockCount.Pad.csproj -c Release -f net10.0-android
```

上述命令只是规划中的执行步骤；**当前仓库尚未创建这些项目**，现阶段不能据此声称构建成功。

## 6. 公共配置

- 可以提交 `appsettings.Example.json` 和文档里的占位符。
- 不可提交含内网地址或凭据的 `appsettings.*.json` 真值、APK 签名私钥或测试/生产 SQL 用户密码。
- CI 必须使用测试替身/机密存储；不得让公开 PR 工作流访问 ERP 生产凭据。

关联：[架构](../architecture.md) · [项目说明](PROJECT_GUIDE.md) · [安全与环境](SECURITY_AND_ENVIRONMENTS.md)。