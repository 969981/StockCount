---
title: "StockCount · Obsidian 知识库使用与维护指南"
created: 2026-11-23
updated: 2026-11-23
status: active
type: moc
area: meta
tags:
  - StockCount
  - obsidian
  - meta
aliases:
  - "Obsidian 使用指南"
  - "文档双链规范"
---

# StockCount · Obsidian 知识库使用与维护指南

[[00-StockCount-知识库主页|← 知识库主页]] · [Markdown](../00-StockCount-%E7%9F%A5%E8%AF%86%E5%BA%93%E4%B8%BB%E9%A1%B5.md) · [[docs/00-文档导航|文档总目录]] · [Markdown](00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)

## 1. 直接打开 Git 仓库为 Vault

1. `git clone https://github.com/969981/StockCount.git`。
2. Obsidian → **Open folder as vault**，选择整个 `StockCount/` 根目录；**不要只打开 `docs/`**。
3. 打开 `00-StockCount-知识库主页.md`，通过主题 MOC 进入各笔记。
4. 推荐在 Obsidian 的 **Settings → Files and links** 开启自动更新内部链接；可根据个人习惯选择 `Use [[Wikilinks]]`，仓库笔记同时保留相对 Markdown 链接。GitHub 能正常点击 Markdown 链接，但不保证解析 WikiLink。

## 2. 双链接约定

每个重要跳转同时放两种格式：

```md
[[requirements|需求规范]] · [Markdown](../requirements.md)
[[docs/pda/海康PDA-广播协议与接入总览|海康广播协议]]
[Markdown](pda/%E6%B5%B7%E5%BA%B7PDA-%E5%B9%BF%E6%92%AD%E5%8D%8F%E8%AE%AE%E4%B8%8E%E6%8E%A5%E5%85%A5%E6%80%BB%E8%A7%88.md)
```

- WikiLink 的目标**从 Vault 根目录写路径、去掉 .md 后缀**，避免同名文件和别名歧义；`[[路径|显示名称]]` 用在行内导航。
- GitHub Markdown 相对链接以当前文件目录为起点；中文文件名推荐百分号编码；避免绝对路径、机器本地路径。
- **表格中不要直接写含 `|` 的 WikiLink**（可能使 Markdown 表格分列）；在表格中用普通 Markdown 链接，下面加一段 Wiki 双链。
- 链接到标题可使用 `[[Note#Heading|标题]]`，但 GitHub 的锚点规则可能不同，关键导航优先全文链接。
- Obsidian 的“反向链接（Backlinks）”由笔记相互指向自动计算，**不是在 Markdown 中手工维护一个伪造的反向列表**。

## 3. 建议知识地图（MOC）

- [[docs/development/00-研发规范地图|研发与 AI 规范]] · [Markdown](development/00-%E7%A0%94%E5%8F%91%E8%A7%84%E8%8C%83%E5%9C%B0%E5%9B%BE.md)
- [[docs/database/00-数据库知识地图|数据库与字段字典]] · [Markdown](database/00-%E6%95%B0%E6%8D%AE%E5%BA%93%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- [[docs/pda/00-PDA扫码知识地图|海康 PDA 扫码]] · [Markdown](pda/00-PDA%E6%89%AB%E7%A0%81%E7%9F%A5%E8%AF%86%E5%9C%B0%E5%9B%BE.md)
- [[docs/00-文档导航|全仓文档目录]] · [Markdown](00-%E6%96%87%E6%A1%A3%E5%AF%BC%E8%88%AA.md)

每篇笔记末尾有“知识库导航”块：WikiLink 供 Obsidian 使用，Markdown 链接供 GitHub 阅读。主题 MOC 引用子笔记、子笔记又指回 MOC，形成可探索的双向知识关系图。

## 4. YAML Properties

仓库现有 Markdown 注入统一的 Frontmatter：

```yaml
---
title: "示例：项目架构"
aliases: ["StockCount / 项目架构"]
created: 2026-11-23
updated: 2026-11-23
status: specification
area: development
tags: [StockCount, development]
---
```

这些属性便于 Obsidian 的 Properties、搜索、Dataview（可选插件）以及筛选；**不依赖任何社区插件才能打开或跳转**。文档源于旧业务归档时，不把 `created` 伪称为原始业务事件日期；文档中仍明确标注来源与核验状态。

## 5. 修改规范

- 新文档先选择 `templates/技术文档模板.md` 或 `templates/ADR决策模板.md`，填 title/aliases/tags/source，然后补双链导航与 Markdown 链接。
- 不改变代码/SQL 的链接和文件结构，只维护 Markdown 的文档关系和 frontmatter。
- 用户确认的新需求先更新 `requirements.md` / `docs/DECISIONS.md`，再同步 API、数据库与扫码文档。
- 不提交个人 `.obsidian/workspace*.json`、缓存、设备本地偏好；它们与项目内容无关。
- 文档在 GitHub 与 Obsidian 均可浏览，实际“Backlinks”以 Obsidian 中计算的结果为准。

## 6. 官方帮助

- [Obsidian Internal links](https://obsidian.md/help/links)
- [Obsidian Properties](https://obsidian.md/help/properties)
- [Obsidian Settings](https://obsidian.md/help/settings)
