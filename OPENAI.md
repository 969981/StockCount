---
title: "OpenAI / Codex 统一执行说明"
aliases:
  - "StockCount · OPENAI"
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

# OpenAI / Codex 统一执行说明

> 供 ChatGPT、Codex、OpenAI API 驱动的 coding agent 使用。**不是 API 密钥文件**，不假定仓库已经配置模型、权限或自动化执行。

## 1. 工作入口

`AGENTS.md` → `requirements.md` → `architecture.md` → `docs/DECISIONS.md` → **数据库任务必读 `docs/database/SCHEMA_INDEX.md`（含 ATABZON / ATEXTE）** → 本次任务相关 docs → 当前源码与测试。

业务行为以最新确认的 `requirements.md` 为准；技术实现必须基于当前真实代码。发现差异时先记录，不得无声替换/覆盖。

## 2. 推荐任务提示词模板

```text
目标仓库：969981/StockCount
任务：<一个明确的前端或后端工作项>
需求编号：<requirements.md 中的 P-xx>
工作范围：<可编辑的项目与文件>
必读：AGENTS.md、requirements.md、architecture.md、相关 docs
禁止：生产 ERP 写入；自动新建/关闭盘点单；标签 STA 状态白名单
交付：代码 + 测试 + DTO/API/字段文档同步 + 实测命令及结果
回报：修改文件、已执行测试、未解决阻断、对既有系统的影响
```

## 3. Codex 分阶段顺序

1. **契约优先**：定义枚举、请求/响应 DTO、错误码与环境标识；不把数据库列名强制暴露给 Android。
2. **只读 API**：Health、当前环境分页盘点单、单据详情、库位及标签解析；确认帐套分流。
3. **客户端壳**：MAUI 列表/扫码/设置、扫码 Mock、离线错误与界面状态机。
4. **XTEST 写入验证**：在明确 X3 必填列与授权路径后实现事务、幂等和审计；**暂不开正式写**。
5. **端到端联调**：扫码、手填 STU 数量、可靠提交、再扫下一箱。
6. **上线审核**：全部 P0 测试通过、风险项签字并在后端显式启用生产写。

## 4. AI 严禁假设为事实的事项

- `STA_0 IN (1,7,8)` 是**旧需求**，不能抄入新程序。
- `CUNSTA_0=0` 的中文名称、`1` 是否关闭，均未验证。
- 二维码 `QT` 与 `PG` 不一定相等；`PG` 真实意义未知。
- `COB` 与 `XTEST` 的表结构不保证一致；曾发生跨 Schema 类型转换溢出。
- 旧 APK 返回的自动数量并不是新程序的人工实盘数。**新规则要求人工输入一次，两个落库数值字段 `QTYSTU_0` / `QTYPCU_0` 均写该值、完全相等；不得二次换算覆盖。**
- 只防双击不等于服务端幂等。
- 无设备 / SQL 权限 / Android SDK 时不得声称编译、真机扫码或实库验证成功。

## 5. PR 描述约定

必须包含：**需求编号 / 修改文件 / API 或表映射变化 / 实际构建测试命令及结果 / 数据安全 / 回滚 / 未确认项**。需要业务批准时标注阻断而不是私自实现。

用户已确认“无登录、可切正式/测试帐套、查看全部现有单据、V1不创建盘点单、标签 STA 不限”的最新要求优先于历史需求文档；未来新决定必须同步修改需求、决策记录与测试。

详见 [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md)。

---

## 🧭 知识库导航

- 主题索引：[[docs/development/00-研发规范地图|研发规范地图]] · [Markdown](docs/development/00-%E7%A0%94%E5%8F%91%E8%A7%84%E8%8C%83%E5%9C%B0%E5%9B%BE.md)
- Obsidian 主页：[[00-StockCount-知识库主页|知识库主页]] · [Markdown](00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md)
- 全部文档：[[docs/00-文档导航|文档目录]] · [Markdown](docs/00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- AI 阅读链：[[requirements|需求规范]] · [Markdown](requirements.md) · [[architecture|系统架构]] · [Markdown](architecture.md) · [[docs/DECISIONS|设计决策]] · [Markdown](docs/DECISIONS.md)
