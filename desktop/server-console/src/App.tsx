import { useCallback, useEffect, useMemo, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import {
  Activity, AlertCircle, ArrowRight, Check, CheckCircle2, CircleHelp, Clipboard,
  Database, FileClock, Globe2, HardDrive, KeyRound, LayoutDashboard, LoaderCircle,
  LockKeyhole, Play, Power, RefreshCw, Save, Search, Server,
  Settings2, ShieldCheck, Square, Wifi, XCircle
} from "lucide-react";

type View = "overview" | "database" | "access" | "logs";
type Settings = {
  apiPort: number;
  allowLan: boolean;
  certPath: string;
  sqlServer: string;
  sqlDatabase: string;
  integratedSecurity: boolean;
  sqlUser: string;
  encryptSql: boolean;
  trustServerCertificate: boolean;
  enableTestWrites: boolean;
  enableProductionWrites: boolean;
  schemaReviewed: boolean;
  productionSchemaReviewed: boolean;
  x3AuditUser: string;
};
type SettingsView = Settings & {
  sqlPasswordSaved: boolean;
  certPasswordSaved: boolean;
  deviceKeyReady: boolean;
};
type ServerStatus = {
  running: boolean;
  healthy: boolean;
  apiPort: number;
  allowLan: boolean;
  lastError: string | null;
};
type LogRow = {
  id: number;
  timestampUtc: string;
  level: string;
  category: string;
  message: string;
  exception: string | null;
};
type DatabaseTest = { ok: boolean; message: string };
type Toast = { kind: "success" | "error" | "info"; message: string };

const defaults: SettingsView = {
  apiPort: 5080,
  allowLan: false,
  certPath: "",
  sqlServer: "",
  sqlDatabase: "sagex3",
  integratedSecurity: false,
  sqlUser: "",
  encryptSql: true,
  trustServerCertificate: false,
  enableTestWrites: false,
  enableProductionWrites: false,
  schemaReviewed: false,
  productionSchemaReviewed: false,
  x3AuditUser: "",
  sqlPasswordSaved: false,
  certPasswordSaved: false,
  deviceKeyReady: false,
};
const navItems = [
  { id: "overview" as const, label: "服务总览", caption: "运行状态与快捷操作", icon: LayoutDashboard },
  { id: "database" as const, label: "数据库连接", caption: "SQL Server 配置", icon: Database },
  { id: "access" as const, label: "API 与设备", caption: "HTTPS 和 PDA 接入", icon: Globe2 },
  { id: "logs" as const, label: "SQLite 日志", caption: "运行事件与诊断", icon: FileClock },
];

function formatUtc(value: string) {
  try { return new Date(value).toLocaleString("zh-CN", { hour12: false }); }
  catch { return value; }
}

export default function App() {
  const [view, setView] = useState<View>("overview");
  const [settings, setSettings] = useState<SettingsView>(defaults);
  const [sqlPassword, setSqlPassword] = useState("");
  const [certPassword, setCertPassword] = useState("");
  const [status, setStatus] = useState<ServerStatus>({
    running: false, healthy: false, apiPort: 5080, allowLan: false, lastError: null
  });
  const [logs, setLogs] = useState<LogRow[]>([]);
  const [search, setSearch] = useState("");
  const [selectedLog, setSelectedLog] = useState<LogRow | null>(null);
  const [dbTest, setDbTest] = useState<DatabaseTest | null>(null);
  const [logFile, setLogFile] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<Toast | null>(null);

  const toast = (kind: Toast["kind"], text: string) => setMessage({ kind, message: text });
  const set = <K extends keyof Settings>(name: K, value: Settings[K]) =>
    setSettings(previous => ({ ...previous, [name]: value }));

  const refreshStatus = useCallback(async () => {
    try { setStatus(await invoke<ServerStatus>("server_status")); }
    catch { /* When opened in a plain browser, the native Tauri commands are not available. */ }
  }, []);

  const refreshLogs = useCallback(async (filter: string) => {
    try { setLogs(await invoke<LogRow[]>("read_logs", { search: filter, limit: 250 })); }
    catch (error) { toast("error", String(error)); }
  }, []);

  useEffect(() => {
    let mounted = true;
    Promise.all([
      invoke<SettingsView>("read_settings"),
      invoke<string>("log_path")
    ]).then(([loaded, file]) => {
      if (mounted) { setSettings(loaded); setLogFile(file); }
    }).catch(error => {
      if (mounted) toast("info", "请通过 Tauri 桌面程序打开；浏览器预览无法管理本机服务：" + String(error));
    });
    void refreshStatus();
    const timer = window.setInterval(() => void refreshStatus(), 2200);
    return () => { mounted = false; window.clearInterval(timer); };
  }, [refreshStatus]);

  useEffect(() => {
    if (view !== "logs") return;
    void refreshLogs(search);
    const timer = window.setInterval(() => void refreshLogs(search), 4200);
    return () => window.clearInterval(timer);
  }, [view, search, refreshLogs]);

  const save = async (showToast = true) => {
    const config: Settings = {
      apiPort: settings.apiPort,
      allowLan: settings.allowLan,
      certPath: settings.certPath,
      sqlServer: settings.sqlServer,
      sqlDatabase: settings.sqlDatabase,
      integratedSecurity: settings.integratedSecurity,
      sqlUser: settings.sqlUser,
      encryptSql: settings.encryptSql,
      trustServerCertificate: settings.trustServerCertificate,
      enableTestWrites: settings.enableTestWrites,
      enableProductionWrites: settings.enableProductionWrites,
      schemaReviewed: settings.schemaReviewed,
      productionSchemaReviewed: settings.productionSchemaReviewed,
      x3AuditUser: settings.x3AuditUser,
    };
    // Credentials are sent to trusted native Rust IPC, never written into JSON.
    const saved = await invoke<SettingsView>("save_settings", {
      settings: config,
      sqlPassword: sqlPassword || null,
      certPassword: certPassword || null
    });
    setSettings(saved);
    setSqlPassword("");
    setCertPassword("");
    if (showToast) toast("success", "配置已保存到本机；密码保存在 Windows 凭据管理器。");
  };

  const perform = async (action: () => Promise<void>) => {
    if (busy) return;
    setBusy(true); setMessage(null);
    try { await action(); }
    catch (error) { toast("error", String(error)); }
    finally { setBusy(false); await refreshStatus(); }
  };

  const start = () => perform(async () => {
    await save(false);
    await invoke("start_server");
    toast("success", "API 子进程已发起启动，等待健康检查确认监听成功。");
  });

  const stop = () => perform(async () => {
    await invoke("stop_server");
    toast("info", "已请求后端安全停止，SQLite 日志将进行收尾写入。");
    if (view === "logs") await refreshLogs(search);
  });

  const test = () => perform(async () => {
    if (!status.running) await save(false);
    const result = await invoke<DatabaseTest>("test_sql_connection");
    setDbTest(result);
    toast(result.ok ? "success" : "error", result.message);
  });

  const copyDeviceKey = () => perform(async () => {
    await invoke("copy_device_key");
    toast("success", "已由本机系统剪贴板复制密钥；请只配置到可信 PDA 的安全存储。");
  });

  const pageTitle = navItems.find(n => n.id === view)?.label ?? "服务管理";
  const active = status.running && status.healthy;
  const baseAddress = settings.allowLan
    ? "https://<现场服务器域名>:" + settings.apiPort
    : "http://127.0.0.1:" + settings.apiPort;
  const alerts = useMemo(() => {
    const items: string[] = [];
    if (!settings.sqlServer.trim()) items.push("SQL Server 尚未配置");
    if (settings.allowLan && !settings.certPath.trim()) items.push("HTTPS 证书尚未配置");
    if (status.lastError) items.push(status.lastError);
    return items;
  }, [settings.sqlServer, settings.allowLan, settings.certPath, status.lastError]);

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark"><Server size={24} strokeWidth={2.2} /></div>
          <div><strong>StockCount</strong><span>SERVER CONSOLE</span></div>
        </div>
        <div className="menu-caption">管理中心</div>
        <nav aria-label="服务管理导航">
          {navItems.map(item => {
            const Icon = item.icon;
            return (
              <button key={item.id} className={"nav-item " + (view === item.id ? "active" : "")}
                onClick={() => setView(item.id)}>
                <span className="nav-icon"><Icon size={19} /></span>
                <span className="nav-copy"><strong>{item.label}</strong><small>{item.caption}</small></span>
                {view === item.id && <ArrowRight size={15} className="nav-arrow" />}
              </button>
            );
          })}
        </nav>
        <div className="sidebar-spacer" />
        <div className="sidebar-info">
          <div className="mini-status"><span className={"dot " + (active ? "green" : status.running ? "amber" : "gray")} />
            {active ? "API 运行正常" : status.running ? "正在检查服务" : "API 尚未启动"}
          </div>
          <p>后端 .NET 10 · 桌面 Tauri 2</p>
          <small>所有写入功能仍受服务器安全门禁约束</small>
        </div>
      </aside>

      <main className="main">
        <header className="topbar">
          <div>
            <div className="crumb">StockCount <span>/</span> 服务器管理 <span>/</span> {pageTitle}</div>
            <h1>{pageTitle}</h1>
            <p>{view === "overview" ? "控制 API 服务运行，并监控与 Sage X3 的连接。" :
              view === "database" ? "数据库配置保留占位，由现场人员在本机填写。" :
              view === "access" ? "管理服务监听方式、TLS 与 PDA 设备接入。" :
              "查看由 .NET ILogger 写入 SQLite 的服务运行记录。"}</p>
          </div>
          <div className={"top-status " + (active ? "positive" : status.running ? "pending" : "")}>
            <span className={"dot " + (active ? "green" : status.running ? "amber" : "gray")} />
            {active ? "运行中" : status.running ? "启动检查中" : "未启动"}
          </div>
        </header>

        {message && <div className={"notification " + message.kind} role="status">
          {message.kind === "success" ? <CheckCircle2 size={18} /> :
            message.kind === "error" ? <AlertCircle size={18} /> : <CircleHelp size={18} />}
          <span>{message.message}</span>
          <button aria-label="关闭提示" onClick={() => setMessage(null)}>×</button>
        </div>}

        {view === "overview" && <>
          <div className="stat-grid">
            <section className="stat-card"><div className="stat-name"><Activity size={18} /> 服务状态</div>
              <strong className={active ? "stat-good" : ""}>{active ? "运行正常" : status.running ? "正在检查" : "已停止"}</strong>
              <span>{active ? "健康接口已响应" : status.running ? "等待健康检查 / 查看日志" : "点击启动服务运行 API"}</span>
            </section>
            <section className="stat-card"><div className="stat-name"><Globe2 size={18} /> 监听端口</div>
              <strong className="mono">{settings.apiPort}</strong><span>{settings.allowLan ? "HTTPS · 局域网" : "HTTP · 仅本机"}</span>
            </section>
            <section className="stat-card"><div className="stat-name"><HardDrive size={18} /> 数据源</div>
              <strong>{settings.sqlServer ? "已填写配置" : "待配置"}</strong>
              <span>sagex3 · COB / XTEST 可独立授权写入</span>
            </section>
          </div>

          <section className="panel hero-panel">
            <div className="hero-art"><Power size={30} strokeWidth={1.7} /></div>
            <div className="hero-main">
              <div className="eyebrow">SERVICE CONTROL</div>
              <h2>StockCount API 服务</h2>
              <p>同一套 C# API 由 Tauri Sidecar 托管；可以直接启动或停止，不需要单独打开命令行。</p>
              <div className="endpoint"><Wifi size={17} /><span>{baseAddress}</span></div>
            </div>
            <div className="hero-actions">
              <button className="primary-btn" disabled={busy || status.running} onClick={start}>
                {busy ? <LoaderCircle className="spinning" size={18} /> : <Play fill="currentColor" size={17} />}
                启动服务
              </button>
              <button className="outline-btn" disabled={busy || !status.running} onClick={stop}>
                <Square size={15} /> 停止服务
              </button>
            </div>
          </section>

          <div className="overview-grid">
            <section className="panel quick-panel">
              <h3>快捷操作</h3><p>常用的服务和数据库诊断入口</p>
              <button onClick={test} disabled={busy} className="wide-action">
                <Database size={19}/><span><strong>测试数据库连接</strong><small>通过 SQL Server 执行 SELECT 1</small></span><ArrowRight size={17}/>
              </button>
              <button onClick={() => { setView("database"); setMessage(null); }} className="wide-action">
                <Settings2 size={19}/><span><strong>填写数据库配置</strong><small>地址、认证、加密及最小权限</small></span><ArrowRight size={17}/>
              </button>
              <button onClick={() => { setView("logs"); setMessage(null); }} className="wide-action">
                <FileClock size={19}/><span><strong>查看 SQLite 日志</strong><small>运行过程、HTTP 请求、错误信息</small></span><ArrowRight size={17}/>
              </button>
              {dbTest && <div className={"inline-result " + (dbTest.ok ? "good" : "bad")}>
                {dbTest.ok ? <Check size={16}/> : <XCircle size={16}/>} {dbTest.message}
              </div>}
            </section>
            <section className="panel checklist-panel">
              <h3>部署提示</h3><p>先完成配置，再向 PDA 开放访问</p>
              <div className="checkline"><span className={settings.sqlServer ? "check yes" : "check"}>{settings.sqlServer ? <Check size={13}/> : "1"}</span>
                <div><strong>数据库连接</strong><small>{settings.sqlServer ? "已填写服务器地址，仍须实际连接测试" : "请在数据库连接页填写现场服务器和账号"}</small></div></div>
              <div className="checkline"><span className="check">2</span>
                <div><strong>验证只读 API</strong><small>先在 XTEST 查看盘点单、库位与箱码</small></div></div>
              <div className="checkline"><span className={settings.allowLan && settings.certPath ? "check yes" : "check"}>{settings.allowLan && settings.certPath ? <Check size={13}/> : "3"}</span>
                <div><strong>PDA 内网接入</strong><small>配置可信 HTTPS 证书和设备密钥</small></div></div>
              <div className="security-note"><ShieldCheck size={18}/><span>测试与正式帐套可分别授权写入。仅服务端开关、结构核验和数据库权限均满足时才允许实际提交。</span></div>
            </section>
          </div>

          {alerts.length > 0 && <section className="inline-alert"><AlertCircle size={17}/><div>{alerts.map((alert, i) => <p key={i}>{alert}</p>)}</div></section>}
        </>}

        {view === "database" && <div className="form-layout">
          <section className="panel form-panel">
            <div className="section-head"><div><h3>SQL Server 连接</h3><p>由运维人员填入真实数据库信息。测试、正式使用同一数据库下的不同 Schema。</p></div><Database size={23}/></div>
            <div className="form-grid">
              <label className="field full">服务器 / 实例地址
                <input disabled={status.running || busy} value={settings.sqlServer} onChange={e=>set("sqlServer",e.target.value)}
                  placeholder="例如：SQL-SERVER 或 SQL-SERVER\INSTANCE（现场填写）"/>
              </label>
              <label className="field">数据库名
                <input disabled={status.running || busy} value={settings.sqlDatabase} onChange={e=>set("sqlDatabase",e.target.value)}
                  placeholder="sagex3"/>
              </label>
              <label className="field">认证方式
                <select disabled={status.running || busy} value={settings.integratedSecurity ? "windows" : "sql"}
                  onChange={e=>set("integratedSecurity",e.target.value==="windows")}>
                  <option value="sql">SQL Server 用户认证</option>
                  <option value="windows">Windows 集成认证</option>
                </select>
              </label>
              {!settings.integratedSecurity && <>
                <label className="field">SQL 用户名
                  <input disabled={status.running || busy} value={settings.sqlUser} onChange={e=>set("sqlUser",e.target.value)}
                    placeholder="只读数据库账号"/>
                </label>
                <label className="field">SQL 密码
                  <input disabled={status.running || busy} type="password" value={sqlPassword}
                    onChange={e=>setSqlPassword(e.target.value)}
                    placeholder={settings.sqlPasswordSaved ? "已安全保存；留空代表不修改" : "由现场人员填写"}/>
                </label>
              </>}
            </div>
            <div className="toggle-group">
              <label className="toggle"><input type="checkbox" disabled={status.running || busy} checked={settings.encryptSql}
                onChange={e=>set("encryptSql",e.target.checked)}/><span>加密 SQL Server 连接</span></label>
              <label className="toggle"><input type="checkbox" disabled={status.running || busy} checked={settings.trustServerCertificate}
                onChange={e=>set("trustServerCertificate",e.target.checked)}/><span>跳过证书验证（仅受控测试）</span></label>
            </div>
            <div className="hint-block" style={{ marginTop: 26 }}>
              <ShieldCheck size={18}/>
              <div style={{ flex: 1 }}>
                <strong style={{ display: "block", marginBottom: 8 }}>XTEST 实盘数据写入（受控测试）</strong>
                <p style={{ marginBottom: 11, lineHeight: 1.6 }}>
                  启用前请由数据库管理员执行 sql/migrations/001_stockcount_submission.sql，
                  核对 XTEST 的 XCOUTD.ROWID 为 IDENTITY、全部非空字段、Sage X3 触发器及 1:1 单位规则。
                  当前仅允许 CUNSTA_0=0 的已有盘点单提交。XTEST/COB 的启用开关彼此独立。
                </p>
                <label className="field" style={{ marginBottom: 13 }}>X3 审计用户编码（最大5字符）
                  <input disabled={status.running || busy} maxLength={5}
                    value={settings.x3AuditUser}
                    onChange={e=>set("x3AuditUser",e.target.value)}
                    placeholder="例如：PDA（须经现场确认）"/>
                </label>
                <label className="toggle" style={{ marginBottom: 12 }}>
                  <input type="checkbox" disabled={status.running || busy}
                    checked={settings.schemaReviewed}
                    onChange={e=>set("schemaReviewed",e.target.checked)}/>
                  <span>我已核查 XTEST 的物理结构、单位与 ERP 写入兼容性</span>
                </label>
                <label className="toggle">
                  <input type="checkbox" disabled={status.running || busy || !settings.schemaReviewed}
                    checked={settings.enableTestWrites}
                    onChange={e=>set("enableTestWrites",e.target.checked)}/>
                  <span>允许向 XTEST.XCOUTD 写入真实测试盘点数据</span>
                </label>
              </div>
            </div>
            <div className="hint-block" style={{ marginTop: 17, background: "#FFF9ED", borderColor: "#F5DBAA", color: "#725329" }}>
              <ShieldCheck size={18}/>
              <div style={{ flex: 1 }}>
                <strong style={{ display: "block", marginBottom: 8 }}>COB 正式帐套写入（独立授权）</strong>
                <p style={{ marginBottom: 11, lineHeight: 1.6 }}>
                  正式帐套同样可通过 POST 提交 XCOUTD 和 GET 查询原提交结果。
                  启用前必须单独核验 COB.XCOUTD 的字段/ROWID/单位/触发器、执行获批准的 SQL Server 幂等表迁移，
                  并确保连接账号对 COB 具备授权写入权限。勾选后仅在重启服务时生效，不改变 XTEST 开关。
                </p>
                <label className="toggle" style={{ marginBottom: 12 }}>
                  <input type="checkbox" disabled={status.running || busy}
                    checked={settings.productionSchemaReviewed}
                    onChange={e=>set("productionSchemaReviewed",e.target.checked)}/>
                  <span>COB 正式帐套的结构、事务与 ERP 写入规则已单独核查</span>
                </label>
                <label className="toggle">
                  <input type="checkbox" disabled={status.running || busy || !settings.productionSchemaReviewed}
                    checked={settings.enableProductionWrites}
                    onChange={e=>set("enableProductionWrites",e.target.checked)}/>
                  <span>允许向 COB.XCOUTD 提交正式盘点数据</span>
                </label>
              </div>
            </div>
            <div className="form-actions">
              <button className="primary-btn" disabled={status.running || busy} onClick={()=>void perform(()=>save())}><Save size={18}/> 保存配置</button>
              <button className="outline-btn" disabled={busy} onClick={test}><RefreshCw size={17}/> 测试连接</button>
            </div>
            {dbTest && <div className={"inline-result " + (dbTest.ok ? "good" : "bad")}>{dbTest.ok ? <Check size={16}/> : <XCircle size={16}/>} {dbTest.message}</div>}
          </section>
          <aside className="panel tip-panel"><LockKeyhole size={24}/><h3>配置安全</h3>
            <p>数据库主机和用户名保存在本地 JSON 中；密码单独存入 Windows 凭据管理器。</p>
            <div className="divider"/><strong>环境固定映射</strong>
            <div className="envline"><span>Test</span><b>XTEST</b></div>
            <div className="envline"><span>Production</span><b>COB</b></div>
            <p>API 不允许客户端自行指定任意 Schema，也不会向 PDA 返回数据库密码或连接字符串。</p>
          </aside>
        </div>}

        {view === "access" && <div className="form-layout">
          <section className="panel form-panel">
            <div className="section-head"><div><h3>监听与网络接入</h3><p>先在本机调试，再配置允许 PDA 访问的 HTTPS 服务。</p></div><Globe2 size={23}/></div>
            <div className="form-grid">
              <label className="field">监听端口
                <input disabled={status.running || busy} type="number" min={1024} max={65534} value={settings.apiPort}
                  onChange={e=>set("apiPort",Number(e.target.value))}/>
              </label>
              <div className="field"><span>运行模式</span>
                <div className="mode-pill">{settings.allowLan ? "HTTPS / 受控内网" : "HTTP / localhost"}</div>
              </div>
              <label className="toggle full"><input disabled={status.running || busy} type="checkbox" checked={settings.allowLan}
                onChange={e=>set("allowLan",e.target.checked)}/>
                <span>允许通过受控局域网向 PDA 提供 HTTPS API</span>
              </label>
              {settings.allowLan && <>
                <label className="field full">HTTPS PFX 证书路径
                  <input disabled={status.running || busy} value={settings.certPath} onChange={e=>set("certPath",e.target.value)}
                    placeholder={"C:\\Certificates\\stockcount-server.pfx"}/>
                </label>
                <label className="field full">PFX 证书密码
                  <input disabled={status.running || busy} type="password" value={certPassword} onChange={e=>setCertPassword(e.target.value)}
                    placeholder={settings.certPasswordSaved ? "已安全保存；留空不修改" : "填写证书密码"}/>
                </label>
              </>}
            </div>
            <div className="form-actions">
              <button className="primary-btn" disabled={status.running || busy} onClick={()=>void perform(()=>save())}><Save size={18}/> 保存 API 配置</button>
            </div>
            <div className="hint-block"><ShieldCheck size={18}/><span>局域网模式额外使用本机管理端口 {settings.apiPort+1}（仅 127.0.0.1）。生产部署需要合法的 PFX 证书与内网防火墙规则。</span></div>
          </section>
          <aside className="panel tip-panel"><KeyRound size={24}/><h3>PDA 访问设置</h3>
            <p>现场 PDA 只需设置 HTTPS API 地址、环境和设备访问密钥；没有 SQL Server 凭据。</p>
            <div className="endpoint-small">{baseAddress}</div>
            <button className="outline-btn full-btn" disabled={busy} onClick={copyDeviceKey}><Clipboard size={16}/> 复制设备接入密钥</button>
            <p className="footnote">密钥首次打开时自动生成并保存到 Windows 凭据管理器。前台 MAUI 配置页后续接入此参数。</p>
          </aside>
        </div>}

        {view === "logs" && <section className="panel log-panel">
          <div className="section-head"><div><h3>运行记录</h3><p>后端写入 SQLite；管理界面只读查询，保留最近 30 天。</p></div><button className="outline-btn" onClick={()=>void refreshLogs(search)}><RefreshCw size={16}/> 刷新</button></div>
          <div className="log-toolbar"><Search size={18}/><input aria-label="搜索运行日志" value={search} onChange={e=>setSearch(e.target.value)}
            placeholder="按等级、模块或日志内容筛选..."/><div className="log-count">{logs.length} 条</div></div>
          <div className="log-scroll"><table><thead><tr><th>时间（本地）</th><th>等级</th><th>模块</th><th>事件说明</th></tr></thead>
            <tbody>{logs.length === 0 ? <tr><td className="empty" colSpan={4}>尚无匹配日志，启动服务后将显示 HTTP 访问和运行记录。</td></tr> :
              logs.map(row => <tr key={row.id} className={selectedLog?.id===row.id ? "selected" : ""}
                onClick={()=>setSelectedLog(row)}>
                <td className="mono">{formatUtc(row.timestampUtc)}</td>
                <td><span className={"level " + row.level.toLowerCase()}>{row.level}</span></td>
                <td>{row.category}</td><td className="ellipsize">{row.message}</td></tr>)}</tbody>
          </table></div>
          <div className="log-detail"><span>详情</span><p>{selectedLog ? selectedLog.message + (selectedLog.exception ? "\n" + selectedLog.exception : "") : "点击一条日志查看详细内容。"}</p></div>
          <div className="log-footer"><HardDrive size={15}/><span title={logFile}>{logFile || "应用目录\\server-logs.db（由本机程序返回实际路径）"}</span>
            <span className="spacer"/><ShieldCheck size={15}/> SQLite WAL</div>
        </section>}
      </main>
    </div>
  );
}
