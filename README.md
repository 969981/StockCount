---
title: "StockCount"
aliases:
  - "StockCount · README"
created: 2026-11-23
updated: 2026-11-23
status: reference
area: home
tags:
  - StockCount
  - home
  - docs
up: "[[docs/development/00-研发规范地图]]"
---

# StockCount

> 基于 .NET 10 的工业 Android PDA 库存盘点应用与 ASP.NET Core API，连接既有 Sage X3 / SQL Server 盘点数据。

**当前阶段：Android MAUI Debug APK 已在 GitHub Actions #46 编译成功，29 项 Core 测试通过；Tauri Windows Setup/Portable 正在 CI 验证。海康真机扫码与 Sage X3 XTEST/COB 实际写入尚待现场验收；两套写入默认关闭、由现场分别授权。**

## Windows 本地编译、部署与源码打包

- [完整 PowerShell 7 使用手册](docs/LOCAL_BUILD_DEPLOY.md)：环境准备、编译 Android APK/Windows Setup/Portable、ADB 安装、Windows 免安装升级和源码归档。
- [一键本地编译](scripts/build-local.ps1)：`-Target All|Windows|Android`，失败即停止，输出 `dist/local/` 与 SHA256。
- [部署到本机或海康 PDA](scripts/deploy-local.ps1)：Windows Setup 向导、Portable 保留 SQLite 日志并备份旧程序、Android `adb install -r -t`。
- [打包完整 Git 源码](scripts/package-source.ps1)：从指定 Commit/Tag 导出 `dist/source/StockCount-Source-v*.zip`，不包含 `.git`、本地密钥或编译文件。
- [Windows 脚本回归测试](.github/workflows/local-build-tooling-check.yml)：检查 PowerShell 语法、免安装升级和源码 ZIP 完整性。

```powershell
pwsh -File .\scripts\build-local.ps1 -Target All -Version 0.2.0-local
pwsh -File .\scripts\package-source.ps1 -Ref HEAD -Version 0.2.0-local
```
## 跨平台安装包与 Releases

- [GitHub Releases](https://github.com/969981/StockCount/releases)：同一预发布将包含已签名测试 APK、Windows Setup、Windows Portable ZIP 和 SHA256SUMS，只有 Android+Windows 构建同时通过才自动创建。
- [统一发布构建流水线](.github/workflows/publish-stockcount.yml) · [发布/免安装使用说明](docs/RELEASE_PACKAGING.md)。
- Windows Portable 解压到当前用户可写的目录后运行 `Start-StockCount.cmd`；`StockCount.Api.exe` 必须与主 EXE 同目录，`server-logs.db` 也在该目录。系统需要 WebView2 Runtime。
- Android 在 [CI #46](https://github.com/969981/StockCount/actions/runs/45110321348) 已生成可安装 **Debug 签名** APK；公开 Releases 的整套资产以统一发布流程通过为准。发布包不是正式企业代码签名或已完成真实 ERP 验收的生产版本。

## 一期产品范围

- **无应用内用户登录**。启动后自动进入盘点单列表，设置页可切换 **Production（正式帐套 `sagex3.COB`）/ Test（测试帐套 `sagex3.XTEST`）**。
- 展示当前帐套中**全部已有盘点单**（采用分页加载，不按状态预先隐藏）；选择一张单据进入盘点。
- 先扫描实际库位，再扫描箱标签二维码；查询物料、批次、单位并**人工输入一次 STU 实盘数量**，由服务端校验并**同值保存到 `XCOUTD.QTYSTU_0` 与 `XCOUTD.QTYPCU_0`**。
- **不按 `XLABELDTL.STA_0` 拦截盘点**；状态仍在接口中返回、可显示和审计。
- 一期**不创建/关闭盘点单**，不修改 `XLABELDTL.QTY_0`，不执行库存调整/ERP 过账。
- 端到端写入须先在 XTEST 受控验证；正式与测试写入默认关闭，分别完成对应帐套验收后独立启用。

## 规范入口（AI / 人类开发者共同遵守）

| 文件 | 权威范围 |
| --- | --- |
| [requirements.md](requirements.md) | 最新产品需求、行为和验收 |
| [architecture.md](architecture.md) | 分层设计、状态与环境隔离 |
| [AGENTS.md](AGENTS.md) | 全仓 AI 编程硬性约束 |
| [OPENAI.md](OPENAI.md) | OpenAI / Codex 工作规范与执行模板 |
| [docs/TECH_STACK.md](docs/TECH_STACK.md) | 语言、依赖、项目结构和构建约定 |
| [docs/PROJECT_GUIDE.md](docs/PROJECT_GUIDE.md) | 项目说明、职责、开发交付流程 |
| [docs/DATABASE_MAPPING.md](docs/DATABASE_MAPPING.md) | 核心字段/DTO 映射与业务风险 |
| [docs/database/SCHEMA_INDEX.md](docs/database/SCHEMA_INDEX.md) | **七表字段与 SQL 总索引**（五张盘点业务表 + `ATABZON`/`ATEXTE` 字典表；未核实物理列如实标注） |
| [docs/API_CONTRACT.md](docs/API_CONTRACT.md) | V1 API 契约草案、错误和幂等 |
| [docs/SCANNER_INTEGRATION.md](docs/SCANNER_INTEGRATION.md) | Android 工业扫描接入规范 |
| [docs/SECURITY_AND_ENVIRONMENTS.md](docs/SECURITY_AND_ENVIRONMENTS.md) | 无登录时的网络安全和正式/测试隔离 |
| [docs/TEST_PLAN.md](docs/TEST_PLAN.md) | 测试矩阵与上线门禁 |
| [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | 前后端 AI 分阶段任务、完成定义 |
| [docs/DECISIONS.md](docs/DECISIONS.md) | 已定规则与仍待决策事项 |

### 规范优先级

用户最近明确确认的要求 > `requirements.md` / `docs/DECISIONS.md` > `architecture.md` / `docs/DATABASE_MAPPING.md` > 其他专题文档。**已确认事实、设计目标、待验证假设须分开**；不得将旧 APK 的行为当成新 StockCount 的强制实现。

## 解决方案规划（部分源码骨架已建立）

```text
StockCount.slnx
src/
  StockCount.Pad/             # .NET MAUI Android
  StockCount.Api/             # ASP.NET Core Web API
  StockCount.Contracts/       # HTTP DTO
  StockCount.Core/            # 盘点业务规则与接口
  StockCount.Infrastructure/  # SQL Server/Dapper 与 Sage X3 适配
tests/
  StockCount.Core.Tests/
  StockCount.Api.Tests/
  StockCount.Pad.Tests/
docs/
```

## Windows 后端服务管理器（Tauri 2）

- **已改为 Tauri 2 + React + TypeScript**：一键启动/停止 C# ASP.NET Core API Sidecar，数据库参数在本机可读/可改，日志写入 SQLite；不使用 WPF。
- [Tauri Server Console 源码与 Windows 使用手册](desktop/server-console/README.md) · [UI/架构设计](docs/SERVER_HOST_UI.md)
- [Windows 构建工作流](.github/workflows/tauri-server-console.yml) · [Sidecar 自包含打包脚本](scripts/build-tauri-sidecar.ps1)
- 配置：`%LOCALAPPDATA%\StockCount\ServerHost\server-settings.json`（不含密码）；密码和设备 Key 由 Windows Credential Manager 保存。日志：**Tauri 程序 EXE 同目录**的 `server-logs.db`，SQLite WAL 辅助文件也在旁边。
- API 正式/测试写入**均可独立开启，默认均关闭**。界面/原生代码已提交，**本轮尚未确认 Windows 编译和安装包通过测试**。
## 受控盘点写入与 PDA 客户端

- [写入接口、DDL 幂等表和双帐套启用验收指南](docs/WRITE_FLOW_IMPLEMENTATION.md)；[只读 XTEST 写入前检](sql/read_only/verify_xcountd_write_prerequisites.sql)；[只读 COB 写入前检](sql/read_only/verify_cob_write_prerequisites.sql)；[DBA 单次幂等迁移](sql/migrations/001_stockcount_submission.sql)。
- [MAUI Android 扫码页面](src/StockCount.Pad/CountingPage.xaml)、[扫码状态机](src/StockCount.Core/CountScanWorkflow.cs)、[重复箱/数量/幂等测试](tests/StockCount.Core.Tests/CountScanWorkflowTests.cs)。
- 测试 XTEST 与正式 COB 均已支持受控事务写入；Tauri 管理器具有各自的核验确认与写入开关。需要管理员独立核验对应 Schema、安装 SQL Server 幂等表、配置 SQL 权限后显式启用；代码提交不会自动修改企业数据。

- [APK UI 与交互规范](docs/pda/PDA-UI-UX-设计规范与交互状态.md) · [MAUI UI 重构任务及验收](docs/pda/PDA-UI-开发任务与验收.md)：面向海康 PDA 的单手扫码、草稿防覆盖、1秒成功反馈和断网幂等恢复。文档和对应 MAUI XAML/扫码状态机重构已提交；真实编译与海康真机验收仍待完成。

- [MAUI Android APK 工作流](.github/workflows/pda-android-build.yml)：尝试在 Windows runner 构建可安装调试 APK 并上传 `StockCount-PDA-Android-Debug-APK`。只有 GitHub Actions 显示成功且工件出现后才可认为已产出 APK。

## API 启动与开发现状

- **源码**：[StockCount.Api](src/StockCount.Api/Program.cs)、[SQL 读取层](src/StockCount.Infrastructure/SqlInventoryReadRepository.cs)、[共享 DTO](src/StockCount.Contracts/Dtos.cs)。
- **Windows 运行文档**：[src/StockCount.Api/RUN.md](src/StockCount.Api/RUN.md)：.NET 10 SDK、SQL Server 连接配置、启动、health/ready、示例接口与部署警告。
- **API CI**：[.github/workflows/api-build.yml](.github/workflows/api-build.yml)：无任何数据库密钥的 restore/build；未取得 CI 完成结果前不能称构建成功。
- `POST .../items` 已有受控事务写入代码；XTEST/COB 均有独立开关，默认关闭，需现场验收后启用，当前尚未取得实际 ERP 写入成功的证据。

## 数据库七表文档和只读 SQL

- [七表字段结构和中文标题总索引](docs/database/SCHEMA_INDEX.md)：已确认四张业务表 16 + 19 + 45 + 41 列物理元数据，另有 ITMMASTER 197 条 X3 字典标题，2026-06-30 历史 DDL 包含 258 个物理字段（今日现网尚待核）；`ATABZON` / `ATEXTE` 的完整物理列需用新脚本查询。
- [五张业务表物理列及中文标题](sql/read_only/inspect_five_tables.sql)；[两张字典表完整结构](sql/read_only/inspect_dictionary_tables.sql)；[ATABZON + ATEXTE 中英文标题关联](sql/read_only/query_dictionary_tables.sql)；[五表中文别名抽样](sql/read_only/select_five_tables_chinese_aliases.sql)；[业务关联 SQL](sql/read_only/business_queries.sql)。

## 重要约束

本仓库为公开仓库。**严禁提交真实数据库密码、连接字符串、API 凭据、内网 IP/域名、真实箱码明细、敏感现场日志或客户原始业务数据**。示例必须使用占位符/合成数据。仅客户端切换环境**不等于后端写入授权**。开发和联调优先 XTEST，生产默认只读。

> 具体状态解释、PCU/STU 单位系数兼容性、库位码协议、同单防重和 X3 必填列写入策略尚有未决点；详见 [决策表](docs/DECISIONS.md)。

---

## 🧭 知识库导航

- 主题索引：[[docs/development/00-研发规范地图|研发规范地图]] · [Markdown](docs/development/00-%E7%A0%94%E5%8F%91%E8%A7%84%E8%8C%83%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](docs/00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- 设备：[[docs/pda/00-PDA扫码知识地图|海康 PDA 扫码地图]] · [Markdown](docs/pda/00-PDA%E6%89%AB%E7%A0%81%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- 数据库：[[docs/database/00-数据库知识地图|Sage X3 数据库地图]] · [Markdown](docs/database/00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
