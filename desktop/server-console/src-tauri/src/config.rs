use keyring::{Entry, Error as KeyringError};
use serde::{Deserialize, Serialize};
use std::{env, fs, path::{Path, PathBuf}};

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
    /// Independent production opt-in. Default false for existing installations.
    pub enable_production_writes: bool,
    /// Historical field for XTEST review; retained for backwards compatibility.
    pub schema_reviewed: bool,
    pub production_schema_reviewed: bool,
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
            enable_production_writes: false,
            schema_reviewed: false,
            production_schema_reviewed: false,
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

/// Both the JSON settings and SQLite logs live beside the desktop EXE.
/// This includes the portable installation and the Tauri debug executable.
fn executable_directory() -> Result<PathBuf, String> {
    let exe = env::current_exe().map_err(|e| format!("无法找到桌面程序路径：{e}"))?;
    Ok(exe.parent().ok_or("无法找到桌面程序所在目录")?.to_path_buf())
}

pub fn log_db_path() -> Result<PathBuf, String> {
    Ok(executable_directory()?.join("server-logs.db"))
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

/// Legacy path is used solely for one-time, non-destructive migration.
/// Passwords and PDA device keys remain in Windows Credential Manager.
fn legacy_settings_path() -> Option<PathBuf> {
    env::var_os("LOCALAPPDATA").map(|dir| {
        PathBuf::from(dir).join("StockCount").join("ServerHost").join("server-settings.json")
    })
}

fn settings_path() -> Result<PathBuf, String> {
    Ok(executable_directory()?.join("server-settings.json"))
}

fn parse_settings(path: &Path) -> Result<Settings, String> {
    let raw = fs::read_to_string(path)
        .map_err(|e| format!("读取配置失败（{}）：{e}", path.display()))?;
    serde_json::from_str(&raw)
        .map_err(|e| format!("配置文件格式错误（{}）：{e}", path.display()))
}

/// The colocated config always wins. If absent, copy and validate the old
/// AppData JSON once; never overwrite an existing local config or delete the
/// legacy copy. An unwritable destination is an error, not a silent fallback.
fn load_or_migrate_settings(local: &Path, legacy: Option<&Path>) -> Result<Settings, String> {
    if local.exists() {
        return parse_settings(local);
    }
    let Some(old) = legacy.filter(|p| p.is_file()) else {
        return Ok(Settings::default());
    };
    let raw = fs::read_to_string(old)
        .map_err(|e| format!("读取旧配置失败（{}）：{e}", old.display()))?;
    let settings: Settings = serde_json::from_str(&raw)
        .map_err(|e| format!("旧配置格式错误（{}），未进行迁移：{e}", old.display()))?;

    let directory = local.parent().ok_or("同目录配置路径无效")?;
    let temporary = directory.join(format!(".stockcount-settings-migrate-{}.tmp",
        uuid::Uuid::new_v4()));
    fs::write(&temporary, raw.as_bytes())
        .map_err(|e| format!("无法迁移配置到 EXE 同目录（{}）：{e}。请检查安装目录写权限。", directory.display()))?;

    if let Err(error) = fs::rename(&temporary, local) {
        let _ = fs::remove_file(&temporary);
        // Another process might have created the local file while migrating.
        // Never overwrite it: respect the new location as the authority.
        if local.exists() { return parse_settings(local); }
        return Err(format!("无法完成同目录配置迁移（{}）：{error}。旧配置仍保留在 {}",
            local.display(), old.display()));
    }
    Ok(settings)
}

pub fn load_settings() -> Result<Settings, String> {
    load_or_migrate_settings(&settings_path()?, legacy_settings_path().as_deref())
}

pub fn save_settings(settings: &Settings) -> Result<(), String> {
    if !(1024..=65534).contains(&settings.api_port) {
        return Err("端口必须在 1024..65534：端口+1 用于本机管理通道。".into());
    }
    if settings.sql_database.trim().is_empty() {
        return Err("数据库名称不可为空。".into());
    }
    if settings.enable_test_writes && !settings.schema_reviewed {
        return Err("XTEST 写入需要先完成该帐套的 XCOUTD 结构和 ERP 事务核验。".into());
    }
    if settings.enable_production_writes && !settings.production_schema_reviewed {
        return Err("COB 正式写入需要独立完成 COB 字段、触发器、事务和单位核验。".into());
    }
    if settings.enable_test_writes || settings.enable_production_writes {
        let code = settings.x3_audit_user.trim();
        if code.is_empty() || code.chars().count() > 5 {
            return Err("启用写入需要填写现场批准的 1–5 字符 X3 审计用户编码。".into());
        }
    }
    // LAN HTTP is opt-in and does not need a PFX. Retain legacy cert_path in
    // serialized Settings so existing user configuration remains compatible.
    let path = settings_path()?;
    // Deliberately do not create an alternative AppData directory.
    // Fail with a useful error if the selected EXE installation is read-only.
    verify_log_directory_writable()?;
    let text = serde_json::to_string_pretty(settings).map_err(|e| e.to_string())?;
    // Only non-secret settings are written to JSON. Never persist passwords or keys.
    fs::write(&path, text)
        .map_err(|e| format!("保存配置失败（{}）：{e}。请将程序放在当前用户可写目录。", path.display()))
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

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    struct Fixture { root: PathBuf }
    impl Fixture {
        fn new() -> Self {
            let root = std::env::temp_dir().join(format!("stockcount-config-test-{}",
                uuid::Uuid::new_v4()));
            fs::create_dir_all(root.join("portable")).unwrap();
            fs::create_dir_all(root.join("old-appdata")).unwrap();
            Self { root }
        }
        fn local(&self) -> PathBuf { self.root.join("portable/server-settings.json") }
        fn legacy(&self) -> PathBuf { self.root.join("old-appdata/server-settings.json") }
    }
    impl Drop for Fixture {
        fn drop(&mut self) { let _ = fs::remove_dir_all(&self.root); }
    }

    #[test]
    fn migrates_legacy_settings_to_executable_directory_without_deleting_original() {
        let fixture = Fixture::new();
        let mut original = Settings::default();
        original.api_port = 6140;
        original.sql_database = "sagex3".into();
        original.sql_server = "TEST-SERVER".into();
        let json = serde_json::to_string_pretty(&original).unwrap();
        fs::write(fixture.legacy(), &json).unwrap();

        let result = load_or_migrate_settings(&fixture.local(), Some(&fixture.legacy())).unwrap();
        assert_eq!(result.api_port, 6140);
        assert_eq!(result.sql_server, "TEST-SERVER");
        assert_eq!(fs::read_to_string(fixture.local()).unwrap(), json);
        assert_eq!(fs::read_to_string(fixture.legacy()).unwrap(), json);
    }

    #[test]
    fn existing_executable_directory_config_wins_over_legacy() {
        let fixture = Fixture::new();
        let mut local = Settings::default();
        local.api_port = 5090;
        let mut old = Settings::default();
        old.api_port = 6100;
        fs::write(fixture.local(), serde_json::to_string(&local).unwrap()).unwrap();
        fs::write(fixture.legacy(), serde_json::to_string(&old).unwrap()).unwrap();

        let loaded = load_or_migrate_settings(&fixture.local(), Some(&fixture.legacy())).unwrap();
        assert_eq!(loaded.api_port, 5090);
        assert_eq!(parse_settings(&fixture.legacy()).unwrap().api_port, 6100);
    }

    #[test]
    fn malformed_local_json_fails_closed_and_does_not_use_legacy() {
        let fixture = Fixture::new();
        fs::write(fixture.local(), "{bad").unwrap();
        fs::write(fixture.legacy(), serde_json::to_string(&Settings::default()).unwrap()).unwrap();
        assert!(load_or_migrate_settings(&fixture.local(), Some(&fixture.legacy())).is_err());
        assert_eq!(fs::read_to_string(fixture.local()).unwrap(), "{bad");
    }

    #[test]
    fn malformed_legacy_json_is_not_migrated() {
        let fixture = Fixture::new();
        fs::write(fixture.legacy(), "{bad").unwrap();
        assert!(load_or_migrate_settings(&fixture.local(), Some(&fixture.legacy())).is_err());
        assert!(!fixture.local().exists());
        assert_eq!(fs::read_to_string(fixture.legacy()).unwrap(), "{bad");
    }

    #[test]
    fn returns_defaults_when_no_config_exists() {
        let fixture = Fixture::new();
        let loaded = load_or_migrate_settings(&fixture.local(), Some(&fixture.legacy())).unwrap();
        assert_eq!(loaded.api_port, 5080);
        assert!(!fixture.local().exists());
    }
}
