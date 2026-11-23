# ATABZON — Sage X3 字段定义字典

> **证据状态：已核实用途和下列查询涉及的列名；未取得 `ATABZON` 自身全部 SQL Server 物理列的现场输出**。此文档不是“已完成物理表结构实测”的声明。根据已保存的 COB/XTEST `ATABZON + ATEXTE` 中文标题查询、Sage X3 字典导出脚本整理。2026-11-23。

## 一、表用途

`ATABZON` 保存 X3 表/字段的**逻辑元数据**，不保存具体盘点数量或标签库存。常用关系：

```text
ATABZON.CODFIC_0   → X3 表代码（例如 XCOUTH、XCOUTD、STOLOC）
ATABZON.CODZONE_0  → X3 字段代码（例如 QTYSTU、PID；一般不含物理后缀 _0）
ATABZON.NOCOURT_0  → ATEXTE.NUMERO_0（普通标题）
ATABZON.NOABREG_0  → ATEXTE.NUMERO_0（短标题）
ATABZON.NOLONG_0   → ATEXTE.NUMERO_0（长标题）
ATABZON.NOLIB_0    → APLSTD.LANCHP_0（本地菜单；不是字段标题）
ATEXTE.LAN_0='CHI' → 中文翻译；'ENG' → 英文翻译
```

**注意**：标准字典定义不等于 SQL 物理表字段、数据库索引或 HTTP DTO 必填属性；同名字段可能有多个物理下标 `_0` / `_1`。

## 二、已在历史只读 SQL 使用过的关键字段

**下表为已知字段代码与功能解释，不代表全列数，不提供未经实测的 SQL 数据类型/长度、可空性或真实中文标题。** 真正完整物理结构见 [元数据查询](../../../sql/read_only/inspect_dictionary_tables.sql)，待用户回传实际结果后补齐逐列类型与官方中文标题。

| SQL 物理字段（既有查询中出现） | 功能说明（业务解释，非实测官方中文标题） | 证据 |
| --- | --- | --- |
| `CODFIC_0` | X3 业务表代码 | SQL05 字段标题提取 |
| `CODZONE_0` | 字段代码（通常剥除 `_0` 后的基础名） | SQL05 字段标题提取 |
| `CODTYP_0` | X3 逻辑字段类型 | SQL05 |
| `NOCOURT_0` | 普通标题的文本编号 | SQL05 |
| `NOABREG_0` | 短标题的文本编号 | SQL05 |
| `NOLONG_0` | 长标题的文本编号 | SQL05 |
| `NOLIB_0` | 本地菜单章节号；枚举文字需另外查 APLSTD | SQL05 |
| `LONG_0` | X3 定义长度；不等于 SQL Server 字节长度 | XTEST.STOLOC 中文对照 |
| `DIME_0` | X3 字段维度 | XTEST.STOLOC 中文对照 |
| `LIEN_0` | X3 关联表代码 | XTEST.STOLOC 中文对照 |
| `OBLIG_0` | X3 字段链接/强制关联类标识；不能直接理解为 API 必填 | 字典导出脚本 |
| `CODACT_0` | X3 活动代码 | 字典导出脚本 |

**本表真实列数、其余列、SQL 物理类型、精度/长度/NULL 属性尚未验证，不能编造。**

## 三、与 StockCount 开发的关系

- 后端取 `XCOUTH`、`XCOUTD`、`XLABELDTL`、`STOLOC`、`ITMMASTER` 等字段中文标题时，按当前帐套对应 Schema 的 `ATABZON` 关联 `ATEXTE`。
- `CODFIC_0 + CODZONE_0` 是**逻辑字典匹配条件**；并非证实 SQL Server 存在此组合的唯一索引。
- `CUNSTA_0` 的状态描述不能单凭 `NOCOURT_0` 推断，菜单需另读 `APLSTD`；且 StockCount V1 **所有盘点单可浏览**。
- 标签 `STA_0` 的码值是否允许盘点**不受字典枚举限制**：StockCount V1 已取消状态白名单。
- 客户端不用直接调用这张表；仅后端/开发时的元数据核查读取。

## 四、配套只读 SQL

- [COB/XTEST 的 ATABZON 与 ATEXTE 全部物理列、数据类型、长度、精度、可空、索引检查](../../../sql/read_only/inspect_dictionary_tables.sql)
- [按业务表获取 X3 字段代码、短/普通/长中文标题、英文标题的字典关联查询](../../../sql/read_only/query_dictionary_tables.sql)
- [原五张业务表的完整结构](../SCHEMA_INDEX.md)

所有 SQL 仅限只读；不要将企业字典的原始大量文本/全部语种内容提交公开仓库。
