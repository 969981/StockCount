---
aliases: ["StockCount 局域网 HTTP 调试", "LAN HTTP"]
title: StockCount · PDA 局域网 HTTP 编译与联调
created: 2026-11-23
updated: 2026-11-23
status: implemented-awaiting-hardware-validation
tags: [StockCount, Android, PDA, Windows, HTTP, LAN, Debug]
---

# StockCount · Windows + 海康 PDA 局域网 HTTP 联调

> 用户要求：PDA → Windows 后端采用 **HTTP**，不使用 HTTPS/PFX。此配置**只供受控、隔离或可信的本地网络**，HTTP 的设备密钥、二维码、盘点数量等都可被网络内具备嗅探能力的设备读取或篡改。不能用于公网、酒店/公共 Wi-Fi 或可被不受信任设备加入的生产网段。

## 1. 当前网络结构

```text
海康 PDA（.NET MAUI / Android）
  │ http://<Windows-局域网-IP>:5080
  │ X-StockCount-Device-Key + X-StockCount-Environment
  ▼
Windows Avalonia / Tauri 2 管理器 → ASP.NET Core 10 Sidecar → SQL Server
                       ├─ 5080 HTTP：仅启用 allowLan 时监听 0.0.0.0
                       └─ 5081 HTTP：始终仅监听 localhost，内部管理用途
```

默认仍为 `allowLan=false`，只有 Windows 本机可连。开启「允许通过受控局域网向 PDA 提供 HTTP API」后才开放 5080；Avalonia 可明确启用可信局域网免密模式，此时 `/api/v1` 与 `/ready` 仅允许直接内网 IPv4 或回环来源；旧版密钥模式仍要求设备鉴权。XTEST 和 COB 写入**默认均关闭**，变更传输协议不等于获得数据库写权限。

Android Manifest 已启用 `android:usesCleartextTraffic="true"`。StockCount 客户端校验 API 地址：仅允许 `http://10.x.x.x`、`http://172.16~31.x.x`、`http://192.168.x.x`、localhost/loopback，或 HTTPS；拒绝通过 HTTP 连接公网 IP/域名。建议直接填写电脑的 RFC1918 局域网 IPv4 地址。

## 2. 编译并启动 Windows 后端

```powershell
cd 'D:\WorkSpace\Work\Business\00_WorkLogs\StockCount'
git pull --ff-only
pwsh -NoProfile -File .\scripts\build-tauri-sidecar.ps1
cd .\desktop\server-console
npm install
npm run dev:desktop
```

如果需要可分发的 Windows 安装包和免安装 ZIP，回到仓库根目录运行 `pwsh -File .\scripts\build-local.ps1 -Target Windows`。修改 C# API 后需要重新发布 Sidecar、重新启动管理器/API；仅改 React UI 可以走 Vite 热更新。

在 Tauri 设置「API 与设备」中启用局域网 HTTP，端口默认 `5080`，保存、停止旧服务、重新启动。**无需生成 PFX，不填写证书路径和密码**。不要禁用 SQL Server 连接加密：SQL Server TLS 与 PDA → API HTTP 是两条独立链路。

Windows 查看当前主机 LAN IPv4：

```powershell
Get-NetIPAddress -AddressFamily IPv4 | Where-Object {
  $_.IPAddress -like '192.168.*' -or $_.IPAddress -like '10.*' -or
  $_.IPAddress -match '^172\.(1[6-9]|2[0-9]|3[01])\.'
} | Select-Object InterfaceAlias, IPAddress, PrefixLength

# 后端进程和本机路由验证
Get-NetTCPConnection -LocalPort 5080 -State Listen
curl.exe http://127.0.0.1:5080/health
```

## 3. Windows 防火墙仅允许 PDA 网段

**必须确认实际 PDA 子网再替换下方示例值**，以下 `192.168.1.0/24` 只是示例，不代表你当前的网络。执行 `New-NetFirewallRule` 需要管理员 PowerShell。不要加 `0.0.0.0/0`、`Any` 或在路由器上做公网端口映射。

```powershell
# 例：只允许可信的 192.168.1.0/24 PDA 网段访问端口 5080
New-NetFirewallRule -DisplayName 'StockCount PDA HTTP 5080' `
  -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5080 `
  -RemoteAddress '192.168.1.0/24' -Profile Private

# 检查规则及其它可能已存在的宽松入站规则
Get-NetFirewallRule -Direction Inbound -Enabled True |
  Where-Object DisplayName -Match 'StockCount|5080'
```

注意：新增限定网段的允许规则，**并不会自动撤销其它已有的广泛允许规则**。若此前配置过开放 5080 的规则，必须一并审查与收紧。

## 4. 构建并安装 Debug APK

```powershell
cd 'D:\WorkSpace\Work\Business\00_WorkLogs\StockCount'
dotnet workload install maui-android
pwsh -NoProfile -File .\scripts\build-local.ps1 -Target Android -AndroidVariant Debug
adb devices
$apk = Get-ChildItem .\dist\local\*-Android-Debug-Signed.apk | Sort-Object LastWriteTime -Descending | Select-Object -First 1
adb install -r -t $apk.FullName
```

在 PDA 设置中填 `http://<Windows-局域网-IP>:5080`（也可直接填写 IP:端口），选择 `Test / XTEST`。Avalonia 免密模式不需要设备密钥；旧版密钥模式才需输入。可先「测试连接」再保存，测试不改变已保存地址、帐套或密钥。**不要把 `http://127.0.0.1:5080` 填到真机上**；那指向的是 PDA 自己，而不是 Windows 电脑。

还可从 PDA 的浏览器打开 `http://<Windows-局域网-IP>:5080/health` 做最基础的网络连通检查。`/health` 正常仅表示 API 进程可达，`/ready` 用于检验数据库，但需要设备密钥等请求头；建议直接用 PDA 设置页的「测试连接」功能检查。

### 4.1 分层连接诊断与后台日志

PDA 逐项显示目标地址、帐套、TCP 连接耗时、API 身份、HTTP 状态和数据库 SELECT 1 结果。TCP 失败时补做 ICMP Ping；没有 Ping 响应可能是设备不支持、目标禁止 ICMP 或网络故障，不能仅凭此判定 IP 不通。连接被拒绝、超时、网络不可达、域名解析失败和 HTTPS 握手失败有不同中文提示。

TCP 成功不代表 StockCount API 可用；只有 /health 返回 StockCount 服务标识才继续 /ready。401 表示设备未授权，403 表示来源或策略拒绝，503 表示数据库未就绪。测试不进行 ERP 写入。

Avalonia 的 SQLite 日志显示「PDA 连接诊断」分类：收到 /health 或 /ready 连接请求时记录来源 IP、监听端口、帐套、诊断编号，完成时记录结果、HTTP 状态及耗时。与 PDA 显示的编号对应查询；本机无标记健康轮询不记录，设备密钥、数据库密码、扫码内容不进入诊断日志。若 PDA 仅有 TCP/Ping 失败、尚未发送 HTTP 请求，后台不会凭空产生请求日志。

### 4.2 中文 Swagger 文档

API 运行后访问 `http://服务器IP:5080/docs`（根地址或 /swagger 也可跳转），后台服务总览提供打开按钮。OpenAPI JSON 地址为 /openapi/v1.json。所有资源随 API 发布，内网无需联网。中文分组、请求头、参数、返回模型可直接查看；页面只允许 GET 在线调试，POST 仅展示文档，不直接提交盘点。设备授权、帐套和写入门禁保持不变。

## 5. 海康扫码与业务验收

扫码广播默认 Action 为 `com.service.scanner.data`，Extra 为 `ScanCode`。在 PDA 设置页扫描测试码，看到广播时间/长度后再验证选择盘点单、扫描库位、扫描完整箱码、人工录入数量。若局域网 API 通、扫描广播不通，则是设备适配问题而不是 HTTP 问题。

在 DBA 同意以前不要打开任何写入开关；只对 XTEST 做只读检验。首次真实写入必须先部署幂等表、确认 XCOUTD 字段和事务回滚，在受控测试单据验证；不得直接操作 COB。

## 6. 常见排查

| 现象 | 首查位置 |
| --- | --- |
| Tauri 本机可以 `/health`，PDA 无法访问 | 未启用 allowLan、未重启 Sidecar、防火墙、PDA 与电脑不在可达网段 |
| 在 PDA 输入 `http://localhost` 后无响应 | localhost 指向 PDA 本机，改用电脑 LAN IPv4 |
| `Cleartext HTTP traffic not permitted` | APK 仍是旧版本，重新编译并安装包含新 Manifest 的 Debug APK |
| `DEVICE_NOT_AUTHORIZED` | PDA 设备密钥与 Windows 管理器的不一致 |
| `/health` 正常而 `/ready` 失败 | SQL Server 网络、权限、配置和 Test/Production 环境 |
| `SCHEMA_NOT_APPROVED`、`ERP_WRITE_UNAVAILABLE` | 测试帐套未批准写入，属预期保护 |

## 7. 安全边界

HTTP 不提供加密、服务端身份验证或防中间人篡改。**设备密钥在 HTTP 中属于可被截获的 bearer credential**。因此仅建议隔离 PDA Wi-Fi / VLAN、严格防火墙白名单、最小权限 SQL 凭据、测试帐套优先、不经公网 NAT 或反向代理暴露、必要时更换设备密钥。若将来上线涉及真实生产盘点，应优先采用 HTTPS 或受控 VPN/专网加密链路，而不是假设“内网天然安全”。

相关：[[docs/LOCAL_BUILD_DEPLOY|本地构建部署]] · [[docs/SCANNER_INTEGRATION|海康广播扫码]] · [[docs/SECURITY_AND_ENVIRONMENTS|环境与安全]]。
