---
title: "StockCount · 海康 PDA 扫码知识地图"
created: 2026-11-23
updated: 2026-11-23
status: active
type: moc
area: pda
tags:
  - StockCount
  - obsidian
  - pda
aliases:
  - "PDA 扫码知识地图"
  - "海康扫描专题"
---

# 海康 PDA 扫码知识地图

[[00-StockCount-知识库主页|← 知识库主页]] · [Markdown](../../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md) · [[docs/00-文档导航|文档总目录]] · [Markdown](../00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)

## 厂商协议 → Android 接收 → StockCount 业务

- [[docs/pda/海康PDA-资料与核验清单|原厂手册 V1.0.1 与历史项目证据]] · [Markdown](%E6%B5%B7%E5%BA%B7PDA-%E8%B5%84%E6%96%99%E4%B8%8E%E6%A0%B8%E9%AA%8C%E6%B8%85%E5%8D%95.md)
- [[docs/pda/海康PDA-广播协议与接入总览|Action / Extras / 扫描服务配置]] · [Markdown](%E6%B5%B7%E5%BA%B7PDA-%E5%B9%BF%E6%92%AD%E5%8D%8F%E8%AE%AE%E4%B8%8E%E6%8E%A5%E5%85%A5%E6%80%BB%E8%A7%88.md)
- [[docs/pda/海康PDA-MAUI实现与示例|BroadcastReceiver + MAUI C#]] · [Markdown](%E6%B5%B7%E5%BA%B7PDA-MAUI%E5%AE%9E%E7%8E%B0%E4%B8%8E%E7%A4%BA%E4%BE%8B.md)
- [[docs/SCANNER_INTEGRATION|StockCount 扫描业务总规范]] · [Markdown](../SCANNER_INTEGRATION.md)
- [[docs/pda/海康PDA-调试排障与验收|ADB 调试 / 真机验收]] · [Markdown](%E6%B5%B7%E5%BA%B7PDA-%E8%B0%83%E8%AF%95%E6%8E%92%E9%9A%9C%E4%B8%8E%E9%AA%8C%E6%94%B6.md)

## APK UI / 交互设计

- [[docs/pda/PDA-UI-UX-设计规范与交互状态|PDA UI 视觉与扫码交互规范]] · [Markdown](PDA-UI-UX-设计规范与交互状态.md)
- [[docs/pda/PDA-UI-开发任务与验收|MAUI UI 开发任务和 UX 验收]] · [Markdown](PDA-UI-开发任务与验收.md)

## 跨模块连接

- [[requirements|盘点业务要求]] · [Markdown](../../requirements.md)
- [[docs/TEST_PLAN|端到端 QA]] · [Markdown](../TEST_PLAN.md)
- [[docs/API_CONTRACT|扫码解析 API]] · [Markdown](../API_CONTRACT.md)
- [[docs/database/00-数据库知识地图|箱标签、库位、物料元数据]] · [Markdown](../database/00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)

> [!IMPORTANT]
> `com.service.scanner.start` 是启动扫描服务，**不是**软件触发一次读码。实体键、Android 广播、MAUI Receiver、API 落库是四个独立验收层级。协议示例来自旧项目/厂商手册；StockCount 真机当前未复测。
