---
title: "技术栈与工程约定"
aliases:
  - "StockCount · docs / TECH_STACK"
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

# 技术栈与工程约定

**阶段：V1 技术规范。仓库已有部分解决方案与后端源码骨架，但不能仅凭文件存在推断依赖已安装、构建或设备联调通过。** 2026-11-23

## 1. 统一栈

| 部位 | 采用技术 | 说明 |
| --- | --- | --- |
| 客户端 | C#、.NET 10、.NET MAUI Android、XAML | 只支持 Android 的工业终端，不引入 WebView 主应用 |
| Windows 后端服务管理器 | Tauri 2 / React / TypeScript / Rust | 仅供 Windows 服务器/管理电脑；Sidecar 托管独立 C# API，替代已撤销的 WPF 草稿 |
| 服务日志 | Microsoft.Data.Sqlite + ILogger（C#）/ rusqlite（Rust 只读） | 本机 SQLite WAL；不保存业务盘点数据 |
| 服务端配置 | 本地 JSON + Windows Credential Manager | DB/证书密码、设备密钥独立安全存储；PDA 不能获得数据库凭据 |
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

## 5. 开发/构建命令（API 工程已创建，MAUI 工程待建）

```bash
dotnet --info
dotnet workload list
dotnet restore StockCount.slnx
dotnet build StockCount.slnx -c Release
dotnet test StockCount.slnx -c Release
# Android SDK + MAUI workload 已配置才发布 APK：
dotnet publish src/StockCount.Pad/StockCount.Pad.csproj -c Release -f net10.0-android
```

上述命令是规范中的验证步骤；仓库中的解决方案与部分后端项目骨架已经开始建立，但 **MAUI Pad、测试项目、Android 工作负载和编译结果仍须以当前代码/实际 CI 为准**，不能据此声称已完成验收。

## 6. 公共配置

- 可以提交 `appsettings.Example.json` 和文档里的占位符。
- 不可提交含内网地址或凭据的 `appsettings.*.json` 真值、APK 签名私钥或测试/生产 SQL 用户密码。
- CI 必须使用测试替身/机密存储；不得让公开 PR 工作流访问 ERP 生产凭据。

关联：[架构](../architecture.md) · [项目说明](PROJECT_GUIDE.md) · [安全与环境](SECURITY_AND_ENVIRONMENTS.md)。

---

## 🧭 知识库导航

- 主题索引：[[docs/development/00-研发规范地图|研发规范地图]] · [Markdown](development/00-%E7%A0%94%E5%8F%91%E8%A7%84%E8%8C%83%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
