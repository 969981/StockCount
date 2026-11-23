using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace StockCount.ServerHost;

/// <summary>Nonsecret settings plus DPAPI-encrypted secret blobs. Never serialize plaintext passwords.</summary>
public sealed class ServerSettings
{
    public int ApiPort { get; set; } = 5080;
    public bool AllowLan { get; set; }
    public string CertificatePath { get; set; } = "";
    public string CertificatePasswordProtected { get; set; } = "";

    // Placeholders. Site personnel must provide the values locally.
    public string SqlServer { get; set; } = "";
    public string SqlDatabase { get; set; } = "sagex3";
    public bool IntegratedSecurity { get; set; }
    public string SqlUser { get; set; } = "";
    public string SqlPasswordProtected { get; set; } = "";
    public bool EncryptSql { get; set; } = true;
    public bool TrustServerCertificate { get; set; }

    public string DeviceKeyProtected { get; set; } = "";
}

public static class SettingsStore
{
    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StockCount", "ServerHost");
    public static string SettingsPath => Path.Combine(DirectoryPath, "server-settings.json");
    public static string LogPath => Path.Combine(DirectoryPath, "logs", "server-logs.db");

    public static ServerSettings Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        ServerSettings settings;
        if (!File.Exists(SettingsPath))
            settings = new ServerSettings();
        else
            settings = JsonSerializer.Deserialize<ServerSettings>(
                File.ReadAllText(SettingsPath)) ??
                throw new InvalidDataException("配置文件为空或格式不正确。");

        // Stable, machine-local device token: 256 bits, never checked into git.
        if (string.IsNullOrWhiteSpace(settings.DeviceKeyProtected))
        {
            settings.DeviceKeyProtected = Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            Save(settings);
        }
        return settings;
    }

    public static void Save(ServerSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        File.Move(temporary, SettingsPath, true);
    }

    public static string Protect(string value) =>
        string.IsNullOrEmpty(value) ? "" :
        Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string protectedValue) =>
        string.IsNullOrEmpty(protectedValue) ? "" :
        Encoding.UTF8.GetString(ProtectedData.Unprotect(
            Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser));

    public static string BuildSqlConnectionString(ServerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SqlServer))
            return ""; // Server can start, /ready reports not configured.

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = settings.SqlServer.Trim(),
            InitialCatalog = settings.SqlDatabase.Trim(),
            IntegratedSecurity = settings.IntegratedSecurity,
            ConnectTimeout = 5,
            TrustServerCertificate = settings.TrustServerCertificate
        };
        builder["Encrypt"] = settings.EncryptSql ? "True" : "False";
        if (!settings.IntegratedSecurity)
        {
            builder.UserID = settings.SqlUser.Trim();
            builder.Password = Unprotect(settings.SqlPasswordProtected);
        }
        return builder.ConnectionString;
    }
}
