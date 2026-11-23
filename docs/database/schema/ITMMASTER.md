# ITMMASTER — 产品主数据 X3 中文字段字典（197 条；物理列待核）

> **证据边界**：2026-11-19 用户已回传 `sagex3.COB.ATABZON` / `ATEXTE` 对 `ITMMASTER` 的 **197 条 X3 字段代码 + 中英文标题**。它们是 **Sage X3 字典定义，不等于 `COB.ITMMASTER` 的 197 个真实 SQL Server 物理列**。目前没有得到 `ITMMASTER` 的完整 `sys.columns` 结果，因此**本文件不是完整物理结构清单**；每个字典代码是否对应 `_0/_1` 等实例列、各列 `nvarchar` 长度/数字精度/是否可空，均须运行配套 SQL 后补齐。不得凭字典数量编造真实物理列数。
>
> [五表索引](../SCHEMA_INDEX.md) · [全物理列/中文字典查询 SQL](../../../sql/read_only/inspect_five_tables.sql) · [中文别名查询 SQL](../../../sql/read_only/select_five_tables_chinese_aliases.sql)

## 目前已用于 StockCount 的候选业务列（物理类型待核）

| 字段 | X3 中文普通标题 | StockCount 用途 |
| --- | --- | --- |
| `ITMREF_0` | 产品 | 按标签 `XLABELDTL.ITMREF_0` 连接产品 |
| `YITMDES4_0` | 品名（企业自定义） | 页面显示“品名” |
| `ITMDES2_0` | 描述 2 | 页面业务别名“规格”，并非原字典标题 |

## 197 条 X3 字段代码和中文标题（不是物理列元数据）

| 序号 | X3 字典字段代码（不带下标） | 中文普通标题 | 中文短标题 | 中文长标题 | 英文标题 | X3 逻辑类型 | 本地菜单号 |
| ---: | --- | --- | --- | --- | --- | --- | ---: |
| 1 | `ITMREF` | 产品 | 产品 | 产品 | Product | ITM | 0 |
| 2 | `CPY` | 公司 | 公司 | 公司 | Company | CPY | 0 |
| 3 | `TSICOD` | 统计组 | 统计组 | 统计组 | Statistical group | ADI | 0 |
| 4 | `TCLCOD` | 类别 | 类别 | 产品种类 | Category | ITG | 0 |
| 5 | `SEAKEY` | 搜索关键字 | 搜索关键字 | 搜索关键字 | Search key | A | 0 |
| 6 | `DIE` | 分析元类型代码 | 分析元类型 | 分析元类型代码 | Dimension type code | DIE | 0 |
| 7 | `CCE` | 分析元 | 分析元 | 分析元 | Analytical dimension | CCE | 0 |
| 8 | `ACCCOD` | 会计代码 | 会计代码 | 会计代码 | Accounting code | CAC | 0 |
| 9 | `ITMDES1` | 描述1 | 描述 | 描述1 | Description 1 | DES | 0 |
| 10 | `ITMDES2` | 描述 2 | 描述 2 | 描述 2 | Description 2 | DES | 0 |
| 11 | `ITMDES3` | 描述 3 | 描述 3 | 描述 3 | Description 3 | DES | 0 |
| 12 | `DES1AXX` | 描述1 | 描述 | 描述1 | Description 1 | AX3 | 0 |
| 13 | `DES2AXX` | 描述 2 | 描述 2 | 描述 2 | Description 2 | AX3 | 0 |
| 14 | `DES3AXX` | 描述 3 | 描述 3 | 描述 3 | Description 3 | AX3 | 0 |
| 15 | `STDFLG` | 管理模式 | 管理模式 | 管理模式 | Management mode | M | 297 |
| 16 | `ITMSTD` | 标准 | 标准 | 标准 | Standard | A | 0 |
| 17 | `EANCOD` | 条形码 | 条形码 | 条形码 | UPC code | A | 0 |
| 18 | `ITMSTA` | 产品状态 | 状态 | 产品状态 | Product status | M | 246 |
| 19 | `DEFPOT` | 默认浓度（%） | 默认浓度（%） | 默认浓度 | Default potency % | DCB | 0 |
| 20 | `DEFACT` | 默认标题 | 国际单位浓度 | 国际单位浓度 | Default title | DCB | 0 |
| 21 | `LIFSTRDAT` | 使用寿命开始 | 使用寿命开始 | 使用寿命开始 | Service life start | D | 0 |
| 22 | `LIFENDDAT` | 使用寿命结束 | 使用寿命结束 | 使用寿命结束 | Service life end | D | 0 |
| 23 | `STOCRD` | 储存清单 | 储存清单 | 储存清单 | Storage sheet | A | 0 |
| 24 | `STU` | 存货单位 | 存货单位 | 存货单位 | Stock unit | UOM | 0 |
| 25 | `PUU` | 采购单位 | 采购单位 | 采购单位 | Purchase unit | UOM | 0 |
| 26 | `SAU` | 销售单位 | 销售单位 | 销售单位 | Sales unit | UOM | 0 |
| 27 | `SSU` | 统计单位 | 统计单位 | 统计单位 | Statistic unit | UOM | 0 |
| 28 | `EEU` | EEC单位 | EEC单位 | EEC单位 | EU unit | UOM | 0 |
| 29 | `PUUSTUCOE` | 采购/存货单位系数 | 采购/存货单位系数 | PUR- STK 转换 | PUR-STK conv. | COE | 0 |
| 30 | `DACPUUCOE` | 采购系数录入 | 系数录入 | 允许采购系数录入 | Purch coeff entry | M | 1 |
| 31 | `SAUSTUCOE` | 销售/存货单位转换 | 销售/存货单位转换 | 销售/存货单位转换 | SAL-STK conv. | COE | 0 |
| 32 | `DACSAUCOE` | 销售系数录入 | 系数录入 | 允许销售系数录入 | Sales coeff entry | M | 1 |
| 33 | `SSUSTUCOE` | 统计/存货单位系数 | 统计/存货单位系数 | 统计/存货单位系数 | STA-STK conv. | COE | 0 |
| 34 | `EEUSTUCOE` | EEC单位-存货单位i系数 | EEC单位-存货单位i系数 | EEC单位-存货单位i系数 | EU-STK conv. | COE | 0 |
| 35 | `WEU` | 重量单位 | 重量单位 | 重量单位 | Weight unit | UOM | 0 |
| 36 | `ITMWEI` | 存货重量 | 存货重量 | 存货重量 | Item weight | WEI | 0 |
| 37 | `VOU` | 体积单位 | 体积单位 | 体积单位 | Volume unit | UOM | 0 |
| 38 | `ITMVOU` | 存货单位体积 | 仓储体积 | 存货单位体积 | STK volume | VOL | 0 |
| 39 | `PCU` | 包装单位 | 包装单位 | 包装单位 | Pack unit | UOM | 0 |
| 40 | `PCUSTUCOE` | 包装/存货单位系数 | 包装/存货单位系数 | 包装/存货单位系数 | PAC-STK conv. | COE | 0 |
| 41 | `DACPCUCOE` | 包装系数录入 | 系数录入 | 允许包装系数录入 | Pack coeff entry | M | 1 |
| 42 | `PCURUL` | 包装单位出库 | 包装单位出库 | 包装单位出库 | Issuing PAC | M | 2706 |
| 43 | `LBEFMT` | 标签格式 | 标签格式 | 标签格式 | Label format | ARP | 0 |
| 44 | `STOMGTCOD` | 存货管理 | 存货管理 | 存货管理 | Stock management | M | 215 |
| 45 | `LOTMGTCOD` | 批次管理 | 批次管理 | 批次管理 | Lot management | M | 2711 |
| 46 | `LOTCOU` | 批次序列号 | 批次序列号 | 批次序列号 | Lot sequence number | ANM | 0 |
| 47 | `SERCOU` | 序列号计数 | 序列号 | 序列号计数 | Serial sequence | ANM | 0 |
| 48 | `SERMGTCOD` | 序列号管理 | 序列号管理 | 序列号管理 | Serial no. management | M | 210 |
| 49 | `EXYMGTCOD` | 过期管理 | 过期管理 | 过期管理 | Expiration management | M | 211 |
| 50 | `EXYSTA` | 过期状态 | 过期状态 | 过期状态 | Expiration status | A | 0 |
| 51 | `SHL` | 保存期限 | 过期 LT | 保存期限 | Shelf life | C | 0 |
| 52 | `SHLLTI` | 重控制期限 | 复核提前期 | 重控制期限 | Check lead time | C | 0 |
| 53 | `NEGSTO` | 允许负库存 | 允许负库存 | 允许负库存 | Stock < 0 authorized | M | 1 |
| 54 | `CSTGRP` | 成本组 | 成本分组 | 成本组 | Cost group | A | 0 |
| 55 | `BRDCOD` | 成本组 | 成本组 | 成本组 | Cost group | M | 325 |
| 56 | `PLAACS` | 访问代码 | 访问管理 | 访问代码 | User access | ACS | 0 |
| 57 | `RPLITM` | 替换产品 | 备选产品 | 替换产品 | Alternate product | ITM | 0 |
| 58 | `EECGES` | 提交至EEC资产交换申报 | 附属内部统计  | 提交至EEC资产交换申报 | Sub Intrastat | M | 1 |
| 59 | `CUSREF` | 海关参考 | 关税参考 | 海关参考 | Customs reference | INO | 0 |
| 60 | `VACITM` | 税务级别 | 税务级别 | 税务级别 | Tax level | TVI | 0 |
| 61 | `FRTHORUOM` | 计划期间单位 | 时间单位 | 计划期间单位 | Planning horizon time unit | M | 291 |
| 62 | `FRTHOR` | 计划范围  | 确认的水平 | 计划期间 | Planning horizon | C | 0 |
| 63 | `FIMHOR` | 确认时界 | 确认时界 | 确认时界 | Firm horizon | C | 0 |
| 64 | `FIMHORUOM` | 确认的水平时间单位 | 时间单位 | 确认的水平时间单位 | Firm horizon time un | M | 291 |
| 65 | `BUY` | 采购员 | 采购员 | 采购员 | Buyer | AUS | 0 |
| 66 | `PLANNER` | 计划员 | 计划员 | 计划员 | Planner | AUS | 0 |
| 67 | `OFS` | 补货提前期 | 补货提前期 | 补货提前期 | Reorder LT | C | 0 |
| 68 | `PURBASPRI` | 基准价 | 基准价 | 基准价 | Base price | MD5 | 0 |
| 69 | `CPRCOE` | 近似费用系数 | 采购因素 | 收货成本因素 | Landed cost coef. | COE | 0 |
| 70 | `CPRAMT` | 单位固定成本 | 单位固定成本 | 定额采购成本 | Fixed cost per unit | MD5 | 0 |
| 71 | `STCNUM` | 成本结构 | 结构 | 成本结构 | Cost structure | VCR | 0 |
| 72 | `MINRMNPRC` | 交货公差% | 交货公差 | 交货公差% | Delivery tolerance % | RAT | 0 |
| 73 | `RCPFLG` | 收货代码 | 收货 | 收货代码 | Receipt code | M | 1 |
| 74 | `PRQFLG` | 必填的采购请求 | 必填采购请求 | 必填的采购请求 | Mandatory PO request | M | 1 |
| 75 | `PURTEX` | 采购文本 | 采购文本 | 采购文本 | Purchase text | TXC | 0 |
| 76 | `MFGTEX` | 生产文本 | 生产文本 | 生产文本 | Production text | TXC | 0 |
| 77 | `ITMEXNFLG` | 免税标识 | 免税 | 免税标识 | Exemption flag | A | 0 |
| 78 | `STATAXFLG` | 课税标识 | 可课税性 | 课税标识状态/本地 | Tax flag | A | 0 |
| 79 | `CFGLIN` | 产品线 | 产品线 | 产品线 | Product line | TLP | 0 |
| 80 | `CFGFLDNUM1` | 数字型字段 1 | 数字型字段 1 | 数字型字段 1 | Numeric field 1 | DCB | 0 |
| 81 | `CFGFLDNUM2` | 数字型字段 2 | 数字型字段 2 | 数字型字段 2 | Numeric field 2 | DCB | 0 |
| 82 | `CFGFLDNUM3` | 数字型字段 3 | 数字型字段 3 | 数字型字段 3 | Numeric field 3 | DCB | 0 |
| 83 | `CFGFLDNUM4` | 数字型字段 4 | 数字型字段 4 | 数字型字段 4 | Numeric field 4 | DCB | 0 |
| 84 | `CFGFLDNUM5` | 数字型字段 5 | 数字型字段 5 | 数字型字段 5 | Numeric field 5 | DCB | 0 |
| 85 | `CFGFLDNUM6` | 数字型字段 6 | 数字型字段 6 | 数字型字段 6 | Numeric field 6 | DCB | 0 |
| 86 | `CFGFLDALP1` | 文字型字段 1 | 文字型字段 1 | 文字型字段 1 | Alpha field 1 | A | 0 |
| 87 | `CFGFLDALP2` | 文字型字段 2 | 文字型字段 2 | 文字型字段 2 | Alpha field 2 | A | 0 |
| 88 | `CFGFLDALP3` | 文字型字段  3 | 文字型字段  3 | 文字型字段  3 | Alpha field 3 | A | 0 |
| 89 | `CFGFLDALP4` | 文字型字段 4 | 文字型字段 4 | 文字型字段 4 | Alpha field 4 | A | 0 |
| 90 | `CFGFLDALP5` | 文字型字段 5 | 文字型字段 5 | 文字型字段 5 | Alpha field 5 | A | 0 |
| 91 | `CFGFLDALP6` | 文字型字段 6 | 文字型字段 6 | 文字型字段 6 | Alpha field 6 | A | 0 |
| 92 | `CFGVCRNUM` | 配置号 | 日记账配置 | 配置号 | Journal number config | A | 0 |
| 93 | `CFGBPRNUM` | 商业伙伴 | 商业伙伴 | 商业伙伴 | BP | A | 0 |
| 94 | `CFGBPRREF` | 商业伙伴参考 | 商业伙伴参考 | 商业伙伴参考 | BP reference | A | 0 |
| 95 | `CFGITMREF` | 参考产品 | 参考产品 | 参考产品 | Reference product | A | 0 |
| 96 | `CFGDELDAT` | 配置清除日期 | 清除日期 | 配置清除日期 | Config purge date | D | 0 |
| 97 | `CREMAC` | 库的创建 | 基础 | 库的创建 | Creation of base | M | 1 |
| 98 | `FLYCAT` | 凭证种类 | 凭证 | 凭证种类 | Voucher category | ADI | 0 |
| 99 | `TPLCONLND` | 寄售合同 | 寄售 | 寄售合同 | Loan contract | COT | 0 |
| 100 | `TPLCONGUA` | 担保合同 | 保证 | 担保合同 | Warranty contract | COT | 0 |
| 101 | `TPLCONSRV` | 服务合同 | 合同 | 服务合同 | Service contract | COT | 0 |
| 102 | `PITCDT` | 信用筹码 | 代币 | 信用筹码 | Tokens to be credited | L | 0 |
| 103 | `PITCDTUOM` | 信用单位 | 单位 | 信用单位 | Credit unit | UOM | 0 |
| 104 | `STULBEFMT` | 标签格式 | 标签格式 | 库存单位标签格式 | Label format | ARP | 0 |
| 105 | `ALTBOMHDK` | 代码 | 代码 | 代码 | Code | TBO | 0 |
| 106 | `HDKITMTYP` | 产品类型 | 类型 | 产品类型 | Product type | M | 2984 |
| 107 | `STOISSDEF` | 存货出库 | 发出 | 存货出库 | Stock issue | M | 1 |
| 108 | `DAYUOM` | 天单位 | 天单位 | 天单位 | Unit for days | UOM | 0 |
| 109 | `HOUUOM` | 小时单位 | 小时单位 | 小时单位 | Unit for hour | UOM | 0 |
| 110 | `MNTUOM` | 分钟单位 | 分钟单位 | 分钟单位 | Unit for minutes | UOM | 0 |
| 111 | `DTY` | 密度 | 密度 | 密度 | Density | COE | 0 |
| 112 | `TRKCOD` | 追溯 | 追溯 | 追溯 | Traceability | M | 754 |
| 113 | `TRKLEV` | 追溯级别 | 级别 | 追溯级别 | Traceability level | M | 2778 |
| 114 | `PURFLG` | 采购的 | 采购的 | 采购的 | Bought | M | 1 |
| 115 | `MFGFLG` | 生产的 | 生产的 | 生产的 | Manufactured | M | 1 |
| 116 | `SCPFLG` | 外协的 | 外协的 | 外协的 | Subcontracted | M | 1 |
| 117 | `SCSFLG` | 外协 | 外协 | 外协 | Subcontract | M | 1 |
| 118 | `PHAFLG` | 虚拟件 | 虚拟件 | 虚拟件 | Phantom | M | 1 |
| 119 | `GENFLG` | 通用 | 通用 | 通用 | Generic | M | 1 |
| 120 | `TOOFLG` | 工具 | 工具 | 工具 | Tools | M | 1 |
| 121 | `DLVFLG` | 可发货的 | 可发货的 | 可发货的 | Deliverable | M | 1 |
| 122 | `SALFLG` | 销售的 | 销售的 | 销售的 | Sold | M | 1 |
| 123 | `INTFLG` | 中间 | 业务 | 中间 | Intermediary | M | 1 |
| 124 | `FLGFAS` | 固定资产 | 固定资产 | 固定资产 | Capitalizable   | M | 1 |
| 125 | `NEWLTISTA` | 重控制状态 | 重控制状态 | 重控制状态 | Recontrol status | A | 0 |
| 126 | `DLU` | 使用期限系数 | 使用期限系数 | 使用期限系数 | UBD coefficient | COE | 0 |
| 127 | `SHLUOM` | 失效时间单位 | 时间单位 | 失效时间单位 | Expir t unit   | M | 2759 |
| 128 | `SHLLTIUOM` | 重控制时间单位 | 时间单位 | 重控制时间单位 | Rectrl time unit | M | 2759 |
| 129 | `EXPNUM` | 导出编号 | 导出数 | 导出编号 | Export number | L | 0 |
| 130 | `CREDAT` | 创建日期 | 创建日期 | 创建日期 | Date created | D | 0 |
| 131 | `CREUSR` | 创建用户 | 创建用户 | 创建用户 | Creation user | A | 0 |
| 132 | `UPDDAT` | 修改日期 | 修改日期 | 修改日期 | Change date | D | 0 |
| 133 | `UPDUSR` | 修改用户 | 修改用户 | 修改用户 | Change user | A | 0 |
| 134 | `STAFED` | 联邦国家 | 联邦国家 | 联邦国家 | Federal state | A | 0 |
| 135 | `PLMITMREF` | PLM产品 | PLM产品 | PLM产品 | PLM product | PLM | 0 |
| 136 | `PLMATTURL` | 相关文档 | 相关文档 | 相关文档 | Linked documents | A | 0 |
| 137 | `PLMHISURL` | PLM历史记录 | PLM归档 | PLM历史记录 | PLM history | A | 0 |
| 138 | `PCCCOD` | 费用属性 | 属性 | 费用属性 | Cost type | A | 0 |
| 139 | `MATTOL` | 三向匹配 | 三向匹配 | 三向匹配 | Matching tolerance | MAT | 0 |
| 140 | `SSTCOD` | SST税务代码 | SST税务代码 | SST税务代码 | SST tax code | ADI | 0 |
| 141 | `ALG` | 过敏原 | 过敏原 | 过敏原 | Allergens | A | 0 |
| 142 | `ALGDAT` | 过敏原变更日期 | 过敏原 | 过敏原变更日期 | Allergen change date | D | 0 |
| 143 | `ALGBOM` | 过敏原BOM代码 | 过敏原 | 过敏原BOM代码 | Allergen BOM code | C | 0 |
| 144 | `CREDATTIM` | 日期时间 | 日期时间 | 日期时间 | Date time | ADATIM | 0 |
| 145 | `UPDDATTIM` | 日期时间 | 日期时间 | 日期时间 | Date time | ADATIM | 0 |
| 146 | `AUUID` | 唯一识别码 | 唯一识别码 | 唯一识别码 | Single identifier | AUUID | 0 |
| 147 | `ITMSFTTYP` | SAF-T产品类型 | 产品类型 | SAF-T产品类型 | SAF-T product type | M | 2273 |
| 148 | `ECCFLG` | 版本管理 | 版本 | 版本管理 | Version management | M | 1 |
| 149 | `ECCSTO` | 存货版本 | 存货版本 | 存货版本 | Stock version | M | 2777 |
| 150 | `ECCMIN` | 次要计数器 | 次要计数器 | 次要计数器 | Minor sequence | ICV | 0 |
| 151 | `ECCMAJ` | 主要计数器 | 主要计数器 | 主要计数器 | Major sequence | ICV | 0 |
| 152 | `ECCBOMALT2` | 代码 | 代码 | BoM 交替  | Code | TBO | 0 |
| 153 | `ECCBOMALT3` | 代码 | 代码 | BoM 交替  | Code | TBO | 0 |
| 154 | `LOAECCFLG` | 版本预加载 | 版本 | 版本预加载 | Version preload | M | 1 |
| 155 | `INVPRODTYP` | 产品类型 | 产品类型 | 产品类型 | Product type | C | 0 |
| 156 | `ECCROUFLG` | 工艺路线版本 | 版本 | 工艺路线版本 | Routing version | M | 1 |
| 157 | `ECCROUALT` | 路径替代 | 工艺路线替换选择 | 路径替代 | Routing code | TRO | 0 |
| 158 | `YITMDES4` | 品名 | 品名 | 品名 | 品名 | DES | 0 |
| 159 | `YFLWJ` | 法兰外径 | 法兰外径 | 法兰外径 | 法兰外径 | A | 0 |
| 160 | `YWJ` | 外径 | 外径 | 外径 | 外径 | A | 0 |
| 161 | `YNJ` | 内径 | 内径 | 内径 | 内径 | A | 0 |
| 162 | `YGD` | 高度 | 高度 | 高度 | Height | A | 0 |
| 163 | `YFLHD` | 法兰厚度 | 法兰厚度 | 法兰厚度 | 法兰厚度 | A | 0 |
| 164 | `YLEN` | 长 | 长 | 长 | 长 | A | 0 |
| 165 | `YWID` | 宽 | 宽 | 宽 | 宽 | A | 0 |
| 166 | `YHIG` | 高(厚) | 高(厚) | 高(厚) | 高(厚) | A | 0 |
| 167 | `YPH` | 牌号 | 牌号 | 牌号 | 牌号 | A | 0 |
| 168 | `YBZ` | 标准 | 标准 | 标准 | Standard | M | 6002 |
| 169 | `YPP` | 品牌 | 品牌 | 品牌 | 品牌 | A | 0 |
| 170 | `YID` | 产品U8编码 | 产品U8编码 | 产品U8编码 | 产品U8编码 | A | 0 |
| 171 | `YU8DES` | 产品U8名称 | 产品U8名称 | 产品U8名称 | 产品U8名称 | A | 0 |
| 172 | `YYJFL` | 一级分类 | 一级分类 | 一级分类 | 一级分类 | ADI | 0 |
| 173 | `YEJFL` | 二级分类代码 | 二级分类代码 | 二级分类代码 | 二级分类代码 | ADI | 0 |
| 174 | `YSJFL` | 三级分类 | 三级分类 | 三级分类 | 三级分类 | ADI | 0 |
| 175 | `YYD` | 硬度 | 硬度 | 硬度 | 硬度 | A | 0 |
| 176 | `YBZ1` | 备注 | 备注 | 备注 | Notes | A | 0 |
| 177 | `YDPSX` | 垫片属性 | 垫片属性 | 垫片属性 | 垫片属性 | A | 0 |
| 178 | `YCCD` | 粗糙度 | 粗糙度 | 粗糙度 | 粗糙度 | A | 0 |
| 179 | `YHJCH` | 合金层厚 | 合金层厚 | 合金层厚 | 合金层厚 | A | 0 |
| 180 | `YBC` | 表层 | 表层 | 表层 | 表层 | A | 0 |
| 181 | `YPFOA` | PFOA | PFOA | PFOA | PFOA | A | 0 |
| 182 | `YAREA` | 面积单位 | 面积单位 | 面积单位 | 面积单位 | UOM | 0 |
| 183 | `YITMAREA` | 存货单位面积 | 存货单位面积 | 存货单位面积 | 存货单位面积 | A | 0 |
| 184 | `YMD` | 密度 | 密度 | 密度 | Density | A | 0 |
| 185 | `YPARTNOSTA` | PartNo.Status | PartNo.Statu | PartNo.Status | PartNo.Status | M | 6001 |
| 186 | `YZJCRL` | 中转框容量 | 中转框容量 | 中转框容量 | 中转框容量 | C | 0 |
| 187 | `YZJRQ` | 中转容器 | 中转容器 | 中转容器 | 中转容器 | M | 6003 |
| 188 | `YXZM1` | 限制码1 | 限制码1 | 限制码1 | 限制码1 | A | 0 |
| 189 | `YHLFLG` | 回炉料标记 | 回炉料标记 | 回炉料标记 | 回炉料标记 | M | 1 |
| 190 | `YHLITM` | 回炉料编码 | 回炉料编码 | 回炉料编码 | 回炉料编码 | ITM | 0 |
| 191 | `YHLJZWEI` | 回炉料浇筑重量 | 回炉料浇筑重量 | 回炉料浇筑重量 | 回炉料浇筑重量 | WEI | 0 |
| 192 | `YHLCZWEI` | 回炉料差值重量 | 回炉料差值重量 | 回炉料差值重量 | 回炉料差值重量 | WEI | 0 |
| 193 | `YCPNITMREF` | 芯子编码 | 芯子编码 | 芯子编码 | 芯子编码 | ITM | 0 |
| 194 | `YJZSIZE` | 浇筑尺寸 | 浇筑尺寸 | 浇筑尺寸 | 浇筑尺寸 | A | 0 |
| 195 | `YJTWJ` | 夹头外经 | 夹头外经 | 夹头外经 | 夹头外经 | A | 0 |
| 196 | `YJTGD` | 夹头高度 | 夹头高度 | 夹头高度 | 夹头高度 | A | 0 |
| 197 | `YSFFL` | 是否维护废料 | 是否维护废料 | 是否维护废料 | 是否维护废料 | M | 1 |

## 获取实际全部 SQL Server 列的下一步

执行 [`sql/read_only/inspect_five_tables.sql`](../../../sql/read_only/inspect_five_tables.sql)，筛选 `Schema=COB/XTEST` 且 `TableName=ITMMASTER`。该脚本依据 `sys.columns` **逐个获取真正存在的字段**，再与 `ATABZON + ATEXTE` 中文字典合并并展示实际类型、字符长度、精度、小数位、可空标志、菜单号；无法匹配标题的物理列也照样保留。

执行 [`sql/read_only/select_five_tables_chinese_aliases.sql`](../../../sql/read_only/select_five_tables_chinese_aliases.sql) 可在指定 Schema 以真实物理列生成中文别名 `TOP (20)` 查询，不会将 197 个字典代码误当成 197 个可 SELECT 列。

**本仓库不提交生产产品数据行。**
