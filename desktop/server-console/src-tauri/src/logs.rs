use rusqlite::{params, Connection, OpenFlags};
use serde::Serialize;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LogRow {
    pub id: i64,
    pub timestamp_utc: String,
    pub level: String,
    pub category: String,
    pub message: String,
    pub exception: Option<String>,
}

#[tauri::command]
pub fn log_path() -> Result<String, String> {
    Ok(crate::config::base_dir()?
        .join("logs").join("server-logs.db").to_string_lossy().to_string())
}

#[tauri::command]
pub fn read_logs(search: Option<String>, limit: Option<usize>) -> Result<Vec<LogRow>, String> {
    let path = crate::config::base_dir()?.join("logs").join("server-logs.db");
    if !path.exists() { return Ok(Vec::new()); }

    let connection = Connection::open_with_flags(path,
        OpenFlags::SQLITE_OPEN_READ_ONLY | OpenFlags::SQLITE_OPEN_NO_MUTEX)
        .map_err(|e| format!("打开 SQLite 日志失败：{e}"))?;
    connection.busy_timeout(std::time::Duration::from_secs(2))
        .map_err(|e| e.to_string())?;

    let mut statement = connection.prepare(
        "SELECT id,timestamp_utc,level,category,message,exception
         FROM log_entries
         WHERE ?1 IS NULL OR category LIKE ?1 OR level LIKE ?1 OR message LIKE ?1
         ORDER BY id DESC LIMIT ?2"
    ).map_err(|e| format!("日志表尚未就绪：{e}"))?;
    let pattern = search.filter(|s| !s.trim().is_empty())
        .map(|s| format!("%{}%", s.trim().chars().take(80).collect::<String>()));
    let records = statement.query_map(params![pattern, limit.unwrap_or(200).clamp(1,500)], |row| {
        Ok(LogRow {
            id: row.get(0)?,
            timestamp_utc: row.get(1)?,
            level: row.get(2)?,
            category: row.get(3)?,
            message: row.get(4)?,
            exception: row.get(5)?,
        })
    }).map_err(|e| e.to_string())?;

    records.map(|row| row.map_err(|e| e.to_string())).collect()
}
