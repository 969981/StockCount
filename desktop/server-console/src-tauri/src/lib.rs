mod config;
mod logs;
mod server;

use config::{Settings, SettingsView};
use tauri::Manager;

#[tauri::command]
fn read_settings() -> Result<SettingsView, String> {
    config::settings_view()
}

#[tauri::command]
fn save_settings(
    app: tauri::AppHandle,
    mut settings: Settings,
    sql_password: Option<String>,
    cert_password: Option<String>,
) -> Result<SettingsView, String> {
    let state = app.state::<server::ServerState>();
    if state.inner.lock().map_err(|_| "状态锁不可用")?.child_is_running() {
        return Err("服务运行中，请先停止服务再保存配置。".into());
    }
    // Schema approval must not silently transfer to a different SQL Server,
    // database, principal or password. A new target needs new review for BOTH schemas.
    let previous = config::load_settings()?;
    let database_identity_changed =
        settings.sql_server != previous.sql_server
        || settings.sql_database != previous.sql_database
        || settings.integrated_security != previous.integrated_security
        || settings.sql_user != previous.sql_user
        || settings.encrypt_sql != previous.encrypt_sql
        || settings.trust_server_certificate != previous.trust_server_certificate
        || sql_password.as_deref().is_some_and(|p| !p.is_empty());
    if database_identity_changed {
        settings.enable_test_writes = false;
        settings.enable_production_writes = false;
        settings.schema_reviewed = false;
        settings.production_schema_reviewed = false;
    }
    config::save_settings(&settings)?;
    if let Some(password) = sql_password {
        if !password.is_empty() { config::set_secret("sql-password", &password)?; }
    }
    if let Some(password) = cert_password {
        if !password.is_empty() { config::set_secret("cert-password", &password)?; }
    }
    config::settings_view()
}

#[tauri::command]
fn copy_device_key() -> Result<(), String> {
    let key = config::device_key()?;
    arboard::Clipboard::new()
        .map_err(|e| format!("无法打开 Windows 剪贴板：{e}"))?
        .set_text(key)
        .map_err(|e| format!("复制设备密钥失败：{e}"))
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(server::ServerState::default())
        .invoke_handler(tauri::generate_handler![
            read_settings,
            save_settings,
            copy_device_key,
            server::start_server,
            server::stop_server,
            server::server_status,
            server::test_sql_connection,
            logs::read_logs,
            logs::log_path,
        ])
        .build(tauri::generate_context!())
        .expect("StockCount Server Console failed to build")
        .run(|app_handle, event| {
            if let tauri::RunEvent::Exit = event {
                server::kill_on_exit(app_handle);
            }
        });
}
