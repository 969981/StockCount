using System.Text;
using System.Text.Json;

namespace StockCount.Server.Hosting;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    public string DirectoryPath { get; }
    public string SettingsPath => Path.Combine(DirectoryPath, "server-settings.json");
    public string LogPath => Path.Combine(DirectoryPath, "server-logs.db");

    private readonly bool _approvedFieldWrites;

    public SettingsStore(string? directory = null, bool approvedFieldWrites = false)
    {
        DirectoryPath = Path.GetFullPath(directory ?? AppContext.BaseDirectory);
        _approvedFieldWrites = approvedFieldWrites;
    }

    private ServerSettings ApplyPolicy(ServerSettings settings)
    {
        if (_approvedFieldWrites) settings.ApplyApprovedFieldWrites();
        return settings;
    }

    public ServerSettings Load()
    {
        if (File.Exists(SettingsPath)) return ApplyPolicy(Parse(SettingsPath));
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StockCount", "ServerHost", "server-settings.json");
        if (!File.Exists(old)) return ApplyPolicy(ServerSettings.CreateFirstUseDefaults());

        // Check old JSON before copying. Only migration reads AppData.
        var content = File.ReadAllText(old, Encoding.UTF8);
        _ = JsonSerializer.Deserialize<ServerSettings>(content, Json)
            ?? throw new InvalidDataException("旧配置文件为空。");
        var temporary = SettingsPath + ".migrate-" + Guid.NewGuid().ToString("N");
        EnsureWritableDirectory();
        try
        {
            File.WriteAllText(temporary, content, Encoding.UTF8);
            try { File.Move(temporary, SettingsPath); }
            catch (IOException) when (File.Exists(SettingsPath)) { /* another instance won */ }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return ApplyPolicy(Parse(SettingsPath)); // Never erase or modify old AppData file.
    }

    public void Save(ServerSettings settings)
    {
        settings = ApplyPolicy(settings.Copy());
        settings.Validate();
        EnsureWritableDirectory();
        var temporary = SettingsPath + ".write-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json), Encoding.UTF8);
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void EnsureWritableDirectory()
    {
        if (!Directory.Exists(DirectoryPath))
            throw new DirectoryNotFoundException("程序目录不存在：" + DirectoryPath);
        var probe = Path.Combine(DirectoryPath, ".stockcount-probe-" + Guid.NewGuid().ToString("N"));
        try { using var _ = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException("EXE 目录不可写，请安装或解压至当前用户可写目录，勿使用 Program Files。", ex);
        }
        finally { if (File.Exists(probe)) File.Delete(probe); }
    }

    private static ServerSettings Parse(string path) =>
        JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(path, Encoding.UTF8), Json)
        ?? throw new InvalidDataException("配置文件为空：" + path);
}
