use keyring::{Entry, Error as KeyringError};
use serde::{Deserialize, Serialize};
use std::{env, fs, path::PathBuf};

const SERVICE: &str = "com.dreamer.stockcount.server";

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Settings {
    pub api_port: u16,
    pub allow_lan: bool,
    pub cert_path: String,
    pub sql_server: String,
    pub sql_database: String,
    pub integrated_security: bool,
    pub sql_user: String,
    pub encrypt_sql: bool,
    pub trust_server_certificate: bool,
    pub enable_test_writes: bool,
    pub schema_reviewed: bool,
    pub x3_audit_user: String,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            api_port: 5080,
            allow_lan: false,
            cert_path: String::new(),
            sql_server: String::new(), // Personnel fill in the real address locally.
            sql_database: "sagex3".into(),
            integrated_security: false,
            sql_user: String::new(),
            encrypt_sql: true,
            trust_server_certificate: false,
            enable_test_writes: false,
            schema_reviewed: false,
            x3_audit_user: String::new(),
        }
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SettingsView {
    #[serde(flatten)]
    pub settings: Settings,
    pub sql_password_saved: bool,
    pub cert_password_saved: bool,
    pub device_key_ready: bool,
}

/// SQLite file beside the Tauri desktop executable, not in AppData.
pub fn log_db_path() -> Result<PathBuf, String> {
    let exe = env::current_exe().map_err(|e| format!("无法找到桌面程序路径：{e}"))?;
    let directory = exe.parent().ok_or("无法找到桌面程序所在目录")?;
    Ok(directory.join("server-logs.db"))
}

/// Fail early if installer/portable directory has no write access.
/// Never silently relocate logs, because that would contradict the operator's choice.
pub fn verify_log_directory_writable() -> Result<(), String> {
    let path = log_db_path()?;
    let directory = path.parent().ok_or("日志文件路径无效")?;
    let probe = directory.join(format!(".stockcount-write-test-{}.tmp", uuid::Uuid::new_v4()));
    let file = std::fs::OpenOptions::new()
        .create_new(true)
        .write(true)
        .open(&probe)
        .map_err(|e| format!("程序目录不可写：{} ({e})。请安装到当前用户可写目录，不要使用受保护的 Program Files 目录。", directory.display()))?;
    drop(file);
    let _ = std::fs::remove_file(probe);
    Ok(())
}

pub fn base_dir() -> Result<PathBuf, String> {
    let base = env::var_os("LOCALAPPDATA")
        .ok_or("无法获取 LOCALAPPDATA：此管理器仅支持 Windows 用户会话。")?;
    Ok(PathBuf::from(base).join("StockCount").join("ServerHost"))
}

fn settings_path() -> Result<PathBuf, String> {
    Ok(base_dir()?.join("server-settings.json"))
}

pub fn load_settings() -> Result<Settings, String> {
    let path = settings_path()?;
    if !path.exists() { return Ok(Settings::default()); }
    let raw = fs::read_to_string(path).map_err(|e| format!("读取配置失败：{e}"))?;
    serde_json::from_str(&raw).map_err(|e| format!("配置文件格式错误：{e}"))
}

pub fn save_settings(settings: &Settings) -> Result<(), String> {
    if !(1024..=65534).contains(&settings.api_port) {
        return Err("端口必须在 1024..65534：端口+1 用于本机管理通道。".into());
    }
    if settings.sql_database.trim().is_empty() {
        return Err("数据库名称不可为空。".into());
    }
    if settings.enable_test_writes {
        if !settings.schema_reviewed {
            return Err("请先现场核对 XTEST 的 XCOUTD 字段与事务，并勾选结构已验收。".into());
        }
        let code = settings.x3_audit_user.trim();
        if code.is_empty() || code.chars().count() > 5 {
            return Err("启用测试写入前需提供1-5字符的 X3 审计用户代码。".into());
        }
    }
    if settings.allow_lan && settings.cert_path.trim().is_empty() {
        return Err("允许 PDA 局域网接入前必须配置 HTTPS PFX 证书。".into());
    }
    let base = base_dir()?;
    fs::create_dir_all(&base).map_err(|e| e.to_string())?;
    let text = serde_json::to_string_pretty(settings).map_err(|e| e.to_string())?;
    // Only non-secret settings are written to JSON.
    fs::write(settings_path()?, text).map_err(|e| format!("保存配置失败：{e}"))
}

fn entry(name: &str) -> Result<Entry, String> {
    Entry::new(SERVICE, name).map_err(|e| format!("Windows 凭据管理器不可用：{e}"))
}

pub fn secret(name: &str) -> Result<Option<String>, String> {
    match entry(name)?.get_password() {
        Ok(value) => Ok(Some(value)),
        Err(KeyringError::NoEntry) => Ok(None),
        Err(error) => Err(format!("读取凭据 {name} 失败：{error}")),
    }
}

pub fn set_secret(name: &str, value: &str) -> Result<(), String> {
    entry(name)?.set_password(value).map_err(|e| format!("保存凭据失败：{e}"))
}

pub fn device_key() -> Result<String, String> {
    if let Some(current) = secret("device-key")? { return Ok(current); }
    // Two random UUIDv4 values: do not put a long-lived shared secret in the repository.
    let value = format!("{}{}", uuid::Uuid::new_v4().simple(), uuid::Uuid::new_v4().simple());
    set_secret("device-key", &value)?;
    Ok(value)
}

pub fn settings_view() -> Result<SettingsView, String> {
    Ok(SettingsView {
        settings: load_settings()?,
        sql_password_saved: secret("sql-password")?.is_some(),
        cert_password_saved: secret("cert-password")?.is_some(),
        device_key_ready: !device_key()?.is_empty(),
    })
}
