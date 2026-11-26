---
title: "StockCount · Obsidian 知识库主页"
created: 2026-11-23
updated: 2026-11-26
status: active
type: moc
area: home
tags:
  - StockCount
  - obsidian
  - home
aliases:
  - "StockCount"
  - "StockCount 知识库"
  - "StockCount Home"
---

# StockCount · 知识库主页

> [!NOTE] Vault 根目录
> 将本仓库根目录 `StockCount/` 直接在 Obsidian 中打开为 Vault。**不要单独只打开 `docs/`**，因为根目录包含 `requirements.md`、`AGENTS.md` 和 `architecture.md`。所有源码、SQL、Markdown 与知识地图同一份 Git 版本管理。

## 导航

- [[docs/00-文档导航|文档总目录]] · [Markdown](docs/00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)
- [[docs/development/00-研发规范地图|研发与 AI 编程规范]] · [Markdown](docs/development/00-%E7%A0%94%E5%8F%91%E8%A7%84%E8%8C%83%E5%9C%B0%E5%9B%BE.md)
- [[docs/database/00-数据库知识地图|Sage X3 数据库知识地图]] · [Markdown](docs/database/00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- [[docs/pda/00-PDA扫码知识地图|海康 PDA 扫码知识地图]] · [Markdown](docs/pda/00-PDA%E6%89%AB%E7%A0%81%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- [[docs/OBSIDIAN_GUIDE|Obsidian 使用与双链接规范]] · [Markdown](docs/OBSIDIAN_GUIDE.md)

## 一期产品业务

```text
无登录启动 → 选择 Production(COB) / Test(XTEST)
  → 列出全部已有 XCOUTH 盘点单（不可创建/关闭）
  → 选一张单据 → 扫库位（STOLOC）
  → 扫箱码（XLABELDTL + ITMMASTER）
  → 人工输入一次 STU 数量
  → XCOUTD.QTYSTU_0 = XCOUTD.QTYPCU_0 = 人工输入值
```

**`XLABELDTL.STA_0` 不限制盘点**。当前规则是本帐套跨单防重、STU 同值写入、原 UUID 幂等；Tauri/独立 API 默认关闭写入，Avalonia 现场版应用已批准写入策略，数据库结构和权限仍须现场核验。

## 从这里开始

- **产品/范围**：[[requirements|V1 产品需求]] · [Markdown](requirements.md)
- **前后端架构**：[[architecture|分层与环境隔离]] · [Markdown](architecture.md)
- **AI 编码任务**：[[AGENTS|全仓编程规范]] · [Markdown](AGENTS.md)
- **表和 SQL**：[[docs/database/SCHEMA_INDEX|七表字段索引]] · [Markdown](docs/database/SCHEMA_INDEX.md)
- **硬件接入**：[[docs/SCANNER_INTEGRATION|工业扫码接口]] · [Markdown](docs/SCANNER_INTEGRATION.md)

- **部署/网络**：[[docs/StockCount-Windows-5080-Firewall-Guide|Windows 5080 端口开放与完整现场排查（2026-11-25～26）]] · [Markdown](docs/StockCount-Windows-5080-Firewall-Guide.md)

## 使用提示

- **Obsidian**：点击 `[[...]]` 跳转，反向链接 / 关系图自动显示。
- **GitHub**：点击每条旁的 `Markdown` 链接跳转；GitHub 不原生解析所有 Obsidian WikiLink。
- 文档中的 YAML Properties 便于按 `tags`、`area`、`status` 检索。详见 [[docs/OBSIDIAN_GUIDE|管理规范]] · [Markdown](docs/OBSIDIAN_GUIDE.md)。

> [!WARNING]
> 公开仓库只放开发规范、字段结构和**合成示例**；不得上传企业真实凭据、箱码、明细数据、厂商私有 PDF 原文或生产环境配置。


## 现场使用与业务 SQL（2026-11-26）

- [[docs/USER_GUIDE|StockCount 软件使用手册]] · [Markdown](docs/USER_GUIDE.md) — Windows 管理器、PDA 配置、扫码、异常恢复、历史记录与排障。
- [[docs/database/BUSINESS_SQL_REFERENCE|业务 SQL 实现字典]] · [Markdown](docs/database/BUSINESS_SQL_REFERENCE.md) — 源码 Q01–Q09 / W01–W06、字段与参数、只读核验。
