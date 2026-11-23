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
    settings: Settings,
    sql_password: Option<String>,
    cert_password: Option<String>,
) -> Result<SettingsView, String> {
    let state = app.state::<server::ServerState>();
    if state.inner.lock().map_err(|_| "状态锁不可用")?.child_is_running() {
        return Err("服务运行中，请先停止服务再保存配置。".into());
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
fn reveal_device_key() -> Result<String, String> {
    config::device_key()
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(server::ServerState::default())
        .invoke_handler(tauri::generate_handler![
            read_settings,
            save_settings,
            reveal_device_key,
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
