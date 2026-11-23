# 海康 PDA 扫码资料来源与证据状态

## 历史依据

| 资料 | 来源/版本 | 用途 | 存档约束 |
| --- | --- | --- | --- |
| 《读码模组扫描服务广播协议开发指南》 | 海康原厂 V1.0.1，30 页 PDF，已在用户私有资料库 | 启停、OutputMode、ScanMode、ScanCode/Type、广播配置 | 不把整份厂商 PDF 复制到公开 GitHub |
| 《海康 PDA 扫码广播接入与外协作业调用链》 | 用户历史私有工作笔记，2026-11-19 | MV-IDP5204 / Android 11、MAUI `BroadcastReceiver` / `IScannerService` 还原 | 项目历史事实，不等于最终原始源码 |
| 《外协扫码PDA程序_完整AI开发提示词》 | 2026-07-03 历史需求说明 | .NET 10 / MAUI / 仓库扫描 API 的架构思路 | OutsourceFlow 的工序/随车卡/流水号业务**不能移植进 StockCount** |
| StockCount 规范 | 现仓库 `requirements.md` / `AGENTS.md` | StockCount 实盘业务规则优先级最高 | 禁止被旧外协项目业务覆盖 |

## 厂商协议 V1.0.1 页码线索

- PDF 印刷第 3～5 页：启动/停止服务、`SINGLEMODE`、三种扫码模式；
- PDF 印刷第 12 页：`BROADCASTMODE`，默认 `FOCUSANDCOVERMODE`；
- PDF 印刷第 16～18 页：`com.service.scanner.data`、`ScanCode`、`ScanCodeType` 及自定义 Action/Extra；
- PDF 印刷第 15～16 页：震动/提示音；
- 需要确认其它参数（字符编码/前缀/剪切）时，以原 PDF 对应 2.x 章节为准，而不是根据字段名猜测。

## 需要现场再验的事实

- [ ] 当前目标 PDA 真正的型号、Android 版本和扫描服务版本；
- [ ] 当前配置的 Action/Extra 是否为 V1.0.1 默认；
- [ ] `BROADCASTMODE` 是否正确工作；
- [ ] 是否仍由**实体扫码键**触发；服务 `start` 与软件触发操作区别；
- [ ] Android 13/14+ 上动态接收器权限、注册生命周期；
- [ ] 真实设备连续扫码、弱网、切换帐套时的异常恢复；
- [ ] StockCount `XTEST` 业务闭环（不等于旧 OutsourceFlow 流程）。

完整指引见 [[海康PDA-广播协议与接入总览]]、[[海康PDA-调试排障与验收]]；研发边界见 [[requirements]]、[[AGENTS]]。
