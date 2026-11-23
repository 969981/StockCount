using System.Text.Json;
using StockCount.Server.Hosting;
using Xunit;

namespace StockCount.Server.Hosting.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void SavesCamelCaseJsonBesideDesktopExeAndReadsItAgain()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stockcount-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(dir);
            store.Save(new ServerSettings { ApiPort = 5140, SqlServer = "localhost" });
            Assert.True(File.Exists(Path.Combine(dir, "server-settings.json")));
            var json = File.ReadAllText(store.SettingsPath);
            Assert.Contains("\"apiPort\": 5140", json);
            Assert.DoesNotContain("sqlPassword", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("localhost", store.Load().SqlServer);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void DefaultsToApiAutostartButNeverToUnreviewedErpWrites()
    {
        var settings = new ServerSettings();
        Assert.True(settings.AutoStartApi);
        Assert.False(settings.SchemaReviewed);
        Assert.False(settings.EnableTestWrites);
        Assert.False(settings.ProductionSchemaReviewed);
        Assert.False(settings.EnableProductionWrites);
    }

    [Fact]
    public void SchemaApprovalIsNotInheritedAfterDatabaseIdentityChange()
    {
        var previous = new ServerSettings { SqlServer = "SERVER-A", SchemaReviewed = true, ProductionSchemaReviewed = true };
        var next = new ServerSettings { SqlServer = "SERVER-B", SchemaReviewed = true, ProductionSchemaReviewed = true,
            EnableTestWrites = true, EnableProductionWrites = true };
        Assert.True(next.DatabaseIdentityChangedFrom(previous, newPassword: false));
        next.RevokeWriteApprovals();
        Assert.False(next.EnableTestWrites);
        Assert.False(next.EnableProductionWrites);
        Assert.False(next.SchemaReviewed);
        Assert.False(next.ProductionSchemaReviewed);
    }

    [Fact]
    public void WriteRequiresIndependentSchemaReview()
    {
        var settings = new ServerSettings { EnableProductionWrites = true, X3AuditUser = "PDA" };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void ReadingMalformedLocalJsonIsAnErrorNotSilentDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stockcount-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(dir);
            File.WriteAllText(store.SettingsPath, "{ invalid");
            Assert.Throws<JsonException>(() => store.Load());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
