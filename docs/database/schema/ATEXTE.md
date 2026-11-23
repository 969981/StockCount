# ATEXTE — Sage X3 多语言标题文本表

> **证据状态：既有 SQL 已确认以下字段能参与字典连接，当前尚未取得 `ATEXTE` 自身全部 SQL Server 物理列元数据。** 不把已知的 3 个关键列误称为“全表只有3列”。2026-11-23。

## 一、表用途与关系

`ATEXTE` 是 Sage X3 的文本/语言翻译数据来源，字段的普通/短/长标题由 `ATABZON` 的文本编号指向不同 `ATEXTE.NUMERO_0` 记录；`CHI` 常用于中文、`ENG` 用于英文。

```text
ATABZON.NOCOURT_0   ─┐
ATABZON.NOABREG_0   ├─→ ATEXTE.NUMERO_0
ATABZON.NOLONG_0    ┘
                          + ATEXTE.LAN_0 = 'CHI' / 'ENG'
                          → ATEXTE.TEXTE_0 标题文字
```

**注意**：短标题用 `NOABREG_0`，不是 `NOLIB_0`。后者连接 `APLSTD` 本地菜单编号。只依靠 `NUMERO_0` 不足以选对语言；实际字段类型、索引与物理唯一约束请用元数据查询确认。

## 二、此前 SQL 已使用过的关键字段

| 已确认可被已有查询引用的字段 | 功能说明（不是官方中文标题的声称） | 使用条件 |
| --- | --- | --- |
| `NUMERO_0` | 标题/文本编号 | 与 `ATABZON.NOCOURT_0` / `NOABREG_0` / `NOLONG_0` 连接 |
| `LAN_0` | 语言代码 | `CHI`、`ENG`（视企业安装情况） |
| `TEXTE_0` | 语言文本 | 展示 `CHI` 的中文标题，`ENG` 的英文标题 |

**其它物理列、SQL Server 类型、长度、精度、小数位、可空与索引均待现场回传；不能根据上述三列反推出表总列数。**

## 三、业务使用规范

- StockCount 用它生成字段标题**仅供开发/诊断**；不要用标题文字作 JSON DTO 属性、SQL 列名或唯一 ID。
- 字典标题与 SQL Server 的 `MS_Description` 是不同来源。若中文标题为空、缺少语言行或字典中没有字段，保留真实物理列，并明确标记“无中文标题”。
- 物料业务“规格”对应 `ITMMASTER.ITMDES2_0` 的企业字典是“描述 2”；不能因为前端显示名称不同就覆盖原字典事实。
- 不把 `ATEXTE` 误作仓库业务数据表，更不能为了补充翻译而在生产库修改标题。
- 由于公共仓库安全约束，实例查询使用 `TOP` 和按指定业务表过滤，示例仅含合成值。

## 四、可执行只读 SQL

- [物理字段结构、长度与索引（COB、XTEST）](../../../sql/read_only/inspect_dictionary_tables.sql)
- [ATABZON → ATEXTE 中文/英文短普通长标题关联、少量样本查询](../../../sql/read_only/query_dictionary_tables.sql)
- [七表字段总索引](../SCHEMA_INDEX.md)
