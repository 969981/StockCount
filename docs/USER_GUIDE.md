---
title: "StockCount 软件使用手册"
aliases: ["软件使用说明","PDA盘点操作手册","现场部署指南"]
created: 2026-11-26
updated: 2026-11-26
status: reference
area: usage
tags: [StockCount, usage, documentation]
up: "[[docs/00-文档导航]]"
---

# StockCount 软件使用手册

> 适用源码：`805ce006b2e365e28727fce49159f29e083c7c7e`（2026-11-26，PR #45）。面向仓库操作员与部署维护人员；不含真实账号、地址、箱码或截图。PR #45 的 CI 已成功，真机、现场数据库性能和 ERP 联调需独立验收。

## 1. 软件组成、版本与安装

| 组件 | 职责 | 安装入口 |
| --- | --- | --- |
| Windows Avalonia 管理器 | 管理独立 C# API、数据库配置、日志、端口检查 | [Avalonia 单文件发布指南](StockCount-Avalonia-SingleFile-Publish-Guide.md) |
| Windows Tauri 管理器 | 同一 API 的另一管理器，独立环境授权 | [Tauri 管理器说明](SERVER_HOST_UI.md) |
| Android MAUI PDA | 扫码、录入、提交、只读历史 | [APK 与 ADB 安装](StockCount-Release-APK-ADB-Guide.md) |
| SQL Server / Sage X3 | 现有数据库与盘点单、标签、明细 | [业务 SQL 字典](database/BUSINESS_SQL_REFERENCE.md) |

推荐现场使用已验收的 Avalonia 便携版。将 StockCount.Server.Desktop.exe 与 StockCount.Api.exe 放在同一个用户可写目录（例如 D:\Apps\StockCount\），默认框架依赖发布须安装 x64 ASP.NET Core 10 Runtime；Tauri 需要 WebView2，便携包必须完整解压。**两种管理器不要同时监听同一端口。** APK 与 Windows API 应使用匹配版本，PR #44 新 /history 接口需要同步升级 API；仅 PR #45 本地开关不需再升级 #44 后端。

Debug APK 用于测试；正式使用固定签名 Release。ADB 安装示例（文件路径自行替换）：

```powershell
adb devices
adb install -r ".\StockCount-PDA.apk"
```

签名不同不能覆盖安装；不要为解决签名冲突直接卸载存在待确认提交的应用。升级前确认原 UUID 已处理、备份配置和日志，停止服务与 PDA 作业。发布版本与签名详见 [发布打包](RELEASE_PACKAGING.md)、[正式签名](ANDROID_RELEASE_SIGNING.md)、[本地部署](LOCAL_BUILD_DEPLOY.md)。

## 2. Windows Avalonia：首次配置

1. 打开管理器 → 数据库配置，输入现场 SQL Server 地址/实例、数据库（默认 sagex3）、认证方式（Windows 集成或 SQL 用户/密码）。密码留空保留已保存密码；加密推荐开启，信任服务器证书仅经核准的受控环境使用。
2. 保存数据库配置，测试已保存连接（SELECT 1）。成功仅表示连接可用，不代表真实业务表和写入权限均正确。
3. API 与 PDA 访问：业务端口默认5080，管理端口自动+1（仅本机）；可信隔离局域网 HTTP 及免密模式按实际安全策略设置。首次 Avalonia UI 默认 LAN、免密及信任证书开启；既有配置可能不同。不要对公网开放。
4. 保存 API 连接设置；旧版密钥认证模式复制旧版密钥给可信 PDA，免密模式不填密钥。服务总览确认 API 健康，必要时手动启动/停止/刷新。
5. **注意 Avalonia 现场授权策略：** 当前代码在批准现场写入模式下应用 XTEST 与 COB 写入及结构核验标志为 true，保存数据库配置也会应用该策略。这不是新用户的一般安全默认，不代表真实结构或权限已经核验。上线前管理员必须核验两帐套、SQL 账号权限、触发器、单位和 ROWID。不能依赖旧文档的“默认关闭”来保护 Avalonia 现场版。API 仍执行事务校验；未获授权环境请使用只读数据库账号或停止对 PDA 开放。
6. X3 操作员编码可选，留空使用 PDA（最多5字符）；仅应用审计标识，不代表真实操作员登录。
7. 盘点库位校验规则：XTEST/COB 各有站点、仓库两个开关，默认四项关闭，仍必须存在库位。勾选后对应维度必须与单头匹配；两项都勾选时多条范围匹配拒绝。保存会重启当前管理器的 API，先暂停 PDA 提交。

| 校验站点 | 校验仓库 | 条件 |
| --- | --- | --- |
| 关 | 关 | 本帐套存在库位 |
| 开 | 关 | 库位与单头站点一致 |
| 关 | 开 | 库位与单头仓库一致 |
| 开 | 开 | 站点、仓库均一致且范围内唯一 |

服务总览可打开中文 Swagger（/docs），只提供 GET 在线测试，不在 Swagger 中提交。独立 /health 仅检测 API 存活、/ready 才查询 SQL Server。发现外部 API 时先确认进程身份，不随意结束不明 PID；明确停止其他设备提交后按专用确认入口处理。

### Tauri 管理器差异

使用服务总览、数据库连接、API 与设备、SQLite 日志四页；先停止服务再保存配置。Tauri 的 Test/Production 写入与结构审核保持独立 opt-in 默认关闭；与 Avalonia 现场策略不同。旧 Tauri 界面中关于额外幂等迁移表的提示已不符合当前 API：原生 XCOUTD.AUUID_0 已承担幂等标识，不执行旧迁移。两个管理器共用同类 API 和数据文件约定，但不要假设配置策略完全相同。

## 3. PDA 首次配置与连接

打开软件，无应用内登录。选择盘点单页右上角齿轮进入「连接与设备设置」。

| 设置项 | 默认/说明 |
| --- | --- |
| 服务端 API 地址 | 首次未配置；填现场 Windows LAN IP:5080 或完整 URL，不填 SQL Server 地址 |
| 旧版服务密钥 | 可选；可信免密模式留空 |
| 帐套 | 测试 Test/XTEST；正式 Production/COB 切换需确认 |
| 广播 Action | com.service.scanner.data |
| Extra 字段 | ScanCode |
| 声音、震动、成功/失败反馈 | 开 |
| 业务音量增益 | 1.0×；0.5×～3.0×，步长0.25× |
| 额外读码音 | 关 |
| 业务提示显示时长 | 2秒；0.5/1/2/3/5秒 |
| 允许查看已盘记录 | 关 |

输入地址后点「测试连接」，无需先保存，依次检查 TCP、API 健康、SQL 数据库就绪；然后点击「保存设置」。扫码 Action/Extra 修改后点击「应用扫码参数（即时生效）」，在此页面按实体扫码键验证接收诊断。声音/震动及盘点交互选项修改即时保存，不依赖底部保存按钮。应用不会直连 SQL。

## 4. 日常盘点（操作员）

1. 确认页顶帐套，通常先用 XTEST 验收。列表显示当前帐套所有现有盘点单，按日期降序，支持按单号/名称搜索、刷新、每批100条加载更多；可提交/只读是授权状态。
2. 选择目标盘点单，核对单号、名称、站点、仓库，扫描实际库位（LC. 前缀或纯库位码）。成功后库位保留。
3. 扫描箱标签完整二维码（示例 ID.BOX-DEMO-001@@QT.100），成功后显示箱码、产品编码、品名、规格、批号、存货单位；同帐套所有盘点单的重复箱号会被拒绝。不根据标签 STA_0 拦截。
4. 数字键盘自动弹出，人工输入实际 STU 数量，允许0及符合服务端精度的小数，不从 QR QT 或标签数量自动填充；点 × 清空重输。箱码识别后自动滚至详情底部，环境及库位保持可见；宽度足够时数量与提交同行，窄屏自动上下布局。无需关闭键盘即可提交。
5. 点击提交一次，等待服务器确认；忙碌状态不能重复提交或扫码覆盖当前箱。成功后清当前箱信息和数量、保留库位、收起键盘并回到详情顶部，成功音及200ms震动，显示非阻塞成功提示默认2秒。
6. 不需要等提示消失即可扫描下一箱。切换库位时扫描新库位；尚有未提交箱时按提示选择继续或丢弃，不能用新扫描悄悄覆盖旧数量。

### 失败、未知和恢复

- 扫码/数量错误、重复箱码（含明确并发重复拒绝）：红色提示按统一时长自动消失，无需点击；失败音及1000ms震动。其他提交拒绝仍人工确认。
- 超时或结果待确认：不要重建 UUID、不要换帐套或服务地址。使用「检查提交结果」或「重试原提交」，原 UUID 与数量保留。
- 重启应用：选择盘点单列表「恢复待确认提交」，恢复原单据、库位、箱号、数量，先检查结果。
- 服务器已确认成功但本机收尾失败：按提示处理，不再盲目提交。
- 成功音可能在库位与箱码解析时出现，不代表盘点已入库；只有明确提交成功才是确认结果。

## 5. 已盘记录（默认隐藏）

设置 → 盘点交互设置 → 开启「允许查看已盘记录」，返回盘点单再进入扫码页，点右上角入口。首页显示真实总记录行数与筛选结果，50条一批、距末尾5条自动加载。支持箱码包含搜索（20字符）和实盘库位精确筛选（10字符），点「查询 / 筛选」生效，清空筛选恢复全部；刷新读取最新数据。单窗口最多500条，点「继续更早记录」换段；加载失败保留原游标可重试。历史记录只读，不能修改删除。关闭开关隐藏入口并保护页面查询；它不是权限控制，用户可自行开启。

## 6. 配置、声音和诊断

| 本地键 | 默认/存储 |
| --- | --- |
| StockCountApiUrl | 空，Preferences |
| StockCountEnvironment | Test，Preferences |
| StockCountDeviceKey | 旧版密钥，SecureStorage |
| StockCount.Scanner.BroadcastAction | com.service.scanner.data |
| StockCount.Scanner.BarcodeExtra | ScanCode |
| StockCount.Feedback.SoundEnabled / VibrationEnabled | true |
| StockCount.Feedback.SuccessEnabled / FailureEnabled | true |
| StockCount.Feedback.ReadEnabled | false |
| StockCount.Interaction.SuccessToastDurationMs | 2000 |
| StockCount.Interaction.HistoryEnabled | false |

反馈音频：原版成功 OGG 626ms/震动200ms，原版失败 OGG 840ms/震动1000ms，额外合成读码 WAV 125ms/无震动；设置页可试听，不发送业务请求。声音使用媒体音量，SoundPool 未就绪或播放失败时业务音用 MediaPlayer 备用，不自动提高音量。用 adb logcat -s StockCountFeedback 核查播放状态（不含箱码）。客户端性能诊断最近512阶段样本，仅内存保留，支持刷新、复制、清空。

Windows 非敏感配置 server-settings.json、SQLite 日志 server-logs.db 位于管理器 EXE 同目录，可能存在 -wal/-shm 文件；停止服务后一起备份，Windows Credential Manager 凭据不会随文件迁移。Avalonia SQLite 日志可搜索最近500条、显示最多200条，启动清理30天前日志，手动清理支持1/3/7/14/30天且二次确认；它只删除日志，不删除 ERP 数据。日志可包含业务字段，限制授权人员访问，不向公开仓库上传。

## 7. 常见故障与处理

| 现象 | 检查/操作 |
| --- | --- |
| TCP 5080 失败但 ping 通 | 服务监听、PDA Wi-Fi、Windows 防火墙的来源网段范围；跨子网不能只依赖 LocalSubnet |
| /health 通但 /ready 失败 | SQL 地址、认证、证书、数据库权限 |
| 401/403 | 密钥/免密策略、来源网段、帐套写入授权 |
| 扫码无响应 | Action、Extra、实体扫码广播；在设置页诊断 |
| 库位不存在/范围不符 | 帐套、库位码、站点仓库校验开关 |
| 该箱号已扫描 | 当前帐套全单防重；用业务 SQL 按箱号核验，不删除历史 |
| 无已盘记录入口 | 设置开启本地开关后重新进入 |
| 历史查询失败 | 检查 API 已含 PR #44 /history 路由及连接 |
| 提交待确认 | 复用原 UUID 检查/重试 |
| 键盘遮挡提交 | 确认新版 APK、系统输入法和字体；记录真机现象 |

网络排查命令（仅替换为本地实际地址）：

```powershell
ipconfig
Get-NetTCPConnection -LocalPort 5080 -State Listen
Get-NetFirewallRule -DisplayName "StockCount API TCP 5080" | Get-NetFirewallAddressFilter
Test-NetConnection <API_HOST> -Port 5080
```

防火墙修改由管理员依据可信 PDA 实际网段执行，禁止无范围开放公网；详见 [Windows 5080 排障](StockCount-Windows-5080-Firewall-Guide.md)、[局域网联调](HTTP_LAN_DEBUG.md)。

## 8. 交付验收清单

- Windows 正确启动/停止，数据库连接正常，PDA 同/跨子网接入。
- 测试帐套扫码库位、箱码，数量0/小数、键盘直接提交、声音震动、2秒提示与连续作业。
- 重复箱、断网与同 UUID 恢复，核对两数量相等及原请求查询。
- 历史开关默认关闭，开启后总数/筛选/50条分页、500条窗口，重启保存。
- 先经授权 XTEST 验证，COB 上线另核对数据库权限、结构和现场安全措施。不得为验收擅自写正式数据。

关联：[业务 SQL 字典](database/BUSINESS_SQL_REFERENCE.md) · [API](API_CONTRACT.md) · [PDA 专项文档索引](pda/00-PDA扫码知识地图.md) · [验收计划](TEST_PLAN.md)。


业务音量增益仅影响软件成功/失败/待确认音：1.0× 原始 OGG，低于1.0×用播放器衰减，高于1.0×使用构建时预生成的限峰 WAV。试听即时生效；不改系统媒体音量、硬件读码声或震动；0媒体音量仍静音。Android 本地编译需要 Windows x64、PowerShell 7 与 MAUI Android 工作负载。FFmpeg 8.1.2 由项目自动下载、校验并缓存，无需手动安装或配置 PATH；首次生成需联网，之后缓存可离线复用。可先执行 `pwsh -File scripts/generate-android-feedback-gain.ps1`。
