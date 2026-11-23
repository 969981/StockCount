---
title: "帐套切换与安全边界"
aliases:
  - "StockCount · docs / SECURITY_AND_ENVIRONMENTS"
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

# 帐套切换与安全边界

**重要：V1 取消应用内登录，但仍然会写入 ERP 盘点明细。必须由部署和服务端控制风险。**

## 1. 环境定义

| 用户选择 | 可信服务端枚举 | Sage X3 数据目标 | V1 行为 |
| --- | --- | --- | --- |
| 正式帐套 | `Production` | `sagex3.COB` | 查询允许；支持独立授权后执行真实事务写入，默认关闭 |
| 测试帐套 | `Test` | `sagex3.XTEST` | 查询允许；支持独立授权后执行真实事务写入，默认关闭 |
| 其他 Schema | 不允许 | 包括 `COBTEST2` | 不出现在 UI、后端拒绝 |

服务端必须按白名单映射 `Production → COB` / `Test → XTEST`；`environment` 只是选择器，客户端不得上传任意 SQL server/database/schema/连接字符串。

**Tauri 2 桌面管理端（2026-11-23）**：Windows 本机 JSON 只保存非秘密设置；SQL Server 密码、PFX 密码、设备接入 Key 使用 Windows Credential Manager，Rust 启动 C# Sidecar 时通过进程环境传递。PDA 前端自行配置 API URL/环境/设备 Key，不提供 SQL 凭据读取接口。公网禁止匿名开放；LAN 必须可信 HTTPS 证书、设备 Key 和网络 ACL。Sidecar 的管理端口只监听 loopback，并另验一次性 AdminToken；启动服务不等于自动开放正式写入；需在 Tauri 中分别核验并打开对应帐套写入开关。

## 2. 无登录的可接受防护

不使用用户密码/登录页，并不意味着 API 对公网匿名开放。建议：受控工厂内网或 VPN / 专用设备网络、服务器防火墙源地址限制、HTTPS、设备准入/反向代理层鉴别、与环境分离的最小数据库凭据、所有写操作结构化审计。具体访问控制可以由部署环境承担，**不能把硬编码在 APK 里的共享密钥误认为足够强的身份验证**。

如果没有可落实的网络/设备级身份约束，正式写入不可启用；缺乏应用内账号也需考虑如何审计由哪个 PDA/作业班次提交，不能伪造真实 X3 用户姓名。

## 3. 生产保护

- `EnableProductionWrites=false` 与 `EnableTestWrites=false` 为首次安装默认值；**两个开关均可由管理员在 Tauri 中分别启用**，还必须满足 Production/Test 独立的 SchemaReviewed 条件；客户端 UI 必须持续展示环境。
- 服务端只读/写入权限可采用不同 SQL principal（优先）。
- 测试/正式连接和应用配置不得通过日志、编译常量或前端接口泄露。
- 切换帐套强制确认；清空盘点单/库位/箱信息/缓存；对状态未知的提交先查询原环境结果，不可转发到新环境。
- HTTP 请求带环境 enum，所有请求与响应均返回环境以检测竞态串库。
- 未经授权不能通过旧 APK 的 GET `CloseXCOUTH` 等接口触发业务变更。
- 移动端绝不可直接使用 SQL Server 用户名/密码或拼接任意 Schema。

## 4. 幂等、审计、隐私

- 将 `requestId` 与 `environment`、`countNumber`、设备审计标识（若允许收集）相关联。
- 记录结果状态、变更前提、错误码、时间、可追溯的服务端请求日志，不公开原始二维码及全量标签数据。
- 不把测试数据写入 COB；发布流程中强制标注“当前环境”和“是否启用写入”。
- 公开仓库只收录结构和虚构示例，不出现真实 IP、生产数据或签名私钥。
- 如果采用本地离线草稿，只存最低必需字段并明确设备丢失处理；未知结果不能被当作失败重试新 ID。

## 5. 生产写入审批检查表

- [ ] 所有数据访问路径都用受信任环境白名单
- [ ] 服务端确实有可靠的网络/设备访问限制
- [ ] XTEST 已验证 X3 完整写入字段、事务、防重、审计及回滚
- [ ] 数量/状态/库位/复盘规则已业务确认
- [ ] COB / XTEST 独立写入开关与结构复核具备可审计的部署变更
- [ ] 已经过真实设备和生产只读联调
- [ ] 具有备份/故障回滚、日志告警、问题应急处置办法

有关“没有登录”的测试见 [TEST_PLAN.md](TEST_PLAN.md)。

---

## 🧭 知识库导航

- 主题索引：[[docs/development/00-研发规范地图|研发规范地图]] · [Markdown](development/00-%E7%A0%94%E5%8F%91%E8%A7%84%E8%8C%83%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
