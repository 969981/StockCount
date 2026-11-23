# StockCount — 五表完整结构与中文字段字典索引

> **2026-11-23 归档。** 只包含 Sage X3 字段字典、SQL 查询与字段解释，不提交真实生产/测试业务数据。**正式帐套：`[sagex3].[COB]`；测试帐套：`[sagex3].[XTEST]`**。本文是字段证据与开发索引，不能替代真实数据库权限与运行时核验。

## 五张表的证据覆盖

用户本次列了两次 `XCOUTH`，结合 StockCount 必需表头/明细关系，将第二次理解为 **`XCOUTD`**，因此按以下 5 张表整理：

| 表 | 用途 | 已取得的物理列证据 | 中文标题来源 | 详细字段清单 |
| --- | --- | --- | --- | --- |
| `XCOUTH` | 库存盘点单表头 | **COB 16/16** 列、类型、可空已实测 | COB.ATABZON / ATEXTE | [16 列完整映射](schema/XCOUTH.md) |
| `XCOUTD` | 库存盘点明细 | **COB 19/19** 列、类型、可空已实测 | COB.ATABZON / ATEXTE | [19 列完整映射](schema/XCOUTD.md) |
| `XLABELDTL` | 箱标签与完整二维码 | **COB 45/45** 列、类型、可空已实测 | COB.ATABZON / ATEXTE | [45 列完整映射](schema/XLABELDTL.md) |
| `STOLOC` | 库位主数据 | **XTEST 41/41** 列、物理类型、存储字节、可空已实测；其中 numeric 精度/scale 尚待查询 | XTEST.ATABZON / ATEXTE，其中 39 个有中文标题 | [41 列完整映射](schema/STOLOC.md) |
| `ITMMASTER` | 物料主数据、品名与规格 | **197 条 X3 字段字典已实测，但完整 SQL 物理列清单尚未回传** | COB.ATABZON / ATEXTE | [197 条字典及物理核验方法](schema/ITMMASTER.md) |

**请不要把 197 条 ITMMASTER X3 字典码误认作 197 个实际 SQL Server 物理列。** 该表的完整物理结构须运行下面 SQL，再按实际回传结果补齐。不同帐套可能有结构差异（`CUNTYP_0` 已发现跨 Schema 转换溢出），不能只凭正式帐套数据断言测试帐套字段完全相同。

## 可直接使用的 SQL（全部只读）

| SQL 脚本 | 内容 | 范围 |
| --- | --- | --- |
| [inspect_five_tables.sql](../../sql/read_only/inspect_five_tables.sql) | 依据 `sys.columns` 实际列关联 `ATABZON + ATEXTE`，输出五表全部字段、类型、长度、精度、小数位、可空、中文普通/长标题、菜单与 `MS_Description` | **同时查询 COB 和 XTEST 元数据**；能补齐 ITMMASTER 实际物理列 |
| [select_five_tables_chinese_aliases.sql](../../sql/read_only/select_five_tables_chinese_aliases.sql) | 动态按 SQL 真实物理列生成 `[中文标题（字段名）]`，分别 `TOP 20` 查询五表，支持按单号、箱号/二维码、物料、站点、仓库、库位筛选 | **默认 XTEST**，可手工设为 COB |
| [business_queries.sql](../../sql/read_only/business_queries.sql) | 盘点单列表、指定单据明细与数量、标签+物料、库位、物料资料、五表业务关联，共六组查询 | **默认 XTEST**，可手工设为 COB；各查询最多 TOP 30 |
  
## 常用字段关联

```text
XCOUTH.CUNNUM_0   = XCOUTD.CUNNUM_0          -- 一对多盘点单/明细
XCOUTD.BOXNUM_0   = XLABELDTL.PID_0         -- 箱码/标签ID
XLABELDTL.ITMREF_0 = ITMMASTER.ITMREF_0      -- 产品编码
STOLOC.STOFCY_0 + STOLOC.WRH_0 + STOLOC.LOC_0
  对应 XCOUTH.STOFCY_0 + XCOUTH.WRH_0 + XCOUTD.LOC_0
   -- 库位建议组合匹配；需按真实业务确认
```

- **盘点单全部展示**：V1 列表不以 `CUNSTA_0` 过滤；是否允许写某单是独立规则。
- **标签全部状态都允许盘点**：`XLABELDTL.STA_0` 不作为准入白名单，历史 `IN (1,7,8)` 规则废弃。
- **实盘数量人工输入一次、同时保存两列同值**：`XCOUTD.QTYSTU_0 = XCOUTD.QTYPCU_0 = 人工实盘数量`，不读标签 `QTY_0` 或二维码 `QT` 作为实盘数。**`PCUSTUCOE_0` 和 `PCU_0` 的写入一致性尚待 XTEST 验证**。
- **绝不盲目拼接生产 INSERT**：`AUUID_0`、`ROWID`、`UPDTICK_0`、非空审计字段和业务写入机制先核实。
- **不要以当前标签数据倒推历史实盘**：标签库位、标签当前数量都可能在盘点后变动。

## 继续补齐 ITMMASTER 的真实物理字段

1. 在有数据库读取权限的管理端执行 [inspect_five_tables.sql](../../sql/read_only/inspect_five_tables.sql)。
2. 仅回传**字段元数据**（表名/SQL字段名/类型/字节长度/数字精度/小数位/中文标题），**不要上传生产物料数据行、账号或内网地址到公开 GitHub**。
3. 以 `ITMMASTER` 实际物理列清单替换其“物理字段待核”状态，并核验测试 `XTEST` 与正式 `COB` 的差异。
4. 对 `STOLOC` 数字列补齐 `precision/scale`；检查两帐套字段是否一致，完善 API 和 Dapper 实际映射。

## 关联

- [核心字段与 DTO 关系](../DATABASE_MAPPING.md)
- [库存盘点需求](../../requirements.md)
- [AI 规范](../../AGENTS.md)
- [API 契约](../API_CONTRACT.md)
- [安全与帐套](../SECURITY_AND_ENVIRONMENTS.md)
