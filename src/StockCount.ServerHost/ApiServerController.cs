using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockCount.Api;

namespace StockCount.ServerHost;

/// <summary>Embeds existing ASP.NET Core endpoints in the WPF process.</summary>
public sealed class ApiServerController
{
    private WebApplication? _application;
    public bool IsRunning => _application is not null;

    public async Task StartAsync(ServerSettings settings, SqliteLogStore logs)
    {
        if (IsRunning) throw new InvalidOperationException("服务已启动。");
        if (settings.ApiPort is < 1024 or > 65535)
            throw new InvalidOperationException("端口只能使用 1024–65535。");

        var deviceKey = SettingsStore.Unprotect(settings.DeviceKeyProtected);
        if (settings.AllowLan)
        {
            if (string.IsNullOrWhiteSpace(deviceKey))
                throw new InvalidOperationException("局域网接入必须配置设备访问密钥。");
            if (string.IsNullOrWhiteSpace(settings.CertificatePath) ||
                !File.Exists(settings.CertificatePath))
                throw new InvalidOperationException("局域网接入必须填写有效的 HTTPS PFX 证书路径。");
        }

        var connectionString = SettingsStore.BuildSqlConnectionString(settings);
        var app = ApiApplication.Build([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SageX3"] = connectionString,
                ["StockCount:DeviceKey"] = deviceKey,
                ["AllowedHosts"] = settings.AllowLan ? "*" : "localhost;127.0.0.1",
                ["StockCount:EnableProductionWrites"] = "false",
                ["StockCount:EnableTestWrites"] = "false"
            });
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(logs);
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.WebHost.ConfigureKestrel(options =>
            {
                if (!settings.AllowLan)
                {
                    options.ListenLocalhost(settings.ApiPort);
                    return;
                }

                var certificatePassword = SettingsStore.Unprotect(settings.CertificatePasswordProtected);
                options.ListenAnyIP(settings.ApiPort,
                    endpoint => endpoint.UseHttps(settings.CertificatePath, certificatePassword));
            });
        });

        try
        {
            await app.StartAsync();
            _application = app;
            logs.Add("Information", "ServerHost",
                $"API 已启动，监听 {(settings.AllowLan ? "HTTPS / LAN" : "HTTP / localhost")}，端口 {settings.ApiPort}。");
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async Task StopAsync(SqliteLogStore logs)
    {
        var app = _application;
        if (app is null) return;
        _application = null;
        try { await app.StopAsync(TimeSpan.FromSeconds(10)); }
        finally
        {
            await app.DisposeAsync();
            logs.Add("Information", "ServerHost", "API 服务已停止。");
        }
    }
}
