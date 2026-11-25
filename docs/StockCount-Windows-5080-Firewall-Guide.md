---
title: "StockCount Windows 5080 端口开放与局域网排查"
aliases: ["5080 端口", "Windows 防火墙", "PDA 网络排查"]
created: 2026-11-25
tags: [StockCount, Windows, Firewall, PDA, Network]
---

# StockCount Windows 5080 端口开放与局域网排查

适用 Windows 10/11、Windows Server 及 StockCount Avalonia 后台。以下以默认业务端口 TCP 5080 为例；如修改业务端口，命令与 PDA 地址同步修改。本文未修改实际服务器防火墙，请由现场管理员执行。

## 1. 端口用途

| 端口 | 用途 | 是否开放入站 |
|---|---|---|
| TCP 5080 | PDA 业务 API、健康检查、Swagger | 仅可信 PDA 网段 |
| TCP 5081 | 本机管理与内部健康检查（业务端口 + 1） | 否，仅回环 |
| TCP 1433（示例） | 服务器到 Sage X3 SQL Server | 不需要为 PDA 在 API 主机开放 |

依据 [API Program.cs](../src/StockCount.Api/Program.cs) 与 [Avalonia 界面](../src/StockCount.Server.Desktop/MainWindow.axaml)：启用可信局域网 HTTP 后，Kestrel 业务端口监听所有 IP；关闭时只监听 localhost。管理端口始终 localhost。不要做公网端口映射、不要关闭整个 Windows 防火墙。免密访问仅用于隔离可信 PDA 网络。

## 2. 启动 API 与确认网卡

双击 StockCount.Server.Desktop.exe，进入 API / PDA，确认监听端口 5080，勾选“允许可信隔离局域网 HTTP (监听所有 IP)”，按现场认证政策配置设备密钥或隔离网免密，保存后启动或重启 API。暂停 PDA 提交再重启。

在 PowerShell 检查 IP、网络类别：

```powershell
ipconfig
Get-NetIPConfiguration
Get-NetConnectionProfile | Format-Table InterfaceAlias,NetworkCategory,IPv4Connectivity
```

选择与 PDA 互通的实际网卡 IPv4。以下以 192.168.1.100 为服务器示例，PDA 子网 192.168.1.0/24 为示例，不能直接套用到所有现场。域网络或可信专用网络才使用下列默认规则；若网卡为 Public，请先由管理员确认网络性质与组织策略，不要盲目切换网络类别。

## 3. 创建防火墙规则（管理员 PowerShell）

推荐限定真实 PDA 网段（替换示例 CIDR）：

```powershell
New-NetFirewallRule -Name "StockCount-API-TCP-5080" -DisplayName "StockCount API TCP 5080" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Domain,Private -RemoteAddress 192.168.1.0/24
```

如果尚不知道具体 CIDR 且整个本地子网均为可信设备，可改用 LocalSubnet：

```powershell
New-NetFirewallRule -Name "StockCount-API-TCP-5080" -DisplayName "StockCount API TCP 5080" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Domain,Private -RemoteAddress LocalSubnet
```

二选一，不要重复创建。跨 VLAN 的 PDA 需按实际源网段配置并核查路由和 ACL。LocalSubnet 可能涵盖多个本机连接的子网，固定可信 CIDR 更精确。

已存在同名规则时，更新而不是重复新增（替换 CIDR）：

```powershell
Set-NetFirewallRule -Name "StockCount-API-TCP-5080" -Enabled True -Direction Inbound -Action Allow -Profile Domain,Private -RemoteAddress 192.168.1.0/24
Get-NetFirewallRule -Name "StockCount-API-TCP-5080" | Get-NetFirewallPortFilter
```

若既有规则的协议或端口不正确，先核实其用途再更正或重建，不能仅依据显示名称认定规则正确。

CMD 备选（管理员 CMD，不与 PowerShell 方法重复创建）：

```cmd
netsh advfirewall firewall add rule name="StockCount API TCP 5080" dir=in action=allow protocol=TCP localport=5080 profile=domain,private remoteip=192.168.1.0/24
```

图形界面：Win+R → wf.msc → 入站规则 → 新建规则 → 端口 → TCP → 特定本地端口 5080 → 允许连接 → 域、专用（不选公用） → 命名。创建后打开规则属性 → 作用域 → 远程 IP 地址，限定实际 PDA 网段。

## 4. 查看防火墙是否放行

```powershell
Get-NetFirewallRule -Name "StockCount-API-TCP-5080" -PolicyStore ActiveStore | Format-List Name,DisplayName,Enabled,Direction,Action,Profile
Get-NetFirewallRule -Name "StockCount-API-TCP-5080" -PolicyStore ActiveStore | Get-NetFirewallPortFilter | Format-List Protocol,LocalPort,RemotePort
Get-NetFirewallRule -Name "StockCount-API-TCP-5080" -PolicyStore ActiveStore | Get-NetFirewallAddressFilter | Format-List LocalAddress,RemoteAddress
Get-NetFirewallProfile | Format-Table Name,Enabled,DefaultInboundAction
```

预期 Enabled=True、Inbound、Allow、TCP、LocalPort=5080，当前网卡类别匹配 Profile，PDA 地址匹配 RemoteAddress。ActiveStore 检查有效策略；若本地规则被域策略限制或存在显式 Block，Allow 规则也未必能生效。

若用 GUI/CMD 创建或名称不同，可以用显示名称查找：

```powershell
Get-NetFirewallRule -DisplayName "StockCount API TCP 5080" -PolicyStore ActiveStore | Format-List Name,Enabled,Direction,Action,Profile
```

查找所有明确配置 5080 的有效 TCP 入站规则：

```powershell
Get-NetFirewallPortFilter -PolicyStore ActiveStore |
  Where-Object { $_.Protocol -in @('TCP','6') -and $_.LocalPort -contains '5080' } |
  Get-NetFirewallRule |
  Where-Object Direction -eq Inbound |
  Format-Table Name,DisplayName,Enabled,Action,Profile
```

该筛选只查显式 5080，不涵盖所有端口、端口范围、程序规则及其他策略。最终以远端实际访问验证为准。

## 5. 查看监听端口及进程

```powershell
Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue |
  Format-Table LocalAddress,LocalPort,State,OwningProcess
Get-NetTCPConnection -LocalPort 5080,5081 -State Listen -ErrorAction SilentlyContinue |
  Select-Object LocalAddress,LocalPort,State,OwningProcess,@{Name='ProcessName';Expression={(Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName}}
```

CMD：

```cmd
netstat -ano | findstr :5080
```

只关注本地地址为 5080 且状态为 LISTENING 的行。示例：tasklist /FI "PID eq 1234"（替换 PID）。不要随意终止未知进程。

| 结果 | 含义 |
|---|---|
| 0.0.0.0:5080 | 所有 IPv4 网卡 |
| [::]:5080 | IPv6 通配监听，Kestrel 可能双栈，实际测试 IPv4 |
| 127.0.0.1:5080 / [::1]:5080 | 本机回环 |
| 无监听 | API 未启动、启动失败或端口设为其他值 |
| 5081 仅回环 | 正常的内部管理监听 |

## 6. 验证本机及 PDA 访问

服务器 PowerShell：

```powershell
Test-NetConnection 127.0.0.1 -Port 5080
Invoke-RestMethod "http://127.0.0.1:5080/health"
Invoke-RestMethod "http://192.168.1.100:5080/health"
```

健康接口预期 service=StockCount.Api、status=Running（示例，不是现场实测）。本机访问自身 LAN IP 成功不证明远程入站防火墙已放行。

在另一台同网段 Windows 电脑执行（替换实际 IP）：

```powershell
Test-NetConnection 192.168.1.100 -Port 5080
Invoke-RestMethod "http://192.168.1.100:5080/health"
```

TcpTestSucceeded=True 表示 TCP 连接成功；HTTP /health 返回成功才表示应用可达。PDA 浏览器打开 http://192.168.1.100:5080/health，或使用 PDA 设置页中的连接测试。API 基础地址填 http://192.168.1.100:5080（不要填 localhost 或 /health）。可信局域网 Swagger 可访问 /docs。

/health 只确认 API 存活；/ready 才测试数据库。数据库就绪请求需要按当前配置传递环境及必要的认证头，详见 [API 运行手册](../src/StockCount.Api/RUN.md)。不能把 401/403 或数据库连接失败等同于防火墙阻断。

## 7. 常见故障定位

| 现象 | 优先检查 |
|---|---|
| 本机 127.0.0.1:5080 无法连接 | API 未启动、端口冲突、运行时缺失、程序启动日志 |
| 本机成功、服务器 LAN IP 失败 | 监听地址是否只为回环，启用局域网 HTTP 并重启 API |
| 本机 LAN IP 成功、远端 TCP 失败 | 防火墙 Profile/作用域、域策略 Block、VLAN ACL、Wi-Fi 客户端隔离、实际 IP |
| TCP 成功、HTTP 401/403 | 设备认证、访问来源和应用策略 |
| TCP 成功、HTTP 404 | URL、服务类型、端口 |
| /health 成功但 /ready 失败 | SQL Server 连接、凭据、环境与权限，不是 5080 防火墙问题 |
| ping 不通 | ICMP 可能被禁止，不能单独据此判断 TCP 5080 |

## 8. 撤销开放

管理员 PowerShell（仅修改本文创建的规则）：

```powershell
Disable-NetFirewallRule -Name "StockCount-API-TCP-5080"
# 确认不再需要后删除：
Remove-NetFirewallRule -Name "StockCount-API-TCP-5080"
```

若通过 GUI/CMD 创建，先用 Get-NetFirewallRule -DisplayName 确认唯一规则，再按实际 Name 禁用或删除。若不再需要 PDA 远程访问，管理器中取消可信局域网 HTTP，保存并重启 API。不要关闭整个防火墙。

相关文档：[本地构建与部署](LOCAL_BUILD_DEPLOY.md)、[Avalonia 单行发布](StockCount-Avalonia-SingleFile-Publish-Guide.md)、[Microsoft New-NetFirewallRule](https://learn.microsoft.com/powershell/module/netsecurity/new-netfirewallrule)、[Microsoft Test-NetConnection](https://learn.microsoft.com/powershell/module/nettcpip/test-netconnection)。

现场验收：确认本机监听、有效防火墙规则、另一台设备 TCP 连通、PDA /health 成功、按环境验证 /ready；本文未在现场机器执行命令。
