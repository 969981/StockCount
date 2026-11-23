use crate::config::{self, Settings};
use serde::Serialize;
use std::{path::Path, sync::Mutex, time::Duration};
use tauri::{AppHandle, Manager, State};
use tauri_plugin_shell::{process::{CommandChild, CommandEvent, Command}, ShellExt};

#[derive(Default)]
pub struct ServerState {
    pub inner: Mutex<Inner>,
}

#[derive(Default)]
pub struct Inner {
    child: Option<CommandChild>,
    pid: Option<u32>,
    admin_token: String,
    port: u16,
    last_error: Option<String>,
}

impl Inner {
    pub fn child_is_running(&self) -> bool { self.child.is_some() }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ServerStatus {
    pub running: bool,
    pub healthy: bool,
    pub api_port: u16,
    pub allow_lan: bool,
    pub last_error: Option<String>,
}

fn sidecar_command(app: &AppHandle, settings: &Settings, admin: &str) -> Result<Command, String> {
    let sql_password = config::secret("sql-password")?.unwrap_or_default();
    let cert_password = config::secret("cert-password")?.unwrap_or_default();
    let device_key = config::device_key()?;

    let command = app.shell().sidecar("StockCount.Api")
        .map_err(|e| format!("找不到 .NET API Sidecar，先执行 publish/copy 脚本：{e}"))?;

    Ok(command
        .env("STOCKCOUNT_SQL_SERVER", &settings.sql_server)
        .env("STOCKCOUNT_SQL_DATABASE", &settings.sql_database)
        .env("STOCKCOUNT_SQL_INTEGRATED", settings.integrated_security.to_string())
        .env("STOCKCOUNT_SQL_USER", &settings.sql_user)
        .env("STOCKCOUNT_SQL_PASSWORD", sql_password)
        .env("STOCKCOUNT_SQL_NO_ENCRYPT", (!settings.encrypt_sql).to_string())
        .env("STOCKCOUNT_SQL_TRUST_CERT", settings.trust_server_certificate.to_string())
        .env("STOCKCOUNT_API_PORT", settings.api_port.to_string())
        .env("STOCKCOUNT_ALLOW_LAN", settings.allow_lan.to_string())
        .env("STOCKCOUNT_CERT_PATH", &settings.cert_path)
        .env("STOCKCOUNT_CERT_PASSWORD", cert_password)
        .env("STOCKCOUNT_DEVICE_KEY", device_key)
        .env("STOCKCOUNT_ADMIN_TOKEN", admin))
}

#[tauri::command]
pub async fn start_server(app: AppHandle, state: State<'_, ServerState>) -> Result<(), String> {
    let settings = config::load_settings()?;
    if !(1024..=65534).contains(&settings.api_port) {
        return Err("无效监听端口，必须在 1024..65534。".into());
    }
    if settings.allow_lan && !Path::new(&settings.cert_path).is_file() {
        return Err("局域网模式必须配置一个现有的 PFX HTTPS 证书文件。".into());
    }

    let admin_token = uuid::Uuid::new_v4().to_string();
    let command = sidecar_command(&app, &settings, &admin_token)?;
    let mut rx = {
        let mut inner = state.inner.lock().map_err(|_| "管理器状态锁不可用")?;
        if inner.child.is_some() { return Err("服务已在运行。".into()); }
        let (rx, child) = command.spawn().map_err(|e| format!("无法启动 API 子进程：{e}"))?;
        inner.port = settings.api_port;
        inner.pid = Some(child.pid());
        inner.admin_token = admin_token;
        inner.last_error = None;
        inner.child = Some(child);
        rx
    };

    // Drain stdout/stderr to avoid blocking the child; never stream raw output
    // (potentially including environment-specific diagnostics) into the UI.
    let app_for_task = app.clone();
    tauri::async_runtime::spawn(async move {
        while let Some(event) = rx.recv().await {
            if let CommandEvent::Terminated(_) = event {
                let state = app_for_task.state::<ServerState>();
                if let Ok(mut inner) = state.inner.lock() {
                    // A stale event must never clear a newly started process.
                    if let Some(pid) = inner.pid {
                        // We only ever start one child; check its last known PID.
                        if inner.child.as_ref().map(|child| child.pid()) == Some(pid) {
                            inner.child = None;
                            inner.pid = None;
                            inner.admin_token.clear();
                            inner.last_error = Some("API 子进程已经退出。请查看 SQLite 日志或检查端口及证书。".into());
                        }
                    }
                }
                break;
            }
        }
    });
    Ok(())
}

#[tauri::command]
pub async fn stop_server(state: State<'_, ServerState>) -> Result<(), String> {
    let (child, port, token) = {
        let mut inner = state.inner.lock().map_err(|_| "管理器状态锁不可用")?;
        let child = inner.child.take();
        inner.pid = None;
        let token = std::mem::take(&mut inner.admin_token);
        (child, inner.port, token)
    };

    let Some(child) = child else { return Ok(()); };
    // Request graceful shutdown to flush ILogger/SQLite channel first.
    if !token.is_empty() {
        let client = reqwest::Client::builder()
            .timeout(Duration::from_millis(1200))
            .build().map_err(|e| e.to_string())?;
        let _ = client
            .post(format!("http://127.0.0.1:{}/internal/shutdown", port + 1))
            .header("X-StockCount-Admin", token)
            .send().await;
    }

    // Fall back to OS termination if the child has not exited on its own.
    // A ProcessNotFound error here means graceful shutdown already worked.
    tokio::time::sleep(Duration::from_millis(700)).await;
    let _ = child.kill();
    Ok(())
}

#[tauri::command]
pub async fn server_status(state: State<'_, ServerState>) -> Result<ServerStatus, String> {
    let settings = config::load_settings()?;
    let (running, port, last_error) = {
        let inner = state.inner.lock().map_err(|_| "管理器状态锁不可用")?;
        (inner.child.is_some(), inner.port, inner.last_error.clone())
    };

    let healthy = if running {
        let client = reqwest::Client::builder()
            .timeout(Duration::from_millis(550))
            .build().map_err(|e| e.to_string())?;
        client.get(format!("http://127.0.0.1:{}/health", port + 1))
            .send().await.map(|r| r.status().is_success()).unwrap_or(false)
    } else { false };

    Ok(ServerStatus {
        running,
        healthy,
        api_port: settings.api_port,
        allow_lan: settings.allow_lan,
        last_error,
    })
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DatabaseTest {
    pub ok: bool,
    pub message: String,
}

#[tauri::command]
pub async fn test_sql_connection(app: AppHandle) -> Result<DatabaseTest, String> {
    let settings = config::load_settings()?;
    if settings.sql_server.is_empty() {
        return Ok(DatabaseTest { ok: false, message: "请先填写并保存 SQL Server 地址。".into() });
    }

    let command = sidecar_command(&app, &settings, "")?.arg("--test-db");
    let output = command.output().await.map_err(|e| format!("数据库连接检查进程失败：{e}"))?;
    Ok(DatabaseTest {
        ok: output.status.success(),
        message: if output.status.success() {
            "SQL Server SELECT 1 连接成功；仍需核查业务表的实际读取权限。".into()
        } else {
            "连接失败：请检查数据库地址、账号、凭据、证书和 SQL Server 网络访问。".into()
        },
    })
}

pub fn kill_on_exit(app: &AppHandle) {
    let state = app.state::<ServerState>();
    if let Ok(mut inner) = state.inner.lock() {
        if let Some(child) = inner.child.take() {
            // Emergency process cleanup; normal stop uses the graceful endpoint.
            let _ = child.kill();
        }
    };
}
